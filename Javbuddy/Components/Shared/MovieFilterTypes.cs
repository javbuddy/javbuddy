using Javbuddy.Models;
using Javbuddy.Services.Movies;
using Javbuddy.Services.Nfo;

namespace Javbuddy.Components.Shared;

public enum MovieResolutionFilterOption
{
    Sd,
    Hd,
    FourK,
    EightK,
}

public enum MovieScanTypeFilterOption
{
    Progressive,
    Interlaced,
}

public enum MovieFeatureFilterOption
{
    HasSubtitles,
    HasTrailers,
    MultipleVersions,
    // The former "NFO metadata conflict" / "NFO drift: any" option, superseded by the separate
    // NFO drift filter group. Kept only so saved cookies keep their integer values — never
    // offered or queried; MovieFilterOptions.MigrateLegacyNfoFeatures converts it on load.
    LegacyNfoConflict,
    UnmatchedActors,
    MissingCast,
    BrokenBFrames,
    // Appended last, not listed first: the Movies view-state cookie stores these
    // as their integer values, so inserting earlier would silently remap a saved selection.
    // Display order is set by MovieFilterOptions.Features instead.
    Favorites,
    // Briefly-shipped drift direction options (the first cut), superseded by the NFO drift
    // filter group; legacy for the same reason as LegacyNfoConflict.
    LegacyNfoDriftJavbuddyChanged,
    LegacyNfoDriftExternalEdit,
    LegacyNfoDriftBothChanged,
    //; appended for the same cookie reason as Favorites, shown right after it.
    HasFavoriteScene,
    //; appended for the same cookie reason, shown between Favorites and HasFavoriteScene.
    HasScenes,
    //; appended for the same cookie reason. A movie with any VR / 3D format (Movie.IsVr).
    Vr,
}

public readonly record struct MovieSortChange(string Field, bool Descending, int? RandomSortSeed = null);

public static class MovieFilterOptions
{
    /// <summary>Reconciles a saved Actress Attributes selection with what the library offers now: cup sizes
    /// nobody has any more are dropped, and each range is pulled back inside its bounds (an end at or past an end
    /// stop means "no limit"; none at all when nobody offered has that value). Without this a stale selection would
    /// keep narrowing the grid while the sliders and chips show nothing active.</summary>
    public static ActorAttributeSelection NormalizeActorAttributes(ActorAttributeSelection selection, ActorAttributeOptions options)
    {
        var offered = new HashSet<string>(options.CupSizes, StringComparer.OrdinalIgnoreCase);
        return new ActorAttributeSelection
        {
            CupSizes = selection.CupSizes.Where(offered.Contains).ToList(),
            Height = NormalizeRange(selection.Height, options.Height),
            Bust = NormalizeRange(selection.Bust, options.Bust),
            Waist = NormalizeRange(selection.Waist, options.Waist),
            Hips = NormalizeRange(selection.Hips, options.Hips),
            Age = NormalizeRange(selection.Age, options.Age),
        };
    }

    // The edit comes from a filter that showed the normalised selection, so an untouched range arrives in its
    // clamped form; keeping the remembered one then stops an unrelated edit from widening it (the other
    // Scenes/Highlights view may show it differently).
    private static IntRange MergeRange(IntRange current, IntRange edited, RangeBounds? bounds) =>
        bounds is null || edited == NormalizeRange(current, bounds) ? current : edited;

    private static IntRange NormalizeRange(IntRange range, RangeBounds? bounds)
    {
        if (bounds is not { } b) return IntRange.None;
        int? min = range.Min is { } mn ? (mn <= b.Min ? null : Math.Min(mn, b.Max)) : null;
        int? max = range.Max is { } mx ? (mx >= b.Max ? null : Math.Max(mx, b.Min)) : null;
        return new IntRange(min, max);
    }

    /// <summary>Folds the edits made in a view back into the remembered selection without losing what the
    /// view doesn't offer (kept for the other Scenes/Highlights view).</summary>
    public static ActorAttributeSelection MergeActorAttributes(ActorAttributeSelection current, ActorAttributeSelection edited, ActorAttributeOptions options)
    {
        var offered = new HashSet<string>(options.CupSizes, StringComparer.OrdinalIgnoreCase);
        return new ActorAttributeSelection
        {
            CupSizes = current.CupSizes.Where(c => !offered.Contains(c)).Concat(edited.CupSizes).ToList(),
            Height = MergeRange(current.Height, edited.Height, options.Height),
            Bust = MergeRange(current.Bust, edited.Bust, options.Bust),
            Waist = MergeRange(current.Waist, edited.Waist, options.Waist),
            Hips = MergeRange(current.Hips, edited.Hips, options.Hips),
            Age = MergeRange(current.Age, edited.Age, options.Age),
        };
    }

    public static readonly MovieResolutionFilterOption[] Resolutions = Enum.GetValues<MovieResolutionFilterOption>();
    public static readonly MovieScanTypeFilterOption[] ScanTypes = Enum.GetValues<MovieScanTypeFilterOption>();
    private static readonly Dictionary<MovieFeatureFilterOption, NfoDriftKind[]> LegacyNfoFeatures = new()
    {
        [MovieFeatureFilterOption.LegacyNfoConflict] = [NfoDriftKind.JavbuddyChanged, NfoDriftKind.ExternalEdit, NfoDriftKind.BothChanged, NfoDriftKind.Unreadable],
        [MovieFeatureFilterOption.LegacyNfoDriftJavbuddyChanged] = [NfoDriftKind.JavbuddyChanged],
        [MovieFeatureFilterOption.LegacyNfoDriftExternalEdit] = [NfoDriftKind.ExternalEdit],
        [MovieFeatureFilterOption.LegacyNfoDriftBothChanged] = [NfoDriftKind.BothChanged],
    };

    public static readonly MovieFeatureFilterOption[] Features =
    [
        MovieFeatureFilterOption.Favorites,
        MovieFeatureFilterOption.HasScenes,
        MovieFeatureFilterOption.HasFavoriteScene,
        .. Enum.GetValues<MovieFeatureFilterOption>()
            .Where(f => f is not MovieFeatureFilterOption.Favorites and not MovieFeatureFilterOption.HasScenes and not MovieFeatureFilterOption.HasFavoriteScene && !LegacyNfoFeatures.ContainsKey(f)),
    ];

    /// <summary>The NFO drift filter group's options — every direction a movie can drift
    /// in, OR-combined within the group like Scan Type. Selecting all of them means "any drift".</summary>
    public static readonly NfoDriftKind[] NfoDriftKinds =
        [NfoDriftKind.JavbuddyChanged, NfoDriftKind.ExternalEdit, NfoDriftKind.BothChanged, NfoDriftKind.Unreadable];

    public static string NfoDriftLabel(NfoDriftKind kind) => NfoDriftDetector.Label(kind);

    /// <summary>Moves NFO options saved under Features by an earlier version (the old "any"
    /// option, or the first-cut direction options) into the NFO drift group, so a saved view
    /// keeps showing the same movies after the options moved.</summary>
    public static void MigrateLegacyNfoFeatures(ISet<MovieFeatureFilterOption> features, ISet<NfoDriftKind> nfoDriftKinds)
    {
        foreach (var (legacy, kinds) in LegacyNfoFeatures)
        {
            if (!features.Remove(legacy)) continue;
            nfoDriftKinds.UnionWith(kinds);
        }
    }

    public static string ResolutionLabel(MovieResolutionFilterOption option) => option switch
    {
        MovieResolutionFilterOption.Sd => "SD",
        MovieResolutionFilterOption.Hd => "HD (720p & 1080p)",
        MovieResolutionFilterOption.FourK => "4K",
        MovieResolutionFilterOption.EightK => "8K",
        _ => option.ToString(),
    };

    public static string FeatureLabel(MovieFeatureFilterOption option) => option switch
    {
        MovieFeatureFilterOption.HasSubtitles => "Has subtitles",
        MovieFeatureFilterOption.HasTrailers => "Has trailers",
        MovieFeatureFilterOption.MultipleVersions => "Multiple versions",
        MovieFeatureFilterOption.UnmatchedActors => "Unmatched actors",
        MovieFeatureFilterOption.MissingCast => "Missing actors / No cast",
        MovieFeatureFilterOption.BrokenBFrames => "Broken B-frames",
        MovieFeatureFilterOption.Favorites => "Favorites",
        MovieFeatureFilterOption.HasFavoriteScene => "Has favorite scene",
        MovieFeatureFilterOption.HasScenes => "Has scenes",
        MovieFeatureFilterOption.Vr => "VR",
        _ => option.ToString(),
    };

    public static readonly string[] SortFields = ["added", "title", "size", "status", "release", "runtime", "favorites", "random"];

    public static string SortFieldLabel(string field) => field switch
    {
        "title" => "Title",
        "size" => "Size on disk",
        "status" => "Status",
        "release" => "Release date",
        "runtime" => "Runtime",
        "favorites" => "Favorites",
        "random" => "Random",
        _ => "Added",
    };

    public static bool DefaultSortDescending(string field) => field != "title" && field != "status";
}
