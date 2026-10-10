using Javbuddy.Services.Tags;

namespace Javbuddy.Components.Shared;

/// <summary>An actor tag's name as the pills and the hover popover show it, "Hair › Long" for a subtag.</summary>
public static class ActorTagPillStyle
{
    public static string Label(IReadOnlyList<ActorTagListItem> library, int tagId) =>
        library.FirstOrDefault(t => t.Id == tagId)?.Label ?? $"Tag {tagId}";
}
