using Javbuddy.Models;
using Javbuddy.Services.ActorEnrichment;
using Javbuddy.Services.ActorEnrichment.Sources;
using Javbuddy.Services.Warashi;
using Javbuddy.Tests.TestSupport;
using NSubstitute;

namespace Javbuddy.Tests.Services.Warashi;

public class WarashiActorMetadataSourceTests
{
    [Fact]
    public async Task IsAvailableAsync_ReturnsFalseWhenDisabledOrMissing()
    {
        using var dbFactory = new TestDbContextFactory();
        var client = Substitute.For<IWarashiClient>();
        var source = new WarashiActorMetadataSource(client, dbFactory);

        var available = await source.IsAvailableAsync();

        Assert.False(available);
    }

    [Fact]
    public async Task IsAvailableAsync_ReturnsTrueWhenEnabled()
    {
        using var dbFactory = new TestDbContextFactory();
        await using (var db = await dbFactory.CreateDbContextAsync())
        {
            db.WarashiSettings.Add(new WarashiSettings { Enabled = true });
            await db.SaveChangesAsync();
        }

        var client = Substitute.For<IWarashiClient>();
        var source = new WarashiActorMetadataSource(client, dbFactory);

        var available = await source.IsAvailableAsync();

        Assert.True(available);
    }

    [Fact]
    public async Task LookupAsync_ReturnsEnrichedMetadataWhenMatched()
    {
        using var dbFactory = new TestDbContextFactory();
        await using (var db = await dbFactory.CreateDbContextAsync())
        {
            db.WarashiSettings.Add(new WarashiSettings { Enabled = true });
            await db.SaveChangesAsync();
        }

        var client = Substitute.For<IWarashiClient>();
        client.SearchPerformersAsync("三上悠亜", Arg.Any<CancellationToken>())
            .Returns(new[]
            {
                new WarashiSearchResult(
                    Name: "Yua MIKAMI",
                    JapaneseName: "三上悠亜",
                    PathOrUrl: "/en/s-2-0/yua-mikami/asian-female-pornstar/2922",
                    ImageUrl: null,
                    CareerActivity: "2015 - still active",
                    IsExactMatch: true,
                    KnownAliases: new[] { "Momona KITO" })
            });

        client.GetPerformerDetailAsync("/en/s-2-0/yua-mikami/asian-female-pornstar/2922", Arg.Any<CancellationToken>())
            .Returns(new WarashiPerformerDetail
            {
                Name = "Yua MIKAMI",
                JapaneseName = "三上悠亜",
                HeightCm = 159,
                CupSize = "F",
                Bust = 83,
                Waist = 57,
                Hips = 88,
                BirthDate = new DateTime(1993, 8, 16),
                IsRetired = false,
                Aliases = new List<string> { "Momona KITO" }
            });

        var source = new WarashiActorMetadataSource(client, dbFactory);

        var context = new ActorEnrichmentContext(
            ActorId: 1,
            DisplayName: "Mikami Yua",
            FirstName: "Yua",
            LastName: "Mikami",
            JapaneseNameKanji: "三上悠亜",
            JapaneseNameKana: null,
            ExistingAliases: Array.Empty<string>(),
            LinkedMovieCodes: Array.Empty<string>());

        var result = await source.LookupAsync(context);

        Assert.NotNull(result);
        Assert.Equal("Warashi", result.SourceName);
        Assert.Equal("三上悠亜", result.JapaneseNameKanji);
        Assert.Equal(159, result.HeightCm);
        Assert.Equal("F", result.CupSize);
        Assert.Equal(83, result.Bust);
        Assert.Equal(57, result.Waist);
        Assert.Equal(88, result.Hips);
        Assert.Equal(new DateTime(1993, 8, 16), result.BirthDate);
        Assert.False(result.IsRetired);
        Assert.Contains("Momona KITO", result.Aliases);
    }

    [Fact]
    public void CanAutoEnrich_ReturnsFalse()
    {
        using var dbFactory = new TestDbContextFactory();
        var client = Substitute.For<IWarashiClient>();
        var source = new WarashiActorMetadataSource(client, dbFactory);

        Assert.False(source.CanAutoEnrich);
    }

    [Fact]
    public async Task LookupAsync_ReturnsNullWhenMatchedPerformerHasNoKnownAttributes()
    {
        using var dbFactory = new TestDbContextFactory();
        await using (var db = await dbFactory.CreateDbContextAsync())
        {
            db.WarashiSettings.Add(new WarashiSettings { Enabled = true });
            await db.SaveChangesAsync();
        }

        var client = Substitute.For<IWarashiClient>();
        client.SearchPerformersAsync("伊藤はる", Arg.Any<CancellationToken>())
            .Returns(new[]
            {
                new WarashiSearchResult(
                    Name: "Haru ITÔ",
                    JapaneseName: "伊藤はる",
                    PathOrUrl: "/en/s-4-1/haru-ito/female-pornstar/56136",
                    ImageUrl: null,
                    CareerActivity: null,
                    IsExactMatch: true,
                    KnownAliases: Array.Empty<string>())
            });

        client.GetPerformerDetailAsync("/en/s-4-1/haru-ito/female-pornstar/56136", Arg.Any<CancellationToken>())
            .Returns(new WarashiPerformerDetail
            {
                Name = "Haru ITÔ",
                JapaneseName = "伊藤はる",
                PathOrUrl = "/en/s-4-1/haru-ito/female-pornstar/56136",
                HeightCm = null,
                CupSize = null,
                Bust = null,
                Waist = null,
                Hips = null,
                BirthDate = null,
                IsRetired = null,
                Aliases = new List<string>()
            });

        var source = new WarashiActorMetadataSource(client, dbFactory);

        var context = new ActorEnrichmentContext(
            ActorId: 1,
            DisplayName: "Haru Ito",
            FirstName: "Haru",
            LastName: "Ito",
            JapaneseNameKanji: "伊藤はる",
            JapaneseNameKana: null,
            ExistingAliases: Array.Empty<string>(),
            LinkedMovieCodes: Array.Empty<string>());

        var result = await source.LookupAsync(context);

        Assert.Null(result);
    }
}
