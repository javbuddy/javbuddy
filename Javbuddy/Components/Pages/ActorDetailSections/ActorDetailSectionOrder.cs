namespace Javbuddy.Components.Pages.ActorDetailSections;

/// <summary>A section of Actor Detail below the hero card.</summary>
public enum ActorDetailSection
{
    Photos,
    Movies,
    Scenes,
    Highlights,
    Apexes,
}

/// <summary>The order of Actor Detail's sections, set in Settings > UI and kept per browser
/// in a cookie as the sections' names, comma-separated ("Movies,Photos,Scenes,Highlights,Apexes").
/// Parsing is forgiving: unknown or repeated names are dropped and sections the value leaves out keep
/// their default place after the listed ones, so a cookie saved before a section existed still works.</summary>
public static class ActorDetailSectionOrder
{
    public const string CookieName = "ls-actor-detail-sections";

    public static IReadOnlyList<ActorDetailSection> Default { get; } =
        [ActorDetailSection.Photos, ActorDetailSection.Movies, ActorDetailSection.Scenes, ActorDetailSection.Highlights, ActorDetailSection.Apexes];

    public static IReadOnlyList<ActorDetailSection> Parse(string? value)
    {
        var order = new List<ActorDetailSection>();
        foreach (var name in (value ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (Enum.TryParse<ActorDetailSection>(name, ignoreCase: true, out var section)
                && Enum.IsDefined(section)
                && !int.TryParse(name, out _)
                && !order.Contains(section))
            {
                order.Add(section);
            }
        }
        order.AddRange(Default.Where(s => !order.Contains(s)));
        return order;
    }

    public static string Serialize(IEnumerable<ActorDetailSection> order) => string.Join(',', order);

    /// <summary>The order with the section at index moved one place up (-1) or down (+1); unchanged at either end.</summary>
    public static IReadOnlyList<ActorDetailSection> Move(IReadOnlyList<ActorDetailSection> order, int index, int direction)
    {
        var target = index + direction;
        if (index < 0 || index >= order.Count || target < 0 || target >= order.Count) return order;
        var moved = order.ToList();
        (moved[index], moved[target]) = (moved[target], moved[index]);
        return moved;
    }

    /// <summary>The order the request's cookie holds, for the prerender's first paint. A live circuit's
    /// HttpContext can hold a stale cookie, so the page checks the browser's again once
    /// it's interactive.</summary>
    public static IReadOnlyList<ActorDetailSection> Load(IHttpContextAccessor? httpContextAccessor) =>
        Parse(httpContextAccessor?.HttpContext?.Request.Cookies[CookieName]);
}
