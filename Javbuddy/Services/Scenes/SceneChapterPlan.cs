using System.Globalization;
using System.Text;
using Javbuddy.Services.VrMerge;

namespace Javbuddy.Services.Scenes;

/// <summary>One chapter to write. Title is null for a gap between scenes.</summary>
public sealed record PlannedChapter(double StartSeconds, double EndSeconds, string? Title)
{
    public bool IsGap => Title is null;
}

/// <summary>Turns a movie's scenes into the chapter list written into its video file:
/// chapters must cover the whole file, so every gap — before the first scene, between an ended scene
/// and the next, after the last — becomes an untitled filler chapter. Pure, so it's testable
/// without ffmpeg.</summary>
public static class SceneChapterPlan
{
    // Shorter slivers (rounding between a scene's end and the next start) aren't worth a chapter.
    private const double MinGapSeconds = 0.5;

    public static IReadOnlyList<PlannedChapter> Build(IReadOnlyList<ResolvedScene> scenes, double fileDurationSeconds)
    {
        var chapters = new List<PlannedChapter>();
        var cursor = 0.0;
        foreach (var resolved in scenes.OrderBy(r => r.Scene.StartSeconds))
        {
            var start = Math.Min(resolved.Scene.StartSeconds, fileDurationSeconds);
            var end = Math.Min(resolved.EffectiveEndSeconds ?? fileDurationSeconds, fileDurationSeconds);
            if (end <= start) continue;

            if (start - cursor >= MinGapSeconds)
            {
                chapters.Add(new PlannedChapter(cursor, start, null));
            }
            else if (chapters.Count > 0)
            {
                // Close a sub-MinGap sliver by stretching the previous chapter to this start.
                chapters[^1] = chapters[^1] with { EndSeconds = start };
            }
            else
            {
                start = 0;
            }

            chapters.Add(new PlannedChapter(start, end, resolved.DisplayTitle));
            cursor = end;
        }

        if (fileDurationSeconds - cursor >= MinGapSeconds)
        {
            chapters.Add(new PlannedChapter(cursor, fileDurationSeconds, null));
        }
        else if (chapters.Count > 0)
        {
            chapters[^1] = chapters[^1] with { EndSeconds = fileDurationSeconds };
        }

        return chapters;
    }

    /// <summary>FFMETADATA1 text for the chapters, in milliseconds. ffmpeg's MP4 muxer drops an
    /// untitled chapter (and garbles the rest), so for MP4 a gap gets a single-space title that
    /// displays blank; Matroska keeps gaps truly untitled.</summary>
    public static string ToFfMetadata(IReadOnlyList<PlannedChapter> chapters, bool blankTitleForGaps)
    {
        var sb = new StringBuilder(";FFMETADATA1\n");
        foreach (var chapter in chapters)
        {
            sb.Append("[CHAPTER]\n");
            sb.Append("TIMEBASE=1/1000\n");
            sb.Append(CultureInfo.InvariantCulture, $"START={(long)Math.Round(chapter.StartSeconds * 1000)}\n");
            sb.Append(CultureInfo.InvariantCulture, $"END={(long)Math.Round(chapter.EndSeconds * 1000)}\n");
            if (chapter.Title is { } title)
            {
                sb.Append($"title={VrPartDetector.EscapeFfMetadataValue(title)}\n");
            }
            else if (blankTitleForGaps)
            {
                sb.Append("title= \n");
            }
        }
        return sb.ToString();
    }

    /// <summary>MP4-family containers (whose muxer can't hold untitled chapters).</summary>
    public static bool IsMp4Family(string path) =>
        Path.GetExtension(path).ToLowerInvariant() is ".mp4" or ".m4v" or ".mov";
}
