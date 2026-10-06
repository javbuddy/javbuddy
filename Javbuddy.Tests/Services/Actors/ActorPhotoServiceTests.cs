using Javbuddy.Models;
using Javbuddy.Services.Actors;
using Javbuddy.Services.Images;
using Javbuddy.Services.Infrastructure;
using Javbuddy.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using SkiaSharp;

namespace Javbuddy.Tests.Services.Actors;

public class ActorPhotoServiceTests : IDisposable
{
    private readonly string cacheRoot = Path.Combine(Path.GetTempPath(), $"javbuddy-actor-photo-tests-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(cacheRoot)) Directory.Delete(cacheRoot, recursive: true);
    }

    [Fact]
    public async Task GetGalleryAsync_ReturnsAlbumsWithCountsAndNewestPhotosFirst()
    {
        using var factory = new TestDbContextFactory();
        int actorId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var actor = new Actor { FirstName = "Yua", LastName = "Mikami" };
            db.Actors.Add(actor);
            await db.SaveChangesAsync();
            actorId = actor.Id;
            var album = new ActorAlbum { ActorId = actorId, Name = "Events" };
            db.ActorAlbums.Add(album);
            await db.SaveChangesAsync();
            db.ActorPhotos.AddRange(
                new ActorPhoto { ActorId = actorId, AlbumId = album.Id, AspectRatio = 1.5, UploadedAt = DateTime.UtcNow.AddMinutes(-1) },
                new ActorPhoto { ActorId = actorId, AspectRatio = 0.75, UploadedAt = DateTime.UtcNow });
            await db.SaveChangesAsync();
        }

        var service = CreateService(factory);
        var gallery = await service.GetGalleryAsync(actorId);

        var albumResult = Assert.Single(gallery.Albums);
        Assert.Equal("Events", albumResult.Name);
        Assert.Equal(1, albumResult.PhotoCount);
        Assert.Null(gallery.Photos[0].AlbumId);
        Assert.Equal(0.75, gallery.Photos[0].AspectRatio);
        Assert.Equal(albumResult.Id, gallery.Photos[1].AlbumId);
        Assert.Equal(1.5, gallery.Photos[1].AspectRatio);
    }

    [Fact]
    public async Task GetGalleryAsync_BreaksUploadTimeTiesByNewestId()
    {
        using var factory = new TestDbContextFactory();
        int actorId;
        var uploadedAt = DateTime.UtcNow;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var actor = new Actor { FirstName = "Yua", LastName = "Mikami" };
            db.Actors.Add(actor);
            await db.SaveChangesAsync();
            actorId = actor.Id;
            db.ActorPhotos.AddRange(
                new ActorPhoto { ActorId = actorId, UploadedAt = uploadedAt },
                new ActorPhoto { ActorId = actorId, UploadedAt = uploadedAt },
                new ActorPhoto { ActorId = actorId, UploadedAt = uploadedAt });
            await db.SaveChangesAsync();
        }

        var gallery = await CreateService(factory).GetGalleryAsync(actorId);

        Assert.Equal(gallery.Photos.Select(photo => photo.Id).OrderDescending(), gallery.Photos.Select(photo => photo.Id));
    }

    [Fact]
    public async Task DeleteAlbumAsync_RemovesItsPhotosAndStorageFiles()
    {
        using var factory = new TestDbContextFactory();
        int actorId;
        int albumId;
        var thumbId = Guid.NewGuid();
        var fullId = Guid.NewGuid();
        await using (var db = await factory.CreateDbContextAsync())
        {
            var actor = new Actor { FirstName = "Yua", LastName = "Mikami" };
            db.Actors.Add(actor);
            await db.SaveChangesAsync();
            actorId = actor.Id;
            var album = new ActorAlbum { ActorId = actorId, Name = "Gravure" };
            db.ActorAlbums.Add(album);
            await db.SaveChangesAsync();
            albumId = album.Id;
            db.ActorPhotos.Add(new ActorPhoto { ActorId = actorId, AlbumId = albumId, ThumbStorageId = thumbId, FullStorageId = fullId });
            await db.SaveChangesAsync();
        }
        Directory.CreateDirectory(cacheRoot);
        await File.WriteAllBytesAsync(StoragePath(thumbId), [1]);
        await File.WriteAllBytesAsync(StoragePath(fullId), [2]);

        var result = await CreateService(factory).DeleteAlbumAsync(actorId, albumId);

        Assert.True(result.Success);
        Assert.False(File.Exists(StoragePath(thumbId)));
        Assert.False(File.Exists(StoragePath(fullId)));
        await using var verify = await factory.CreateDbContextAsync();
        Assert.Empty(await verify.ActorPhotos.ToListAsync());
        Assert.Empty(await verify.ActorAlbums.ToListAsync());
    }

    [Fact]
    public async Task UploadAsync_ConvertsAndStoresBothImageVariants()
    {
        using var factory = new TestDbContextFactory();
        int actorId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var actor = new Actor { FirstName = "Yua", LastName = "Mikami" };
            db.Actors.Add(actor);
            await db.SaveChangesAsync();
            actorId = actor.Id;
        }

        using var bitmap = new SKBitmap(2, 2);
        bitmap.Erase(SKColors.CornflowerBlue);
        using var skImage = SKImage.FromBitmap(bitmap);
        using var encoded = skImage.Encode(SKEncodedImageFormat.Png, 100);
        var image = encoded.ToArray();
        var result = await CreateService(factory).UploadAsync(actorId, [image], albumId: null);

        Assert.True(result.Success);
        await using var verify = await factory.CreateDbContextAsync();
        var photo = await verify.ActorPhotos.SingleAsync();
        Assert.True(File.Exists(StoragePath(photo.ThumbStorageId)));
        Assert.True(File.Exists(StoragePath(photo.FullStorageId)));
    }

    [Fact]
    public async Task UploadAsync_PreservesSourceAndLazilyGeneratesVariantsOnFirstAccess()
    {
        using var factory = new TestDbContextFactory();
        int actorId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var actor = new Actor { FirstName = "Yua", LastName = "Mikami" };
            db.Actors.Add(actor);
            await db.SaveChangesAsync();
            actorId = actor.Id;
        }

        using var bitmap = new SKBitmap(2, 2);
        bitmap.Erase(SKColors.CornflowerBlue);
        using var skImage = SKImage.FromBitmap(bitmap);
        using var encoded = skImage.Encode(SKEncodedImageFormat.Png, 100);
        var image = encoded.ToArray();
        var dataStore = new InMemoryDataStore();
        var service = CreateService(factory, dataStore);

        Assert.True((await service.UploadAsync(actorId, [image], albumId: null)).Success);
        await using var verify = await factory.CreateDbContextAsync();
        var photo = await verify.ActorPhotos.SingleAsync();
        var sourceStorageId = Assert.IsType<Guid>(photo.SourceStorageId);
        Assert.Equal(".jpg", photo.SourceExtension);
        Assert.Equal(image, await dataStore.ReadAsync(sourceStorageId, photo.SourceExtension));
        Assert.False(File.Exists(StoragePath(photo.ThumbStorageId)));
        Assert.False(File.Exists(StoragePath(photo.FullStorageId)));

        var regeneratedPath = (await service.GetImageAsync(photo.Id, ActorImageCacheService.VariantFull)).FilePath;
        Assert.Equal(StoragePath(photo.FullStorageId), regeneratedPath);
        Assert.True(File.Exists(regeneratedPath));
    }

    [Fact]
    public async Task MovePhotoAsync_RejectsAlbumOwnedByAnotherActor()
    {
        using var factory = new TestDbContextFactory();
        int firstActorId;
        int secondAlbumId;
        int photoId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var first = new Actor { FirstName = "Yua", LastName = "Mikami" };
            var second = new Actor { FirstName = "Ai", LastName = "Uehara" };
            db.Actors.AddRange(first, second);
            await db.SaveChangesAsync();
            firstActorId = first.Id;
            var album = new ActorAlbum { ActorId = second.Id, Name = "Private" };
            var photo = new ActorPhoto { ActorId = firstActorId };
            db.AddRange(album, photo);
            await db.SaveChangesAsync();
            secondAlbumId = album.Id;
            photoId = photo.Id;
        }

        var result = await CreateService(factory).MovePhotoAsync(firstActorId, photoId, secondAlbumId);

        Assert.False(result.Success);
        await using var verify = await factory.CreateDbContextAsync();
        Assert.Null((await verify.ActorPhotos.SingleAsync()).AlbumId);
    }

    [Fact]
    public async Task UploadAsync_RejectsImageExceedingConfiguredUploadLimit()
    {
        using var factory = new TestDbContextFactory();
        int actorId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var actor = new Actor { FirstName = "Yua", LastName = "Mikami" };
            db.Actors.Add(actor);
            await db.SaveChangesAsync();
            actorId = actor.Id;
        }

        var customSettings = new ImageUploadSettings(1);
        var service = CreateService(factory, uploadSettings: customSettings);

        var oversized = new byte[1 * 1024 * 1024 + 1];
        var result = await service.UploadAsync(actorId, [oversized], albumId: null);

        Assert.False(result.Success);
        Assert.Equal("Each image must be no larger than 1 MB.", result.ErrorMessage);
    }

    [Fact]
    public async Task UploadAsync_WithItems_StreamsAndStoresImagesWithProgress()
    {
        using var factory = new TestDbContextFactory();
        int actorId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var actor = new Actor { FirstName = "Yua", LastName = "Mikami" };
            db.Actors.Add(actor);
            await db.SaveChangesAsync();
            actorId = actor.Id;
        }

        using var bitmap = new SKBitmap(2, 2);
        bitmap.Erase(SKColors.CornflowerBlue);
        using var skImage = SKImage.FromBitmap(bitmap);
        using var encoded = skImage.Encode(SKEncodedImageFormat.Png, 100);
        var imageBytes = encoded.ToArray();

        var reportedProgress = new List<int>();
        var progress = new ImmediateProgress<int>(reportedProgress.Add);

        var items = new List<ActorPhotoUploadItem>
        {
            new(() => new MemoryStream(imageBytes), imageBytes.LongLength),
            new(() => new MemoryStream(imageBytes), imageBytes.LongLength),
        };

        var service = CreateService(factory);
        var result = await service.UploadAsync(actorId, items, albumId: null, progress: progress);

        Assert.True(result.Success);
        await using var verify = await factory.CreateDbContextAsync();
        var photos = await verify.ActorPhotos.ToListAsync();
        Assert.Equal(2, photos.Count);
        Assert.Contains(1, reportedProgress);
        Assert.Contains(2, reportedProgress);
    }

    [Fact]
    public async Task UploadAsync_RejectsBatchExceedingConfiguredBatchLimit()
    {
        using var factory = new TestDbContextFactory();
        int actorId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var actor = new Actor { FirstName = "Yua", LastName = "Mikami" };
            db.Actors.Add(actor);
            await db.SaveChangesAsync();
            actorId = actor.Id;
        }

        var customSettings = new ImageUploadSettings(20, 2);
        var service = CreateService(factory, uploadSettings: customSettings);

        var items = new List<ActorPhotoUploadItem>
        {
            new(() => new MemoryStream([1]), 1),
            new(() => new MemoryStream([2]), 1),
            new(() => new MemoryStream([3]), 1),
        };
        var result = await service.UploadAsync(actorId, items, albumId: null);

        Assert.False(result.Success);
        Assert.Equal("You can upload at most 2 photos at a time.", result.ErrorMessage);
    }

    [Fact]
    public async Task UploadAsync_WhenCancelledMidway_CleansUpStagedFilesAndRethrows()
    {
        using var factory = new TestDbContextFactory();
        int actorId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var actor = new Actor { FirstName = "Yua", LastName = "Mikami" };
            db.Actors.Add(actor);
            await db.SaveChangesAsync();
            actorId = actor.Id;
        }

        using var bitmap = new SKBitmap(2, 2);
        bitmap.Erase(SKColors.CornflowerBlue);
        using var skImage = SKImage.FromBitmap(bitmap);
        using var encoded = skImage.Encode(SKEncodedImageFormat.Png, 100);
        var imageBytes = encoded.ToArray();

        using var cts = new CancellationTokenSource();
        var dataStore = new InMemoryDataStore();
        var service = CreateService(factory, dataStore: dataStore);

        var items = new List<ActorPhotoUploadItem>
        {
            new(() => new MemoryStream(imageBytes), imageBytes.LongLength),
            new(() =>
            {
                cts.Cancel();
                cts.Token.ThrowIfCancellationRequested();
                return new MemoryStream(imageBytes);
            }, imageBytes.LongLength),
        };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.UploadAsync(actorId, items, albumId: null, ct: cts.Token));

        Assert.Equal(0, dataStore.Count);
        Assert.Equal(0, dataStore.StagedCount);
        if (Directory.Exists(cacheRoot))
        {
            Assert.Empty(Directory.GetFiles(cacheRoot));
        }

        await using var verify = await factory.CreateDbContextAsync();
        Assert.Empty(await verify.ActorPhotos.ToListAsync());
    }

    [Fact]
    public async Task GetGlobalPhotosAsync_ReturnsPhotosAcrossActors_WithFilteringAndSorting()
    {
        using var factory = new TestDbContextFactory();
        int actor1Id;
        int actor2Id;
        int album1Id;

        await using (var db = await factory.CreateDbContextAsync())
        {
            var actor1 = new Actor { FirstName = "Yua", LastName = "Mikami", IsFavorite = true };
            var actor2 = new Actor { FirstName = "Eimi", LastName = "Fukada", IsFavorite = false };
            db.Actors.AddRange(actor1, actor2);
            await db.SaveChangesAsync();

            actor1Id = actor1.Id;
            actor2Id = actor2.Id;

            var album1 = new ActorAlbum { ActorId = actor1Id, Name = "Summer Gravure" };
            db.ActorAlbums.Add(album1);
            await db.SaveChangesAsync();
            album1Id = album1.Id;

            db.ActorPhotos.AddRange(
                new ActorPhoto { ActorId = actor1Id, AlbumId = album1Id, AspectRatio = 1.5, UploadedAt = DateTime.UtcNow.AddHours(-2) },
                new ActorPhoto { ActorId = actor1Id, AlbumId = null, AspectRatio = 0.75, UploadedAt = DateTime.UtcNow.AddHours(-1) },
                new ActorPhoto { ActorId = actor2Id, AlbumId = null, AspectRatio = 1.33, UploadedAt = DateTime.UtcNow });
            await db.SaveChangesAsync();
        }

        var service = CreateService(factory);

        // 1. All photos - default newest first
        var all = await service.GetGlobalPhotosAsync();
        Assert.Equal(3, all.Count);
        Assert.Equal("Fukada Eimi", all[0].ActorDisplayName);
        Assert.Equal("Mikami Yua", all[1].ActorDisplayName);
        Assert.Null(all[1].AlbumName);
        Assert.Equal(0.75, all[1].AspectRatio);
        Assert.Equal("Summer Gravure", all[2].AlbumName);
        Assert.Equal(1.5, all[2].AspectRatio);

        // 2. Filter by actor
        var actor1Photos = await service.GetGlobalPhotosAsync(actorId: actor1Id);
        Assert.Equal(2, actor1Photos.Count);
        Assert.All(actor1Photos, p => Assert.Equal(actor1Id, p.ActorId));

        // 3. Filter by album
        var albumPhotos = await service.GetGlobalPhotosAsync(albumId: album1Id);
        var albumPhoto = Assert.Single(albumPhotos);
        Assert.Equal(album1Id, albumPhoto.AlbumId);

        // 4. Filter uncategorized only
        var uncategorized = await service.GetGlobalPhotosAsync(uncategorizedOnly: true);
        Assert.Equal(2, uncategorized.Count);
        Assert.All(uncategorized, p => Assert.Null(p.AlbumId));

        // 5. Favorites only
        var favorites = await service.GetGlobalPhotosAsync(favoritesOnly: true);
        Assert.Equal(2, favorites.Count);
        Assert.All(favorites, p => Assert.Equal(actor1Id, p.ActorId));

        // 6. Search by album
        var searchAlbum = await service.GetGlobalPhotosAsync(search: "Gravure");
        Assert.Single(searchAlbum);
        Assert.Equal("Summer Gravure", searchAlbum[0].AlbumName);

        // 7. Search by actor name
        var searchActor = await service.GetGlobalPhotosAsync(search: "Fukada");
        Assert.Single(searchActor);
        Assert.Equal("Fukada Eimi", searchActor[0].ActorDisplayName);

        // 8. Sort oldest first
        var oldest = await service.GetGlobalPhotosAsync(sort: "oldest");
        Assert.Equal(1.5, oldest[0].AspectRatio);
        Assert.Equal(1.33, oldest[2].AspectRatio);
    }

    [Fact]
    public async Task GetGlobalPhotosAsync_ReportsHasOriginal_OnlyForPhotosWithSourceStorageId()
    {
        using var factory = new TestDbContextFactory();
        await using (var db = await factory.CreateDbContextAsync())
        {
            var actor = new Actor { FirstName = "Yua", LastName = "Mikami" };
            db.Actors.Add(actor);
            await db.SaveChangesAsync();
            db.ActorPhotos.AddRange(
                new ActorPhoto { ActorId = actor.Id, SourceStorageId = Guid.NewGuid(), UploadedAt = DateTime.UtcNow },
                new ActorPhoto { ActorId = actor.Id, SourceStorageId = null, UploadedAt = DateTime.UtcNow.AddMinutes(-1) });
            await db.SaveChangesAsync();
        }

        var service = CreateService(factory);
        var photos = await service.GetGlobalPhotosAsync();

        Assert.True(photos[0].HasOriginal);
        Assert.False(photos[1].HasOriginal);
    }

    [Fact]
    public async Task GetActorsWithPhotosAsync_And_GetTotalPhotoCountAsync_WorkAccurately()
    {
        using var factory = new TestDbContextFactory();
        await using (var db = await factory.CreateDbContextAsync())
        {
            var actor1 = new Actor { FirstName = "Yua", LastName = "Mikami" };
            var actor2 = new Actor { FirstName = "Eimi", LastName = "Fukada" };
            var actor3 = new Actor { FirstName = "Arina", LastName = "Hashimoto" };
            db.Actors.AddRange(actor1, actor2, actor3);
            await db.SaveChangesAsync();

            db.ActorPhotos.AddRange(
                new ActorPhoto { ActorId = actor1.Id, UploadedAt = DateTime.UtcNow },
                new ActorPhoto { ActorId = actor1.Id, UploadedAt = DateTime.UtcNow },
                new ActorPhoto { ActorId = actor2.Id, UploadedAt = DateTime.UtcNow });
            await db.SaveChangesAsync();
        }

        var service = CreateService(factory);

        var actors = await service.GetActorsWithPhotosAsync();
        Assert.Equal(2, actors.Count);
        // Ordered alphabetically by DisplayName ("Fukada Eimi", "Mikami Yua")
        Assert.Equal("Fukada Eimi", actors[0].DisplayName);
        Assert.Equal(1, actors[0].PhotoCount);
        Assert.Equal("Mikami Yua", actors[1].DisplayName);
        Assert.Equal(2, actors[1].PhotoCount);

        var totalCount = await service.GetTotalPhotoCountAsync();
        Assert.Equal(3, totalCount);
    }

    [Fact]
    public async Task UploadAsync_CalculatesAndStoresAspectRatio()
    {
        using var factory = new TestDbContextFactory();
        int actorId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var actor = new Actor { FirstName = "Yua", LastName = "Mikami" };
            db.Actors.Add(actor);
            await db.SaveChangesAsync();
            actorId = actor.Id;
        }

        using var bitmap = new SKBitmap(300, 200);
        bitmap.Erase(SKColors.CornflowerBlue);
        using var skImage = SKImage.FromBitmap(bitmap);
        using var encoded = skImage.Encode(SKEncodedImageFormat.Png, 100);
        var imageBytes = encoded.ToArray();

        var service = CreateService(factory);
        var result = await service.UploadAsync(actorId, [imageBytes], albumId: null);

        Assert.True(result.Success);
        await using (var db = await factory.CreateDbContextAsync())
        {
            var photo = await db.ActorPhotos.SingleAsync(p => p.ActorId == actorId);
            Assert.NotNull(photo.AspectRatio);
            Assert.Equal(1.5, photo.AspectRatio!.Value, precision: 2);
        }
    }

    [Fact]
    public async Task GetOrCreateBothAsync_WhenFilesExist_BackfillsMissingAspectRatio()
    {
        using var factory = new TestDbContextFactory();
        int photoId;
        var thumbId = Guid.NewGuid();
        var fullId = Guid.NewGuid();

        await using (var db = await factory.CreateDbContextAsync())
        {
            var actor = new Actor { FirstName = "Yua", LastName = "Mikami" };
            db.Actors.Add(actor);
            await db.SaveChangesAsync();

            var photo = new ActorPhoto
            {
                ActorId = actor.Id,
                ThumbStorageId = thumbId,
                FullStorageId = fullId,
                AspectRatio = null
            };
            db.ActorPhotos.Add(photo);
            await db.SaveChangesAsync();
            photoId = photo.Id;
        }

        Directory.CreateDirectory(cacheRoot);
        using var bitmap = new SKBitmap(400, 300);
        bitmap.Erase(SKColors.Green);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Webp, 80);
        await File.WriteAllBytesAsync(StoragePath(fullId), data.ToArray());
        await File.WriteAllBytesAsync(StoragePath(thumbId), data.ToArray());

        var service = CreateService(factory);
        var (thumb, full) = await service.GetOrCreateBothAsync(photoId);

        Assert.NotNull(thumb);
        Assert.NotNull(full);
        Assert.True(File.Exists(thumb));
        Assert.True(File.Exists(full));

        await using (var db = await factory.CreateDbContextAsync())
        {
            var photo = await db.ActorPhotos.SingleAsync(p => p.Id == photoId);
            Assert.NotNull(photo.AspectRatio);
            Assert.Equal(1.3333, photo.AspectRatio!.Value, precision: 3);
        }
    }

    [Fact]
    public async Task GetOrCreateBothAsync_WhenFilesMissing_RegeneratesFromDataStoreAndBackfillsAspectRatio()
    {
        using var factory = new TestDbContextFactory();
        var dataStore = new InMemoryDataStore();
        int photoId;
        var thumbId = Guid.NewGuid();
        var fullId = Guid.NewGuid();
        var sourceId = Guid.NewGuid();

        await using (var db = await factory.CreateDbContextAsync())
        {
            var actor = new Actor { FirstName = "Yua", LastName = "Mikami" };
            db.Actors.Add(actor);
            await db.SaveChangesAsync();

            var photo = new ActorPhoto
            {
                ActorId = actor.Id,
                ThumbStorageId = thumbId,
                FullStorageId = fullId,
                SourceStorageId = sourceId,
                AspectRatio = null
            };
            db.ActorPhotos.Add(photo);
            await db.SaveChangesAsync();
            photoId = photo.Id;
        }

        using var bitmap = new SKBitmap(400, 200);
        bitmap.Erase(SKColors.Red);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        await dataStore.WriteAsync(sourceId, data.ToArray());

        var service = CreateService(factory, dataStore);
        var (thumb, full) = await service.GetOrCreateBothAsync(photoId);

        Assert.NotNull(thumb);
        Assert.NotNull(full);
        Assert.True(File.Exists(thumb));
        Assert.True(File.Exists(full));

        await using (var db = await factory.CreateDbContextAsync())
        {
            var photo = await db.ActorPhotos.SingleAsync(p => p.Id == photoId);
            Assert.NotNull(photo.AspectRatio);
            Assert.Equal(2.0, photo.AspectRatio!.Value, precision: 2);
        }
    }

    [Fact]
    public async Task GetImageAsync_WhenFileExists_BackfillsMissingAspectRatio()
    {
        using var factory = new TestDbContextFactory();
        int photoId;
        var thumbId = Guid.NewGuid();
        var fullId = Guid.NewGuid();

        await using (var db = await factory.CreateDbContextAsync())
        {
            var actor = new Actor { FirstName = "Yua", LastName = "Mikami" };
            db.Actors.Add(actor);
            await db.SaveChangesAsync();

            var photo = new ActorPhoto
            {
                ActorId = actor.Id,
                ThumbStorageId = thumbId,
                FullStorageId = fullId,
                AspectRatio = null
            };
            db.ActorPhotos.Add(photo);
            await db.SaveChangesAsync();
            photoId = photo.Id;
        }

        Directory.CreateDirectory(cacheRoot);
        using var bitmap = new SKBitmap(300, 150);
        bitmap.Erase(SKColors.Blue);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Webp, 80);
        await File.WriteAllBytesAsync(StoragePath(fullId), data.ToArray());
        await File.WriteAllBytesAsync(StoragePath(thumbId), data.ToArray());

        var service = CreateService(factory);
        var path = (await service.GetImageAsync(photoId, "thumb")).FilePath;

        Assert.Equal(StoragePath(thumbId), path);

        await using (var db = await factory.CreateDbContextAsync())
        {
            var photo = await db.ActorPhotos.SingleAsync(p => p.Id == photoId);
            Assert.NotNull(photo.AspectRatio);
            Assert.Equal(2.0, photo.AspectRatio!.Value, precision: 2);
        }
    }

    [Fact]
    public async Task GetImageAsync_OriginalVariant_ServesStoredSource_WhenPhotoHasOriginal()
    {
        using var factory = new TestDbContextFactory();
        var dataStore = new InMemoryDataStore();
        var sourceId = Guid.NewGuid();
        int photoId;

        await using (var db = await factory.CreateDbContextAsync())
        {
            var actor = new Actor { FirstName = "Yua", LastName = "Mikami" };
            db.Actors.Add(actor);
            await db.SaveChangesAsync();

            var photo = new ActorPhoto { ActorId = actor.Id, SourceStorageId = sourceId };
            db.ActorPhotos.Add(photo);
            await db.SaveChangesAsync();
            photoId = photo.Id;
        }

        await dataStore.WriteAsync(sourceId, new byte[] { 1, 2, 3 });
        var service = CreateService(factory, dataStore);

        var result = await service.GetImageAsync(photoId, "original");

        Assert.Equal(ImageServingResultKind.Stored, result.Kind);
        await using var stored = result.Object!;
        Assert.Equal($"{sourceId:N}.jpg", stored.Key);
        using var bytes = new MemoryStream();
        await stored.Content.CopyToAsync(bytes);
        Assert.Equal(new byte[] { 1, 2, 3 }, bytes.ToArray());
    }

    [Fact]
    public async Task GetImageAsync_OriginalVariant_NotFound_WhenPhotoHasNoSourceStorageId()
    {
        using var factory = new TestDbContextFactory();
        var dataStore = new InMemoryDataStore();
        int photoId;

        await using (var db = await factory.CreateDbContextAsync())
        {
            var actor = new Actor { FirstName = "Yua", LastName = "Mikami" };
            db.Actors.Add(actor);
            await db.SaveChangesAsync();

            var photo = new ActorPhoto { ActorId = actor.Id, SourceStorageId = null };
            db.ActorPhotos.Add(photo);
            await db.SaveChangesAsync();
            photoId = photo.Id;
        }

        var service = CreateService(factory, dataStore);

        var result = await service.GetImageAsync(photoId, "original");

        Assert.Equal(ImageServingResultKind.NotFound, result.Kind);
    }

    [Fact]
    public async Task GetGalleryAsync_ReportsHasOriginal_OnlyForPhotosWithSourceStorageId()
    {
        using var factory = new TestDbContextFactory();
        int actorId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var actor = new Actor { FirstName = "Yua", LastName = "Mikami" };
            db.Actors.Add(actor);
            await db.SaveChangesAsync();
            actorId = actor.Id;
            db.ActorPhotos.AddRange(
                new ActorPhoto { ActorId = actorId, SourceStorageId = Guid.NewGuid(), UploadedAt = DateTime.UtcNow },
                new ActorPhoto { ActorId = actorId, SourceStorageId = null, UploadedAt = DateTime.UtcNow.AddMinutes(-1) });
            await db.SaveChangesAsync();
        }

        var service = CreateService(factory);
        var gallery = await service.GetGalleryAsync(actorId);

        Assert.True(gallery.Photos.Single(p => p.UploadedAt == gallery.Photos.Max(x => x.UploadedAt)).HasOriginal);
        Assert.False(gallery.Photos.Single(p => p.UploadedAt == gallery.Photos.Min(x => x.UploadedAt)).HasOriginal);
    }

    private ActorPhotoService CreateService(TestDbContextFactory factory, IActorImageDataStore? dataStore = null, ImageUploadSettings? uploadSettings = null)
    {
        var cache = Substitute.For<ILocalImageCache>();
        cache.QualityFull.Returns(ImageConverter.WebPQualityFull);
        cache.QualityThumb.Returns(ImageConverter.WebPQualityThumb);
        cache.GetStoragePath(Arg.Any<Guid>()).Returns(call => StoragePath(call.Arg<Guid>()));
        return new ActorPhotoService(factory, cache, dataStore, uploadSettings);
    }

    private string StoragePath(Guid id) => Path.Combine(cacheRoot, id.ToString("N") + ".webp");

    private sealed class InMemoryDataStore : IActorImageDataStore
    {
        private readonly Dictionary<Guid, byte[]> files = [];
        private readonly Dictionary<Guid, byte[]> staged = [];

        public Task<string> WriteAsync(Guid storageId, ReadOnlyMemory<byte> bytes, CancellationToken ct = default)
        {
            files[storageId] = bytes.ToArray();
            return Task.FromResult(".jpg");
        }

        public Task<string> StageAsync(Guid storageId, ReadOnlyMemory<byte> bytes, CancellationToken ct = default)
        {
            staged[storageId] = bytes.ToArray();
            return Task.FromResult(".jpg");
        }

        public Task CommitAsync(Guid storageId, CancellationToken ct = default)
        {
            if (staged.Remove(storageId, out var bytes))
            {
                files[storageId] = bytes;
            }
            return Task.CompletedTask;
        }

        public void DeleteStaged(Guid storageId) => staged.Remove(storageId);

        public void PurgeStaging() => staged.Clear();

        public Task<byte[]?> ReadAsync(Guid storageId, string? extension, CancellationToken ct = default) =>
            Task.FromResult(files.GetValueOrDefault(storageId));

        public Task DeleteAsync(Guid storageId, string? extension, CancellationToken ct = default)
        {
            files.Remove(storageId);
            return Task.CompletedTask;
        }

        public int Count => files.Count;

        public int StagedCount => staged.Count;

        public Task<StoredObject?> OpenReadAsync(Guid storageId, string? extension, CancellationToken ct = default) =>
            Task.FromResult(files.TryGetValue(storageId, out var bytes)
                ? new StoredObject($"{storageId:N}.jpg", new MemoryStream(bytes), bytes.Length, DateTimeOffset.UnixEpoch, "\"test\"")
                : null);

        public Task<bool> ExistsAsync(Guid storageId, string? extension, CancellationToken ct = default) => Task.FromResult(files.ContainsKey(storageId));

        public IAsyncEnumerable<StoredActorImageSource> ListAsync(CancellationToken ct = default) =>
            files.Keys.Select(id => new StoredActorImageSource(id, ".jpg", DateTimeOffset.UnixEpoch)).ToAsyncEnumerable();
    }
}
