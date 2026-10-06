using Javbuddy.Models;
using Javbuddy.Services.DeoVr;
using Javbuddy.Services.SceneMedia;

namespace Javbuddy.Services.Images;

public enum ImageCacheMode
{
    Disabled,
    Thumbnails,
    LocalMovies,
    AllMovies,
}

/// <summary>Controls which images use the on-disk cache and optional retention limits. Settings
/// come from ImageCache:Mode, ImageCache:TtlHours, and ImageCache:MaxSizeMb (environment form
/// ImageCache__Mode, ImageCache__TtlHours, and ImageCache__MaxSizeMb). Unset or invalid limits
/// are unlimited, and the default mode caches every local movie and actor image only.</summary>
public sealed record ImageCacheSettings(ImageCacheMode Mode, TimeSpan? Ttl, long? MaxSizeBytes)
{
    public static ImageCacheSettings Default { get; } = new(ImageCacheMode.LocalMovies, null, null);

    public static ImageCacheSettings FromConfiguration(IConfiguration configuration)
    {
        var mode = Enum.TryParse<ImageCacheMode>(configuration["ImageCache:Mode"], ignoreCase: true, out var configuredMode)
            ? configuredMode
            : Default.Mode;
        var ttl = TryGetPositiveDouble(configuration["ImageCache:TtlHours"]) is { } ttlHours
            ? (TimeSpan?)TimeSpan.FromHours(ttlHours)
            : null;
        var maxSizeBytes = TryGetPositiveDouble(configuration["ImageCache:MaxSizeMb"]) is { } maxSizeMb
            ? ToMaxSizeBytes(maxSizeMb)
            : null;
        return new ImageCacheSettings(mode, ttl, maxSizeBytes);
    }

    public bool CachesLocalVariant(string variant) => Mode switch
    {
        ImageCacheMode.Disabled => false,
        ImageCacheMode.Thumbnails => variant == LocalImageCacheService.VariantThumb,
        _ => true,
    };

    public bool CachesExternalImages => Mode == ImageCacheMode.AllMovies;

    public bool IsExpired(DateTime updatedAtUtc) => Ttl is { } ttl && updatedAtUtc < DateTime.UtcNow - ttl;

    private static double? TryGetPositiveDouble(string? raw) =>
        double.TryParse(raw, out var value) && value > 0 && !double.IsInfinity(value) && !double.IsNaN(value)
            ? value
            : null;

    // A configured MaxSizeMb large enough to overflow long once converted to bytes is invalid
    // input, not a real limit — fall back to unlimited rather than throwing at startup.
    private static long? ToMaxSizeBytes(double maxSizeMb)
    {
        var bytes = maxSizeMb * 1024d * 1024d;
        return bytes < long.MaxValue ? (long)bytes : null;
    }
}

/// <summary>Reads the local image cache root directory and WebP quality settings from environment
/// variables / appsettings. Path falls back to a folder next to the app when unset; quality falls
/// back to ImageConverter built-in defaults.</summary>
public static class LocalImageCacheEnvConfig
{
    public static string? GetPath(IConfiguration configuration)
    {
        var path = configuration["ImageCache:Path"];
        return string.IsNullOrWhiteSpace(path) ? null : path;
    }

    public static int GetQuality(IConfiguration configuration, string key, int defaultValue)
    {
        var raw = configuration[$"ImageCache:{key}"];
        if (!int.TryParse(raw, out var value)) return defaultValue;
        return Math.Clamp(value, 1, 100);
    }
}

/// <summary>Maps a cached image opaque StorageId to its sharded WebP location (a WebM video for a
/// scene/highlight hover preview, a JPEG for a DeoVR timeline mosaic).</summary>
public interface ILocalImageCache
{
    /// <summary>The file patterns every cached file matches, for walking the cache root.</summary>
    static readonly string[] FilePatterns = ["*.webp", "*.webm", "*.jpg"];

    static IEnumerable<string> EnumerateFiles(string rootPath) =>
        FilePatterns.SelectMany(pattern => Directory.EnumerateFiles(rootPath, pattern, SearchOption.AllDirectories));

    static string FileExtension(string variant) => variant switch
    {
        SceneMediaService.VariantPreview => ".webm",
        DeoVrTimeline.CacheVariant => ".jpg",
        _ => ".webp",
    };

    string RootPath { get; }

    int QualityFull { get; }

    int QualityThumb { get; }

    ImageCacheSettings Settings => ImageCacheSettings.Default;

    string GetStoragePath(Guid storageId);

    string GetStoragePath(Guid storageId, string variant) => Path.ChangeExtension(GetStoragePath(storageId), FileExtension(variant));

    string GetStoragePath(CachedImage row) => GetStoragePath(row.StorageId, row.Variant);

    bool IsCachedFile(string path)
    {
        var relativePath = Path.GetRelativePath(RootPath, path);
        return relativePath != ".." && !relativePath.StartsWith(".." + Path.DirectorySeparatorChar);
    }
}

public class LocalImageCache(IConfiguration configuration, IWebHostEnvironment env) : ILocalImageCache
{
    public string RootPath { get; } = LocalImageCacheEnvConfig.GetPath(configuration)
            ?? Path.Combine(env.ContentRootPath, "cache");

    public int QualityFull { get; } = LocalImageCacheEnvConfig.GetQuality(configuration, "QualityFull", ImageConverter.WebPQualityFull);

    public int QualityThumb { get; } = LocalImageCacheEnvConfig.GetQuality(configuration, "QualityThumb", ImageConverter.WebPQualityThumb);

    public ImageCacheSettings Settings { get; } = ImageCacheSettings.FromConfiguration(configuration);

    public string GetStoragePath(Guid storageId)
    {
        var name = storageId.ToString("N");
        return Path.Combine(RootPath, name[..2], name + ".webp");
    }
}
