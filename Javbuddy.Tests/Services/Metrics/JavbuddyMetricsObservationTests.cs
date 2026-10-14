using Javbuddy.Services.Metrics;
using Javbuddy.Tests.TestSupport;

namespace Javbuddy.Tests.Services.Metrics;

public class JavbuddyMetricsObservationTests
{
    [Fact]
    public void WasObservedWithin_IsFalseUntilAGaugeIsScraped()
    {
        using var metrics = new JavbuddyMetrics();
        using var recorder = new MetricsRecorder(metrics);

        Assert.False(metrics.WasObservedWithin(TimeSpan.FromMinutes(5)));

        recorder.SampleGauges();

        Assert.True(metrics.WasObservedWithin(TimeSpan.FromMinutes(5)));
    }

    [Fact]
    public void WasObservedWithin_IsFalseOnceTheWindowHasPassed()
    {
        using var metrics = new JavbuddyMetrics();
        using var recorder = new MetricsRecorder(metrics);
        recorder.SampleGauges();

        Assert.False(metrics.WasObservedWithin(TimeSpan.Zero));
    }
}
