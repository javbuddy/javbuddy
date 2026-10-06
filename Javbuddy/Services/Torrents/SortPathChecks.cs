using System.Text;
using Javbuddy.Services.Javinizer;

namespace Javbuddy.Services.Torrents;

/// <summary>Flags organize-preview names that are too long for common filesystems (255 bytes per
/// name), which long Japanese titles hit at about 85 characters.</summary>
public static class SortPathChecks
{
    public const int MaxSegmentBytes = 255;

    public static IReadOnlyList<string> FindOverlongSegments(OrganizePreviewResponseDto preview)
    {
        var names = new List<string?> { preview.FolderName, preview.FileName };
        names.AddRange((preview.VideoFiles ?? []).Select(Path.GetFileName));
        return names
            .OfType<string>()
            .Where(n => Encoding.UTF8.GetByteCount(n) > MaxSegmentBytes)
            .Distinct()
            .ToList();
    }
}
