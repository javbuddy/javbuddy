namespace Javbuddy.Services.Trickplay;

/// <summary>The rules for a highlight's own, denser trickplay set: about one sheet's
/// worth of thumbnails over the highlight, but at least a second apart. A highlight long enough that
/// this reaches the movie's own interval gets no set of its own; the movie's serves it as well.</summary>
public static class HighlightTrickplay
{
    /// <summary>One 10x10 sheet.</summary>
    public const int TargetThumbnails = TrickplayGenerator.TilesPerSide * TrickplayGenerator.TilesPerSide;

    public const int MinIntervalSeconds = 1;

    /// <summary>Whole seconds, since ffmpeg's fps filter and the layout's IntervalMs take them
    /// exactly; null when the highlight needs no set of its own.</summary>
    public static int? IntervalSeconds(double lengthSeconds)
    {
        if (!(lengthSeconds > 0)) return null;
        var interval = Math.Max(MinIntervalSeconds, (int)Math.Ceiling(lengthSeconds / TargetThumbnails));
        return interval < TrickplayGenerator.IntervalSeconds ? interval : null;
    }

    /// <summary>The set's identity for the highlight [start, end] of the file with
    /// <paramref name="fileIdentity"/>; null when it needs no set of its own.</summary>
    public static string? Identity(string fileIdentity, double startSeconds, double endSeconds) =>
        IntervalSeconds(endSeconds - startSeconds) is { } interval
            ? TrickplayIdentity.ForClip(fileIdentity, Milliseconds(startSeconds), Milliseconds(endSeconds), interval * 1000)
            : null;

    private static long Milliseconds(double seconds) => (long)Math.Round(seconds * 1000);
}
