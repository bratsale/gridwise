using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Confluent.Kafka;
using Confluent.Kafka.Admin;
using GridWise.Batch.Data;
using GridWise.Batch.Events;
using GridWise.Batch.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.Kafka;

namespace GridWise.Batch.IntegrationTests;

public class KafkaFixture : IAsyncLifetime
{
    private readonly KafkaContainer _kafka = new KafkaBuilder("apache/kafka:4.4.0").Build();

    public string BootstrapServers => _kafka.GetBootstrapAddress();

    public async Task InitializeAsync()
    {
        await _kafka.StartAsync();

        using var admin = new AdminClientBuilder(new AdminClientConfig { BootstrapServers = BootstrapServers }).Build();
        await admin.CreateTopicsAsync(
        [
            new TopicSpecification { Name = Topics.BatchChunks, NumPartitions = 6 },
            new TopicSpecification { Name = Topics.BatchResults, NumPartitions = 3 },
            new TopicSpecification { Name = Topics.BatchDlq, NumPartitions = 1 }
        ]);
    }

    public Task DisposeAsync() => _kafka.DisposeAsync().AsTask();
}

[Collection("batch-dispatch")]
public class ChunkPublisherTests(BatchFactory factory, KafkaFixture kafka) : IClassFixture<KafkaFixture>
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task Chunks_are_published_once_to_batch_chunks_with_jobId_and_chunkNo_as_key()
    {
        var csv = "customer_id,consumption_kwh,tax_group\n" + string.Concat(Enumerable.Range(1, 1200).Select(i => $"c{i},1,a\n"));
        var form = new MultipartFormDataContent { { new ByteArrayContent(Encoding.UTF8.GetBytes(csv)), "file", "c.csv" } };
        var created = await (await factory.CreateClient().PostAsync("/jobs", form)).Content.ReadFromJsonAsync<JsonElement>();
        var jobId = created.GetProperty("id").GetGuid();

        using (var scope = factory.Services.CreateScope())
            await BatchDispatcher.StepAsync(
                scope.ServiceProvider.GetRequiredService<BatchDbContext>(), factory.Time, 500, 20, CancellationToken.None);

        using var producer = new ProducerBuilder<string, string>(new ProducerConfig
        {
            BootstrapServers = kafka.BootstrapServers,
            Acks = Acks.All,
            EnableIdempotence = true
        }).Build();

        // Other tests in the shared database may have left unpublished rows ahead of ours.
        while (await Publish(producer) > 0) { }

        var messages = Consume(jobId);
        Assert.Equal(3, messages.Count);
        Assert.Equal([$"{jobId}:1", $"{jobId}:2", $"{jobId}:3"], messages.Select(m => m.Message.Key).Order());

        var envelopes = messages.Select(m => JsonSerializer.Deserialize<EventEnvelope>(m.Message.Value, Web)!).ToList();
        Assert.All(envelopes, e =>
        {
            Assert.Equal(EventTypes.BatchChunkRequested, e.Type);
            Assert.Equal(1, e.Version);
            Assert.Equal(jobId, e.Data.GetProperty("jobId").GetGuid());
        });
        Assert.Equal([1, 2, 3], envelopes.Select(e => e.Data.GetProperty("chunkNo").GetInt32()).Order());
        Assert.Equal([500, 500, 200], envelopes.OrderBy(e => e.Data.GetProperty("chunkNo").GetInt32())
            .Select(e => e.Data.GetProperty("rows").GetArrayLength()));

        using var check = factory.Services.CreateScope();
        var db = check.ServiceProvider.GetRequiredService<BatchDbContext>();
        var rows = await db.OutboxMessages.AsNoTracking().Where(m => m.AggregateId.StartsWith(jobId + ":")).ToListAsync();
        Assert.All(rows, r => Assert.NotNull(r.PublishedAt));
        Assert.Equal(rows.Select(r => r.Id).Order(), envelopes.Select(e => e.Id).Order());

        Assert.Equal(0, await Publish(producer));
        Assert.Equal(3, Consume(jobId).Count);
    }

    private async Task<int> Publish(IProducer<string, string> producer)
    {
        using var scope = factory.Services.CreateScope();
        return await OutboxPublisher.PublishPendingAsync(
            scope.ServiceProvider.GetRequiredService<BatchDbContext>(), producer, TimeProvider.System, 100, CancellationToken.None);
    }

    // Reads the topic from the start until it has been quiet for 3 s (at most 20 s) and returns the messages of the job (keys start with the job id).
    private List<ConsumeResult<string, string>> Consume(Guid jobId)
    {
        using var consumer = new ConsumerBuilder<string, string>(new ConsumerConfig
        {
            BootstrapServers = kafka.BootstrapServers,
            GroupId = Guid.NewGuid().ToString(),
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = false
        }).Build();
        consumer.Subscribe(Topics.BatchChunks);

        var found = new List<ConsumeResult<string, string>>();
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(20);
        var lastMessage = DateTime.UtcNow;
        while (DateTime.UtcNow < deadline && (found.Count == 0 || DateTime.UtcNow - lastMessage < TimeSpan.FromSeconds(3)))
        {
            var result = consumer.Consume(TimeSpan.FromMilliseconds(500));
            if (result is null)
                continue;
            lastMessage = DateTime.UtcNow;
            if (result.Message.Key.StartsWith(jobId + ":"))
                found.Add(result);
        }
        return found;
    }
}
