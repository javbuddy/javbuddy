using Javbuddy.Models;

namespace Javbuddy.Components.Pages.MoviesSections;

/// <summary>The contiguous slice of the filtered movie list the Movies grid has in the DOM, with
/// VirtualizedGrid reserving the exact height of the rows on either side of it (see
/// VirtualizedGrid.razor.js's header for the geometry). <see cref="InitialSize"/> is what the
/// prerendered response and any filter change start with, before the browser has reported real
/// layout; from then on the window size is whatever the viewport needs, and <see cref="MaxItems"/>
/// is only a backstop against a bogus or absurdly tall request turning into an unbounded query.
///
/// Not thread-safe: every member is called on the owning component's synchronization context.</summary>
public sealed class MovieGridWindow(Func<int, int, Task<List<Movie>>> fetchRange)
{
    public const int InitialSize = 60;
    public const int MaxItems = 1000;

    public List<Movie> Visible { get; private set; } = [];
    public int Start { get; private set; }
    public int FilteredCount { get; set; }

    // Bumped by every reset/refresh that replaces the window wholesale. EnsureAsync checks it
    // across its awaits: a filter change that lands mid-load would otherwise have its fresh window
    // overwritten by the stale one the in-flight range request was still assembling.
    private int generation;

    // Bumped by MarkDataChanged on a cross-tab notification (typically a Library Import scan inserting
    // rows), independently of generation above. Inserts shift every loaded row's absolute offset, so
    // reusing a slice of Visible across a bump can serve the same row at two offsets and produce a
    // duplicate @key that crashes the circuit. Comparing loadedDataVersion against dataVersion makes
    // EnsureAsync fall back to a full re-fetch only for a stale window.
    private int dataVersion;
    private int loadedDataVersion;

    // Last column count VirtualizedGrid reported. Only used to keep Start on a row boundary when
    // the server has to clamp it itself (see AlignDown) — the grid's leading spacer reserves whole
    // rows, so a window starting mid-row would reserve the wrong height.
    private int columns = 1;

    public int Columns
    {
        get => columns;
        set => columns = Math.Max(1, value);
    }

    /// <summary>Starts a wholesale replacement; any older one still awaiting is now stale.</summary>
    public int BeginReplace() => ++generation;

    public bool IsCurrent(int replaceGeneration) => replaceGeneration == generation;

    public void MarkDataChanged() => dataVersion++;

    public int AlignDown(int index) => columns > 1 ? index - (index % columns) : index;

    public Task<List<Movie>> FetchAsync(int skip, int take) => fetchRange(skip, take);

    public void Replace(List<Movie> movies, int start)
    {
        Visible = movies;
        Start = start;
        loadedDataVersion = dataVersion;
    }

    /// <summary>Makes the loaded window cover [start, start + count), reusing the Movie instances
    /// it already has in common with the requested range. That reuse is what stops a scroll from
    /// flashing: the shared cards keep their <c>@key</c> identity, so Blazor leaves their DOM —
    /// and their already-loaded poster <c>&lt;img&gt;</c> — alone instead of tearing the whole grid
    /// down and rebuilding it. Returns whether the window actually changed.
    ///
    /// The reuse only holds when loadedDataVersion still matches dataVersion: reuse works purely
    /// off offsets, not row identity, so if a row was inserted/removed since Visible was loaded
    /// (e.g. a Library Import scan running concurrently), every already-loaded row's true offset
    /// has shifted and offset-based reuse can hand back the same Movie at two different offsets in
    /// the same window. Falling back to a full re-fetch for one window avoids that duplicate
    /// <c>@key</c> (it previously crashed the circuit) at the cost of a single flash.</summary>
    public async Task<bool> EnsureAsync(int start, int count)
    {
        var startGeneration = generation;
        // Captured once and used for every check below: dataVersion can keep incrementing while
        // this method awaits, and stamping loadedDataVersion with whatever it reads *after* the
        // fact would claim the merge below is consistent with data it never actually saw (this is
        // exactly how a live duplicate @key crash reproduced — dataVersion had moved on by the time
        // of the final write, even though the reused slice was only ever valid against the old one).
        var capturedDataVersion = dataVersion;

        if (FilteredCount <= 0)
        {
            if (Visible.Count == 0 && Start == 0) return false;
            Visible = [];
            Start = 0;
            loadedDataVersion = capturedDataVersion;
            return true;
        }

        start = AlignDown(Math.Clamp(start, 0, FilteredCount - 1));
        count = Math.Clamp(count, 1, MaxItems);
        var end = Math.Min(FilteredCount, start + count);

        var loadedStart = Start;
        var loaded = Visible;
        var loadedEnd = loadedStart + loaded.Count;
        var loadedIsCurrent = loadedDataVersion == capturedDataVersion;
        if (loadedIsCurrent && start >= loadedStart && end <= loadedEnd) return false;

        var overlapStart = Math.Max(start, loadedStart);
        var overlapEnd = Math.Min(end, loadedEnd);

        List<Movie> next;
        if (loadedIsCurrent && overlapEnd > overlapStart)
        {
            next = new List<Movie>(end - start);
            if (start < overlapStart)
            {
                next.AddRange(await fetchRange(start, overlapStart - start));
                if (startGeneration != generation || dataVersion != capturedDataVersion) return false;
            }
            next.AddRange(loaded.GetRange(overlapStart - loadedStart, overlapEnd - overlapStart));
            if (end > overlapEnd)
            {
                next.AddRange(await fetchRange(overlapEnd, end - overlapEnd));
                if (startGeneration != generation || dataVersion != capturedDataVersion) return false;
            }

            // dataVersion is still only a best-effort signal: LibraryImportService throttles its
            // notifications to once per 2 seconds (see MovieChangeNotifier's doc comment), so several
            // scan inserts can shift every row's true offset without ever bumping dataVersion at all.
            // Confirmed live: this reused-slice + fresh-fetch merge produced an actual duplicate Id
            // with dataVersion unchanged throughout. Detecting the collision directly — rather than
            // trying to predict every timing gap through a counter — is what actually guarantees no
            // duplicate reaches PosterCard's @key, whatever caused the shift.
            if (HasDuplicateIds(next))
            {
                next = await fetchRange(start, end - start);
                if (startGeneration != generation) return false;
            }
        }
        else
        {
            next = await fetchRange(start, end - start);
            if (startGeneration != generation) return false;
        }

        Visible = next;
        Start = start;
        loadedDataVersion = capturedDataVersion;
        return true;
    }

    private static bool HasDuplicateIds(List<Movie> movies)
    {
        var seen = new HashSet<int>(movies.Count);
        foreach (var movie in movies)
        {
            if (!seen.Add(movie.Id)) return true;
        }
        return false;
    }
}
