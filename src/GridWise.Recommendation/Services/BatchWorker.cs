using Confluent.Kafka;
using GridWise.Recommendation.Events;

namespace GridWise.Recommendation.Services;

/// <summary>
/// Consumer of batch.chunks for the worker mode (<c>dotnet GridWise.Recommendation.dll worker</c>). The offset is committed after the
/// chunk was written, so a chunk is processed at least once; processed_chunks makes the repeat harmless.
/// </summary>
public class BatchWorker(
    IServiceScopeFactory scopeFactory, IConfiguration config, TimeProvider time, ILogger<BatchWorker> logger) : BackgroundService
{
    public const string GroupId = "batch-workers";

    // How fast a dead worker is noticed. The broker waits this long without a heartbeat before it takes the partitions of a worker
    // away and gives them to the others (the librdkafka default is 45 s, so a killed worker held up its chunks for that long).
    // The trade-off: a worker that stalls for longer than this (a long GC pause, a starved or frozen container, a network hiccup)
    // is also taken for dead. That triggers a rebalance, and a chunk it was working on is delivered again to another worker;
    // processed_chunks makes that harmless, it only costs some work. Heartbeats come from a background thread, so a slow chunk
    // alone does not trigger it. Both values can be set with Kafka:SessionTimeoutMs and Kafka:HeartbeatIntervalMs.
    public const int DefaultSessionTimeoutMs = 10_000;
    public const int DefaultHeartbeatIntervalMs = 3_000;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Yield();

        var bootstrapServers = config["Kafka:BootstrapServers"];
        using var consumer = new ConsumerBuilder<string, string>(new ConsumerConfig
        {
            BootstrapServers = bootstrapServers,
            GroupId = GroupId,
            SessionTimeoutMs = config.GetValue("Kafka:SessionTimeoutMs", DefaultSessionTimeoutMs),
            HeartbeatIntervalMs = config.GetValue("Kafka:HeartbeatIntervalMs", DefaultHeartbeatIntervalMs),
            EnableAutoCommit = false,
            AutoOffsetReset = AutoOffsetReset.Earliest
        }).Build();
        using var producer = new ProducerBuilder<string, string>(new ProducerConfig
        {
            BootstrapServers = bootstrapServers,
            Acks = Acks.All,
            EnableIdempotence = true,
            MessageTimeoutMs = 10000
        }).Build();

        consumer.Subscribe(Topics.BatchChunks);

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                ConsumeResult<string, string>? message;
                try
                {
                    message = consumer.Consume(TimeSpan.FromMilliseconds(500));
                }
                catch (ConsumeException ex)
                {
                    logger.LogWarning(ex, "Consuming from Kafka failed.");
                    await Task.Delay(CatalogConsumer.ErrorWait, time, stoppingToken);
                    continue;
                }

                if (message is null)
                    continue;

                await MessageProcessing.ProcessAsync(consumer, producer, message, async ct =>
                {
                    using var scope = scopeFactory.CreateScope();
                    var outcome = await scope.ServiceProvider.GetRequiredService<BatchChunkProcessor>()
                        .HandleAsync(message.Message.Value, ct);
                    logger.LogInformation("Chunk {Key} ({Topic}[{Partition}]@{Offset}): {Outcome}",
                        message.Message.Key, message.Topic, message.Partition.Value, message.Offset.Value, outcome);
                }, time, logger, stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            consumer.Close();
            producer.Flush(TimeSpan.FromSeconds(5));
        }
    }
}
