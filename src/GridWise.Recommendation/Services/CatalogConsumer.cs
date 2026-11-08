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
    public static readonly TimeSpan ErrorWait = TimeSpan.FromSeconds(5);

    public readonly record struct FailureDecision(bool DeadLetter, TimeSpan Wait);

    // failures = number of failed attempts so far for this message, not counting database outages.
    public static FailureDecision Decide(Exception ex, int failures)
    {
        if (ex is PoisonMessageException)
            return new(true, TimeSpan.Zero);
        if (IsTemporary(ex))
            return new(false, DatabaseWait);
        return failures >= MaxAttempts
            ? new(true, TimeSpan.Zero)
            : new(false, TimeSpan.FromSeconds(failures));
    }

    // Not the message's fault, so it is retried without counting: the database is down, or the data it needs has not arrived yet.
    public static bool IsTemporary(Exception ex)
        => ex is (NpgsqlException and not PostgresException) or RetryLimitExceededException or TemporarilyUnavailableException;

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

    private Task ProcessAsync(
        IConsumer<string, string> consumer, IProducer<string, string> producer,
        ConsumeResult<string, string> message, CancellationToken ct)
        => MessageProcessing.ProcessAsync(consumer, producer, message, async token =>
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<RecommendationDbContext>();

            var outcome = await handler.HandleAsync(db, message.Message.Value, token);
            logger.LogDebug("Message {Topic}[{Partition}]@{Offset}: {Outcome}",
                message.Topic, message.Partition.Value, message.Offset.Value, outcome);
        }, time, logger, ct);
}
