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

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Yield();

        var bootstrapServers = config["Kafka:BootstrapServers"];
        using var consumer = new ConsumerBuilder<string, string>(new ConsumerConfig
        {
            BootstrapServers = bootstrapServers,
            GroupId = GroupId,
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
