using Javbuddy.Models;
using Javbuddy.Services.ActorEnrichment;
using Javbuddy.Services.Actors;
using Javbuddy.Services.Images;
using Javbuddy.Services.Warashi;
using Javbuddy.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace Javbuddy.Tests.Services.Actors;

public class ActorServiceWarashiTests
{
    [Fact]
    public async Task FindMatchingActorAsync_MatchesByKanjiOrDisplayName()
    {
        using var dbFactory = new TestDbContextFactory();
        await using (var db = await dbFactory.CreateDbContextAsync())
        {
            db.Actors.Add(new Actor
            {
                FirstName = "Yua",
                LastName = "Mikami",
                JapaneseNameKanji = "三上悠亜"
            });
            await db.SaveChangesAsync();
        }

        var service = new ActorService(dbFactory);

        // Match by Kanji
        var matchByKanji = await service.FindMatchingActorAsync("Some Other Name", "三上悠亜");
        Assert.NotNull(matchByKanji);
        Assert.Equal("Mikami Yua", matchByKanji.DisplayName);

        // Match by Name (Western order)
        var matchByName = await service.FindMatchingActorAsync("Yua Mikami");
        Assert.NotNull(matchByName);
        Assert.Equal("Mikami Yua", matchByName.DisplayName);
    }

    [Fact]
    public async Task FindMatchingActorAsync_MatchesReversedNameAndAliases_ReturningTheFirstActorById_WithAliases()
    {
        using var dbFactory = new TestDbContextFactory();
        int firstId, secondId;
        await using (var db = await dbFactory.CreateDbContextAsync())
        {
            var first = new Actor { FirstName = "Yua", LastName = "Mikami" };
            first.Aliases.Add(new ActorAlias { Name = "Yuachan" });
            var second = new Actor { FirstName = "Shared" };
            second.Aliases.Add(new ActorAlias { Name = "yuachan" });
            second.Aliases.Add(new ActorAlias { Name = "Sharer" });
            db.Actors.AddRange(first, second);
            await db.SaveChangesAsync();
            firstId = first.Id;
            secondId = second.Id;
        }

        var service = new ActorService(dbFactory);

        // An alias both actors carry resolves to the lower ID, as the full-table scan did.
        var byAlias = await service.FindMatchingActorAsync("YUACHAN");
        Assert.Equal(firstId, byAlias?.Id);
        Assert.Single(byAlias!.Aliases);

        // Reversed (Western-order) name.
        Assert.Equal(firstId, (await service.FindMatchingActorAsync("  yua mikami "))?.Id);

        // A provided alias matching another actor's display name or alias.
        var byProvidedAlias = await service.FindMatchingActorAsync("Unknown", aliases: ["Nope", "sharer"]);
        Assert.Equal(secondId, byProvidedAlias?.Id);
        Assert.Equal(2, byProvidedAlias!.Aliases.Count);
        Assert.Equal(secondId, (await service.FindMatchingActorAsync("Unknown", aliases: ["SHARED"]))?.Id);

        Assert.Null(await service.FindMatchingActorAsync("Unknown", aliases: ["Nobody"]));
    }

    [Fact]
    public async Task ImportOrEnrichFromWarashiAsync_EnrichesExistingActor()
    {
        using var dbFactory = new TestDbContextFactory();
        await using (var db = await dbFactory.CreateDbContextAsync())
        {
            db.Actors.Add(new Actor
            {
                FirstName = "Yua",
                LastName = "Mikami",
                JapaneseNameKanji = "三上悠亜"
            });
            await db.SaveChangesAsync();
        }

        var service = new ActorService(dbFactory);
        var detail = new WarashiPerformerDetail
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
        };

        var result = await service.ImportOrEnrichFromWarashiAsync(detail);
        Assert.True(result.Success);

        await using var verifyDb = await dbFactory.CreateDbContextAsync();
        var actor = await verifyDb.Actors.Include(a => a.Aliases).FirstAsync(a => a.FirstName == "Yua");
        Assert.Equal(159, actor.HeightCm);
        Assert.Equal("F", actor.CupSize);
        Assert.Equal(83, actor.Bust);
        Assert.Equal(57, actor.Waist);
        Assert.Equal(88, actor.Hips);
        Assert.Equal(new DateTime(1993, 8, 16), actor.BirthDate);
        Assert.False(actor.IsRetired);
        Assert.Contains(actor.Aliases, a => a.Name == "Momona KITO");
    }

    [Fact]
    public async Task ImportOrEnrichFromWarashiAsync_CreatesNewActorWhenNotTracked()
    {
        using var dbFactory = new TestDbContextFactory();
        var service = new ActorService(dbFactory);

        var detail = new WarashiPerformerDetail
        {
            Name = "Kokoro AMAMI",
            GivenName = "Kokoro",
            FamilyName = "AMAMI",
            JapaneseName = "天海こころ",
            HeightCm = 158,
            CupSize = "B",
            Bust = 80,
            Waist = 60,
            Hips = 86,
            BirthDate = new DateTime(1998, 3, 3),
            IsRetired = false,
            Aliases = new List<string> { "Eimi FUKADA" }
        };

        var result = await service.ImportOrEnrichFromWarashiAsync(detail);
        Assert.True(result.Success);

        await using var verifyDb = await dbFactory.CreateDbContextAsync();
        var actor = await verifyDb.Actors.Include(a => a.Aliases).FirstOrDefaultAsync(a => a.JapaneseNameKanji == "天海こころ");
        Assert.NotNull(actor);
        Assert.Equal("Kokoro", actor.FirstName);
        Assert.Equal("Amami", actor.LastName);
        Assert.Equal("Amami Kokoro", actor.DisplayName);
        Assert.Equal(158, actor.HeightCm);
        Assert.Equal("B", actor.CupSize);
        Assert.Contains(actor.Aliases, a => a.Name == "Eimi FUKADA");
    }

    [Fact]
    public async Task ImportOrEnrichFromWarashiAsync_RespectsSelectiveFieldOptions()
    {
        using var dbFactory = new TestDbContextFactory();
        await using (var db = await dbFactory.CreateDbContextAsync())
        {
            db.Actors.Add(new Actor
            {
                FirstName = "Yua",
                LastName = "Mikami",
                JapaneseNameKanji = "旧三上",
                HeightCm = 160,
                CupSize = "E",
                IsRetired = false
            });
            await db.SaveChangesAsync();
        }

        var service = new ActorService(dbFactory);
        var detail = new WarashiPerformerDetail
        {
            Name = "Yua MIKAMI",
            JapaneseName = "三上悠亜",
            HeightCm = 159,
            CupSize = "F",
            Bust = 83,
            Waist = 57,
            Hips = 88,
            BirthDate = new DateTime(1993, 8, 16),
            IsRetired = true,
            Aliases = new List<string> { "Momona KITO" }
        };

        var options = new WarashiImportOptions
        {
            ImportJapaneseName = false,
            ImportHeight = false,
            ImportCupSize = true,
            ImportMeasurements = true,
            ImportBirthDate = false,
            ImportRetiredStatus = false,
            ImportAliases = false
        };

        var result = await service.ImportOrEnrichFromWarashiAsync(detail, options: options);
        Assert.True(result.Success);

        await using var verifyDb = await dbFactory.CreateDbContextAsync();
        var actor = await verifyDb.Actors.Include(a => a.Aliases).FirstAsync(a => a.FirstName == "Yua");

        // Height and Japanese Name were NOT imported
        Assert.Equal(160, actor.HeightCm);
        Assert.Equal("旧三上", actor.JapaneseNameKanji);
        Assert.Null(actor.BirthDate);
        Assert.False(actor.IsRetired);
        Assert.Empty(actor.Aliases);

        // Cup size and measurements WERE imported
        Assert.Equal("F", actor.CupSize);
        Assert.Equal(83, actor.Bust);
        Assert.Equal(57, actor.Waist);
        Assert.Equal(88, actor.Hips);
    }

    [Fact]
    public async Task ImportOrEnrichFromWarashiAsync_WithImportPhotos_DownloadsImagesUploadsToWapdbAlbumAndSetsPortrait()
    {
        using var dbFactory = new TestDbContextFactory();
        int actorId;
        await using (var db = await dbFactory.CreateDbContextAsync())
        {
            var actor = new Actor
            {
                FirstName = "Yua",
                LastName = "Mikami",
                JapaneseNameKanji = "三上悠亜"
            };
            db.Actors.Add(actor);
            await db.SaveChangesAsync();
            actorId = actor.Id;
        }

        var warashiClient = Substitute.For<IWarashiClient>();
        warashiClient.DownloadImageAsync("https://example.com/p1.jpg", Arg.Any<CancellationToken>())
            .Returns(new byte[] { 1, 2, 3 });
        warashiClient.DownloadImageAsync("https://example.com/p2.jpg", Arg.Any<CancellationToken>())
            .Returns(new byte[] { 4, 5, 6 });

        var imageCacheService = Substitute.For<IActorImageCacheService>();
        imageCacheService.SaveCustomImageAsync(actorId, Arg.Any<byte[]>(), null, Arg.Any<CancellationToken>())
            .Returns((null, null));

        var actorPhotoService = Substitute.For<IActorPhotoService>();
        actorPhotoService.UploadAsync(actorId, Arg.Any<IReadOnlyList<byte[]>>(), Arg.Any<int?>(), Arg.Any<CancellationToken>())
            .Returns(ActorPhotoOperationResult.Ok());

        var service = new ActorService(
            dbFactory,
            imageCacheService: imageCacheService,
            actorPhotoService: actorPhotoService,
            warashiClient: warashiClient);

        var detail = new WarashiPerformerDetail
        {
            Name = "Yua MIKAMI",
            JapaneseName = "三上悠亜",
            MainPhotoUrl = "https://example.com/p1.jpg",
            AdditionalPhotoUrls = new List<string> { "https://example.com/p2.jpg" }
        };

        var options = new WarashiImportOptions { ImportPhotos = true };
        var result = await service.ImportOrEnrichFromWarashiAsync(detail, options: options);
        Assert.True(result.Success);

        // Images were downloaded
        await warashiClient.Received(1).DownloadImageAsync("https://example.com/p1.jpg", Arg.Any<CancellationToken>());
        await warashiClient.Received(1).DownloadImageAsync("https://example.com/p2.jpg", Arg.Any<CancellationToken>());

        // Since actor had no portrait, first image was saved as portrait
        await imageCacheService.Received(1).SaveCustomImageAsync(actorId, Arg.Is<byte[]>(b => b.SequenceEqual(new byte[] { 1, 2, 3 })), null, Arg.Any<CancellationToken>());

        // ActorAlbum "WAPdB" was created in the database
        await using var verifyDb = await dbFactory.CreateDbContextAsync();
        var album = await verifyDb.ActorAlbums.FirstOrDefaultAsync(a => a.ActorId == actorId && a.Name == "WAPdB");
        Assert.NotNull(album);

        // Photos were uploaded to the WAPdB album
        await actorPhotoService.Received(1).UploadAsync(
            actorId,
            Arg.Is<IReadOnlyList<byte[]>>(list => list.Count == 2),
            album.Id,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ImportOrEnrichFromWarashiAsync_WithImportPhotos_WhenActorHasPortrait_DoesNotOverwritePortrait()
    {
        using var dbFactory = new TestDbContextFactory();
        int actorId;
        await using (var db = await dbFactory.CreateDbContextAsync())
        {
            var actor = new Actor
            {
                FirstName = "Yua",
                LastName = "Mikami",
                JapaneseNameKanji = "三上悠亜"
            };
            db.Actors.Add(actor);
            await db.SaveChangesAsync();
            actorId = actor.Id;

            db.ActorImages.Add(new ActorImage
            {
                ActorId = actorId,
                Variant = "thumb",
                SourceMovieCode = "CUSTOM",
                StorageId = Guid.NewGuid(),
                UpdatedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        var warashiClient = Substitute.For<IWarashiClient>();
        warashiClient.DownloadImageAsync("https://example.com/p1.jpg", Arg.Any<CancellationToken>())
            .Returns(new byte[] { 1, 2, 3 });

        var imageCacheService = Substitute.For<IActorImageCacheService>();
        var actorPhotoService = Substitute.For<IActorPhotoService>();
        actorPhotoService.UploadAsync(actorId, Arg.Any<IReadOnlyList<byte[]>>(), Arg.Any<int?>(), Arg.Any<CancellationToken>())
            .Returns(ActorPhotoOperationResult.Ok());

        var service = new ActorService(
            dbFactory,
            imageCacheService: imageCacheService,
            actorPhotoService: actorPhotoService,
            warashiClient: warashiClient);

        var detail = new WarashiPerformerDetail
        {
            Name = "Yua MIKAMI",
            JapaneseName = "三上悠亜",
            MainPhotoUrl = "https://example.com/p1.jpg"
        };

        var options = new WarashiImportOptions { ImportPhotos = true };
        var result = await service.ImportOrEnrichFromWarashiAsync(detail, options: options);
        Assert.True(result.Success);

        // Portrait was NOT overwritten
        await imageCacheService.DidNotReceive().SaveCustomImageAsync(actorId, Arg.Any<byte[]>(), Arg.Any<NormalizedCropRect?>(), Arg.Any<CancellationToken>());

        // Photos were still uploaded to WAPdB album
        await actorPhotoService.Received(1).UploadAsync(actorId, Arg.Any<IReadOnlyList<byte[]>>(), Arg.Any<int?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ImportOrEnrichFromWarashiAsync_WithImportPhotos_ReusesExistingWapdbAlbum()
    {
        using var dbFactory = new TestDbContextFactory();
        int actorId;
        int existingAlbumId;
        await using (var db = await dbFactory.CreateDbContextAsync())
        {
            var actor = new Actor
            {
                FirstName = "Yua",
                LastName = "Mikami",
                JapaneseNameKanji = "三上悠亜"
            };
            db.Actors.Add(actor);
            await db.SaveChangesAsync();
            actorId = actor.Id;

            var existingAlbum = new ActorAlbum
            {
                ActorId = actorId,
                Name = "WAPdB"
            };
            db.ActorAlbums.Add(existingAlbum);
            await db.SaveChangesAsync();
            existingAlbumId = existingAlbum.Id;
        }

        var warashiClient = Substitute.For<IWarashiClient>();
        warashiClient.DownloadImageAsync("https://example.com/p1.jpg", Arg.Any<CancellationToken>())
            .Returns(new byte[] { 1, 2, 3 });

        var imageCacheService = Substitute.For<IActorImageCacheService>();
        var actorPhotoService = Substitute.For<IActorPhotoService>();
        actorPhotoService.UploadAsync(actorId, Arg.Any<IReadOnlyList<byte[]>>(), Arg.Any<int?>(), Arg.Any<CancellationToken>())
            .Returns(ActorPhotoOperationResult.Ok());

        var service = new ActorService(
            dbFactory,
            imageCacheService: imageCacheService,
            actorPhotoService: actorPhotoService,
            warashiClient: warashiClient);

        var detail = new WarashiPerformerDetail
        {
            Name = "Yua MIKAMI",
            JapaneseName = "三上悠亜",
            MainPhotoUrl = "https://example.com/p1.jpg"
        };

        var options = new WarashiImportOptions { ImportPhotos = true };
        var result = await service.ImportOrEnrichFromWarashiAsync(detail, options: options);
        Assert.True(result.Success);

        // Reused existing album ID
        await actorPhotoService.Received(1).UploadAsync(actorId, Arg.Any<IReadOnlyList<byte[]>>(), existingAlbumId, Arg.Any<CancellationToken>());

        // Still only 1 WAPdB album in DB
        await using var verifyDb = await dbFactory.CreateDbContextAsync();
        var albums = await verifyDb.ActorAlbums.Where(a => a.ActorId == actorId && a.Name == "WAPdB").ToListAsync();
        Assert.Single(albums);
    }

    [Fact]
    public async Task ImportOrEnrichFromWarashiAsync_WithImportPhotos_WhenUploadFails_StillSucceedsButReturnsWarning()
    {
        using var dbFactory = new TestDbContextFactory();
        int actorId;
        await using (var db = await dbFactory.CreateDbContextAsync())
        {
            var actor = new Actor
            {
                FirstName = "Yua",
                LastName = "Mikami",
                JapaneseNameKanji = "三上悠亜"
            };
            db.Actors.Add(actor);
            await db.SaveChangesAsync();
            actorId = actor.Id;
        }

        var warashiClient = Substitute.For<IWarashiClient>();
        warashiClient.DownloadImageAsync("https://example.com/p1.jpg", Arg.Any<CancellationToken>())
            .Returns(new byte[] { 1, 2, 3 });

        var imageCacheService = Substitute.For<IActorImageCacheService>();
        imageCacheService.SaveCustomImageAsync(actorId, Arg.Any<byte[]>(), null, Arg.Any<CancellationToken>())
            .Returns((null, null));

        var actorPhotoService = Substitute.For<IActorPhotoService>();
        actorPhotoService.UploadAsync(actorId, Arg.Any<IReadOnlyList<byte[]>>(), Arg.Any<int?>(), Arg.Any<CancellationToken>())
            .Returns(ActorPhotoOperationResult.Fail("Each image must be no larger than 10 MB."));

        var service = new ActorService(
            dbFactory,
            imageCacheService: imageCacheService,
            actorPhotoService: actorPhotoService,
            warashiClient: warashiClient);

        var detail = new WarashiPerformerDetail
        {
            Name = "Yua MIKAMI",
            JapaneseName = "三上悠亜",
            HeightCm = 159,
            MainPhotoUrl = "https://example.com/p1.jpg"
        };

        var options = new WarashiImportOptions { ImportHeight = true, ImportPhotos = true };
        var result = await service.ImportOrEnrichFromWarashiAsync(detail, options: options);

        // The overall import still succeeds — text fields were saved — but the caller is told
        // the photo upload failed instead of it being silently swallowed.
        Assert.True(result.Success);
        Assert.Equal("Each image must be no larger than 10 MB.", result.Warning);

        await using var verifyDb = await dbFactory.CreateDbContextAsync();
        var updatedActor = await verifyDb.Actors.FirstAsync(a => a.Id == actorId);
        Assert.Equal(159, updatedActor.HeightCm);
    }

    [Fact]
    public async Task RefreshMetadataAsync_TargetsLocalSourceOnly_AndDoesNotTargetWarashi()
    {
        using var dbFactory = new TestDbContextFactory();
        var enrichmentService = Substitute.For<IActorEnrichmentService>();
        enrichmentService.EnrichActorAsync(42, Arg.Any<ActorEnrichmentOptions>(), Arg.Any<CancellationToken>())
            .Returns(ActorEnrichmentResult.Ok("Local", 1, ["JapaneseNameKanji"], [], false));

        var service = new ActorService(dbFactory, actorEnrichmentService: enrichmentService);

        var result = await service.RefreshMetadataAsync(42);

        Assert.True(result.Success);
        await enrichmentService.Received(1).EnrichActorAsync(
            42,
            Arg.Is<ActorEnrichmentOptions>(opt => opt.Source == "Local"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ImportOrEnrichFromWarashiAsync_SkipsBirthDateMakingActorUnder18()
    {
        using var dbFactory = new TestDbContextFactory();
        await using (var db = await dbFactory.CreateDbContextAsync())
        {
            db.Actors.Add(new Actor { FirstName = "Yua", LastName = "Mikami", JapaneseNameKanji = "三上悠亜", BirthDate = new DateTime(1993, 8, 16) });
            await db.SaveChangesAsync();
        }

        var service = new ActorService(dbFactory);
        var underage = DateTime.UtcNow.Date.AddYears(-17);

        var enriched = await service.ImportOrEnrichFromWarashiAsync(new WarashiPerformerDetail
        {
            Name = "Yua MIKAMI",
            JapaneseName = "三上悠亜",
            HeightCm = 159,
            BirthDate = underage
        });
        var created = await service.ImportOrEnrichFromWarashiAsync(new WarashiPerformerDetail
        {
            Name = "Remu SUZUMORI",
            BirthDate = underage
        });

        Assert.True(enriched.Success);
        Assert.True(created.Success);
        await using var verifyDb = await dbFactory.CreateDbContextAsync();
        var existing = await verifyDb.Actors.SingleAsync(a => a.FirstName == "Yua");
        Assert.Equal(new DateTime(1993, 8, 16), existing.BirthDate);
        Assert.Equal(159, existing.HeightCm);
        Assert.Null((await verifyDb.Actors.SingleAsync(a => a.FirstName == "Remu")).BirthDate);
    }
}
