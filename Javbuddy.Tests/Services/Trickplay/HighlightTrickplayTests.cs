using Javbuddy.Services.Trickplay;

namespace Javbuddy.Tests.Services.Trickplay;

public class HighlightTrickplayTests
{
    [Theory]
    [InlineData(30, 1)]
    [InlineData(100, 1)]
    [InlineData(100.5, 2)]
    [InlineData(150, 2)]
    [InlineData(900, 9)]
    public void IntervalSeconds_AimsForOneSheet_AtLeastASecondApart(double length, int expected) =>
        Assert.Equal(expected, HighlightTrickplay.IntervalSeconds(length));

    [Theory]
    [InlineData(900.5)]
    [InlineData(3600)]
    [InlineData(0)]
    [InlineData(-5)]
    public void IntervalSeconds_IsNull_WhenTheMoviesSetIsAsDense_OrTheRangeIsEmpty(double length) =>
        Assert.Null(HighlightTrickplay.IntervalSeconds(length));

    [Fact]
    public void Identity_FollowsTheFileAndTheRange_AndIsAValidSetIdentity()
    {
        var file = TrickplayIdentity.For("ABC-123.mp4", 3600, 1920, 1080)!;
        var other = TrickplayIdentity.For("ABC-123.mkv", 3600, 1920, 1080)!;

        var identity = HighlightTrickplay.Identity(file, 100, 130);

        Assert.True(TrickplayIdentity.IsValid(identity));
        Assert.Equal(identity, HighlightTrickplay.Identity(file, 100.0004, 130));
        Assert.NotEqual(identity, HighlightTrickplay.Identity(file, 100, 131));
        Assert.NotEqual(identity, HighlightTrickplay.Identity(other, 100, 130));
        Assert.NotEqual(file, identity);
        Assert.Null(HighlightTrickplay.Identity(file, 0, 3600));
    }
}
