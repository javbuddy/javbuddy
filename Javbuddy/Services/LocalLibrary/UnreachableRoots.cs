namespace Javbuddy.Services.LocalLibrary;

/// <summary>Decides whether a movie can be judged "gone from disk" when some library roots are offline.
/// A scan can't see inside an unreachable root, so absence there proves nothing.</summary>
public sealed class UnreachableRoots(IEnumerable<string> roots)
{
    private readonly HashSet<string> roots = roots.Select(Normalize).ToHashSet(StringComparer.Ordinal);

    // A trailing separator added or dropped in the configured root shouldn't make a stored root look different.
    private static string Normalize(string root) => root.TrimEnd('/', '\\');

    /// <summary>True when the movie's local files could be on an offline root: its recorded root is
    /// offline, or it has no recorded root and any root is offline.</summary>
    public bool MayHide(string? localRootPath) =>
        localRootPath is null ? roots.Count > 0 : roots.Contains(Normalize(localRootPath));
}
