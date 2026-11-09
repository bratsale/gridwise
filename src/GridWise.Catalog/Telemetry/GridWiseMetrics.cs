using System.Diagnostics.Metrics;

namespace GridWise.Catalog.Telemetry;

/// <summary>
/// The service's own metrics, one Meter. The gauges report values that the background loops store here (the outbox publisher,
/// the consumers), so reading a gauge never touches the database or Kafka.
/// </summary>
public static class GridWiseMetrics
{
    public const string MeterName = "GridWise.Catalog";
    private const string Service = "catalog";

    public static readonly Meter Meter = new(MeterName);

    // Nothing is reported until a publisher has counted: a process without an outbox publisher (the workers) has no outbox gauges.
    private static volatile bool _outboxMeasured;
    private static long _outboxPending;
    private static double _outboxOldestAgeSeconds;

    static GridWiseMetrics()
    {
        Meter.CreateObservableGauge("gridwise.outbox.pending",
            () => _outboxMeasured ? [new Measurement<long>(Interlocked.Read(ref _outboxPending))] : Array.Empty<Measurement<long>>(),
            "{row}", "Outbox rows not yet published to Kafka.");
        Meter.CreateObservableGauge("gridwise.outbox.oldest_age",
            () => _outboxMeasured ? [new Measurement<double>(Volatile.Read(ref _outboxOldestAgeSeconds))] : Array.Empty<Measurement<double>>(),
            "s", "Age of the oldest unpublished outbox row; 0 when there is none.");
    }

    /// <summary>Called by the outbox publisher loop with what it just counted.</summary>
    public static void RecordOutboxBacklog(int pending, DateTimeOffset? oldestOccurredAt, DateTimeOffset now)
    {
        Interlocked.Exchange(ref _outboxPending, pending);
        Volatile.Write(ref _outboxOldestAgeSeconds, oldestOccurredAt is { } oldest ? Math.Max(0, (now - oldest).TotalSeconds) : 0);
        _outboxMeasured = true;
    }
}
