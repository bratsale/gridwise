using Confluent.Kafka;
using GridWise.Batch.Events;

namespace GridWise.Batch.Services;

/// <summary>
/// Consumer of batch.results. The offset is committed after the result was written, so a result is applied at least once;
/// the Dispatched -> Completed update of the chunk makes a repeat harmless.
/// </summary>
public class BatchResultsConsumer(
    IServiceScopeFactory scopeFactory, IConfiguration config, TimeProvider time, ILogger<BatchResultsConsumer> logger)
    : BackgroundService
{
    public const string GroupId = "batch";

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

        consumer.Subscribe(Topics.BatchResults);

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
                    await Task.Delay(MessageProcessing.ErrorWait, time, stoppingToken);
                    continue;
                }

                if (message is null)
                    continue;

                await MessageProcessing.ProcessAsync(consumer, producer, message, async ct =>
                {
                    using var scope = scopeFactory.CreateScope();
                    var outcome = await scope.ServiceProvider.GetRequiredService<BatchResultHandler>()
                        .HandleAsync(message.Message.Value, ct);

                    var level = outcome is ResultOutcome.Applied or ResultOutcome.Duplicate ? LogLevel.Debug : LogLevel.Warning;
                    logger.Log(level, "Result {Key} ({Topic}[{Partition}]@{Offset}): {Outcome}",
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
