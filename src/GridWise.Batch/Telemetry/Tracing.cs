using System.Diagnostics;
using System.Text;
using Confluent.Kafka;

namespace GridWise.Batch.Telemetry;

/// <summary>
/// Trace context across the outbox and Kafka. The W3C <c>traceparent</c> of the code that writes an outbox row is stored in the row
/// (same transaction); the publisher continues it in a "publish" span and sends it as a Kafka header; a consumer continues it in a
/// "process" span. So one request, or one batch chunk, is one trace over all the services it touches.
/// </summary>
public static class Tracing
{
    public const string SourceName = "GridWise.Batch";
    public const string TraceParentHeader = "traceparent";

    public static readonly ActivitySource Source = new(SourceName);

    /// <summary>The traceparent to store in an outbox row, or null when nothing is being traced.</summary>
    public static string? CurrentTraceParent()
        => Activity.Current is { IdFormat: ActivityIdFormat.W3C } activity ? activity.Id : null;

    /// <summary>A "publish" span, a child of the context stored in the outbox row.</summary>
    public static Activity? StartPublish(string? traceParent, string topic, Guid messageId)
    {
        var activity = Source.StartActivity("publish", ActivityKind.Producer, ParentOf(traceParent));
        activity?.SetTag("messaging.system", "kafka");
        activity?.SetTag("messaging.destination.name", topic);
        activity?.SetTag("messaging.operation.type", "publish");
        activity?.SetTag("messaging.message.id", messageId.ToString());
        return activity;
    }

    /// <summary>The Kafka headers of a published message: the context of the publish span (or the stored one when nothing is listening).</summary>
    public static Headers? HeadersFor(Activity? publish, string? storedTraceParent)
    {
        var traceParent = publish?.Id ?? storedTraceParent;
        return traceParent is null ? null : new Headers { { TraceParentHeader, Encoding.UTF8.GetBytes(traceParent) } };
    }

    /// <summary>A "process" span, a child of the context in the message's traceparent header.</summary>
    public static Activity? StartProcess(ConsumeResult<string, string> message)
    {
        var activity = Source.StartActivity("process", ActivityKind.Consumer, ParentOf(TraceParentOf(message)));
        activity?.SetTag("messaging.system", "kafka");
        activity?.SetTag("messaging.destination.name", message.Topic);
        activity?.SetTag("messaging.destination.partition.id", message.Partition.Value.ToString());
        activity?.SetTag("messaging.kafka.offset", message.Offset.Value);
        activity?.SetTag("messaging.kafka.message.key", message.Message.Key);
        activity?.SetTag("messaging.operation.type", "process");
        return activity;
    }

    public static string? TraceParentOf(ConsumeResult<string, string> message)
        => message.Message.Headers is { } headers && headers.TryGetLastBytes(TraceParentHeader, out var bytes)
            ? Encoding.UTF8.GetString(bytes)
            : null;

    // No (or an unreadable) traceparent: the default context, which starts a new trace.
    private static ActivityContext ParentOf(string? traceParent)
        => traceParent is not null && ActivityContext.TryParse(traceParent, null, true, out var context) ? context : default;
}
