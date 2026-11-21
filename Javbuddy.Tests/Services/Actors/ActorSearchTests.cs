using Javbuddy.Models;
using Javbuddy.Services.Actors;
using Javbuddy.Tests.TestSupport;

namespace Javbuddy.Tests.Services.Actors;

public class ActorSearchTests
{
    private static async Task<TestDbContextFactory> SeedAsync()
    {
        var factory = new TestDbContextFactory();
        await using var db = await factory.CreateDbContextAsync();
        db.Actors.AddRange(
            new Actor { FirstName = "Yui", LastName = "Hatano" },
            new Actor { FirstName = "Ichiiyūka", LastName = "Aoi" },
            new Actor { FirstName = "Mia", LastName = "100%_Real" });
        await db.SaveChangesAsync();
        return factory;
    }

    [Theory]
    [InlineData("Hatano Yui")]
    [InlineData("Yui Hatano")]
    [InlineData("hatano yui")]
    [InlineData("ano Yu")]
    public async Task SearchActorsAsync_MatchesFullNameInEitherOrder(string term)
    {
        using var factory = await SeedAsync();

        var found = await new ActorService(factory).SearchActorsAsync(term);

        Assert.Equal(["Hatano Yui"], found.Select(a => a.DisplayName));
    }

    [Fact]
    public async Task SearchActorsAsync_MatchesNonAsciiNamesWithoutCaseFolding()
    {
        using var factory = await SeedAsync();
        var service = new ActorService(factory);

        Assert.Single(await service.SearchActorsAsync("iiyūka"));
        Assert.Empty(await service.SearchActorsAsync("IIYŪKA"));
        Assert.Single(await service.SearchActorsAsync("ICHIIYūKA"));
    }

    [Fact]
    public async Task SearchActorsAsync_TakesWildcardCharactersLiterally()
    {
        using var factory = await SeedAsync();
        var service = new ActorService(factory);

        Assert.Single(await service.SearchActorsAsync("100%_"));
        Assert.Empty(await service.SearchActorsAsync("Y_i"));
    }

    [Fact]
    public async Task GetMergeCandidatesAsync_UsesTheSameFullNameSearch()
    {
        using var factory = await SeedAsync();
        int sourceId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var source = new Actor { FirstName = "Someone", LastName = "Else" };
            db.Actors.Add(source);
            await db.SaveChangesAsync();
            sourceId = source.Id;
        }

        var candidates = await new ActorService(factory).GetMergeCandidatesAsync(sourceId, "Hatano Yui");

        Assert.Equal(["Hatano Yui"], candidates.Select(c => c.DisplayName));
    }
}
