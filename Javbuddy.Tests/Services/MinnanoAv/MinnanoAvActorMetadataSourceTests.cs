using Javbuddy.Models;
using Javbuddy.Services.ActorEnrichment;
using Javbuddy.Services.ActorEnrichment.Sources;
using Javbuddy.Services.MinnanoAv;
using Javbuddy.Tests.TestSupport;
using NSubstitute;

namespace Javbuddy.Tests.Services.MinnanoAv;

public class MinnanoAvActorMetadataSourceTests
{
    [Fact]
    public async Task IsAvailableAsync_ReturnsFalseWhenDisabledOrMissing()
    {
        using var dbFactory = new TestDbContextFactory();
        var client = Substitute.For<IMinnanoAvClient>();
        var source = new MinnanoAvActorMetadataSource(client, dbFactory);

        var available = await source.IsAvailableAsync();

        Assert.False(available);
    }

    [Fact]
    public async Task IsAvailableAsync_ReturnsTrueWhenEnabled()
    {
        using var dbFactory = new TestDbContextFactory();
        await using (var db = await dbFactory.CreateDbContextAsync())
        {
            db.MinnanoAvSettings.Add(new MinnanoAvSettings { Enabled = true });
            await db.SaveChangesAsync();
        }

        var client = Substitute.For<IMinnanoAvClient>();
        var source = new MinnanoAvActorMetadataSource(client, dbFactory);

        var available = await source.IsAvailableAsync();

        Assert.True(available);
    }

    [Fact]
    public async Task LookupAsync_ReturnsEnrichedMetadataWhenMatched()
    {
        using var dbFactory = new TestDbContextFactory();
        await using (var db = await dbFactory.CreateDbContextAsync())
        {
            db.MinnanoAvSettings.Add(new MinnanoAvSettings { Enabled = true });
            await db.SaveChangesAsync();
        }

        var client = Substitute.For<IMinnanoAvClient>();
        client.SearchPerformersAsync("通野未帆", Arg.Any<CancellationToken>())
            .Returns(new[]
            {
                new MinnanoAvSearchResult(
                    Name: "通野未帆",
                    Kana: "とおのみほ",
                    Romaji: "Tohno Miho",
                    PathOrUrl: "actress148426.html",
                    ImageUrl: null,
                    DebutInfo: null,
                    IsExactMatch: true,
                    KnownAliases: new[] { "澤口美帆" })
            });

        client.GetPerformerDetailAsync("actress148426.html", Arg.Any<CancellationToken>())
            .Returns(new MinnanoAvPerformerDetail
            {
                Name = "通野未帆",
                Kana = "とおのみほ",
                Romaji = "Tohno Miho",
                PathOrUrl = "actress148426.html",
                HeightCm = 160,
                CupSize = "E",
                Bust = 84,
                Waist = 59,
                Hips = 85,
                BirthDate = new DateTime(1991, 1, 21),
                IsRetired = true,
                Aliases = new List<string> { "澤口美帆" }
            });

        var source = new MinnanoAvActorMetadataSource(client, dbFactory);

        var context = new ActorEnrichmentContext(
            ActorId: 1,
            DisplayName: "Tohno Miho",
            FirstName: "Miho",
            LastName: "Tohno",
            JapaneseNameKanji: "通野未帆",
            JapaneseNameKana: null,
            ExistingAliases: Array.Empty<string>(),
            LinkedMovieCodes: Array.Empty<string>());

        var result = await source.LookupAsync(context);

        Assert.NotNull(result);
        Assert.Equal("MinnanoAv", result.SourceName);
        Assert.Equal("通野未帆", result.JapaneseNameKanji);
        Assert.Equal("とおのみほ", result.JapaneseNameKana);
        Assert.Equal(160, result.HeightCm);
        Assert.Equal("E", result.CupSize);
        Assert.Equal(84, result.Bust);
        Assert.Equal(59, result.Waist);
        Assert.Equal(85, result.Hips);
        Assert.Equal(new DateTime(1991, 1, 21), result.BirthDate);
        Assert.True(result.IsRetired);
        Assert.Contains("澤口美帆", result.Aliases);
    }

    [Fact]
    public void CanAutoEnrich_ReturnsFalse()
    {
        using var dbFactory = new TestDbContextFactory();
        var client = Substitute.For<IMinnanoAvClient>();
        var source = new MinnanoAvActorMetadataSource(client, dbFactory);

        Assert.False(source.CanAutoEnrich);
    }

    [Fact]
    public async Task LookupAsync_ReturnsNullWhenMatchedPerformerHasNoKnownAttributes()
    {
        using var dbFactory = new TestDbContextFactory();
        await using (var db = await dbFactory.CreateDbContextAsync())
        {
            db.MinnanoAvSettings.Add(new MinnanoAvSettings { Enabled = true });
            await db.SaveChangesAsync();
        }

        var client = Substitute.For<IMinnanoAvClient>();
        client.SearchPerformersAsync("伊藤はる", Arg.Any<CancellationToken>())
            .Returns(new[]
            {
                new MinnanoAvSearchResult(
                    Name: "伊藤はる",
                    Kana: "いとうはる",
                    Romaji: "Ito Haru",
                    PathOrUrl: "actress99999.html",
                    ImageUrl: null,
                    DebutInfo: null,
                    IsExactMatch: true,
                    KnownAliases: Array.Empty<string>())
            });

        client.GetPerformerDetailAsync("actress99999.html", Arg.Any<CancellationToken>())
            .Returns(new MinnanoAvPerformerDetail
            {
                Name = "伊藤はる",
                Kana = "いとうはる",
                Romaji = "Ito Haru",
                PathOrUrl = "actress99999.html",
                Aliases = new List<string>()
            });

        var source = new MinnanoAvActorMetadataSource(client, dbFactory);

        var context = new ActorEnrichmentContext(
            ActorId: 1,
            DisplayName: "Ito Haru",
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
