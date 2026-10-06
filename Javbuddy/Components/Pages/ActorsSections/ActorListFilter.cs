using Javbuddy.Services.Actors;

namespace Javbuddy.Components.Pages.ActorsSections;

// Saved in the view-state cookie as their integer values, so only ever append.
public enum ActorMovieFilter { All, HasMovies, NoMovies }
public enum ActorPhotoFilter { All, HasPhoto, MissingPhoto }
public enum ActorImageFilter { All, HasImages, NoImages }
public enum ActorStatusFilter { All, ActiveOnly, RetiredOnly }
public enum ActorViewMode { Grid, Table }

/// <summary>The Actors list's selected filters and sort order, and the in-memory query they make
/// over the full actor list.</summary>
public sealed class ActorListFilter
{
    public static readonly string[] SortFields = ["name", "movie_count", "owned", "missing", "image_count", "recent", "favorites", "age", "height", "cup_size"];

    public string Text { get; set; } = "";
    public ActorMovieFilter Movies { get; set; } = ActorMovieFilter.All;
    public ActorPhotoFilter Photo { get; set; } = ActorPhotoFilter.All;
    public ActorImageFilter Images { get; set; } = ActorImageFilter.All;
    public ActorStatusFilter Status { get; set; } = ActorStatusFilter.All;
    public bool JellyfinLinkedOnly { get; set; }
    public bool FavoritesOnly { get; set; }
    public bool NoMetadataOnly { get; set; }
    public bool MissingMoviesOnly { get; set; }
    public HashSet<string> CupSizes { get; } = new(StringComparer.OrdinalIgnoreCase);
    public int? HeightMin { get; private set; }
    public int? HeightMax { get; private set; }
    public string SortField { get; private set; } = "name";
    public bool SortDescending { get; private set; }

    /// <summary>The smallest and largest height in the library, or null when no actor has one — the
    /// height filter's track. Set by the page once the actors are loaded.</summary>
    public (int Min, int Max)? HeightBounds { get; set; }

    public int EffectiveHeightMin => HeightBounds is { } b ? Math.Clamp(HeightMin ?? b.Min, b.Min, b.Max) : 0;
    public int EffectiveHeightMax => HeightBounds is { } b ? Math.Clamp(HeightMax ?? b.Max, b.Min, b.Max) : 0;

    private bool HeightFilterActive => HeightBounds is { } b && (EffectiveHeightMin > b.Min || EffectiveHeightMax < b.Max);

    public int ActiveFilterCount =>
        (Movies != ActorMovieFilter.All ? 1 : 0) +
        (Photo != ActorPhotoFilter.All ? 1 : 0) +
        (Images != ActorImageFilter.All ? 1 : 0) +
        (JellyfinLinkedOnly ? 1 : 0) +
        (FavoritesOnly ? 1 : 0) +
        (NoMetadataOnly ? 1 : 0) +
        (MissingMoviesOnly ? 1 : 0) +
        (Status != ActorStatusFilter.All ? 1 : 0) +
        CupSizes.Count +
        (HeightFilterActive ? 1 : 0);

    public bool HasActiveFilters => !string.IsNullOrWhiteSpace(Text) || ActiveFilterCount > 0;

    /// <summary>True when "favorites only" is the sole filter — the empty list then means "no
    /// favorites yet" rather than "nothing matches".</summary>
    public bool OnlyFavoritesFiltered =>
        FavoritesOnly && Movies == ActorMovieFilter.All && Photo == ActorPhotoFilter.All && Images == ActorImageFilter.All
        && !JellyfinLinkedOnly && CupSizes.Count == 0 && string.IsNullOrWhiteSpace(Text);

    public string MovieFilterLabel => Movies == ActorMovieFilter.NoMovies ? "No Movies" : "Has Movies";
    public string PhotoFilterLabel => Photo == ActorPhotoFilter.MissingPhoto ? "Missing Photo" : "Has Photo";
    public string ImageFilterLabel => Images == ActorImageFilter.NoImages ? "No Images" : "Has Images";
    public string StatusFilterLabel => Status switch
    {
        ActorStatusFilter.ActiveOnly => "Active only",
        ActorStatusFilter.RetiredOnly => "Retired only",
        _ => "Status: All"
    };

    public void CycleMovies() => Movies = Movies switch
    {
        ActorMovieFilter.All => ActorMovieFilter.HasMovies,
        ActorMovieFilter.HasMovies => ActorMovieFilter.NoMovies,
        _ => ActorMovieFilter.All
    };

    public void CyclePhoto() => Photo = Photo switch
    {
        ActorPhotoFilter.All => ActorPhotoFilter.HasPhoto,
        ActorPhotoFilter.HasPhoto => ActorPhotoFilter.MissingPhoto,
        _ => ActorPhotoFilter.All
    };

    public void CycleImages() => Images = Images switch
    {
        ActorImageFilter.All => ActorImageFilter.HasImages,
        ActorImageFilter.HasImages => ActorImageFilter.NoImages,
        _ => ActorImageFilter.All
    };

    public void CycleStatus() => Status = Status switch
    {
        ActorStatusFilter.All => ActorStatusFilter.ActiveOnly,
        ActorStatusFilter.ActiveOnly => ActorStatusFilter.RetiredOnly,
        _ => ActorStatusFilter.All
    };

    /// <summary>A limit at the edge of <see cref="HeightBounds"/> is stored as "no limit", so the
    /// filter keeps covering the whole library as it grows.</summary>
    public void SetHeightRange((int Min, int Max) range)
    {
        if (HeightBounds is not { } bounds) return;

        HeightMin = range.Min <= bounds.Min ? null : range.Min;
        HeightMax = range.Max >= bounds.Max ? null : range.Max;
    }

    /// <summary>Clicking the current sort field flips its direction; a new field starts descending,
    /// except Name, which starts A–Z.</summary>
    public void SetSort(string field)
    {
        if (SortField == field)
        {
            SortDescending = !SortDescending;
        }
        else
        {
            SortField = field;
            SortDescending = field != "name";
        }
    }

    /// <summary>Clears every filter; the sort order stays.</summary>
    public void Clear()
    {
        Text = "";
        Movies = ActorMovieFilter.All;
        Photo = ActorPhotoFilter.All;
        Images = ActorImageFilter.All;
        Status = ActorStatusFilter.All;
        JellyfinLinkedOnly = false;
        FavoritesOnly = false;
        NoMetadataOnly = false;
        MissingMoviesOnly = false;
        CupSizes.Clear();
        HeightMin = null;
        HeightMax = null;
    }

    /// <summary>The filter/sort part of the saved view; the page adds its display settings.</summary>
    public ActorsViewState ToViewState() => new(
        Text, Movies, Photo, Images, Status,
        JellyfinLinkedOnly, FavoritesOnly, CupSizes.ToList(), HeightMin, HeightMax,
        SortField, SortDescending,
        NoMetadataOnly: NoMetadataOnly, MissingMoviesOnly: MissingMoviesOnly);

    public void Apply(ActorsViewState state)
    {
        Text = state.TextFilter;
        Movies = state.MovieFilter;
        Photo = state.PhotoFilter;
        Images = state.ImageFilter;
        Status = state.StatusFilter;
        JellyfinLinkedOnly = state.JellyfinLinkedOnly;
        FavoritesOnly = state.FavoritesOnly;
        if (state.CupSizeFilter is not null)
        {
            CupSizes.Clear();
            CupSizes.UnionWith(state.CupSizeFilter);
        }
        HeightMin = state.HeightMin;
        HeightMax = state.HeightMax;
        SortField = state.SortField;
        SortDescending = state.SortDescending;
        NoMetadataOnly = state.NoMetadataOnly;
        MissingMoviesOnly = state.MissingMoviesOnly;
    }

    public IEnumerable<ActorCardModel> Apply(IEnumerable<ActorCardModel> actors)
    {
        var query = actors;

        if (!string.IsNullOrWhiteSpace(Text))
        {
            var search = Text.Trim();
            query = query.Where(a =>
                a.DisplayName.Contains(search, StringComparison.OrdinalIgnoreCase)
                || (a.JapaneseNameKanji != null && a.JapaneseNameKanji.Contains(search, StringComparison.OrdinalIgnoreCase))
                || (a.JapaneseNameKana != null && a.JapaneseNameKana.Contains(search, StringComparison.OrdinalIgnoreCase))
                || (a.Aliases != null && a.Aliases.Any(al => al.Contains(search, StringComparison.OrdinalIgnoreCase))));
        }

        if (Movies == ActorMovieFilter.HasMovies)
        {
            query = query.Where(a => a.MovieCount > 0);
        }
        else if (Movies == ActorMovieFilter.NoMovies)
        {
            query = query.Where(a => a.MovieCount == 0);
        }

        if (Photo == ActorPhotoFilter.HasPhoto)
        {
            query = query.Where(a => a.HasImage);
        }
        else if (Photo == ActorPhotoFilter.MissingPhoto)
        {
            query = query.Where(a => !a.HasImage);
        }

        if (Images == ActorImageFilter.HasImages)
        {
            query = query.Where(a => a.ImageCount > 0);
        }
        else if (Images == ActorImageFilter.NoImages)
        {
            query = query.Where(a => a.ImageCount == 0);
        }

        if (JellyfinLinkedOnly)
        {
            query = query.Where(a => a.HasJellyfin);
        }

        if (FavoritesOnly)
        {
            query = query.Where(a => a.IsFavorite);
        }

        if (Status == ActorStatusFilter.ActiveOnly)
        {
            query = query.Where(a => !a.IsRetired);
        }
        else if (Status == ActorStatusFilter.RetiredOnly)
        {
            query = query.Where(a => a.IsRetired);
        }

        if (NoMetadataOnly)
        {
            query = query.Where(a => !a.HasMetadata);
        }

        if (MissingMoviesOnly)
        {
            query = query.Where(a => a.MissingCount > 0);
        }

        if (CupSizes.Count > 0)
        {
            query = query.Where(a => a.CupSize != null && CupSizes.Contains(a.CupSize));
        }

        if (HeightFilterActive)
        {
            var min = EffectiveHeightMin;
            var max = EffectiveHeightMax;
            query = query.Where(a => a.HeightCm.HasValue && a.HeightCm.Value >= min && a.HeightCm.Value <= max);
        }

        return (SortField, SortDescending) switch
        {
            ("name", false) => query.OrderBy(a => a.DisplayName),
            ("name", true) => query.OrderByDescending(a => a.DisplayName),
            ("movie_count", false) => query.OrderBy(a => a.MovieCount).ThenBy(a => a.DisplayName),
            ("movie_count", true) => query.OrderByDescending(a => a.MovieCount).ThenBy(a => a.DisplayName),
            ("image_count", false) => query.OrderBy(a => a.ImageCount).ThenBy(a => a.DisplayName),
            ("image_count", true) => query.OrderByDescending(a => a.ImageCount).ThenBy(a => a.DisplayName),
            ("recent", false) => query.OrderBy(a => a.CreatedAt).ThenBy(a => a.DisplayName),
            ("recent", true) => query.OrderByDescending(a => a.CreatedAt).ThenBy(a => a.DisplayName),
            ("favorites", false) => query.OrderBy(a => a.IsFavorite).ThenBy(a => a.DisplayName),
            ("favorites", true) => query.OrderByDescending(a => a.IsFavorite).ThenBy(a => a.DisplayName),
            ("height", false) => query.OrderBy(a => !a.HeightCm.HasValue).ThenBy(a => a.HeightCm).ThenBy(a => a.DisplayName),
            ("height", true) => query.OrderBy(a => !a.HeightCm.HasValue).ThenByDescending(a => a.HeightCm).ThenBy(a => a.DisplayName),
            ("age", false) => query.OrderBy(a => !a.Age.HasValue).ThenBy(a => a.Age).ThenBy(a => a.DisplayName),
            ("age", true) => query.OrderBy(a => !a.Age.HasValue).ThenByDescending(a => a.Age).ThenBy(a => a.DisplayName),
            ("owned", false) => query.OrderBy(a => a.OwnedCount).ThenBy(a => a.DisplayName),
            ("owned", true) => query.OrderByDescending(a => a.OwnedCount).ThenBy(a => a.DisplayName),
            ("missing", false) => query.OrderBy(a => a.MissingCount).ThenBy(a => a.DisplayName),
            ("missing", true) => query.OrderByDescending(a => a.MissingCount).ThenBy(a => a.DisplayName),
            ("cup_size", false) => query.OrderBy(a => a.CupSize is null).ThenBy(a => CupSizeRank(a)).ThenBy(a => a.DisplayName),
            ("cup_size", true) => query.OrderBy(a => a.CupSize is null).ThenByDescending(a => CupSizeRank(a)).ThenBy(a => a.DisplayName),
            _ => query.OrderBy(a => a.DisplayName)
        };
    }

    // Cup sizes aren't limited to a single letter (the cup chips merge in any free-text value seen in
    // the data), so plain alphabetical order would sort "AA" before "B". Rank by length first, then
    // value, matching the chips' own ordering.
    private static (int Length, string Value) CupSizeRank(ActorCardModel a) =>
        a.CupSize is null ? (0, "") : (a.CupSize.Length, a.CupSize);
}

/// <summary>The Actors page's view as saved in its cookie and carried from the
/// prerender into the interactive circuit. The property names are the cookie's
/// JSON, so renaming one drops that part of every saved view.</summary>
public sealed record ActorsViewState(
    string TextFilter = "",
    ActorMovieFilter MovieFilter = ActorMovieFilter.All,
    ActorPhotoFilter PhotoFilter = ActorPhotoFilter.All,
    ActorImageFilter ImageFilter = ActorImageFilter.All,
    ActorStatusFilter StatusFilter = ActorStatusFilter.All,
    bool JellyfinLinkedOnly = false,
    bool FavoritesOnly = false,
    List<string>? CupSizeFilter = null,
    int? HeightMin = null,
    int? HeightMax = null,
    string SortField = "name",
    bool SortDescending = false,
    ActorViewMode ViewMode = ActorViewMode.Grid,
    int SizeStep = ActorsViewState.DefaultSizeStep,
    bool NoMetadataOnly = false,
    bool MissingMoviesOnly = false,
    List<string>? TableColumns = null)
{
    public const string CookieName = "javbuddy-actors-view";
    public const int MaxSizeStep = 3;
    public const int DefaultSizeStep = 2;
}
