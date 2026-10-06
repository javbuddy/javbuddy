using Microsoft.AspNetCore.Components;

namespace Javbuddy.Components.Shared;

/// <summary>One button in a <see cref="StatusFilterButtonGroup"/> — label text (including any
/// "(@count)" suffix the caller wants), whether it's the currently-active filter, and the click
/// handler that applies it. Named generically (not "StatusFilterOption") because ActorMissing.razor
/// already has its own private status-filter enum of that name.</summary>
public readonly record struct FilterButtonOption(string Label, bool IsActive, EventCallback OnClick);
