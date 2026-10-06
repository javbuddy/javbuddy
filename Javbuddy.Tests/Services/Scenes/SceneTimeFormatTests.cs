using Javbuddy.Services.Scenes;

namespace Javbuddy.Tests.Services.Scenes;

public class SceneTimeFormatTests
{
    [Theory]
    [InlineData(0, "0:00")]
    [InlineData(65.9, "1:05")]
    [InlineData(3723, "1:02:03")]
    [InlineData(-5, "0:00")]
    public void Format_WholeSeconds(double seconds, string expected) =>
        Assert.Equal(expected, SceneTimeFormat.Format(seconds));

    [Theory]
    [InlineData(65, "1:05")]
    [InlineData(65.5, "1:05.5")]
    [InlineData(65.1234, "1:05.123")]
    [InlineData(3723.25, "1:02:03.25")]
    [InlineData(59.9999, "1:00")]
    public void FormatPrecise_KeepsMilliseconds(double seconds, string expected) =>
        Assert.Equal(expected, SceneTimeFormat.FormatPrecise(seconds));

    [Theory]
    [InlineData("90", 90)]
    [InlineData("1:30", 90)]
    [InlineData(" 1:02:03 ", 3723)]
    [InlineData("12:30.5", 750.5)]
    [InlineData("0:05", 5)]
    [InlineData("75:00", 4500)] // leading component may exceed 59
    public void TryParse_AcceptsSupportedForms(string text, double expected)
    {
        Assert.True(SceneTimeFormat.TryParse(text, out var seconds));
        Assert.Equal(expected, seconds, precision: 6);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("1:60")]
    [InlineData("1:2:3:4")]
    [InlineData("-5")]
    [InlineData("1::30")]
    [InlineData("1.5:30")]
    public void TryParse_RejectsInvalid(string? text) =>
        Assert.False(SceneTimeFormat.TryParse(text, out _));

    [Theory]
    [InlineData(750.5)]
    [InlineData(3723.125)]
    public void FormatPrecise_RoundTripsThroughTryParse(double seconds)
    {
        Assert.True(SceneTimeFormat.TryParse(SceneTimeFormat.FormatPrecise(seconds), out var parsed));
        Assert.Equal(seconds, parsed, precision: 6);
    }
}
