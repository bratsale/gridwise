using System.Diagnostics.Metrics;
using GridWise.Catalog.Telemetry;

namespace GridWise.Catalog.Tests;

public class OutboxMetricsTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 9, 12, 0, 0, TimeSpan.Zero);

    // Reads the outbox gauges the way an exporter does: a collection calls the gauge callbacks.
    private static Dictionary<string, double> ReadGauges()
    {
        var values = new Dictionary<string, double>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == GridWiseMetrics.MeterName && instrument.Name.StartsWith("gridwise.outbox."))
                l.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<long>((instrument, value, _, _) => values[instrument.Name] = value);
        listener.SetMeasurementEventCallback<double>((instrument, value, _, _) => values[instrument.Name] = value);
        listener.Start();
        listener.RecordObservableInstruments();
        return values;
    }

    [Fact]
    public void Gauges_report_the_latest_backlog()
    {
        GridWiseMetrics.RecordOutboxBacklog(3, Now.AddSeconds(-42.5), Now);

        var values = ReadGauges();

        Assert.Equal(3, values["gridwise.outbox.pending"]);
        Assert.Equal(42.5, values["gridwise.outbox.oldest_age"]);
    }

    [Fact]
    public void An_empty_outbox_reports_zero_rows_and_zero_age()
    {
        GridWiseMetrics.RecordOutboxBacklog(5, Now.AddMinutes(-3), Now);
        GridWiseMetrics.RecordOutboxBacklog(0, null, Now);

        var values = ReadGauges();

        Assert.Equal(0, values["gridwise.outbox.pending"]);
        Assert.Equal(0, values["gridwise.outbox.oldest_age"]);
    }
}
