using System.Diagnostics;
using System.Text;
using Confluent.Kafka;
using GridWise.Recommendation.Events;
using GridWise.Recommendation.Telemetry;

namespace GridWise.Recommendation.Services;

/// <summary>Not an error of the message: something it needs is not there yet (for example no active plans). The message is retried later without counting an attempt.</summary>
public class TemporarilyUnavailableException(string message) : Exception(message);

/// <summary>Handling of one Kafka message with the failure rules shared by the catalog consumer and the batch worker.</summary>
public static class MessageProcessing
{
    /// <summary>
    /// Runs the handler, then commits the offset. Failures follow <see cref="CatalogConsumer.Decide"/>:
    /// a temporary problem or an outage is retried without committing, a poison message or the third failure goes to the dead-letter topic.
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
                if (!CatalogConsumer.IsTemporary(ex))
                    failures++;

                var decision = CatalogConsumer.Decide(ex, failures);
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
                await producer.ProduceAsync(Topics.RecommendationDlq, dead, ct);
                return;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Publishing to {Topic} failed; retrying.", Topics.RecommendationDlq);
                await Task.Delay(CatalogConsumer.ErrorWait, time, ct);
            }
        }
    }
}
