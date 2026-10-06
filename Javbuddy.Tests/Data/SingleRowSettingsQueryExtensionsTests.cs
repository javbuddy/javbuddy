using Javbuddy.Data;
using Javbuddy.Models;
using Javbuddy.Tests.TestSupport;

namespace Javbuddy.Tests.Data;

public class SingleRowSettingsQueryExtensionsTests
{
    [Fact]
    public async Task ReadSingleRowAsync_EmptyTable_ReturnsNull()
    {
        using var factory = new TestDbContextFactory();
        await using var db = await factory.CreateDbContextAsync();

        Assert.Null(await db.JellyfinSettings.ReadSingleRowAsync());
    }

    [Fact]
    public async Task ReadSingleRowAsync_ReturnsTheLowestIdRow_Untracked()
    {
        using var factory = new TestDbContextFactory();
        await using (var seed = await factory.CreateDbContextAsync())
        {
            seed.JellyfinSettings.AddRange(new JellyfinSettings { BaseUrl = "first" }, new JellyfinSettings { BaseUrl = "second" });
            await seed.SaveChangesAsync();
        }

        await using var db = await factory.CreateDbContextAsync();
        var row = await db.JellyfinSettings.ReadSingleRowAsync();

        Assert.Equal("first", row?.BaseUrl);
        Assert.Empty(db.ChangeTracker.Entries());
    }
}
