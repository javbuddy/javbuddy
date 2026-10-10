using Javbuddy.Services.Scenes;

namespace Javbuddy.Components.Shared;

/// <summary>One grayed, read-only chip <see cref="ImplicitChips"/> draws: a tag a scene, highlight or the movie
/// shows only because something inside it carries it, or an actor a scene, highlight or apex inherits.</summary>
public sealed record ImplicitChip(string Label, string Tooltip)
{
    /// <summary>The actor an inherited-actor chip stands for; null for a tag chip.</summary>
    public int? ActorId { get; init; }

    public static IReadOnlyList<ImplicitChip> ForTags(IEnumerable<ImplicitTag> tags) =>
        tags.Select(t => new ImplicitChip(TagLabel(t.Tag), "From " + string.Join(", ", t.Sources))).ToList();

    /// <summary>The inherited actors, each labelled with the parent they come from; nothing for explicit ones.</summary>
    public static IReadOnlyList<ImplicitChip> ForActors(EffectiveActors actors) =>
        actors.IsInherited
            ? actors.Actors.Select(a => new ImplicitChip(a.Name, "From " + actors.From) { ActorId = a.ActorId }).ToList()
            : [];

    /// <summary>A redundant explicit tag's tooltip: where it comes from anyway.</summary>
    public static string RedundantTagTooltip(IEnumerable<string> sources) =>
        $"Also from {string.Join(", ", sources)}. Remove it and it stays, grayed, as implied.";

    public static string TagLabel(SceneTagItem tag) => tag.ParentName is null ? tag.Name : $"{tag.ParentName} › {tag.Name}";
}
