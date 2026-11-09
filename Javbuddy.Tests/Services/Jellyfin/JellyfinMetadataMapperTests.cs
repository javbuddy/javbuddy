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

}
