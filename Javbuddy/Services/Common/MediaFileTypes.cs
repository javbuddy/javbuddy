namespace Javbuddy.Services.Common;

/// <summary>File-extension sets shared across the library scanner, the torrent sorter and the
/// VR-merge detector, so a format added in one place is recognised everywhere.</summary>
public static class MediaFileTypes
{
    /// <summary>Image formats read from or written to a movie/actor folder. Order matters to the
    /// local-library poster lookup (earlier extensions win).</summary>
    public static readonly string[] ImageExtensions = [".jpg", ".jpeg", ".png", ".webp", ".jfif"];

    /// <summary>Video formats of a release as downloaded, before it is sorted into the library
    /// (includes disc/transport containers such as .iso and .m2ts).</summary>
    public static readonly string[] ReleaseVideoExtensions = [".mp4", ".mkv", ".avi", ".wmv", ".iso", ".ts", ".m2ts", ".mov", ".flv"];

    public static bool IsImage(string? pathOrExtension) => HasExtension(pathOrExtension, ImageExtensions);

    /// <summary>True for a release video (see <see cref="ReleaseVideoExtensions"/>), case-insensitively.</summary>
    public static bool IsReleaseVideo(string? pathOrName) => HasExtension(pathOrName, ReleaseVideoExtensions);

    private static bool HasExtension(string? pathOrExtension, string[] extensions)
    {
        if (string.IsNullOrWhiteSpace(pathOrExtension)) return false;
        var ext = Path.GetExtension(pathOrExtension);
        return !string.IsNullOrEmpty(ext) && extensions.Contains(ext, StringComparer.OrdinalIgnoreCase);
    }
}
