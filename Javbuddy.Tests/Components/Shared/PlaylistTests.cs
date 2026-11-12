using Javbuddy.Components.Shared;

namespace Javbuddy.Tests.Components.Shared;

public class PlaylistTests
{
    [Fact]
    public void StartsAtTheFirstEntry()
    {
        var playlist = new Playlist<string>(["a", "b", "c"]);

        Assert.Equal("a", playlist.Current);
        Assert.Equal(1, playlist.Position);
        Assert.Equal(3, playlist.Count);
        Assert.False(playlist.CanGoPrevious);
    }

    [Theory]
    [InlineData(2, "c")]
    [InlineData(-1, "a")]
    [InlineData(9, "c")]
    public void TheStartIndexIsClampedIntoTheList(int start, string expected) =>
        Assert.Equal(expected, new Playlist<string>(["a", "b", "c"], start).Current);

    [Fact]
    public void TryNext_StepsForward_AndStaysOnTheLastEntry()
    {
        var playlist = new Playlist<string>(["a", "b"]);

        Assert.True(playlist.TryNext());
        Assert.Equal("b", playlist.Current);
        Assert.Equal(2, playlist.Position);
        Assert.False(playlist.TryNext());
        Assert.Equal("b", playlist.Current);
    }

    [Fact]
    public void TryPrevious_StepsBack_AndStaysOnTheFirstEntry()
    {
        var playlist = new Playlist<string>(["a", "b"], 1);

        Assert.True(playlist.CanGoPrevious);
        Assert.True(playlist.TryPrevious());
        Assert.Equal("a", playlist.Current);
        Assert.False(playlist.TryPrevious());
        Assert.False(playlist.CanGoPrevious);
    }
}
