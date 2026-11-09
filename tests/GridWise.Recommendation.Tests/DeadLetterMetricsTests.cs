using System.Diagnostics.Metrics;
using GridWise.Recommendation.Telemetry;

namespace GridWise.Recommendation.Tests;

public class DeadLetterMetricsTests
{
    private record Measured(long Value, string? Service, string? SourceTopic);

    // Collects the DLQ counter's measurements for one source topic (unique per test, so tests running at the same time do not mix).
    private static List<Measured> Listen(string sourceTopic, Action act)
    {
        var measured = new List<Measured>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == GridWiseMetrics.MeterName && instrument.Name == "gridwise.dlq.messages")
                l.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<long>((_, value, tags, _) =>
        {
            var all = tags.ToArray().ToDictionary(t => t.Key, t => t.Value as string);
            if (all.GetValueOrDefault("source_topic") == sourceTopic)
                lock (measured) measured.Add(new(value, all.GetValueOrDefault("service"), all.GetValueOrDefault("source_topic")));
        });
        listener.Start();
        act();
        return measured;
    }

    [Fact]
    public void A_dead_letter_adds_one_with_the_service_and_the_source_topic()
    {
        var topic = $"test.{Guid.NewGuid():N}";

        var measured = Listen(topic, () => GridWiseMetrics.DeadLettered(topic));

        Assert.Equal([new Measured(1, "recommendation", topic)], measured);
    }

    [Fact]
    public void Initializing_starts_each_source_topic_at_zero()
    {
        var topic = $"test.{Guid.NewGuid():N}";

        var measured = Listen(topic, () => GridWiseMetrics.InitializeDeadLetters(topic));

        Assert.Equal([new Measured(0, "recommendation", topic)], measured);
    }
}
