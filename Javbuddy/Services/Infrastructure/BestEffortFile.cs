namespace Javbuddy.Services.Infrastructure;

/// <summary>Cleanup-only file/directory deletes that never throw for the failures that are routine
/// on a network share (file in use, permissions) — a leftover temp file must not fail the
/// operation that was already done with it.</summary>
public static class BestEffortFile
{
    /// <summary>Deletes <paramref name="path"/> if it exists. Returns true only when a file was
    /// actually deleted.</summary>
    public static bool TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
                return true;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }

        return false;
    }

    /// <summary>Recursively deletes <paramref name="directory"/> if it exists. Returns true only
    /// when the directory was actually deleted.</summary>
    public static bool TryDeleteDirectory(string directory)
    {
        try
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
                return true;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }

        return false;
    }
}
