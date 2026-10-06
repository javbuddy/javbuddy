using Javbuddy.Services.Actors;

namespace Javbuddy.Components.Pages.ActorsSections;

/// <summary>One optional or fixed column of the Actors table view.</summary>
public sealed record ActorTableColumn(string Key, string Label, string Width, string? SortField, Func<ActorCardModel, string> Value, bool Optional = true);

/// <summary>What the Actors grid cards and table rows show for an actor.</summary>
public static class ActorCardText
{
    public static readonly string[] DefaultTableColumns = ["owned", "missing", "images", "added", "state"];

    public static readonly ActorTableColumn[] TableColumns =
    [
        new("age", "Age", "70px", "age", a => a.Age is int age ? age.ToString() : "—"),
        new("height", "Height", "85px", "height", a => ActorPhysicalAttributesHelper.FormatHeight(a.HeightCm) ?? "—"),
        new("cup", "Cup", "60px", "cup_size", a => a.CupSize ?? "—"),
        new("owned", "Owned", "75px", "owned", a => a.OwnedCount.ToString()),
        new("missing", "Missing", "80px", "missing", a => a.MissingCount.ToString()),
        new("images", "Images", "75px", "image_count", a => a.ImageCount.ToString()),
        new("added", "Added", "110px", "recent", a => a.CreatedAt.ToString("yyyy-MM-dd")),
        new("state", "State", "minmax(160px, 1.5fr)", null, _ => ""),
    ];

    public static string SortFieldLabel(string field) => field switch
    {
        "movie_count" => "Movie Count",
        "owned" => "Owned",
        "missing" => "Missing",
        "age" => "Age",
        "image_count" => "Image Count",
        "recent" => "Recently Added",
        "favorites" => "Favorites",
        "height" => "Height",
        "cup_size" => "Cup Size",
        _ => "Name"
    };

    // Surfaces the value being sorted on directly on each card, since the grid's reordering
    // alone gives no visible indication of the value it's ordering by (unlike Movie Count,
    // Image Count, etc. which are already shown in actor-meta).
    public static bool SortsByAttribute(string sortField) => sortField is "age" or "height" or "cup_size";

    public static string? SortValueBadge(ActorCardModel actor, string sortField) => sortField switch
    {
        "age" => actor.Age is int age ? $"{age} yrs" : null,
        "height" => ActorPhysicalAttributesHelper.FormatHeight(actor.HeightCm),
        "cup_size" => actor.CupSize is null ? null : $"Cup {actor.CupSize}",
        _ => null
    };

    /// <summary>The standard cups plus any other value seen in the data, shortest first ("B" before "AA").</summary>
    public static IReadOnlyList<string> AvailableCupSizes(IEnumerable<ActorCardModel> actors)
    {
        var list = new HashSet<string>(ActorPhysicalAttributesHelper.StandardCupSizes, StringComparer.OrdinalIgnoreCase);
        foreach (var a in actors)
        {
            if (!string.IsNullOrWhiteSpace(a.CupSize))
            {
                list.Add(a.CupSize.Trim().ToUpperInvariant());
            }
        }
        return list.OrderBy(c => c.Length).ThenBy(c => c).ToList();
    }

    public static (int Min, int Max)? HeightBounds(IEnumerable<ActorCardModel> actors)
    {
        var heights = actors.Where(a => a.HeightCm.HasValue).Select(a => a.HeightCm!.Value).ToList();
        return heights.Count == 0 ? null : (heights.Min(), heights.Max());
    }

    public static string ThumbUrl(ActorCardModel actor) =>
        $"/actor-image/{actor.Id}/thumb{(actor.ImageVersion.HasValue ? $"?v={actor.ImageVersion.Value}" : "")}";

    public static string CountsText(ActorCardModel actor) =>
        actor.MovieCount == 0 ? "No movies"
        : actor.MissingCount == 0 ? $"{actor.OwnedCount} owned"
        : $"{actor.OwnedCount} owned · {actor.MissingCount} missing";

    public static bool NeedsAttention(ActorCardModel actor) =>
        !actor.HasImage || !actor.HasMetadata || actor.MovieCount == 0;
}
