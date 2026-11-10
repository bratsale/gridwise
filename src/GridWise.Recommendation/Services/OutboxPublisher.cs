using System.Text.Json;
using Confluent.Kafka;
using GridWise.Recommendation.Data;
using GridWise.Recommendation.Events;
using GridWise.Recommendation.Telemetry;
using Microsoft.EntityFrameworkCore;

namespace GridWise.Recommendation.Services;

public class OutboxPublisher(
    IServiceScopeFactory scopeFactory, IConfiguration config, TimeProvider time, ILogger<OutboxPublisher> logger)
    : BackgroundService
{
    private const int BatchSize = 100;
    private static readonly TimeSpan IdleWait = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan ErrorWait = TimeSpan.FromSeconds(5);

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var producer = new ProducerBuilder<string, string>(new ProducerConfig
        {
            BootstrapServers = config["Kafka:BootstrapServers"],
            Acks = Acks.All,
            EnableIdempotence = true,
            MessageTimeoutMs = 10000
        }.WithKafkaCredentials(config)).Build();

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var wait = IdleWait;
                try
                {
                    using var scope = scopeFactory.CreateScope();
                    var db = scope.ServiceProvider.GetRequiredService<RecommendationDbContext>();

                    // Counted before publishing, so the gauges also move when Kafka is down and publishing fails.
                    await MeasureBacklogAsync(db, time, stoppingToken);
                    var published = await PublishPendingAsync(db, producer, time, BatchSize, stoppingToken, logger);
                    if (published > 0)
                        wait = TimeSpan.Zero;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogWarning(ex, "Outbox publishing failed.");
                    wait = ErrorWait;
                }

                if (wait > TimeSpan.Zero)
                    await Task.Delay(wait, time, stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            producer.Flush(TimeSpan.FromSeconds(5));
        }
    }

    /// <summary>Counts the unpublished rows and finds the oldest, for the outbox gauges (two queries on the partial index).</summary>
    public static async Task MeasureBacklogAsync(RecommendationDbContext db, TimeProvider time, CancellationToken ct)
    {
        var unpublished = db.OutboxMessages.Where(m => m.PublishedAt == null);
        var pending = await unpublished.CountAsync(ct);
        var oldest = pending == 0 ? null : await unpublished.MinAsync(m => (DateTimeOffset?)m.OccurredAt, ct);
        GridWiseMetrics.RecordOutboxBacklog(pending, oldest, time.GetUtcNow());
    }

    public static async Task<int> PublishPendingAsync(
        RecommendationDbContext db, IProducer<string, string> producer, TimeProvider time, int batchSize,
        CancellationToken ct, ILogger? logger = null)
    {
        var rows = await db.OutboxMessages
            .Where(m => m.PublishedAt == null)
            .OrderBy(m => m.OccurredAt).ThenBy(m => m.Id)
            .Take(batchSize)
            .ToListAsync(ct);

        var published = 0;
        foreach (var row in rows)
        {
            var topic = row.AggregateType switch
            {
                "recommendation" => Topics.RecommendationEvents,
                "batch-chunk-result" => Topics.BatchResults,
                _ => null
            };
            if (topic is null)
            {
                logger?.LogError(
                    "Outbox message {Id} has unknown aggregate type {AggregateType}; stopping the batch.",
                    row.Id, row.AggregateType);
                break;
            }

            var envelope = new EventEnvelope(
                row.Id, row.EventType, row.EventVersion, row.OccurredAt, row.CorrelationId,
                JsonSerializer.Deserialize<JsonElement>(row.Payload, Json));

            // A child of the code that wrote the row; the Kafka message carries this span's context for the consumer.
            using var publish = Tracing.StartPublish(row.TraceParent, topic, row.Id);
            await producer.ProduceAsync(
                topic,
                new Message<string, string>
                {
                    Key = row.AggregateId,
                    Value = JsonSerializer.Serialize(envelope, Json),
                    Headers = Tracing.HeadersFor(publish, row.TraceParent)
                },
                ct);

            row.PublishedAt = time.GetUtcNow();
            await db.SaveChangesAsync(ct);
            published++;
        }

        return published;
    }
}
