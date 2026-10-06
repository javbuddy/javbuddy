using Javbuddy.Components.Shared;
using Javbuddy.Models;

namespace Javbuddy.Services.Movies;

/// <summary>The Filter menu's predicates shared by the Movies grid (translated to SQL) and Actor
/// Detail (applied in memory to the actor's movies via AsQueryable), so both pages give the same
/// results for the same Filter menu choices. In memory, the movies need
/// MovieTags → Tag → ParentTag and Scenes loaded.</summary>
public static class MovieFilterPredicates
{
    /// <summary>Boundaries by width (height varies with aspect ratio, e.g. vertical/VR video); HD deliberately
    /// spans both 720p and 1080p as one bucket, matching the README's filter spec. Multi-select: a movie matches
    /// if its width falls in ANY of the checked buckets.</summary>
    public static IQueryable<Movie> WhereResolutions(IQueryable<Movie> query, IReadOnlyCollection<MovieResolutionFilterOption>? resolutions)
    {
        if (resolutions is not { Count: > 0 }) return query;

        var sd = resolutions.Contains(MovieResolutionFilterOption.Sd);
        var hd = resolutions.Contains(MovieResolutionFilterOption.Hd);
        var fourK = resolutions.Contains(MovieResolutionFilterOption.FourK);
        var eightK = resolutions.Contains(MovieResolutionFilterOption.EightK);
        return query.Where(m =>
            (sd && m.MediaWidth < 1280) ||
            (hd && m.MediaWidth >= 1280 && m.MediaWidth < 3840) ||
            (fourK && m.MediaWidth >= 3840 && m.MediaWidth < 7680) ||
            (eightK && m.MediaWidth >= 7680));
    }

    public static IQueryable<Movie> WhereScanTypes(IQueryable<Movie> query, IReadOnlyCollection<MovieScanTypeFilterOption>? scanTypes)
    {
        if (scanTypes is not { Count: > 0 }) return query;

        var progressive = scanTypes.Contains(MovieScanTypeFilterOption.Progressive);
        var interlaced = scanTypes.Contains(MovieScanTypeFilterOption.Interlaced);
        return query.Where(m =>
            (progressive && m.MediaScanType != null && m.MediaScanType.Trim().ToLower() == "progressive") ||
            (interlaced && m.MediaScanType != null && m.MediaScanType.Trim().ToLower() != "progressive"));
    }

    public static IQueryable<Movie> WhereCodecs(IQueryable<Movie> query, IReadOnlyCollection<string>? codecs) =>
        codecs is { Count: > 0 } ? query.Where(m => m.MediaVideoCodec != null && codecs.Contains(m.MediaVideoCodec)) : query;

    public static IQueryable<Movie> WhereStudios(IQueryable<Movie> query, IReadOnlyCollection<string>? studios) =>
        studios is { Count: > 0 } ? query.Where(m => m.MetaStudio != null && studios.Contains(m.MetaStudio)) : query;

    /// <summary>Actress cup size / measurements / age for a movie: one linked actress must meet every
    /// criterion set. Age is her age on the movie's release date, or on <paramref name="today"/> when it has none.
    /// Cup size is the one she had on the release date, see <see cref="Javbuddy.Models.ActorCupSizePeriod"/>.</summary>
    public static IQueryable<Movie> WhereActorAttributes(IQueryable<Movie> query, ActorAttributeSelection selection, DateOnly today)
    {
        if (selection.IsEmpty) return query;

        var cups = selection.CupSizes.Count > 0 ? selection.CupSizes.Select(c => c.Trim().ToUpperInvariant()).ToList() : null;
        var (hMin, hMax) = (selection.Height.Min, selection.Height.Max);
        var (bMin, bMax) = (selection.Bust.Min, selection.Bust.Max);
        var (wMin, wMax) = (selection.Waist.Min, selection.Waist.Max);
        var (pMin, pMax) = (selection.Hips.Min, selection.Hips.Max);
        var hasAge = selection.Age.IsSet;
        var (ageLo, ageHi) = (selection.Age.Min ?? int.MinValue, selection.Age.Max ?? int.MaxValue);
        var todayDate = today.ToDateTime(TimeOnly.MinValue);

        return query.Where(m => m.MovieActors.Any(ma =>
            // The latest cup size period starting on or before the release, else the regular cup size.
            // Never "" in cups, so the ?? "" just keeps the in-memory evaluation null-safe.
            (cups == null || cups.Contains((ma.Actor.CupSizePeriods
                .Where(p => m.MetaReleaseDate != null && p.EffectiveFrom <= m.MetaReleaseDate)
                .OrderByDescending(p => p.EffectiveFrom)
                .Select(p => p.CupSize)
                .FirstOrDefault() ?? ma.Actor.CupSize ?? "").Trim().ToUpper())) &&
            (hMin == null || (ma.Actor.HeightCm != null && ma.Actor.HeightCm >= hMin)) &&
            (hMax == null || (ma.Actor.HeightCm != null && ma.Actor.HeightCm <= hMax)) &&
            (bMin == null || (ma.Actor.Bust != null && ma.Actor.Bust >= bMin)) &&
            (bMax == null || (ma.Actor.Bust != null && ma.Actor.Bust <= bMax)) &&
            (wMin == null || (ma.Actor.Waist != null && ma.Actor.Waist >= wMin)) &&
            (wMax == null || (ma.Actor.Waist != null && ma.Actor.Waist <= wMax)) &&
            (pMin == null || (ma.Actor.Hips != null && ma.Actor.Hips >= pMin)) &&
            (pMax == null || (ma.Actor.Hips != null && ma.Actor.Hips <= pMax)) &&
            (!hasAge || (ma.Actor.BirthDate != null &&
                ((m.MetaReleaseDate ?? todayDate).Year - ma.Actor.BirthDate.Value.Year - (((m.MetaReleaseDate ?? todayDate).Month < ma.Actor.BirthDate.Value.Month || ((m.MetaReleaseDate ?? todayDate).Month == ma.Actor.BirthDate.Value.Month && (m.MetaReleaseDate ?? todayDate).Day < ma.Actor.BirthDate.Value.Day)) ? 1 : 0)) >= ageLo &&
                ((m.MetaReleaseDate ?? todayDate).Year - ma.Actor.BirthDate.Value.Year - (((m.MetaReleaseDate ?? todayDate).Month < ma.Actor.BirthDate.Value.Month || ((m.MetaReleaseDate ?? todayDate).Month == ma.Actor.BirthDate.Value.Month && (m.MetaReleaseDate ?? todayDate).Day < ma.Actor.BirthDate.Value.Day)) ? 1 : 0)) <= ageHi))));
    }

    /// <summary>The actor-level criteria of <see cref="WhereActorAttributes(IQueryable{Movie}, ActorAttributeSelection, DateOnly)"/>
    /// except Cup Size and Age, which need the movie's release date (the scene wall adds them per scene).</summary>
    public static IQueryable<Actor> WhereActors(IQueryable<Actor> actors, ActorAttributeSelection selection)
    {
        if (!selection.Height.IsSet && !selection.Bust.IsSet && !selection.Waist.IsSet && !selection.Hips.IsSet) return actors;

        var (hMin, hMax) = (selection.Height.Min, selection.Height.Max);
        var (bMin, bMax) = (selection.Bust.Min, selection.Bust.Max);
        var (wMin, wMax) = (selection.Waist.Min, selection.Waist.Max);
        var (pMin, pMax) = (selection.Hips.Min, selection.Hips.Max);

        return actors.Where(a =>
            (hMin == null || (a.HeightCm != null && a.HeightCm >= hMin)) &&
            (hMax == null || (a.HeightCm != null && a.HeightCm <= hMax)) &&
            (bMin == null || (a.Bust != null && a.Bust >= bMin)) &&
            (bMax == null || (a.Bust != null && a.Bust <= bMax)) &&
            (wMin == null || (a.Waist != null && a.Waist >= wMin)) &&
            (wMax == null || (a.Waist != null && a.Waist <= wMax)) &&
            (pMin == null || (a.Hips != null && a.Hips >= pMin)) &&
            (pMax == null || (a.Hips != null && a.Hips <= pMax)));
    }

    /// <summary>OR within the NFO drift group, like Scan Type and Resolution.</summary>
    public static IQueryable<Movie> WhereNfoDriftKinds(IQueryable<Movie> query, IReadOnlyCollection<NfoDriftKind>? kinds) =>
        kinds is { Count: > 0 } ? query.Where(m => kinds.Contains(m.NfoDriftKind)) : query;

    /// <summary>Matched via the canonical Tags/MovieTags relation, not a MetaGenres substring check
    ///. Supports hierarchy rollup: a parent tag matches movies tagged with any of its
    /// subtags, and "Parent##Subtag" or the bare subtag name matches directly.</summary>
    public static IQueryable<Movie> WhereGenres(IQueryable<Movie> query, IReadOnlyCollection<string>? genres)
    {
        if (genres is not { Count: > 0 }) return query;

        var directNames = new HashSet<string>(genres, StringComparer.OrdinalIgnoreCase);
        var parentNames = new HashSet<string>(genres, StringComparer.OrdinalIgnoreCase);
        foreach (var genre in genres)
        {
            if (genre.Contains(Tag.HierarchyDelimiter))
            {
                directNames.Add(genre.Split(Tag.HierarchyDelimiter, 2)[1]);
            }
        }

        return query.Where(m => m.MovieTags.Any(mt =>
            directNames.Contains(mt.Tag.Name) ||
            (mt.Tag.ParentTag != null && parentNames.Contains(mt.Tag.ParentTag.Name))));
    }

    /// <summary>Every selected Features option must hold (AND across the group).</summary>
    public static IQueryable<Movie> WhereFeatures(IQueryable<Movie> query, IReadOnlyCollection<MovieFeatureFilterOption>? features)
    {
        if (features is not { Count: > 0 }) return query;

        if (features.Contains(MovieFeatureFilterOption.HasSubtitles))
        {
            // MediaHasSubtitleFile is a sidecar file (the common case for this library);
            // MediaSubtitleCount is a track embedded in the video container itself (rare, but a
            // movie could have both or either) — "has subtitles" means checking both.
            query = query.Where(m => m.MediaHasSubtitleFile || (m.MediaSubtitleCount != null && m.MediaSubtitleCount > 0));
        }
        if (features.Contains(MovieFeatureFilterOption.HasTrailers))
        {
            query = query.Where(m => m.MediaHasTrailerFile);
        }
        if (features.Contains(MovieFeatureFilterOption.Vr))
        {
            query = query.Where(m => m.VrType != null);
        }
        if (features.Contains(MovieFeatureFilterOption.MultipleVersions))
        {
            query = query.Where(m => m.FileCount > 1);
        }
        if (features.Contains(MovieFeatureFilterOption.UnmatchedActors))
        {
            query = query.Where(m => m.HasUnmatchedActors);
        }
        if (features.Contains(MovieFeatureFilterOption.MissingCast))
        {
            // Gated on MetaFetchedAt so a movie that simply hasn't had metadata fetched yet
            // (MetaActresses null for an unrelated reason) doesn't count as "no cast".
            query = query.Where(m => m.MetaFetchedAt != null && (m.MetaActresses == null || m.MetaActresses == ""));
        }
        if (features.Contains(MovieFeatureFilterOption.BrokenBFrames))
        {
            query = query.Where(m => m.HasBrokenBFrames);
        }
        if (features.Contains(MovieFeatureFilterOption.HasScenes))
        {
            query = query.Where(m => m.Scenes.Any());
        }
        if (features.Contains(MovieFeatureFilterOption.HasFavoriteScene))
        {
            query = query.Where(m => m.Scenes.Any(s => s.IsFavorite));
        }
        if (features.Contains(MovieFeatureFilterOption.Favorites))
        {
            query = query.Where(m => m.IsFavorite);
        }
        return query;
    }

    /// <summary>Genre filter options from linked (tag, parent) name pairs. A subtag contributes
    /// its parent (for rollup filtering) and its compound "Parent##Subtag" path. The dedupe is
    /// case-insensitive because Tags.Name's unique index is case-sensitive at the SQLite level.</summary>
    public static List<string> BuildGenreOptions(IEnumerable<(string Name, string? ParentName)> linkedTags)
    {
        var genreNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, parentName) in linkedTags)
        {
            if (parentName != null)
            {
                genreNames.Add(parentName);
                genreNames.Add($"{parentName}{Tag.HierarchyDelimiter}{name}");
            }
            else
            {
                genreNames.Add(name);
            }
        }
        return genreNames.OrderBy(g => g, StringComparer.OrdinalIgnoreCase).ToList();
    }
}
