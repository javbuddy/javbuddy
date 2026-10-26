using Microsoft.AspNetCore.Components;

namespace Javbuddy.Components.Shared;

/// <summary>One checkable item inside a <see cref="MultiSelectFilterGroup"/> — display text,
/// whether it's currently selected, and the click handler that toggles it. Selection is entirely
/// the caller's state (a HashSet, typically) — this just renders what it's given. IsNested marks a subtag whose Label
/// is "Parent › Name": the group indents it under its parent (which comes just before it, still selectable) and shows only
/// the name, until a search is typed and the full label shows flat. IsExcluded marks an option an include/exclude
/// group excludes (shown struck through with ✕); such a group's OnClick steps it through neither → included → excluded.</summary>
public readonly record struct MultiSelectOption(string Label, bool IsSelected, EventCallback OnClick, bool IsNested = false, bool IsExcluded = false)
{
    public const string Separator = Javbuddy.Services.Tags.ActorTagOptions.LabelSeparator;

    /// <summary>The part of a nested label after its parent.</summary>
    public string NestedLabel => IsNested && Label.LastIndexOf(Separator, StringComparison.Ordinal) is var at and >= 0 ? Label[(at + Separator.Length)..] : Label;
}
