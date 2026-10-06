using Microsoft.AspNetCore.Components;

namespace Javbuddy.Components.Shared;

/// <summary>One checkable item inside a <see cref="MultiSelectFilterGroup"/> — display text,
/// whether it's currently selected, and the click handler that toggles it. Selection is entirely
/// the caller's state (a HashSet, typically) — this just renders what it's given.</summary>
public readonly record struct MultiSelectOption(string Label, bool IsSelected, EventCallback OnClick);
