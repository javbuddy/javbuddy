namespace Javbuddy.Services.Infrastructure;

/// <summary>Recursive <see cref="Directory.Delete(string, bool)"/> that retries on
/// <see cref="IOException"/> — chiefly "Directory not empty". On a network share a
/// file another client still has open isn't gone when it's unlinked (NFS renames it to a hidden
/// <c>.nfsXXXX</c> file, SMB leaves it delete-pending) until that client lets go, and the final
/// rmdir fails meanwhile; a concurrent writer (e.g. Jellyfin dropping trickplay/metadata into the
/// folder) does the same. Permission errors aren't retried — waiting won't fix those.</summary>
public static class RetryingDirectoryDelete
{
    /// <summary>~3.75s in total: long enough for a just-stopped stream's file handle to close.</summary>
    public static readonly IReadOnlyList<TimeSpan> DefaultRetryDelays =
    [
        TimeSpan.FromMilliseconds(250),
        TimeSpan.FromMilliseconds(500),
        TimeSpan.FromSeconds(1),
        TimeSpan.FromSeconds(2),
    ];

    /// <summary>Deletes <paramref name="path"/> recursively, waiting each of
    /// <paramref name="retryDelays"/> before another attempt. Stops early (successfully) if the folder
    /// has disappeared; rethrows the last error once the retries run out.
    /// <paramref name="deleteDirectory"/>/<paramref name="directoryExists"/> are test seams.</summary>
    public static async Task DeleteAsync(
        string path,
        IReadOnlyList<TimeSpan> retryDelays,
        Action<string>? deleteDirectory = null,
        Func<string, bool>? directoryExists = null,
        CancellationToken ct = default)
    {
        deleteDirectory ??= p => Directory.Delete(p, recursive: true);
        directoryExists ??= Directory.Exists;

        for (var attempt = 0; ; attempt++)
        {
            try
            {
                deleteDirectory(path);
                return;
            }
            catch (DirectoryNotFoundException)
            {
                return;
            }
            catch (IOException) when (attempt < retryDelays.Count)
            {
            }

            await Task.Delay(retryDelays[attempt], ct);
            if (!directoryExists(path)) return;
        }
    }
}
