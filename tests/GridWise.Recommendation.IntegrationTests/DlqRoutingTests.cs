using System.Text;
using Confluent.Kafka;
using GridWise.Recommendation.Data;
using GridWise.Recommendation.Events;
using GridWise.Recommendation.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace GridWise.Recommendation.IntegrationTests;

// Own Kafka, so the messages of DlqReprocessorTests do not mix with these. The tests here do not depend on each other's leftovers.
[Collection("recommendation")]
public class DlqRoutingTests(RecommendationFactory factory, KafkaFixture kafka) : IClassFixture<KafkaFixture>
{
    [Fact]
    public async Task Batch_chunk_in_the_dlq_is_processed_like_by_the_worker()
    {
        await SeedPlanAndTaxGroup();
        var jobId = Guid.NewGuid();
        var json = BatchChunkProcessorTests.Chunk(jobId, 1, "c1,250,household", "c2,abc,household");

        using var producer = new ProducerBuilder<string, string>(new ProducerConfig { BootstrapServers = kafka.BootstrapServers }).Build();
        await producer.ProduceAsync(Topics.RecommendationDlq, DlqReprocessorTests.Dead($"{jobId}:1", json, Topics.BatchChunks));
        await producer.ProduceAsync(Topics.RecommendationDlq, DlqReprocessorTests.Dead($"{jobId}:1", json, Topics.BatchChunks)); // a duplicate

        var result = await Run(producer);

        Assert.True(result.Reprocessed >= 2); // other tests of this class may have left requeued messages in the topic
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RecommendationDbContext>();
        Assert.Equal(1, await db.ProcessedChunks.CountAsync(c => c.JobId == jobId));
        var row = await db.OutboxMessages.AsNoTracking().SingleAsync(m => m.AggregateId == jobId.ToString());
        Assert.Equal("batch-chunk-result", row.AggregateType);
        Assert.Equal(EventTypes.BatchChunkCompleted, row.EventType);
        var data = System.Text.Json.JsonDocument.Parse(row.Payload).RootElement;
        Assert.Equal(1, data.GetProperty("results").GetArrayLength());
        Assert.Equal(3, data.GetProperty("errors")[0].GetProperty("line").GetInt32());
    }

    [Theory]
    [InlineData("something.else")]
    [InlineData(null)]
    public async Task Message_with_an_unknown_or_missing_source_topic_is_requeued_not_skipped(string? sourceTopic)
    {
        var key = "unknown-" + Guid.NewGuid();
        var value = CatalogEventHandlerTests.PlanEnvelope(Guid.NewGuid());

        using var producer = new ProducerBuilder<string, string>(new ProducerConfig { BootstrapServers = kafka.BootstrapServers }).Build();
        await producer.ProduceAsync(Topics.RecommendationDlq, DlqReprocessorTests.Dead(key, value, sourceTopic));

        var result = await Run(producer);

        Assert.True(result.FailedAgain >= 1);
        var requeued = ReadDlq(key).Last();
        Assert.Equal(value, requeued.Message.Value);
        Assert.True(int.Parse(Header(requeued, "attempt")) >= 1);
        Assert.Contains("Cannot reprocess", Header(requeued, "error"));
        Assert.Equal(sourceTopic, requeued.Message.Headers.TryGetLastBytes("source-topic", out var b) ? Encoding.UTF8.GetString(b) : null);
    }

    private async Task SeedPlanAndTaxGroup()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RecommendationDbContext>();
        await db.Database.ExecuteSqlRawAsync("DELETE FROM plan_versions");
        await db.Database.ExecuteSqlRawAsync("DELETE FROM tax_group_versions");
        const string tiers = """[{"threshold":null,"pricePerKwh":0.06}]""";
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO plan_versions (plan_id, version_no, name, discount, status, effective_from, tiers, received_at)
            VALUES ({Guid.NewGuid()}, 1, 'Standard', 0.05, 'Active', '2026-01-01', {tiers}::jsonb, now())
            """);
        await db.Database.ExecuteSqlRawAsync("""
            INSERT INTO tax_group_versions (code, version_no, vat, eco_tax, status, effective_from, received_at)
            VALUES ('household', 1, 0.17, 0.005, 'Active', '2026-01-01', now())
            """);
    }

    private async Task<DlqResult> Run(IProducer<string, string> producer)
    {
        using var scope = factory.Services.CreateScope();
        var options = scope.ServiceProvider.GetRequiredService<DbContextOptions<RecommendationDbContext>>();
        return await DlqReprocessor.RunAsync(
            () => new RecommendationDbContext(options),
            new ConsumerConfig { BootstrapServers = kafka.BootstrapServers }, producer, NullLogger.Instance,
            new CancellationTokenSource(TimeSpan.FromSeconds(60)).Token);
    }

    private static string Header(ConsumeResult<string, string> message, string key) =>
        Encoding.UTF8.GetString(message.Message.Headers.GetLastBytes(key));

    // The messages with the key, read with the test's own group so the reprocessor's offsets are not touched.
    private List<ConsumeResult<string, string>> ReadDlq(string key)
    {
        using var consumer = new ConsumerBuilder<string, string>(new ConsumerConfig
        {
            BootstrapServers = kafka.BootstrapServers,
            GroupId = "test-" + Guid.NewGuid(),
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = false
        }).Build();
        consumer.Subscribe(Topics.RecommendationDlq);

        var found = new List<ConsumeResult<string, string>>();
        var deadline = DateTime.UtcNow.AddSeconds(20);
        var lastMessage = DateTime.UtcNow;
        while (DateTime.UtcNow < deadline && (found.Count == 0 || DateTime.UtcNow - lastMessage < TimeSpan.FromSeconds(3)))
        {
            var message = consumer.Consume(TimeSpan.FromMilliseconds(500));
            if (message is null)
                continue;
            lastMessage = DateTime.UtcNow;
            if (message.Message.Key == key)
                found.Add(message);
        }
        return found;
    }
}
