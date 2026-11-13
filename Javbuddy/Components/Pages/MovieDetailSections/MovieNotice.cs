namespace Javbuddy.Components.Pages.MovieDetailSections;

/// <summary>A message under Movie Detail's toolbar: the outcome of an action, as a success or a warning.</summary>
public sealed record MovieNotice(string? Message, bool IsWarning);
