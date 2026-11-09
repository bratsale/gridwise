using System.Collections.Concurrent;
using System.Diagnostics;

namespace GridWise.Batch.IntegrationTests;

/// <summary>Listens to every ActivitySource while it lives (so spans are created) and keeps the spans that have stopped.</summary>
public sealed class TraceCapture : IDisposable
{
    private readonly ActivityListener _listener;
    private readonly ConcurrentQueue<Activity> _stopped = new();

    public TraceCapture()
    {
        _listener = new ActivityListener
        {
            ShouldListenTo = _ => true,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = _stopped.Enqueue
        };
        ActivitySource.AddActivityListener(_listener);
    }

    public IReadOnlyCollection<Activity> Stopped => _stopped.ToArray();

    // A traceparent for a trace that "started elsewhere" (another service), as a header value, with its parts.
    public static (string TraceParent, string TraceId) NewRemoteTrace()
    {
        var traceId = ActivityTraceId.CreateRandom().ToHexString();
        return ($"00-{traceId}-{ActivitySpanId.CreateRandom().ToHexString()}-01", traceId);
    }

    public void Dispose() => _listener.Dispose();
}
