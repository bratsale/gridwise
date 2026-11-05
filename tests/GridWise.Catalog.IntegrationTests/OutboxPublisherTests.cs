using System.Net.Http.Json;
using System.Text.Json;
using Confluent.Kafka;
using Confluent.Kafka.Admin;
using GridWise.Catalog.Api;
using GridWise.Catalog.Data;
using GridWise.Catalog.Events;
using GridWise.Catalog.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.Kafka;

namespace GridWise.Catalog.IntegrationTests;

public class KafkaFixture : IAsyncLifetime
{
    private readonly KafkaContainer _kafka = new KafkaBuilder("apache/kafka:4.4.0").Build();

    public string BootstrapServers => _kafka.GetBootstrapAddress();

    public async Task InitializeAsync()
    {
        await _kafka.StartAsync();

        using var admin = new AdminClientBuilder(new AdminClientConfig { BootstrapServers = BootstrapServers }).Build();
        await admin.CreateTopicsAsync([new TopicSpecification { Name = Topics.CatalogPlanEvents, NumPartitions = 3 }]);
    }

    public Task DisposeAsync() => _kafka.DisposeAsync().AsTask();
}

[Collection("catalog")]
public class OutboxPublisherTests(CatalogFactory factory, KafkaFixture kafka) : IClassFixture<KafkaFixture>
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task Plan_event_is_published_once_to_kafka()
    {
        var correlationId = Guid.NewGuid().ToString();
        var request = new HttpRequestMessage(HttpMethod.Post, "/plans")
        {
            Content = JsonContent.Create(new
            {
                name = "Standard",
                discount = 0.05m,
                tiers = new[] { new { threshold = (decimal?)null, pricePerKwh = 0.06m } }
            })
        };
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));
        request.Headers.Add("X-Correlation-Id", correlationId);
        var response = await factory.CreateClient().SendAsync(request);
        response.EnsureSuccessStatusCode();
        var planId = (await response.Content.ReadFromJsonAsync<PlanResponse>())!.Id;

        using var producer = new ProducerBuilder<string, string>(new ProducerConfig
        {
            BootstrapServers = kafka.BootstrapServers,
            Acks = Acks.All,
            EnableIdempotence = true
        }).Build();

        // Other tests in the shared database may have left unpublished rows ahead of ours.
        while (await Publish(producer) > 0) { }

        var message = Consume(planId.ToString(), TimeSpan.FromSeconds(15)).FirstOrDefault();
        Assert.NotNull(message);
        Assert.Equal(planId.ToString(), message.Message.Key);

        var envelope = JsonSerializer.Deserialize<EventEnvelope>(message.Message.Value, Web)!;
        Assert.Equal(EventTypes.PlanVersionCreated, envelope.Type);
        Assert.Equal(1, envelope.Version);
        Assert.Equal(correlationId, envelope.CorrelationId);
        Assert.Equal(planId, envelope.Data.GetProperty("id").GetGuid());
        Assert.Equal(1, envelope.Data.GetProperty("versionNo").GetInt32());

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        var row = await db.OutboxMessages.AsNoTracking().SingleAsync(m => m.AggregateId == planId.ToString());
        Assert.NotNull(row.PublishedAt);
        Assert.Equal(row.Id, envelope.Id);

        Assert.Equal(0, await Publish(producer));
        Assert.Single(ConsumeAll(planId.ToString()));
    }

    private async Task<int> Publish(IProducer<string, string> producer)
    {
        using var scope = factory.Services.CreateScope();
        return await OutboxPublisher.PublishPendingAsync(
            scope.ServiceProvider.GetRequiredService<CatalogDbContext>(),
            producer, TimeProvider.System, 100, CancellationToken.None);
    }

    private IConsumer<string, string> NewConsumer()
    {
        var consumer = new ConsumerBuilder<string, string>(new ConsumerConfig
        {
            BootstrapServers = kafka.BootstrapServers,
            GroupId = Guid.NewGuid().ToString(),
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = false
        }).Build();
        consumer.Subscribe(Topics.CatalogPlanEvents);
        return consumer;
    }

    // Yields messages with the given key until the timeout expires.
    private IEnumerable<ConsumeResult<string, string>> Consume(string key, TimeSpan timeout)
    {
        using var consumer = NewConsumer();
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            var result = consumer.Consume(TimeSpan.FromMilliseconds(500));
            if (result?.Message.Key == key)
                yield return result;
        }
    }

    // Reads the topic from the start; stops once the key was seen and the topic has been quiet for 3 s.
    private List<ConsumeResult<string, string>> ConsumeAll(string key)
    {
        using var consumer = NewConsumer();
        var found = new List<ConsumeResult<string, string>>();
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(15);
        var lastMessage = DateTime.UtcNow;
        while (DateTime.UtcNow < deadline && (found.Count == 0 || DateTime.UtcNow - lastMessage < TimeSpan.FromSeconds(3)))
        {
            var result = consumer.Consume(TimeSpan.FromMilliseconds(500));
            if (result is null)
                continue;
            lastMessage = DateTime.UtcNow;
            if (result.Message.Key == key)
                found.Add(result);
        }
        return found;
    }
}
