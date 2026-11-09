using System.Diagnostics;
using System.Text;
using Confluent.Kafka;
using GridWise.Batch.Events;
using GridWise.Batch.Telemetry;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace GridWise.Batch.Services;

/// <summary>Handling of one Kafka message: the handler, then the offset commit, with the failure rules.</summary>
public static class MessageProcessing
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
        if (IsDatabaseDown(ex))
            return new(false, DatabaseWait);
        return failures >= MaxAttempts
            ? new(true, TimeSpan.Zero)
            : new(false, TimeSpan.FromSeconds(failures));
    }

    public static bool IsDatabaseDown(Exception ex)
        => ex is (NpgsqlException and not PostgresException) or RetryLimitExceededException;

    /// <summary>
    /// Runs the handler, then commits the offset. A database outage is retried without committing, a poison message
    /// or the third failure goes to batch.dlq and is committed.
    /// </summary>
    public static async Task ProcessAsync(
        IConsumer<string, string> consumer, IProducer<string, string> producer, ConsumeResult<string, string> message,
        Func<CancellationToken, Task> handle, TimeProvider time, ILogger logger, CancellationToken ct)
    {
        // The whole handling (retries included) is one span, a child of the publisher's.
        using var activity = Tracing.StartProcess(message);
        var failures = 0;
        while (true)
        {
            try
            {
                await handle(ct);
                consumer.Commit(message);
                return;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                if (!IsDatabaseDown(ex))
                    failures++;

                var decision = Decide(ex, failures);
                if (decision.DeadLetter)
                {
                    logger.LogError(ex, "Message {Topic}[{Partition}]@{Offset} goes to the dead-letter topic.",
                        message.Topic, message.Partition.Value, message.Offset.Value);
                    activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
                    await DeadLetterAsync(producer, message, ex, time, logger, ct);
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
    private static async Task DeadLetterAsync(
        IProducer<string, string> producer, ConsumeResult<string, string> message, Exception error,
        TimeProvider time, ILogger logger, CancellationToken ct)
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
        // The trace goes with the message, so reprocessing it from the dead-letter topic continues the same trace.
        if ((Tracing.CurrentTraceParent() ?? Tracing.TraceParentOf(message)) is { } traceParent)
            dead.Headers.Add(Tracing.TraceParentHeader, Encoding.UTF8.GetBytes(traceParent));

        while (true)
        {
            try
            {
                await producer.ProduceAsync(Topics.BatchDlq, dead, ct);
                GridWiseMetrics.DeadLettered(message.Topic);
                return;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Publishing to {Topic} failed; retrying.", Topics.BatchDlq);
                await Task.Delay(ErrorWait, time, ct);
            }
        }
    }
}
