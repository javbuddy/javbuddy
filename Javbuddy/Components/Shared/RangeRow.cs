using Javbuddy.Services.Movies;

namespace Javbuddy.Components.Shared;

/// <summary>One slider of a <see cref="RangeFilterRows{TSelection}"/>: its label and unit, the bounds the options
/// offer (null hides it), the applied range, and how to put an edited range back into the selection.</summary>
public sealed record RangeRow<TSelection>(string Label, string Unit, RangeBounds? Bounds, IntRange Selected, Func<TSelection, IntRange, TSelection> With);

public static class RangeCommit
{
    /// <summary>The range a slider's thumbs commit: a thumb at its end stop means "no limit" on that side, so a value
    /// past today's extremes isn't excluded.</summary>
    public static IntRange ToIntRange((int Min, int Max) thumbs, RangeBounds bounds) =>
        new(thumbs.Min <= bounds.Min ? null : thumbs.Min, thumbs.Max >= bounds.Max ? null : thumbs.Max);
}
