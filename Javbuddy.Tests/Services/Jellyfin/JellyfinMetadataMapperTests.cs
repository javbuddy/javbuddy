using Javbuddy.Models;
using Javbuddy.Services.Jellyfin;

namespace Javbuddy.Tests.Services.Jellyfin;

public class JellyfinMetadataMapperTests
{
    [Fact]
    public void ApplyMatch_SetsItemIdServerIdAndCheckedAt()
    {
        var movie = new Movie { Code = "ABC-123" };
        var item = new JellyfinItemDto { Id = "item-1", ServerId = "server-1" };

        JellyfinMetadataMapper.ApplyMatch(movie, item);

        Assert.Equal("item-1", movie.JellyfinItemId);
        Assert.Equal("server-1", movie.JellyfinServerId);
        Assert.NotNull(movie.JellyfinCheckedAt);
    }

    [Fact]
    public void ApplyMatch_LibraryIdAndNamePresent_AreSet()
    {
        var movie = new Movie { Code = "ABC-123" };
        var item = new JellyfinItemDto { Id = "item-1", ServerId = "server-1", LibraryId = "lib-1", LibraryName = "Movies" };

        JellyfinMetadataMapper.ApplyMatch(movie, item);

        Assert.Equal("lib-1", movie.JellyfinLibraryId);
        Assert.Equal("Movies", movie.JellyfinLibraryName);
    }

    [Fact]
    public void ApplyMatch_LibraryIdAndNameBlank_LeavesExistingValuesUnchanged()
    {
        var movie = new Movie { Code = "ABC-123", JellyfinLibraryId = "old-lib", JellyfinLibraryName = "Old Library" };
        var item = new JellyfinItemDto { Id = "item-1", ServerId = "server-1", LibraryId = null, LibraryName = null };

        JellyfinMetadataMapper.ApplyMatch(movie, item);

        Assert.Equal("old-lib", movie.JellyfinLibraryId);
        Assert.Equal("Old Library", movie.JellyfinLibraryName);
    }
}
