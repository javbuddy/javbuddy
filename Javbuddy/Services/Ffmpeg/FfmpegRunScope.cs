using System.ComponentModel;
using System.Diagnostics;
using Javbuddy.Services.Metrics;

namespace Javbuddy.Services.Ffmpeg;

/// <summary>Per-run helper handed to a process lifecycle body by <c>FfmpegClient</c>: starts the process at low priority,
/// kills it, and watches its stderr, counting priority failures and kills as they happen and remembering the failure
/// reason for the run's outcome metric. <paramref name="metrics"/> is optional, like on the client.</summary>
public sealed class FfmpegRunScope(string operation, JavbuddyMetrics? metrics, Action<Process>? setPriority = null)
{
    private readonly Action<Process> setPriority = setPriority ?? (p => p.PriorityClass = ProcessPriorityClass.BelowNormal);

    /// <summary>The most specific failure reason seen in the stderr passed to <see cref="Observe"/>, else "other".</summary>
    public string FailureReason { get; private set; } = FfmpegFailureClassifier.Other;

    /// <summary>Starts every ffmpeg/ffprobe run at below-normal priority (nice 10 on Linux): none of them is real-time, and
    /// a decode can saturate every core, which would otherwise starve the Blazor circuits. Set right after start, before
    /// ffmpeg spawns its worker threads, which inherit it. A refused change is counted, not raised: the run just competes
    /// at normal priority.</summary>
    public void StartAtLowPriority(Process process)
    {
        process.Start();
        try
        {
            setPriority(process);
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or PlatformNotSupportedException)
        {
            // Only a nicety (the process may already have exited, or the OS refused).
            metrics?.FfmpegPriorityDowngradeFailures.Add(1);
        }
    }

    /// <summary>Kills the process and its children (a cancelled run must not leave a decode running). Counted only when the
    /// process was still running: killing one that already exited is a silent no-op in .NET, so it is skipped, not counted.</summary>
    public void Kill(Process process)
    {
        try
        {
            if (process.HasExited) return;
            process.Kill(entireProcessTree: true);
            metrics?.FfmpegKills.Add(1, new KeyValuePair<string, object?>("operation", operation));
        }
        catch (InvalidOperationException)
        {
            // Never started, or exited in between.
        }
    }

    /// <summary>Classifies a line (or the whole) of the process's stderr, keeping the most specific reason seen.</summary>
    public void Observe(string? stderr)
    {
        var reason = FfmpegFailureClassifier.Classify(stderr);
        if (FfmpegFailureClassifier.MoreSpecific(reason, FailureReason)) FailureReason = reason;
    }
}
