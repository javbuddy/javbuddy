namespace Javbuddy.Services.Scenes;

/// <summary>Pure helpers for apexes: time validation, playback window, clip shifting and inherited actors — no I/O.</summary>
public static class ApexRanges
{
    /// <summary>Returns an error message, or null when the time is valid: finite, <c>≥ 0</c>, and
    /// <c>≤ duration</c> when the duration is known.</summary>
    public static string? ValidateTime(double seconds, double? durationSeconds)
    {
        if (!double.IsFinite(seconds)) return "The time must be a valid time.";
        if (seconds < 0) return "The time can't be negative.";
        if (durationSeconds is { } duration && seconds > duration) return "The time can't be past the end of the movie.";
        return null;
    }

    /// <summary>Default seconds playback starts before an apex when one is clicked, to see the lead-up,
    /// and runs on past it in its clip; Settings → UI can change both.</summary>
    public const double DefaultLeadInSeconds = 5;
    public const double DefaultTailSeconds = 5;

    /// <summary>Upper bound of a lead-in or tail, in seconds.</summary>
    public const double MaxWindowSeconds = 300;

    /// <summary>Returns an error message, or null when a lead-in/tail pair is valid: each
    /// finite and within [0, <see cref="MaxWindowSeconds"/>], and the tail at least a second so an apex's
    /// clip never ends where it starts. A null side is unset (the default applies) and always valid.</summary>
    public static string? ValidateWindow(double? leadInSeconds, double? tailSeconds)
    {
        if (leadInSeconds is { } lead && (!double.IsFinite(lead) || lead < 0 || lead > MaxWindowSeconds))
            return $"The lead-in must be between 0 and {MaxWindowSeconds:0} seconds.";
        if (tailSeconds is { } tail && (!double.IsFinite(tail) || tail < 1 || tail > MaxWindowSeconds))
            return $"The tail must be between 1 and {MaxWindowSeconds:0} seconds.";
        return null;
    }

    /// <summary>An apex's own lead-in and tail where set, else the defaults'.</summary>
    public static ApexWindow Resolve(double? ownLeadInSeconds, double? ownTailSeconds, ApexWindow defaults) =>
        new(ownLeadInSeconds ?? defaults.LeadInSeconds, ownTailSeconds ?? defaults.TailSeconds);

    /// <summary>The lead-in (or, with <paramref name="tail"/>, the tail) that puts an apex's playback edge at
    /// the playhead, to the millisecond; null when the playhead is on the wrong side of it.</summary>
    public static double? OffsetFromPlayhead(double apexSeconds, double playheadSeconds, bool tail)
    {
        var offset = Math.Round(tail ? playheadSeconds - apexSeconds : apexSeconds - playheadSeconds, 3);
        return offset >= 0 ? offset : null;
    }

    /// <summary>Where playback starts for an apex: its lead-in before it, but not before 0.</summary>
    public static double StartFor(double apexSeconds, double leadInSeconds) => Math.Max(apexSeconds - leadInSeconds, 0);

    /// <summary>Where playback starts for an apex, by its effective window.</summary>
    public static double StartFor(ApexItem apex) => StartFor(apex.Seconds, apex.Window.LeadInSeconds);

    /// <summary>The clip the Scenes wall plays for an apex: from <see cref="StartFor(double, double)"/>
    /// to the window's tail after it, cut at the movie's end when that's known.</summary>
    public static (double Start, double End) ClipFor(double apexSeconds, ApexWindow window, double? durationSeconds)
    {
        var end = apexSeconds + window.TailSeconds;
        return (StartFor(apexSeconds, window.LeadInSeconds), durationSeconds is > 0 and var duration ? Math.Min(end, duration) : end);
    }

    /// <summary>Default and upper bound of the countdown length, in seconds.</summary>
    public const int DefaultCountdownSeconds = 10;
    public const int MaxCountdownSeconds = 60;

    /// <summary>The number the apex countdown shows at <paramref name="currentSeconds"/>: whole seconds left
    /// (rounded up) to the next apex ahead, when it's at most <paramref name="lengthSeconds"/> away; null
    /// when there's none, or the countdown is off (<paramref name="lengthSeconds"/> ≤ 0). An apex at or
    /// before the playhead has passed. ApexCountdown.razor.js mirrors this.</summary>
    public static int? CountdownFor(IEnumerable<double> apexSeconds, double currentSeconds, int lengthSeconds)
    {
        if (lengthSeconds <= 0) return null;
        double? next = null;
        foreach (var seconds in apexSeconds)
        {
            if (seconds > currentSeconds && (next is null || seconds < next)) next = seconds;
        }
        return next is { } apex && apex - currentSeconds <= lengthSeconds ? (int)Math.Ceiling(apex - currentSeconds) : null;
    }

    /// <summary>The apexes inside a clip of [start, end), shifted so the clip starts at 0, for the
    /// clip player's track.</summary>
    public static IReadOnlyList<ApexItem> WithinClip(IReadOnlyList<ApexItem> apexes, double startSeconds, double endSeconds) =>
        apexes.Where(a => a.Seconds >= startSeconds && a.Seconds < endSeconds)
            .Select(a => a with { Seconds = a.Seconds - startSeconds })
            .ToList();

    /// <summary>The apex whose tags a new apex at <paramref name="newSeconds"/> is offered:
    /// the latest one strictly before it, or — when none precedes it or the time isn't set — the most
    /// recently created one (highest id). Null when there are no apexes.</summary>
    public static ApexItem? PreviousFor(IReadOnlyList<ApexItem> apexes, double? newSeconds)
    {
        var before = newSeconds is { } seconds
            ? apexes.Where(a => a.Seconds < seconds).MaxBy(a => (a.Seconds, a.Id))
            : null;
        return before ?? apexes.MaxBy(a => a.Id);
    }

    /// <summary>Whether <paramref name="seconds"/> lies in a scene's effective range [start, end); a null
    /// end runs open-ended. Decides the scene an apex rolls its tags up to and inherits actors from.</summary>
    public static bool IsWithin(double startSeconds, double? endSeconds, double seconds) =>
        startSeconds <= seconds && (endSeconds is not { } end || seconds < end);
}

/// <summary>How long an apex plays before and after it: its lead-in and tail, in seconds.</summary>
public readonly record struct ApexWindow(double LeadInSeconds, double TailSeconds)
{
    public static ApexWindow Default { get; } = new(ApexRanges.DefaultLeadInSeconds, ApexRanges.DefaultTailSeconds);
}
