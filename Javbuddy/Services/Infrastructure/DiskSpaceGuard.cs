namespace Javbuddy.Services.Infrastructure;

/// <summary>Best-effort free-space preflight (5% headroom over the bytes about to be written).
/// Skipped silently if the folder's drive can't be resolved (e.g. a UNC path on this platform)
/// rather than blocking the operation on an unrelated hard-to-anticipate case.</summary>
public static class DiskSpaceGuard
{
    private const double HeadroomFactor = 1.05;

    /// <summary>Returns <paramref name="errorMessage"/> when the drive holding
    /// <paramref name="folderPath"/> has less than <paramref name="requiredBytes"/> plus headroom
    /// free, otherwise null.</summary>
    public static string? CheckFreeSpace(string folderPath, long requiredBytes, string errorMessage)
    {
        try
        {
            var root = Path.GetPathRoot(folderPath);
            if (string.IsNullOrEmpty(root)) return null;

            var drive = new DriveInfo(root);
            if (drive.IsReady && drive.AvailableFreeSpace < requiredBytes * HeadroomFactor)
            {
                return errorMessage;
            }
        }
        catch (ArgumentException)
        {
        }

        return null;
    }
}
