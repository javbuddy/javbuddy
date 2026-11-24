using System.Collections.Concurrent;
using Javbuddy.Services.Images;
using Javbuddy.Services.Infrastructure;

namespace Javbuddy.Services.ClipExport;

/// <summary>A prepared clip waiting to be downloaded: the temp file, the name the browser saves it as and its content type.</summary>
public sealed record ClipExportDownload(string Path, string FileName, string ContentType, DateTime CreatedUtc);

/// <summary>Exported clips between being cut and being downloaded. Each clip is a temp file in <c>clip-exports</c> under the
/// image cache root (the disposable <c>/cache</c> volume in Docker; named <see cref="TempExtension"/>, which cache maintenance
/// ignores), reachable through a random single-use token. The download takes the token and deletes the file when the
/// response is done; a clip nobody downloads, and anything a crash left behind, is deleted after <see cref="Lifetime"/>.</summary>
public sealed class ClipExportDownloads(ILocalImageCache imageCache, TimeProvider? timeProvider = null)
{
    /// <summary>How long a prepared clip waits for its download.</summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromHours(1);

    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;
    private readonly ConcurrentDictionary<string, ClipExportDownload> pending = new(StringComparer.Ordinal);

    public string Folder { get; } = System.IO.Path.Combine(imageCache.RootPath, "clip-exports");

    /// <summary>The temp files' extension. Never a media one: cache maintenance walks the whole cache root and counts its .webp,
    /// .webm and .jpg files, so a .webm clip would be taken for (and cleaned up as) a cache entry. ffmpeg is told the format.</summary>
    public const string TempExtension = ".clip";

    /// <summary>A fresh temp path for ffmpeg to write a clip to (the folder is created).</summary>
    public string NewTempPath()
    {
        Directory.CreateDirectory(Folder);
        return System.IO.Path.Combine(Folder, Guid.NewGuid().ToString("N") + TempExtension);
    }

    /// <summary>Makes the finished clip at <paramref name="path"/> downloadable as <paramref name="fileName"/>; returns its token.</summary>
    public string Add(string path, string fileName, string contentType)
    {
        var token = Guid.NewGuid().ToString("N");
        pending[token] = new ClipExportDownload(path, fileName, contentType, clock.GetUtcNow().UtcDateTime);
        return token;
    }

    /// <summary>Takes the clip for <paramref name="token"/> (it can't be taken twice); null when unknown, taken or expired.</summary>
    public ClipExportDownload? Take(string token)
    {
        if (!pending.TryRemove(token, out var download)) return null;
        if (clock.GetUtcNow().UtcDateTime - download.CreatedUtc <= Lifetime && File.Exists(download.Path)) return download;
        BestEffortFile.TryDeleteFile(download.Path);
        return null;
    }

    /// <summary>Deletes clips that waited longer than <see cref="Lifetime"/>, and any file in the folder older than that
    /// (an export cut short by a restart). A clip being written or waiting is never older, so it stays.</summary>
    public void Sweep()
    {
        var cutoff = clock.GetUtcNow().UtcDateTime - Lifetime;
        foreach (var (token, download) in pending)
        {
            if (download.CreatedUtc < cutoff && pending.TryRemove(token, out _)) BestEffortFile.TryDeleteFile(download.Path);
        }

        if (!Directory.Exists(Folder)) return;
        foreach (var file in new DirectoryInfo(Folder).EnumerateFiles())
        {
            if (file.LastWriteTimeUtc < cutoff) BestEffortFile.TryDeleteFile(file.FullName);
        }
    }
}
