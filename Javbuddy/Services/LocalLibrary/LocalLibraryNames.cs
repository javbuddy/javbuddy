namespace Javbuddy.Services.LocalLibrary;

/// <summary>Display names for the configured local library roots (LocalLibrary:RootPaths), e.g. in the Movies Library filter.</summary>
public static class LocalLibraryNames
{
    /// <summary>The root's last folder name ("/media/jav" becomes "jav"). When another root in <paramref name="allRoots"/>
    /// has the same last folder name, the parent folder is prefixed ("disk1/jav") to tell them apart; if that is still
    /// ambiguous, the full path is shown.</summary>
    public static string Label(string rootPath, IEnumerable<string> allRoots)
    {
        var roots = allRoots.Where(r => !string.IsNullOrWhiteSpace(r)).Distinct(StringComparer.Ordinal).ToList();
        var leaf = Segments(rootPath, 1);
        if (leaf.Length == 0) return rootPath;
        if (roots.Count(r => Segments(r, 1) == leaf) <= 1) return leaf;

        var withParent = Segments(rootPath, 2);
        return roots.Count(r => Segments(r, 2) == withParent) <= 1 ? withParent : rootPath;
    }

    private static string Segments(string path, int count)
    {
        var parts = path.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries);
        return string.Join('/', parts.TakeLast(count));
    }
}
