using System.Runtime.CompilerServices;

namespace Javbuddy.Services.Infrastructure;

/// <summary>An <see cref="IObjectStore"/> over a local folder: a key is a relative path under the
/// root. A write goes to a temp file next to its target and is renamed over it, so it's atomic
/// on the same filesystem.</summary>
public sealed class FileSystemObjectStore(string root) : IObjectStore
{
    /// <summary>Names of in-progress writes; never listed as objects.</summary>
    public const string TempPrefix = ".tmp-";

    public string Root { get; } = root;

    /// <summary>The store's folder: <c>ObjectStore:Path</c>, else
    /// <c>&lt;content root&gt;/data/objects</c>.</summary>
    public static string ResolveRoot(IConfiguration configuration, string contentRootPath)
    {
        var path = configuration["ObjectStore:Path"];
        return string.IsNullOrWhiteSpace(path) ? Path.Combine(contentRootPath, "data", "objects") : path;
    }

    public async Task WriteAsync(string key, Stream content, CancellationToken ct = default)
    {
        var path = PathFor(key);
        if (Path.GetFileName(path).StartsWith(TempPrefix, StringComparison.Ordinal))
        {
            throw new ArgumentException($"Invalid object key '{key}'.", nameof(key));
        }

        var directory = Path.GetDirectoryName(path)!;
        var temp = Path.Combine(directory, TempPrefix + Guid.NewGuid().ToString("N"));
        try
        {
            FileStream file;
            try
            {
                Directory.CreateDirectory(directory);
                file = NewTempFile(temp);
            }
            catch (DirectoryNotFoundException)
            {
                // A concurrent delete pruned the folder just after it was created.
                Directory.CreateDirectory(directory);
                file = NewTempFile(temp);
            }
            await using (file)
            {
                await content.CopyToAsync(file, ct);
            }
            File.Move(temp, path, overwrite: true);
        }
        catch
        {
            TryDeleteFile(temp);
            throw;
        }
    }

    private static FileStream NewTempFile(string path) =>
        new(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true);

    public Task<StoredObject?> OpenReadAsync(string key, CancellationToken ct = default)
    {
        var path = PathFor(key);
        try
        {
            var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete, 81920, useAsync: true);
            var info = new FileInfo(path);
            var lastModified = new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero);
            return Task.FromResult<StoredObject?>(new StoredObject(key, stream, stream.Length, lastModified, ETagFor(lastModified, stream.Length)));
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return Task.FromResult<StoredObject?>(null);
        }
    }

    public Task<bool> ExistsAsync(string key, CancellationToken ct = default) => Task.FromResult(File.Exists(PathFor(key)));

    public Task DeleteAsync(string key, CancellationToken ct = default)
    {
        var path = PathFor(key);
        try
        {
            File.Delete(path);
        }
        catch (DirectoryNotFoundException)
        {
            return Task.CompletedTask;
        }
        PruneEmptyDirectories(Path.GetDirectoryName(path)!);
        return Task.CompletedTask;
    }

    public async Task DeletePrefixAsync(string prefix, CancellationToken ct = default)
    {
        // A prefix ending at a folder boundary is that whole folder, including any in-progress
        // temp files in it; otherwise delete the matching objects one by one.
        if (prefix.EndsWith('/') && prefix.Length > 1)
        {
            var directory = PathFor(prefix.TrimEnd('/'));
            if (Directory.Exists(directory))
            {
                await RetryingDirectoryDelete.DeleteAsync(directory, RetryingDirectoryDelete.DefaultRetryDelays, ct: ct);
            }
            PruneEmptyDirectories(Path.GetDirectoryName(directory)!);
            return;
        }

        await foreach (var item in ListAsync(prefix, ct))
        {
            await DeleteAsync(item.Key, ct);
        }
    }

    public async IAsyncEnumerable<StoredObjectInfo> ListAsync(string prefix, [EnumeratorCancellation] CancellationToken ct = default)
    {
        if (prefix.Length > 0) ValidateKey(prefix.TrimEnd('/'));

        // Only the folder the prefix is in needs walking.
        var slash = prefix.LastIndexOf('/');
        var directory = slash < 0 ? Root : PathFor(prefix[..slash]);
        if (!Directory.Exists(directory)) yield break;

        foreach (var file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
        {
            ct.ThrowIfCancellationRequested();
            if (Path.GetFileName(file).StartsWith(TempPrefix, StringComparison.Ordinal)) continue;

            var key = Path.GetRelativePath(Root, file).Replace(Path.DirectorySeparatorChar, '/');
            if (!key.StartsWith(prefix, StringComparison.Ordinal)) continue;

            FileInfo info = new(file);
            if (!info.Exists) continue;
            yield return new StoredObjectInfo(key, info.Length, new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero));
        }
        await Task.CompletedTask;
    }

    /// <summary>The same shape Program.cs's ImageFile has always sent for local files, so browser
    /// caches stay valid.</summary>
    public static string ETagFor(DateTimeOffset lastModified, long length) =>
        $"\"{lastModified.UtcDateTime.Ticks:x}-{length:x}\"";

    private string PathFor(string key)
    {
        ValidateKey(key);
        return Path.Combine([Root, .. key.Split('/')]);
    }

    /// <summary>Keys come from the domain wrappers, never straight from a request, but a key that
    /// could escape the root is still refused.</summary>
    private static void ValidateKey(string key)
    {
        if (string.IsNullOrEmpty(key)
            || key.Contains('\\')
            || key.Contains('\0')
            || key.Split('/').Any(segment => segment is "" or "." or ".."))
        {
            throw new ArgumentException($"Invalid object key '{key}'.", nameof(key));
        }
    }

    /// <summary>Removes the now-empty folders between directory and the root, the way S3 has no
    /// folders left behind once their last object goes. A concurrent write into one of them
    /// recreates it.</summary>
    private void PruneEmptyDirectories(string directory)
    {
        var root = Path.GetFullPath(Root).TrimEnd(Path.DirectorySeparatorChar);
        var current = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar);
        while (current.Length > root.Length && current.StartsWith(root, StringComparison.Ordinal))
        {
            try
            {
                if (!Directory.Exists(current) || Directory.EnumerateFileSystemEntries(current).Any()) return;
                Directory.Delete(current);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return;
            }
            current = Path.GetDirectoryName(current)!;
        }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
