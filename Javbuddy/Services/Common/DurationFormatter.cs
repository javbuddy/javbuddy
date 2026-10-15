namespace Javbuddy.Services.Common;

public static class DurationFormatter
{
    /// <summary>
    /// An elapsed time as <c>hh:mm:ss</c>, prefixed with <c>Nd </c> once it reaches a day. Negative spans read as zero.
    /// UptimeClock.razor.js mirrors this for the clock it ticks in the browser.
    /// </summary>
    public static string Format(TimeSpan span)
    {
        if (span < TimeSpan.Zero) span = TimeSpan.Zero;
        return span.Days > 0
            ? $"{span.Days}d {span:hh\\:mm\\:ss}"
            : span.ToString(@"hh\:mm\:ss");
    }
}
