using System.Text.Json.Serialization;

namespace Javbuddy.Services.R18Dev;

/// <summary>A series or label in the r18.dev dump, by its dump id (names aren't unique across makers)
/// with the name to show for it.</summary>
public sealed record R18DevCatalogRef(long Id, string Name);

/// <summary>An include/exclude switch for a yes/no catalog classification (VR, compilation).</summary>
public enum R18DevCatalogFlag
{
    Any,
    Only,
    Exclude,
}

/// <summary>How many actresses the dump credits on a release. <see cref="Unknown"/> is a release with
/// no recorded cast, kept apart from <see cref="Solo"/> rather than counted as one.</summary>
public enum R18DevCastSize
{
    Solo,
    Multiple,
    Unknown,
}

/// <summary>Where a catalog release stands against the local library: not tracked at all, tracked as
/// wanted (MovieStatus.Missing), or owned (MovieStatus.Got).</summary>
public enum R18DevCatalogStatus
{
    All,
    Untracked,
    Wanted,
    Got,
}

/// <summary>Whether the catalog browse can query the dump: there's no dump yet, the dump was imported
/// before its catalog columns existed (<see cref="R18DevCatalogFacets"/>) and needs a re-import, or
/// it's ready.</summary>
public enum R18DevCatalogAvailability
{
    NoDump,
    NeedsReimport,
    Ready,
}

/// <summary>The catalog browse's filters. Every facet that's set must match (AND); the
/// values inside one facet are alternatives (OR) — except genres with <see cref="MatchAllGenres"/>,
/// where a release needs every selected genre.</summary>
public sealed record R18DevCatalogFilter
{
    /// <summary>Code prefix (e.g. "MIDE"), matched as a whole prefix: "MIDE" doesn't match "MIDEA-…".</summary>
    public string? CodePrefix { get; init; }

    /// <summary>Case-insensitive "contains" over the code (ignoring hyphens/spaces) and both titles.</summary>
    public string? SearchText { get; init; }

    public IReadOnlyCollection<string> Studios { get; init; } = [];

    /// <summary>r18.dev category names (English, else Japanese).</summary>
    public IReadOnlyCollection<string> Genres { get; init; } = [];

    public bool MatchAllGenres { get; init; }

    public int? YearFrom { get; init; }

    public int? YearTo { get; init; }

    public R18DevCatalogFlag Vr { get; init; }

    public R18DevCatalogFlag Compilation { get; init; }

    public IReadOnlyCollection<R18DevCastSize> CastSizes { get; init; } = [];

    public R18DevCatalogRef? Series { get; init; }

    public R18DevCatalogRef? Label { get; init; }

    public R18DevCatalogStatus Status { get; init; }

    /// <summary>Leaves out releases matching a previously deleted movie that isn't tracked
    /// again. On by default, like the actor Missing page's.</summary>
    public bool HidePreviouslyDeleted { get; init; } = true;

    /// <summary>The filters that narrow the catalog, not counting <see cref="Status"/>, which the
    /// Movies page shows as its own button group, or <see cref="HidePreviouslyDeleted"/>, which the
    /// toolbar counts itself.</summary>
    [JsonIgnore]
    public int ActiveCount =>
        Genres.Count
        + (YearFrom is not null || YearTo is not null ? 1 : 0)
        + (Vr != R18DevCatalogFlag.Any ? 1 : 0)
        + (Compilation != R18DevCatalogFlag.Any ? 1 : 0)
        + CastSizes.Count
        + (Series is not null ? 1 : 0)
        + (Label is not null ? 1 : 0);

    /// <summary>A stable string of every value, for telling whether two filters would query the same.</summary>
    [JsonIgnore]
    public string Key => string.Join('|',
        CodePrefix?.Trim(),
        SearchText?.Trim(),
        string.Join('\u001f', Studios.Order(StringComparer.OrdinalIgnoreCase)),
        string.Join('\u001f', Genres.Order(StringComparer.OrdinalIgnoreCase)),
        MatchAllGenres,
        YearFrom,
        YearTo,
        Vr,
        Compilation,
        string.Join('\u001f', CastSizes.Order()),
        Series?.Id,
        Label?.Id,
        Status,
        HidePreviouslyDeleted);
}

/// <summary>Limits a catalog query to (or away from) a set of canonical codes — how the browse service
/// applies a library-status filter in SQL, since the dump can't see the main database.</summary>
public sealed record R18DevCanonicalKeyFilter(IReadOnlyCollection<string> Keys, bool Exclude);

/// <summary>An r18.dev category offered by the catalog's genre filter, with how many releases have it.</summary>
public sealed record R18DevCategoryOption(string Name, int VideoCount);

/// <summary>What the catalog's filter menu offers: the genres, and the release years its year range
/// slider spans (null when the dump has no dated releases).</summary>
public sealed record R18DevCatalogOptions(IReadOnlyList<R18DevCategoryOption> Categories, int? MinYear, int? MaxYear)
{
    public static readonly R18DevCatalogOptions None = new([], null, null);
}
