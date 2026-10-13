namespace Javbuddy.Services.Common;

/// <summary>The checked rows of a list with row and select-all checkboxes: what each page that offers bulk
/// actions on a list kept as a hand-rolled <see cref="HashSet{T}"/> plus toggle/select-all methods.</summary>
public sealed class SelectionSet<TKey> where TKey : notnull
{
    private readonly HashSet<TKey> keys = [];

    public int Count => keys.Count;

    public bool Contains(TKey key) => keys.Contains(key);

    /// <summary>Checks or unchecks one row.</summary>
    public void Set(TKey key, bool selected)
    {
        if (selected) keys.Add(key);
        else keys.Remove(key);
    }

    public void Toggle(TKey key) => Set(key, !keys.Contains(key));

    /// <summary>Checks or unchecks every given row — a select-all over the rows in view; rows outside it keep
    /// their state.</summary>
    public void SetAll(IEnumerable<TKey> rows, bool selected)
    {
        foreach (var key in rows) Set(key, selected);
    }

    /// <summary>Whether every given row is checked; false for an empty list (the header checkbox stays off).</summary>
    public bool AllSelected(IReadOnlyCollection<TKey> rows) => rows.Count > 0 && rows.All(keys.Contains);

    /// <summary>Drops checked rows that are no longer in <paramref name="rows"/>.</summary>
    public void RetainOnly(IEnumerable<TKey> rows) => keys.IntersectWith(rows);

    public void Clear() => keys.Clear();

    public List<TKey> ToList() => [.. keys];
}
