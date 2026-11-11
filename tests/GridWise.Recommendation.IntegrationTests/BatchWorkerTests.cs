using System.Text.Json;
using Confluent.Kafka;
using GridWise.Recommendation.Data;
using GridWise.Recommendation.Events;
using GridWise.Recommendation.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace GridWise.Recommendation.IntegrationTests;

// The "recommendation-empty" collection has its own database, with no plans until a test adds them.
[Collection("recommendation-empty")]
public class BatchWorkerTests(RecommendationFactory factory, KafkaFixture kafka) : IClassFixture<KafkaFixture>
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(40);

    private async Task<(int Processed, int Outbox)> Counts(Guid jobId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RecommendationDbContext>();
        return (
            await db.ProcessedChunks.CountAsync(c => c.JobId == jobId),
            await db.OutboxMessages.CountAsync(m => m.AggregateId == jobId.ToString()));
    }

    [Fact]
    public async Task Without_active_plans_nothing_is_written_and_the_chunk_is_retried_until_plans_arrive()
    {
        var jobId = Guid.NewGuid();
        var json = BatchChunkProcessorTests.Chunk(jobId, 1, "c1,250,household");

        // Directly: the failure is the "temporary" kind, which the consumer retries without counting an attempt or dead-lettering.
        using (var scope = factory.Services.CreateScope())
        {
            var ex = await Assert.ThrowsAsync<TemporarilyUnavailableException>(
                () => scope.ServiceProvider.GetRequiredService<BatchChunkProcessor>().HandleAsync(json, CancellationToken.None));
            Assert.False(CatalogConsumer.Decide(ex, failures: 100).DeadLetter);
        }
        Assert.Equal((0, 0), await Counts(jobId));

        // Through Kafka: the worker keeps the message (no commit) and processes it once the plans exist.
        using var producer = new ProducerBuilder<string, string>(new ProducerConfig { BootstrapServers = kafka.BootstrapServers }).Build();
        await producer.ProduceAsync(Topics.BatchChunks, new Message<string, string> { Key = $"{jobId}:1", Value = json });

        var config = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["Kafka:BootstrapServers"] = kafka.BootstrapServers }).Build();
        var log = new RecordingLogger<BatchWorker>();
        var worker = new BatchWorker(factory.Services.GetRequiredService<IServiceScopeFactory>(), config, TimeProvider.System, log);
        await worker.StartAsync(CancellationToken.None);
        try
        {
            // The worker has read the chunk once it reports that it will retry it (no plans yet): only then does "nothing written" mean something.
            var readBy = DateTime.UtcNow + Timeout;
            while (!log.Messages.Any(m => m.Contains(Topics.BatchChunks) && m.Contains("retrying")))
            {
                Assert.True(DateTime.UtcNow < readBy, "The worker did not read the chunk.");
                await Task.Delay(100);
            }
            Assert.Equal((0, 0), await Counts(jobId));

            using (var scope = factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<RecommendationDbContext>();
                await db.Database.ExecuteSqlRawAsync("""
                    INSERT INTO tax_group_versions (code, version_no, vat, eco_tax, status, effective_from, received_at)
                    VALUES ('household', 1, 0.17, 0.005, 'Active', '2026-01-01', now()) ON CONFLICT DO NOTHING
                    """);
                const string tiers = """[{"threshold":null,"pricePerKwh":0.06}]""";
                await db.Database.ExecuteSqlInterpolatedAsync($"""
                    INSERT INTO plan_versions (plan_id, version_no, name, discount, status, effective_from, tiers, received_at)
                    VALUES ({Guid.NewGuid()}, 1, 'Standard', 0.05, 'Active', '2026-01-01', {tiers}::jsonb, now())
                    """);
            }

            var deadline = DateTime.UtcNow + Timeout;
            while (await Counts(jobId) != (1, 1))
            {
                Assert.True(DateTime.UtcNow < deadline, "The chunk was not processed after the plans arrived.");
                await Task.Delay(500);
            }
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
        }

        // The recommendation OutboxPublisher sends the row to batch.results with the job id as key.
        using (var scope = factory.Services.CreateScope())
        {
            using var publisherProducer = new ProducerBuilder<string, string>(new ProducerConfig
            {
                BootstrapServers = kafka.BootstrapServers, Acks = Acks.All, EnableIdempotence = true
            }).Build();
            while (await OutboxPublisher.PublishPendingAsync(
                scope.ServiceProvider.GetRequiredService<RecommendationDbContext>(),
                publisherProducer, TimeProvider.System, 100, CancellationToken.None) > 0) { }
        }

        using var consumer = new ConsumerBuilder<string, string>(new ConsumerConfig
        {
            BootstrapServers = kafka.BootstrapServers,
            GroupId = Guid.NewGuid().ToString(),
            AutoOffsetReset = AutoOffsetReset.Earliest
        }).Build();
        consumer.Subscribe(Topics.BatchResults);
        ConsumeResult<string, string>? message = null;
        for (var deadline = DateTime.UtcNow + Timeout; message is null && DateTime.UtcNow < deadline;)
        {
            var result = consumer.Consume(TimeSpan.FromMilliseconds(500));
            if (result?.Message.Key == jobId.ToString())
                message = result;
        }

        Assert.NotNull(message);
        var envelope = JsonSerializer.Deserialize<EventEnvelope>(message.Message.Value, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        Assert.Equal(EventTypes.BatchChunkCompleted, envelope.Type);
        Assert.Equal(1, envelope.Data.GetProperty("results").GetArrayLength());
    }
}

/// <summary>Keeps every message the worker logs, so a test can wait for a step instead of sleeping.</summary>
internal sealed class RecordingLogger<T> : ILogger<T>
{
    public System.Collections.Concurrent.ConcurrentQueue<string> Messages { get; } = new();

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        => Messages.Enqueue(formatter(state, exception));
}
