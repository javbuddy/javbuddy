using Javbuddy.Services.Scenes;

namespace Javbuddy.Components.Shared;

/// <summary>One grayed, read-only chip <see cref="ImplicitChips"/> draws: a tag a scene, highlight or the movie
/// shows only because something inside it carries it, or an actor a scene, highlight or apex inherits.</summary>
public sealed record ImplicitChip(string Label, string Tooltip)
{
    /// <summary>The actor an inherited-actor chip stands for; null for a tag chip.</summary>
    public int? ActorId { get; init; }

    /// <summary>Where an inherited actor comes from ("scene 2"); null for a tag chip.</summary>
    public string? From { get; init; }

    public static IReadOnlyList<ImplicitChip> ForTags(IEnumerable<ImplicitTag> tags) =>
        tags.Select(t => new ImplicitChip(TagLabel(t.Tag), "From " + string.Join(", ", t.Sources))).ToList();

    /// <summary>The inherited actors, each labelled with the parent they come from; nothing for explicit ones.</summary>
    public static IReadOnlyList<ImplicitChip> ForActors(EffectiveActors actors) =>
        actors.IsInherited
            ? actors.Actors.Select(a => new ImplicitChip(a.Name, "From " + actors.From) { ActorId = a.ActorId, From = actors.From }).ToList()
            : [];

    /// <summary>A redundant explicit tag's tooltip: where it comes from anyway.</summary>
    public static string RedundantTagTooltip(IEnumerable<string> sources) =>
        $"Also from {string.Join(", ", sources)}. Remove it and it stays, grayed, as implied.";

    /// <summary>An inherited tag's tooltip when where it comes from isn't known (the wall's cards).</summary>
    public const string InheritedTagTooltip = "Inherited: passed down from the movie, its scene or a highlight, not added on this clip";

    /// <summary>An inherited tag's tooltip: where it comes from.</summary>
    public static string InheritedFromTooltip(IEnumerable<string> sources) =>
        $"Inherited from {string.Join(", ", sources)}, not added on this clip";

    /// <summary>An own tag's tooltip when the clip also inherits it: removed, it stays as an inherited tag.</summary>
    public static string RedundantInheritedTooltip(IEnumerable<string> sources) =>
        $"Also inherited from {string.Join(", ", sources)}. Remove it and it stays, as an inherited ↳ tag.";

    public static string TagLabel(SceneTagItem tag) => Javbuddy.Services.Tags.TagLabel.Format(tag.ParentName, tag.Name);
}
