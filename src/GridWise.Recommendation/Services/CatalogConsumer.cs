using System.Text;
using Confluent.Kafka;
using GridWise.Recommendation.Data;
using GridWise.Recommendation.Events;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace GridWise.Recommendation.Services;

public class CatalogConsumer(
    IServiceScopeFactory scopeFactory, IConfiguration config, CatalogEventHandler handler,
    TimeProvider time, ILogger<CatalogConsumer> logger) : BackgroundService
{
    private const int MaxAttempts = 3;
    private static readonly TimeSpan DatabaseWait = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan ErrorWait = TimeSpan.FromSeconds(5);

    public readonly record struct FailureDecision(bool DeadLetter, TimeSpan Wait);

    // failures = number of failed attempts so far for this message, not counting database outages.
    public static FailureDecision Decide(Exception ex, int failures)
    {
        if (ex is PoisonMessageException)
            return new(true, TimeSpan.Zero);
        if (ex is (NpgsqlException and not PostgresException) or RetryLimitExceededException)
            return new(false, DatabaseWait);
        return failures >= MaxAttempts
            ? new(true, TimeSpan.Zero)
            : new(false, TimeSpan.FromSeconds(failures));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Yield();

        var bootstrapServers = config["Kafka:BootstrapServers"];
        using var consumer = new ConsumerBuilder<string, string>(new ConsumerConfig
        {
            BootstrapServers = bootstrapServers,
            GroupId = config["Kafka:GroupId"],
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

        consumer.Subscribe([Topics.CatalogPlanEvents, Topics.CatalogTaxGroupEvents]);

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
                    await Task.Delay(ErrorWait, time, stoppingToken);
                    continue;
                }

                if (message is not null)
                    await ProcessAsync(consumer, producer, message, stoppingToken);
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

    private async Task ProcessAsync(
        IConsumer<string, string> consumer, IProducer<string, string> producer,
        ConsumeResult<string, string> message, CancellationToken ct)
    {
        var failures = 0;
        while (true)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<RecommendationDbContext>();

                var outcome = await handler.HandleAsync(db, message.Message.Value, ct);
                logger.LogDebug("Message {Topic}[{Partition}]@{Offset}: {Outcome}",
                    message.Topic, message.Partition.Value, message.Offset.Value, outcome);

                consumer.Commit(message);
                return;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                var databaseDown = ex is (NpgsqlException and not PostgresException) or RetryLimitExceededException;
                if (!databaseDown)
                    failures++;

                var decision = Decide(ex, failures);
                if (decision.DeadLetter)
                {
                    logger.LogError(ex, "Message {Topic}[{Partition}]@{Offset} goes to the dead-letter topic.",
                        message.Topic, message.Partition.Value, message.Offset.Value);
                    await DeadLetterAsync(producer, message, ex, ct);
                    consumer.Commit(message);
                    return;
                }

                logger.LogWarning(ex, "Message {Topic}[{Partition}]@{Offset} failed; retrying in {Wait}.",
                    message.Topic, message.Partition.Value, message.Offset.Value, decision.Wait);
                await Task.Delay(decision.Wait, time, ct);
            }
        }
    }

    // The offset is committed only after this succeeds, so keep trying while Kafka is unavailable.
    private async Task DeadLetterAsync(
        IProducer<string, string> producer, ConsumeResult<string, string> message, Exception error, CancellationToken ct)
    {
        var dead = new Message<string, string>
        {
            Key = message.Message.Key,
            Value = message.Message.Value,
            Headers =
            [
                new Header("source-topic", Encoding.UTF8.GetBytes(message.Topic)),
                new Header("source-partition", Encoding.UTF8.GetBytes(message.Partition.Value.ToString())),
                new Header("source-offset", Encoding.UTF8.GetBytes(message.Offset.Value.ToString())),
                new Header("error", Encoding.UTF8.GetBytes(error.Message))
            ]
        };

        while (true)
        {
            try
            {
                await producer.ProduceAsync(Topics.RecommendationDlq, dead, ct);
                return;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Publishing to {Topic} failed; retrying.", Topics.RecommendationDlq);
                await Task.Delay(ErrorWait, time, ct);
            }
        }
    }
}
