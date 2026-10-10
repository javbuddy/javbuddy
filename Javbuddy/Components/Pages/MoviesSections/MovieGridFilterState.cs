using Javbuddy.Components.Shared;
using Javbuddy.Models;
using Javbuddy.Services.Movies;
using Javbuddy.Services.R18Dev;

namespace Javbuddy.Components.Pages.MoviesSections;

/// <summary>The Movies grid's selected filters and sort order: what the user picked, as opposed to
/// the filter options and counts the library offers.</summary>
public sealed class MovieGridFilterState
{
    public MovieStatus? Status { get; set; }
    public string? Library { get; set; }
    public HashSet<MovieResolutionFilterOption> Resolutions { get; private set; } = [];
    public HashSet<MovieScanTypeFilterOption> ScanTypes { get; private set; } = [];
    public HashSet<NfoDriftKind> NfoDriftKinds { get; private set; } = [];
    public HashSet<string> Codecs { get; private set; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> Studios { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> Genres { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<MovieFeatureFilterOption> Features { get; private set; } = [];
    public HashSet<int> ActorIds { get; private set; } = [];
    public HashSet<int> ActorTagIds { get; private set; } = [];
    public ActorAttributeSelection ActorAttributes { get; set; } = ActorAttributeSelection.Empty;
    public string Text { get; set; } = "";
    public string? CodePrefix { get; set; }
    public string SortField { get; set; } = "added";
    public bool SortDescending { get; set; } = true;

    // Regenerated (not merely toggled) every time "Random" is (re)selected, so the resulting order
    // is a fixed shuffle for the lifetime of that selection: the ordered grid query is re-run from
    // scratch on every scroll-driven window fetch, so the sort key has to be deterministic given
    // this seed, not re-randomized per query, or paging through the grid would show
    // duplicate/skipped movies.
    public int RandomSortSeed { get; set; } = Random.Shared.Next(1, int.MaxValue);

    public MovieGridFilter ToFilter() => new(
        Status,
        Library,
        [.. Resolutions],
        [.. ScanTypes],
        [.. Codecs],
        [.. Studios],
        [.. Genres],
        [.. Features],
        Text,
        CodePrefix,
        [.. NfoDriftKinds],
        ActorAttributes.IsEmpty ? null : ActorAttributes,
        [.. ActorIds],
        [.. ActorTagIds]);

    public MovieGridSort ToSort() => new(SortField, SortDescending, RandomSortSeed);

    private int ActiveFilterCount => Resolutions.Count + ScanTypes.Count + NfoDriftKinds.Count + Codecs.Count + Studios.Count + Genres.Count + Features.Count
        + ActorIds.Count + ActorTagIds.Count + ActorAttributes.Count;

    public bool HasActiveFilters =>
        Status is not null
        || !string.IsNullOrEmpty(Library)
        || !string.IsNullOrWhiteSpace(Text)
        || !string.IsNullOrWhiteSpace(CodePrefix)
        || ActiveFilterCount > 0;

    /// <summary>Clears every filter a MovieFilterNavigationState target doesn't set itself (text,
    /// code prefix, studio and genre are set by the page), and makes the actor-tag filter just
    /// actorTagTarget when there is one. Returns whether that changed anything.</summary>
    public bool ClearBesidesNavigationTargets(int? actorTagTarget = null)
    {
        var actorTagsChanged = actorTagTarget is { } target ? !(ActorTagIds.Count == 1 && ActorTagIds.Contains(target)) : ActorTagIds.Count > 0;
        var anyActive = Status is not null || !string.IsNullOrEmpty(Library) || Resolutions.Count > 0
            || ScanTypes.Count > 0 || NfoDriftKinds.Count > 0 || Codecs.Count > 0 || Features.Count > 0
            || ActorIds.Count > 0 || actorTagsChanged || ActorAttributes.Count > 0;
        Status = null;
        Library = null;
        Resolutions.Clear();
        ScanTypes.Clear();
        NfoDriftKinds.Clear();
        Codecs.Clear();
        Features.Clear();
        ActorIds.Clear();
        ActorTagIds.Clear();
        if (actorTagTarget is { } actorTag) ActorTagIds.Add(actorTag);
        ActorAttributes = ActorAttributeSelection.Empty;
        return anyActive;
    }

    /// <summary>Clears every filter; the sort order stays.</summary>
    public void Clear()
    {
        ClearBesidesNavigationTargets();
        Text = string.Empty;
        CodePrefix = null;
        Studios.Clear();
        Genres.Clear();
    }

    public MoviesViewState ToViewState() => new(
        Status, Library, [.. Resolutions], [.. ScanTypes], [.. Codecs],
        [.. Studios], [.. Genres], [.. Features], Text, CodePrefix,
        SortField, SortDescending, [.. NfoDriftKinds],
        ActorAttributes, [.. ActorIds], ActorTagFilter: [.. ActorTagIds]);

    /// <summary>Applies a saved selection; a list the state doesn't carry (a cookie written before
    /// that filter existed) keeps its current value.</summary>
    public void Apply(MoviesViewState state)
    {
        Status = state.Filter;
        Library = state.LibraryFilter;
        if (state.ResolutionFilter is not null) Resolutions = [.. state.ResolutionFilter];
        if (state.ScanTypeFilter is not null) ScanTypes = [.. state.ScanTypeFilter];
        if (state.CodecFilter is not null) Codecs = new HashSet<string>(state.CodecFilter, StringComparer.OrdinalIgnoreCase);
        if (state.StudioFilter is not null) Studios = new HashSet<string>(state.StudioFilter, StringComparer.OrdinalIgnoreCase);
        if (state.GenreFilter is not null) Genres = new HashSet<string>(state.GenreFilter, StringComparer.OrdinalIgnoreCase);
        if (state.FeatureFilter is not null) Features = [.. state.FeatureFilter];
        if (state.NfoDriftFilter is not null) NfoDriftKinds = [.. state.NfoDriftFilter];
        if (state.ActorAttributes is not null) ActorAttributes = state.ActorAttributes;
        if (state.ActorFilter is not null) ActorIds = [.. state.ActorFilter];
        if (state.ActorTagFilter is not null) ActorTagIds = [.. state.ActorTagFilter];
        MovieFilterOptions.MigrateLegacyNfoFeatures(Features, NfoDriftKinds);
        Text = state.TextFilter;
        CodePrefix = state.CodePrefixFilter;
        SortField = state.SortField;
        SortDescending = state.SortDescending;
    }
}

/// <summary>The Movies grid's selected filters/sort as saved in the view-state cookie
/// — a separate cookie from <see cref="MoviePosterOptions"/>. RandomSortSeed isn't
/// included: it's an implementation detail of the "random" sort order, not a selected option, so a
/// fresh session re-shuffles rather than reproducing the exact same order. The property names are
/// the cookie's JSON, so renaming one drops that part of every saved selection.
/// <paramref name="BrowseReleases"/> and <paramref name="CatalogFilter"/> are the Movies page's
/// "All releases (r18.dev)" mode and that browse's own filters; the page sets and
/// reads them, not <see cref="MovieGridFilterState"/>.</summary>
public sealed record MoviesViewState(
    MovieStatus? Filter = null,
    string? LibraryFilter = null,
    List<MovieResolutionFilterOption>? ResolutionFilter = null,
    List<MovieScanTypeFilterOption>? ScanTypeFilter = null,
    List<string>? CodecFilter = null,
    List<string>? StudioFilter = null,
    List<string>? GenreFilter = null,
    List<MovieFeatureFilterOption>? FeatureFilter = null,
    string TextFilter = "",
    string? CodePrefixFilter = null,
    string SortField = "added",
    bool SortDescending = true,
    List<NfoDriftKind>? NfoDriftFilter = null,
    ActorAttributeSelection? ActorAttributes = null,
    List<int>? ActorFilter = null,
    bool BrowseReleases = false,
    R18DevCatalogFilter? CatalogFilter = null,
    List<int>? ActorTagFilter = null)
{
    public const string CookieName = "javbuddy-movies-view";
}
