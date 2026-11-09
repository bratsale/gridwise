using System.Collections.Concurrent;
using System.Diagnostics;

namespace GridWise.Catalog.IntegrationTests;

/// <summary>
/// For one test: starts a known root span (so the test has a trace id of its own) and listens only to spans of that trace.
/// Tests run in parallel in one process, and the spans of the others are neither sampled nor kept here. The code under test runs
/// inside the root span (Activity.Current flows with the test), so what it starts is a child; a message or request that must carry
/// the trace uses <see cref="TraceParent"/>.
/// </summary>
public sealed class TraceCapture : IDisposable
{
    private static readonly ActivitySource TestSource = new("GridWise.IntegrationTests");

    private readonly ActivityListener _listener;
    private readonly ConcurrentQueue<Activity> _stopped = new();
    private readonly Activity _root;

    public TraceCapture()
    {
        var traceId = ActivityTraceId.CreateRandom();
        _listener = new ActivityListener
        {
            ShouldListenTo = _ => true,
            // Only spans whose parent belongs to this trace are created; every other span in the process gets nothing from this listener.
            Sample = (ref ActivityCreationOptions<ActivityContext> options)
                => options.Parent.TraceId == traceId ? ActivitySamplingResult.AllDataAndRecorded : ActivitySamplingResult.None,
            ActivityStopped = activity =>
            {
                if (activity.TraceId == traceId)
                    _stopped.Enqueue(activity);
            }
        };
        ActivitySource.AddActivityListener(_listener);

        _root = TestSource.StartActivity(
            "test", ActivityKind.Internal,
            new ActivityContext(traceId, ActivitySpanId.CreateRandom(), ActivityTraceFlags.Recorded))!;
    }

    /// <summary>The trace id of this test, as hex.</summary>
    public string TraceId => _root.TraceId.ToHexString();

    /// <summary>The W3C traceparent of the root span: the header value for "a message or request that belongs to this test's trace".</summary>
    public string TraceParent => _root.Id!;

    /// <summary>The spans of this test's trace that have stopped.</summary>
    public IReadOnlyCollection<Activity> Stopped => _stopped.ToArray();

    public void Dispose()
    {
        _root.Dispose();
        _listener.Dispose();
    }
}
