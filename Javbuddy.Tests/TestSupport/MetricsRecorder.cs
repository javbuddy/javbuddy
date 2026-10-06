using System.Diagnostics.Metrics;
using Javbuddy.Services.Metrics;

namespace Javbuddy.Tests.TestSupport;

/// <summary>Captures measurements recorded on one specific JavbuddyMetrics instance's meter during
/// a test, via MeterListener — the same subscription mechanism OpenTelemetry itself uses, so tests
/// exercise the real instrument-recording path rather than asserting on JavbuddyMetrics' private
/// state. Scoped to a single instance (by reference, not by meter name) because tests routinely
/// construct more than one JavbuddyMetrics in the same process, all sharing the "Javbuddy" name —
/// a name-based filter would pick up leftover instruments from an unrelated test's instance.</summary>
public sealed class MetricsRecorder : IDisposable
{
    public sealed record Measurement(string InstrumentName, object? Value, IReadOnlyDictionary<string, object?> Tags);

    private readonly MeterListener listener;
    private readonly List<Measurement> measurements = [];

    public MetricsRecorder(JavbuddyMetrics metrics)
    {
        listener = new MeterListener
        {
            InstrumentPublished = (instrument, l) =>
            {
                if (instrument.Meter == metrics.Meter) l.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) => Record(instrument.Name, value, tags));
        listener.SetMeasurementEventCallback<double>((instrument, value, tags, _) => Record(instrument.Name, value, tags));
        listener.SetMeasurementEventCallback<int>((instrument, value, tags, _) => Record(instrument.Name, value, tags));
        listener.Start();
    }

    public IReadOnlyList<Measurement> Measurements
    {
        get
        {
            lock (measurements) return [.. measurements];
        }
    }

    /// <summary>Pulls the current value of every ObservableGauge onto the listener's callbacks —
    /// unlike a Counter/Histogram, a gauge's callback only runs when something asks, same as a
    /// real Prometheus scrape would trigger.</summary>
    public void SampleGauges() => listener.RecordObservableInstruments();

    private void Record(string name, object? value, ReadOnlySpan<KeyValuePair<string, object?>> tags)
    {
        var tagDict = new Dictionary<string, object?>();
        foreach (var tag in tags) tagDict[tag.Key] = tag.Value;
        lock (measurements) measurements.Add(new Measurement(name, value, tagDict));
    }

    public void Dispose() => listener.Dispose();
}
