using System.Collections.Concurrent;
using System.Diagnostics;

namespace Javbuddy.Tests.TestSupport;

/// <summary>Records the spans the given sources finish under a test-owned parent span, so tests running in
/// parallel (which share the process-wide ActivitySource listeners) never see each other's spans.</summary>
public sealed class TraceCapture : IDisposable
{
    private const string ParentSourceName = "Javbuddy.Tests.TraceCapture";

    // Matched by name below: the listener sees this source while the field is still being initialized.
    private static readonly ActivitySource ParentSource = new(ParentSourceName);

    private readonly ActivityListener listener;
    private readonly Activity parent;
    private readonly ConcurrentQueue<Activity> stopped = new();

    public TraceCapture(params string[] sourceNames)
    {
        listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == ParentSourceName || sourceNames.Contains(source.Name),
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity =>
            {
                if (activity.Source.Name != ParentSourceName && activity.TraceId == parent!.TraceId) stopped.Enqueue(activity);
            },
        };
        ActivitySource.AddActivityListener(listener);
        parent = ParentSource.StartActivity("test")!;
    }

    public IReadOnlyList<Activity> Spans => [.. stopped];

    public void Dispose()
    {
        parent.Dispose();
        listener.Dispose();
    }
}
