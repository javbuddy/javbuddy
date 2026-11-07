using Javbuddy.Models;
using Javbuddy.Services.Actors;
using Javbuddy.Services.Images;
using Javbuddy.Services.MinnanoAv;
using Javbuddy.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace Javbuddy.Tests.Services.Actors;

public class ActorServiceMinnanoAvTests
{
    [Fact]
    public async Task ImportOrEnrichFromMinnanoAvAsync_EnrichesExistingActor()
    {
        using var dbFactory = new TestDbContextFactory();
        await using (var db = await dbFactory.CreateDbContextAsync())
        {
            db.Actors.Add(new Actor
            {
                FirstName = "Miho",
                LastName = "Tohno",
                JapaneseNameKanji = "通野未帆"
            });
            await db.SaveChangesAsync();
        }

        var service = new ActorService(dbFactory);
        var detail = new MinnanoAvPerformerDetail
        {
            Name = "通野未帆",
            Kana = "とおのみほ",
            Romaji = "Tohno Miho",
            HeightCm = 160,
            CupSize = "E",
            Bust = 84,
            Waist = 59,
            Hips = 85,
            BirthDate = new DateTime(1991, 1, 21),
            IsRetired = true,
            Aliases = new List<string> { "澤口美帆" }
        };

        var result = await service.ImportOrEnrichFromMinnanoAvAsync(detail);
        Assert.True(result.Success);

        await using var verifyDb = await dbFactory.CreateDbContextAsync();
        var actor = await verifyDb.Actors.Include(a => a.Aliases).FirstAsync(a => a.FirstName == "Miho");
        Assert.Equal("とおのみほ", actor.JapaneseNameKana);
        Assert.Equal(160, actor.HeightCm);
        Assert.Equal("E", actor.CupSize);
        Assert.Equal(84, actor.Bust);
        Assert.Equal(59, actor.Waist);
        Assert.Equal(85, actor.Hips);
        Assert.Equal(new DateTime(1991, 1, 21), actor.BirthDate);
        Assert.True(actor.IsRetired);
        Assert.Contains(actor.Aliases, a => a.Name == "澤口美帆");
    }

    [Fact]
    public async Task ImportOrEnrichFromMinnanoAvAsync_CreatesNewActorWhenNotTracked_UsingFamilyFirstRomajiOrder()
    {
        using var dbFactory = new TestDbContextFactory();
        var service = new ActorService(dbFactory);

        var detail = new MinnanoAvPerformerDetail
        {
            Name = "通野未帆",
            Kana = "とおのみほ",
            Romaji = "Tohno Miho",
            HeightCm = 160,
            CupSize = "E",
            Bust = 84,
            Waist = 59,
            Hips = 85,
            BirthDate = new DateTime(1991, 1, 21),
            IsRetired = false,
            Aliases = new List<string> { "澤口美帆" }
        };

        var result = await service.ImportOrEnrichFromMinnanoAvAsync(detail);
        Assert.True(result.Success);

        await using var verifyDb = await dbFactory.CreateDbContextAsync();
        var actor = await verifyDb.Actors.Include(a => a.Aliases).FirstOrDefaultAsync(a => a.JapaneseNameKanji == "通野未帆");
        Assert.NotNull(actor);

        // "Tohno Miho" is family-name-first on minnano-av.com, matching Javbuddy's own
        // LastName-FirstName DisplayName convention directly.
        Assert.Equal("Tohno", actor.LastName);
        Assert.Equal("Miho", actor.FirstName);
        Assert.Equal("Tohno Miho", actor.DisplayName);
        Assert.Equal("とおのみほ", actor.JapaneseNameKana);
        Assert.Equal(160, actor.HeightCm);
        Assert.Contains(actor.Aliases, a => a.Name == "澤口美帆");
    }

    [Fact]
    public async Task ImportOrEnrichFromMinnanoAvAsync_RespectsSelectiveFieldOptions()
    {
        using var dbFactory = new TestDbContextFactory();
        await using (var db = await dbFactory.CreateDbContextAsync())
        {
            db.Actors.Add(new Actor
            {
                FirstName = "Miho",
                LastName = "Tohno",
                JapaneseNameKanji = "旧通野",
                HeightCm = 160,
                CupSize = "E",
                IsRetired = false
            });
            await db.SaveChangesAsync();
        }

        var service = new ActorService(dbFactory);
        var detail = new MinnanoAvPerformerDetail
        {
            Name = "通野未帆",
            Kana = "とおのみほ",
            Romaji = "Tohno Miho",
            HeightCm = 158,
            CupSize = "D",
            Bust = 83,
            Waist = 58,
            Hips = 86,
            BirthDate = new DateTime(1991, 1, 21),
            IsRetired = true,
            Aliases = new List<string> { "澤口美帆" }
        };

        var options = new MinnanoAvImportOptions
        {
            ImportJapaneseName = false,
            ImportHeight = false,
            ImportCupSize = true,
            ImportMeasurements = true,
            ImportBirthDate = false,
            ImportRetiredStatus = false,
            ImportAliases = false
        };

        var result = await service.ImportOrEnrichFromMinnanoAvAsync(detail, options: options);
        Assert.True(result.Success);

        await using var verifyDb = await dbFactory.CreateDbContextAsync();
        var actor = await verifyDb.Actors.Include(a => a.Aliases).FirstAsync(a => a.FirstName == "Miho");

        // Height and Japanese Name were NOT imported
        Assert.Equal(160, actor.HeightCm);
        Assert.Equal("旧通野", actor.JapaneseNameKanji);
        Assert.Null(actor.BirthDate);
        Assert.False(actor.IsRetired);
        Assert.Empty(actor.Aliases);

        // Cup size and measurements WERE imported
        Assert.Equal("D", actor.CupSize);
        Assert.Equal(83, actor.Bust);
        Assert.Equal(58, actor.Waist);
        Assert.Equal(86, actor.Hips);
    }

    [Fact]
    public async Task ImportOrEnrichFromMinnanoAvAsync_WithImportImage_SavesDownloadedImageAsPortrait()
    {
        using var dbFactory = new TestDbContextFactory();
        int actorId;
        await using (var db = await dbFactory.CreateDbContextAsync())
        {
            var actor = new Actor { FirstName = "Miho", LastName = "Tohno" };
            db.Actors.Add(actor);
            await db.SaveChangesAsync();
            actorId = actor.Id;
        }

        var imageCache = Substitute.For<IActorImageCacheService>();
        imageCache.DownloadImageFromUrlAsync("https://img.example/a.jpg", Arg.Any<CancellationToken>())
            .Returns(new ActorImageDownloadResult(true, new byte[] { 1, 2, 3 }, "image/jpeg"));
        imageCache.SaveCustomImageAsync(actorId, Arg.Any<byte[]>(), null, Arg.Any<CancellationToken>()).Returns((null, null));

        var service = new ActorService(dbFactory, imageCacheService: imageCache);
        var detail = new MinnanoAvPerformerDetail { Name = "通野未帆", Romaji = "Tohno Miho", ImageUrl = "https://img.example/a.jpg" };

        var result = await service.ImportOrEnrichFromMinnanoAvAsync(detail, actorId, new MinnanoAvImportOptions { ImportImage = true });

        Assert.True(result.Success);
        Assert.Null(result.Warning);
        await imageCache.Received(1).SaveCustomImageAsync(
            actorId, Arg.Is<byte[]>(b => b.SequenceEqual(new byte[] { 1, 2, 3 })), null, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ImportOrEnrichFromMinnanoAvAsync_WhenImageDownloadFails_StillImportsFieldsAndWarns()
    {
        using var dbFactory = new TestDbContextFactory();
        int actorId;
        await using (var db = await dbFactory.CreateDbContextAsync())
        {
            var actor = new Actor { FirstName = "Miho", LastName = "Tohno" };
            db.Actors.Add(actor);
            await db.SaveChangesAsync();
            actorId = actor.Id;
        }

        var imageCache = Substitute.For<IActorImageCacheService>();
        imageCache.DownloadImageFromUrlAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new ActorImageDownloadResult(false, ErrorMessage: "HTTP 403"));

        var service = new ActorService(dbFactory, imageCacheService: imageCache);
        var detail = new MinnanoAvPerformerDetail { Name = "通野未帆", Romaji = "Tohno Miho", HeightCm = 160, ImageUrl = "https://img.example/a.jpg" };

        var result = await service.ImportOrEnrichFromMinnanoAvAsync(detail, actorId, new MinnanoAvImportOptions { ImportImage = true });

        Assert.True(result.Success);
        Assert.Contains("HTTP 403", result.Warning);
        await using var verifyDb = await dbFactory.CreateDbContextAsync();
        Assert.Equal(160, (await verifyDb.Actors.FirstAsync(a => a.Id == actorId)).HeightCm);
        await imageCache.DidNotReceive().SaveCustomImageAsync(Arg.Any<int>(), Arg.Any<byte[]>(), null, Arg.Any<CancellationToken>());
    }
}
