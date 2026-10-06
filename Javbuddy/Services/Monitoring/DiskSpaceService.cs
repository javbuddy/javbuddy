using Javbuddy.Services.Infrastructure;
using Javbuddy.Services.LocalLibrary;

namespace Javbuddy.Services.Monitoring;

/// <summary>Backs the System &gt; Status page's Disk Space section (see the reference Sonarr/Radarr
/// screenshot on the driving issue): free/total space for every filesystem location Javbuddy
/// reads from or writes to.</summary>
public interface IDiskSpaceService
{
    /// <summary>dataDirectory is the already-resolved directory holding the SQLite DB
    /// (SystemStatus.razor's ResolveDataDirectory) and imageCachePath the already-resolved
    /// ILocalImageCache.RootPath — passed in rather than re-resolved here so there's one source of
    /// truth for each path. The durable object store reports its own local folder, and is left out
    /// when it has none. Locations whose drive can't be resolved (a path that doesn't
    /// exist, an unmounted network share) are silently omitted, same precedent as
    /// VrMergeService.CheckFreeSpace.</summary>
    Task<List<DiskSpaceInfo>> GetDiskSpaceAsync(string dataDirectory, string imageCachePath, CancellationToken ct = default);
}

public record DiskSpaceInfo(string Location, long FreeBytes, long TotalBytes, string? Label = null)
{
    public long UsedBytes => Math.Max(0, TotalBytes - FreeBytes);
}

public class DiskSpaceService(ILocalLibraryClient localLibraryClient, IObjectStoreProvider objectStores) : IDiskSpaceService
{
    public async Task<List<DiskSpaceInfo>> GetDiskSpaceAsync(string dataDirectory, string imageCachePath, CancellationToken ct = default)
    {
        var locations = new List<(string Path, string? Label)> { (dataDirectory, null) };
        locations.AddRange((await localLibraryClient.GetRootPathsAsync(ct)).Select(path => (path, (string?)null)));
        locations.Add((imageCachePath, "Image Cache"));
        if (objectStores.LocalRoot is { } objectStoreRoot) locations.Add((objectStoreRoot, "Object Store"));

        var result = new List<DiskSpaceInfo>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (path, label) in locations)
        {
            if (!seen.Add(path)) continue;

            var drive = TryGetDriveInfo(path);
            if (drive is null) continue;

            result.Add(new DiskSpaceInfo(path, drive.AvailableFreeSpace, drive.TotalSize, label));
        }

        return result;
    }

    private static DriveInfo? TryGetDriveInfo(string path)
    {
        try
        {
            var drive = new DriveInfo(path);
            return drive.IsReady ? drive : null;
        }
        catch (ArgumentException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
    }
}
