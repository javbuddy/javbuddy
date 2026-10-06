using System.Globalization;
using Javbuddy.Models;

namespace Javbuddy.Services.Movies;

/// <summary>Pure, static helpers for discovering local movie video files, parsing version tags,
/// selecting primary versions, and formatting frame rates.</summary>
public static class MovieVersionParser
{
    public static readonly string[] VideoExtensions = [".mp4", ".mkv", ".avi", ".m4v", ".wmv", ".mov", ".ts", ".webm"];

    /// <summary>Infix VideoRepairService puts in its temporary remux output names
    /// ("MIDE-400.repairing.&lt;guid&gt;.mp4"); such files are never real movie versions.</summary>
    public const string RepairStagingMarker = ".repairing.";

    private static readonly char[] TrimChars = ['-', '_', '.', ' ', '[', ']', '(', ')'];

    /// <summary>Discovers all video files in a movie's folder, excluding sidecar trailers and
    /// in-progress video repair staging files.</summary>
    public static List<string> FindVideoFiles(string folder)
    {
        try
        {
            return Directory.EnumerateFiles(folder)
                .Where(f => VideoExtensions.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase))
                .Where(f => !Path.GetFileNameWithoutExtension(f).EndsWith("-trailer", StringComparison.OrdinalIgnoreCase))
                .Where(f => !Path.GetFileName(f).Contains(RepairStagingMarker, StringComparison.OrdinalIgnoreCase))
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch (IOException)
        {
            return [];
        }
        catch (UnauthorizedAccessException)
        {
            return [];
        }
    }

    /// <summary>Extracts a clean version tag from a video file's name and its movie release code
    /// (e.g. "MIDE-400-RIFE-3.1.webm" -> "RIFE-3.1", "MIDE-400.mkv" -> "Original"). VR/3D format
    /// tokens are left out, since the format is shown on its own:
    /// "SIVR-059.3d.hsbs.mp4" -> "Original", "SIVR-059-4K_180_sbs.mp4" -> "4K".</summary>
    public static string ExtractVersionTag(string fileName, string? movieCode)
    {
        var tag = ExtractRawVersionTag(fileName, movieCode);
        return tag == "Original" ? tag : VrFormat.StripFormatTokens(tag) ?? "Original";
    }

    private static string ExtractRawVersionTag(string fileName, string? movieCode)
    {
        var stem = Path.GetFileNameWithoutExtension(fileName).Trim();
        if (string.IsNullOrWhiteSpace(stem))
        {
            return "Original";
        }

        if (!string.IsNullOrWhiteSpace(movieCode))
        {
            var code = movieCode.Trim();

            // 1. Exact match with code
            if (string.Equals(stem, code, StringComparison.OrdinalIgnoreCase))
            {
                return "Original";
            }

            // 2. Starts with code prefix (e.g. "MIDE-400-RIFE-3.1", "MIDE-400 - 4K", "MIDE-400 [1080p]")
            if (stem.StartsWith(code, StringComparison.OrdinalIgnoreCase))
            {
                var remainder = stem[code.Length..].Trim(TrimChars);
                if (!string.IsNullOrWhiteSpace(remainder))
                {
                    return remainder;
                }
                return "Original";
            }

            // 3. Normalized code check (e.g. "MIDE400-RIFE" for code "MIDE-400")
            var unhyphenated = code.Replace("-", "").Replace("_", "");
            if (stem.StartsWith(unhyphenated, StringComparison.OrdinalIgnoreCase))
            {
                var remainder = stem[unhyphenated.Length..].Trim(TrimChars);
                if (!string.IsNullOrWhiteSpace(remainder))
                {
                    return remainder;
                }
                return "Original";
            }
        }

        var cleaned = stem.Trim(TrimChars);
        return !string.IsNullOrWhiteSpace(cleaned) ? cleaned : "Original";
    }

    /// <summary>Determines which video file is the primary/default version among candidates.</summary>
    public static MovieFile? DeterminePrimaryFile(IEnumerable<MovieFile> files, string? movieCode)
    {
        var list = files.ToList();
        if (list.Count == 0) return null;

        // 0. The version the user picked
        var pinned = list.FirstOrDefault(f => f.IsPrimaryPinned);
        if (pinned is not null) return pinned;

        // 1. Exact code match in file name
        if (!string.IsNullOrWhiteSpace(movieCode))
        {
            var exact = list.FirstOrDefault(f =>
                string.Equals(Path.GetFileNameWithoutExtension(f.FileName), movieCode.Trim(), StringComparison.OrdinalIgnoreCase));
            if (exact is not null) return exact;
        }

        // 2. File tagged as "Original" or "Default"
        var original = list.FirstOrDefault(f =>
            string.Equals(f.VersionTag, "Original", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(f.VersionTag, "Default", StringComparison.OrdinalIgnoreCase));
        if (original is not null) return original;

        // 3. Highest resolution, then largest file size, then first by file name
        return list
            .OrderByDescending(f => (long)(f.Width ?? 0) * (f.Height ?? 0))
            .ThenByDescending(f => f.FileSizeBytes)
            .ThenBy(f => f.FileName, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }

    /// <summary>Formats a frame rate value cleanly (e.g. 60 fps, 29.97 fps, 24 fps).</summary>
    public static string? FormatFps(double? fps)
    {
        if (fps is null or <= 0) return null;

        var val = fps.Value;
        if (val == Math.Floor(val))
        {
            return $"{val:0} fps";
        }

        return $"{val.ToString("0.##", CultureInfo.InvariantCulture)} fps";
    }

    /// <summary>A version's one-line label for the play pickers, e.g.
    /// "RIFE-3.1 · 1080p · 60 fps · 4.2 GB"; parts that weren't probed are left out.</summary>
    public static string FormatVersionLabel(MovieFile file) =>
        string.Join(" · ", new[] { file.VersionTag, file.ResolutionDisplay, FormatFps(file.FrameRate), file.FileSizeBytes > 0 ? file.FileSizeDisplay : null }
            .Where(part => !string.IsNullOrWhiteSpace(part)));
}
