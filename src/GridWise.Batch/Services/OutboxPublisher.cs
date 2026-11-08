using System.Text.Json;
using Confluent.Kafka;
using GridWise.Batch.Data;
using GridWise.Batch.Events;
using Microsoft.EntityFrameworkCore;

namespace GridWise.Batch.Services;

// Assumes a single Batch instance: two publishers would send the same rows twice (consumers deduplicate by event id).
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
        }).Build();

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var wait = IdleWait;
                try
                {
                    using var scope = scopeFactory.CreateScope();
                    var db = scope.ServiceProvider.GetRequiredService<BatchDbContext>();

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

    public static async Task<int> PublishPendingAsync(
        BatchDbContext db, IProducer<string, string> producer, TimeProvider time, int batchSize,
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
                "batch-chunk" => Topics.BatchChunks,
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

            await producer.ProduceAsync(
                topic,
                new Message<string, string> { Key = row.AggregateId, Value = JsonSerializer.Serialize(envelope, Json) },
                ct);

            row.PublishedAt = time.GetUtcNow();
            await db.SaveChangesAsync(ct);
            published++;
        }

        return published;
    }
}
