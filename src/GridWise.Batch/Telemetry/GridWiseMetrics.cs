using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using Confluent.Kafka;

namespace GridWise.Batch.Telemetry;

/// <summary>
/// The service's own metrics, one Meter. The gauges report values that the background loops store here (the outbox publisher,
/// the consumers), so reading a gauge never touches the database or Kafka.
/// </summary>
public static class GridWiseMetrics
{
    public const string MeterName = "GridWise.Batch";
    private const string Service = "batch";

    public static readonly Meter Meter = new(MeterName);

    // Nothing is reported until a publisher has counted: a process without an outbox publisher (the workers) has no outbox gauges.
    private static volatile bool _outboxMeasured;
    private static long _outboxPending;
    private static double _outboxOldestAgeSeconds;

    private static readonly ConcurrentDictionary<(string Group, string Topic, int Partition), long> ConsumerLag = new();

    private static readonly Counter<long> DeadLetters = Meter.CreateCounter<long>(
        "gridwise.dlq.messages", "{message}", "Messages sent to a dead-letter topic.");

    private static readonly Counter<long> BatchRows = Meter.CreateCounter<long>(
        "gridwise.batch.rows", "{row}", "Rows of batch jobs whose result was stored, by outcome (processed or failed).");

    static GridWiseMetrics()
    {
        Meter.CreateObservableGauge("gridwise.outbox.pending",
            () => _outboxMeasured ? [new Measurement<long>(Interlocked.Read(ref _outboxPending))] : Array.Empty<Measurement<long>>(),
            "{row}", "Outbox rows not yet published to Kafka.");
        Meter.CreateObservableGauge("gridwise.outbox.oldest_age",
            () => _outboxMeasured ? [new Measurement<double>(Volatile.Read(ref _outboxOldestAgeSeconds))] : Array.Empty<Measurement<double>>(),
            "s", "Age of the oldest unpublished outbox row; 0 when there is none.");
        Meter.CreateObservableGauge("gridwise.consumer.lag", () => ConsumerLag.Select(e => new Measurement<long>(e.Value,
                new("consumer_group", e.Key.Group), new("topic", e.Key.Topic), new("partition", e.Key.Partition))),
            "{message}", "Messages in a partition that the consumer has not read yet (high watermark - position).");
    }

    /// <summary>Called by the outbox publisher loop with what it just counted.</summary>
    public static void RecordOutboxBacklog(int pending, DateTimeOffset? oldestOccurredAt, DateTimeOffset now)
    {
        Interlocked.Exchange(ref _outboxPending, pending);
        Volatile.Write(ref _outboxOldestAgeSeconds, oldestOccurredAt is { } oldest ? Math.Max(0, (now - oldest).TotalSeconds) : 0);
        _outboxMeasured = true;
    }

    /// <summary>
    /// Called by a consumer loop every few seconds: for each assigned partition, the high watermark (as last seen by the consumer, no
    /// broker call) minus the position (the next offset it will read). A partition it has not read from since the assignment has no
    /// position yet; its committed offset is used instead (one broker call). Partitions that were taken away stop being reported.
    /// </summary>
    public static void RecordConsumerLag(string group, IConsumer<string, string> consumer)
    {
        var assigned = consumer.Assignment;
        foreach (var key in ConsumerLag.Keys.Where(k => k.Group == group))
        {
            if (!assigned.Any(tp => tp.Topic == key.Topic && tp.Partition.Value == key.Partition))
                ConsumerLag.TryRemove(key, out _);
        }

        var positions = assigned.ToDictionary(tp => tp, consumer.Position);

        // Not read from since the assignment (an idle group after a restart): its committed offset is where it will start.
        var unread = positions.Where(p => p.Value == Offset.Unset).Select(p => p.Key).ToList();
        if (unread.Count > 0)
        {
            foreach (var committed in consumer.Committed(unread, TimeSpan.FromSeconds(2)))
                positions[committed.TopicPartition] = committed.Offset;
        }

        foreach (var (tp, position) in positions)
        {
            var high = consumer.GetWatermarkOffsets(tp).High;
            if (position == Offset.Unset || high == Offset.Unset)
                continue; // never committed, or no fetch yet: unknown, not 0
            ConsumerLag[(group, tp.Topic, tp.Partition.Value)] = Math.Max(0, high.Value - position.Value);
        }
    }

    public static void DeadLettered(string sourceTopic)
        => DeadLetters.Add(1, new("service", Service), new("source_topic", sourceTopic));

    /// <summary>
    /// Starts the DLQ series of these source topics at 0. Prometheus sees no increase in a series whose first sample is already 1,
    /// so without this the first dead letter after a start would not count for the "Dead letters" alert.
    /// </summary>
    public static void InitializeDeadLetters(params string[] sourceTopics)
    {
        foreach (var topic in sourceTopics)
            DeadLetters.Add(0, new("service", Service), new("source_topic", topic));
    }

    /// <summary>Starts both row series at 0, for the same reason as <see cref="InitializeDeadLetters"/>: the first job after a start counts.</summary>
    public static void InitializeBatchRows() => BatchRowsStored(0, 0);

    public static void BatchRowsStored(int processed, int failed)
    {
        BatchRows.Add(processed, new KeyValuePair<string, object?>("outcome", "processed"));
        BatchRows.Add(failed, new KeyValuePair<string, object?>("outcome", "failed"));
    }
}
