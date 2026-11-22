namespace Javbuddy.Services.Infrastructure;

/// <summary>Whether a path lies inside a folder. Both sides are normalized with
/// <see cref="Path.GetFullPath(string)"/> and the folder gets a trailing separator, so ".." segments
/// and a sibling sharing the folder's name prefix ("/lib2" against "/lib") don't pass. The check is
/// lexical: symlinks are not resolved. Comparison is case-insensitive on Windows only.</summary>
public static class PathContainment
{
    /// <summary>Whether path resolves to somewhere inside folder. The folder path itself does not count, but the
    /// same path written with a trailing separator does (unchanged behavior of the two helpers this replaced).</summary>
    public static bool IsInside(string folder, string path)
    {
        var fullFolder = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder)) + Path.DirectorySeparatorChar;
        return Path.GetFullPath(path).StartsWith(fullFolder, Comparison);
    }

    /// <summary>Whether path resolves to somewhere inside any of folders; blank entries are skipped.</summary>
    public static bool IsInsideAny(string path, IEnumerable<string> folders)
    {
        foreach (var folder in folders)
        {
            if (string.IsNullOrWhiteSpace(folder)) continue;
            if (IsInside(folder, path)) return true;
        }
        return false;
    }

    private static StringComparison Comparison =>
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
}
