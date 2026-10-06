namespace Javbuddy.Components.Pages.MovieDiscoverSections;

/// <summary>The slice of one studio section's candidates Movies &gt; Discover keeps in the DOM, with
/// that section's VirtualizedGrid reserving the height of the rows on either side of it
///. The page already holds every candidate in memory, so moving a window is only a
/// re-render, never a query.</summary>
public readonly record struct DiscoverSectionWindow(int Start, int Count)
{
    /// <summary>What the prerendered response and a fresh load start every section with, before the
    /// browser has reported real layout: about a first viewport's worth for the top section, and
    /// small enough that the sections further down don't put the whole list back in the DOM.</summary>
    public const int InitialSize = 12;

    /// <summary>A backstop against a bogus or absurdly tall request, not a normal window size.</summary>
    public const int MaxItems = 500;

    public static DiscoverSectionWindow Initial(int total) => new(0, Math.Min(InitialSize, Math.Max(0, total)));

    /// <summary>Fits a requested range (or an old window after the section shrank) to a section of
    /// <paramref name="total"/> candidates laid out in <paramref name="columns"/> columns. The result
    /// always starts on a row boundary, because the leading spacer reserves whole rows, and always
    /// keeps at least one row: VirtualizedGrid measures the row height from a rendered card, so a
    /// section scrolled far away still shows its last (or first) row rather than nothing at all.</summary>
    public static DiscoverSectionWindow Clamp(int start, int count, int columns, int total)
    {
        if (total <= 0) return new(0, 0);

        columns = Math.Max(1, columns);
        var lastRowStart = (total - 1) / columns * columns;
        start = Math.Clamp(start, 0, lastRowStart);
        start -= start % columns;
        count = Math.Clamp(count, columns, MaxItems);
        return new(start, Math.Min(count, total - start));
    }
}
