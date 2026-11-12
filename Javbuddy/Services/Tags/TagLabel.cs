namespace Javbuddy.Services.Tags;

/// <summary>A tag's name as the UI and the cleanup log show it: "Hair › Long" for a subtag, else the name.</summary>
public static class TagLabel
{
    /// <summary>Between a subtag's parent and name; the actor-tag filter menus nest on it.</summary>
    public const string Separator = " › ";

    public static string Format(string? parentName, string name) => parentName is null ? name : $"{parentName}{Separator}{name}";
}
