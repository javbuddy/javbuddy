using Javbuddy.Services.Common;

namespace Javbuddy.Tests.Services.Common;

public class DurationFormatterTests
{
    [Theory]
    [InlineData(0, "00:00:00")]
    [InlineData(-5, "00:00:00")]
    [InlineData(3_725, "01:02:05")]
    [InlineData(86_399, "23:59:59")]
    [InlineData(90_061, "1d 01:01:01")]
    [InlineData(1_036_800, "12d 00:00:00")]
    public void Format_ShowsHoursMinutesSeconds_WithDaysOnlyWhenThereAreAny(int seconds, string expected) =>
        Assert.Equal(expected, DurationFormatter.Format(TimeSpan.FromSeconds(seconds)));
}
