using System.Net;
using Javbuddy.Models;
using Javbuddy.Services.Images;
using Javbuddy.Services.Infrastructure;
using Javbuddy.Services.LocalLibrary;
using Javbuddy.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace Javbuddy.Tests.Services.Images;

public class ActorImageCacheServiceTests
{
    [Fact]
    public async Task GetOrCreateBothAsync_ActorNotFound_ReturnsNull()
    {
        using var factory = new TestDbContextFactory();
        var client = Substitute.For<ILocalLibraryClient>();
        var cache = Substitute.For<ILocalImageCache>();
        var maintenance = Substitute.For<IImageCacheMaintenanceService>();

        var service = new ActorImageCacheService(client, cache, factory, maintenance);
        var (thumb, full) = await service.GetOrCreateBothAsync(999);

        Assert.Null(thumb);
        Assert.Null(full);
        await client.DidNotReceiveWithAnyArgs().ResolveActorImagePathAsync(default!, Arg.Any<IReadOnlyList<string>>(), default);
    }

    [Fact]
    public async Task GetOrCreateBothAsync_NoLinkedMovies_ReturnsNull()
    {
        using var factory = new TestDbContextFactory();
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Actors.Add(new Actor { FirstName = "Tsubomi" });
            await db.SaveChangesAsync();
        }

        var client = Substitute.For<ILocalLibraryClient>();
        var cache = Substitute.For<ILocalImageCache>();
        var maintenance = Substitute.For<IImageCacheMaintenanceService>();

        var service = new ActorImageCacheService(client, cache, factory, maintenance);
        var (thumb, full) = await service.GetOrCreateBothAsync(1);

        Assert.Null(thumb);
        Assert.Null(full);
        await client.DidNotReceiveWithAnyArgs().ResolveActorImagePathAsync(default!, Arg.Any<IReadOnlyList<string>>(), default);
    }

    [Fact]
    public async Task GetOrCreateBothAsync_GeneratesSwappedCandidateWhenLastNameEmpty()
    {
        using var factory = new TestDbContextFactory();
        await using (var db = await factory.CreateDbContextAsync())
        {
            var actor = new Actor { FirstName = "Momose Askura", LastName = null };
            var movie = new Movie { Code = "FGAN-188", Title = "Test Movie" };
            db.Actors.Add(actor);
            db.Movies.Add(movie);
            await db.SaveChangesAsync();

            db.MovieActors.Add(new MovieActor { ActorId = actor.Id, MovieId = movie.Id });
            await db.SaveChangesAsync();
        }

        var client = Substitute.For<ILocalLibraryClient>();
        var cache = Substitute.For<ILocalImageCache>();
        var maintenance = Substitute.For<IImageCacheMaintenanceService>();

        IReadOnlyList<string>? capturedCandidates = null;
        client.ResolveActorImagePathAsync("FGAN-188", Arg.Do<IReadOnlyList<string>>(c => capturedCandidates = c), Arg.Any<CancellationToken>())
            .Returns((string?)null);

        var service = new ActorImageCacheService(client, cache, factory, maintenance);
        await service.GetOrCreateBothAsync(1);

        Assert.NotNull(capturedCandidates);
        Assert.Contains("Momose Askura", capturedCandidates);
        Assert.Contains("Askura Momose", capturedCandidates);
    }

    [Fact]
    public async Task GetOrCreateBothAsync_GeneratesCompactJapaneseCandidates()
    {
        using var factory = new TestDbContextFactory();
        await using (var db = await factory.CreateDbContextAsync())
        {
            var actor = new Actor
            {
                FirstName = "Noa",
                LastName = "Araki",
                JapaneseNameKanji = "新木 希空",
                JapaneseNameKana = "あらき のあ"
            };
            var movie = new Movie { Code = "SIVR-505", Title = "Test VR" };
            db.Actors.Add(actor);
            db.Movies.Add(movie);
            await db.SaveChangesAsync();

            db.MovieActors.Add(new MovieActor { ActorId = actor.Id, MovieId = movie.Id });
            await db.SaveChangesAsync();
        }

        var client = Substitute.For<ILocalLibraryClient>();
        var cache = Substitute.For<ILocalImageCache>();
        var maintenance = Substitute.For<IImageCacheMaintenanceService>();

        IReadOnlyList<string>? capturedCandidates = null;
        client.ResolveActorImagePathAsync("SIVR-505", Arg.Do<IReadOnlyList<string>>(c => capturedCandidates = c), Arg.Any<CancellationToken>())
            .Returns((string?)null);

        var service = new ActorImageCacheService(client, cache, factory, maintenance);
        await service.GetOrCreateBothAsync(1);

        Assert.NotNull(capturedCandidates);
        Assert.Contains("新木 希空", capturedCandidates);
        Assert.Contains("新木希空", capturedCandidates);
        Assert.Contains("あらき のあ", capturedCandidates);
        Assert.Contains("あらきのあ", capturedCandidates);
    }

    private sealed class TempCacheRoot : ILocalImageCache, IDisposable
    {
        public string RootPath { get; } = Path.Combine(Path.GetTempPath(), "javbuddy-tests-actorcache-" + Guid.NewGuid().ToString("N"));
        public int QualityFull => ImageConverter.WebPQualityFull;
        public int QualityThumb => ImageConverter.WebPQualityThumb;
        public ImageCacheSettings Settings { get; } = new(ImageCacheMode.LocalMovies, null, null);

        public TempCacheRoot() => Directory.CreateDirectory(RootPath);

        public string GetStoragePath(Guid storageId) => Path.Combine(RootPath, storageId.ToString("N") + ".webp");

        public bool IsCachedFile(string path) => path.StartsWith(RootPath, StringComparison.OrdinalIgnoreCase);

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(RootPath)) Directory.Delete(RootPath, recursive: true);
            }
            catch (IOException) { }
        }
    }

    private static byte[] CreateSampleImageBytes(int width, int height)
    {
        using var bitmap = new SkiaSharp.SKBitmap(width, height);
        using (var canvas = new SkiaSharp.SKCanvas(bitmap))
        {
            canvas.Clear(SkiaSharp.SKColors.LimeGreen);
        }
        using var image = SkiaSharp.SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    [Fact]
    public async Task SaveCustomImageAsync_WithoutCrop_SavesThumbAndFullCustomImages()
    {
        using var factory = new TestDbContextFactory();
        using var cache = new TempCacheRoot();
        var client = Substitute.For<ILocalLibraryClient>();
        var maintenance = Substitute.For<IImageCacheMaintenanceService>();

        int actorId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var actor = new Actor { FirstName = "Yua", LastName = "Mikami" };
            db.Actors.Add(actor);
            await db.SaveChangesAsync();
            actorId = actor.Id;
        }

        var service = new ActorImageCacheService(client, cache, factory, maintenance);
        var sampleBytes = CreateSampleImageBytes(400, 400);

        var (thumbPath, fullPath) = await service.SaveCustomImageAsync(actorId, sampleBytes);

        Assert.NotNull(thumbPath);
        Assert.NotNull(fullPath);
        Assert.True(File.Exists(thumbPath));
        Assert.True(File.Exists(fullPath));

        await using (var db = await factory.CreateDbContextAsync())
        {
            var records = db.ActorImages.Where(ai => ai.ActorId == actorId).ToList();
            Assert.Equal(2, records.Count);
            Assert.All(records, r => Assert.Equal(ActorImageCacheService.SourceMovieCodeCustom, r.SourceMovieCode));
        }

        var hasCustom = await service.HasCustomImageAsync(actorId);
        Assert.True(hasCustom);
    }

    [Fact]
    public async Task SaveCustomImageAsync_WithCrop_AppliesCropAndSaves()
    {
        using var factory = new TestDbContextFactory();
        using var cache = new TempCacheRoot();
        var client = Substitute.For<ILocalLibraryClient>();
        var maintenance = Substitute.For<IImageCacheMaintenanceService>();

        int actorId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var actor = new Actor { FirstName = "Karen", LastName = "Kaede" };
            db.Actors.Add(actor);
            await db.SaveChangesAsync();
            actorId = actor.Id;
        }

        var service = new ActorImageCacheService(client, cache, factory, maintenance);
        var sampleBytes = CreateSampleImageBytes(600, 400);
        var crop = new NormalizedCropRect(0.1, 0.1, 0.5, 0.5);

        var (thumbPath, fullPath) = await service.SaveCustomImageAsync(actorId, sampleBytes, crop);

        Assert.NotNull(thumbPath);
        Assert.NotNull(fullPath);
        Assert.True(File.Exists(thumbPath));
        Assert.True(File.Exists(fullPath));

        using var fullBitmap = SkiaSharp.SKBitmap.Decode(fullPath);
        Assert.NotNull(fullBitmap);
        // Crop was 0.5 width / 0.5 height of 600x400 -> 300x200
        Assert.Equal(300, fullBitmap.Width);
        Assert.Equal(200, fullBitmap.Height);
    }

    [Fact]
    public async Task GetOrCreateBothAsync_WhenCustomImageExists_ReturnsCustomWithoutResolvingLibrary()
    {
        using var factory = new TestDbContextFactory();
        using var cache = new TempCacheRoot();
        var client = Substitute.For<ILocalLibraryClient>();
        var maintenance = Substitute.For<IImageCacheMaintenanceService>();

        int actorId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var actor = new Actor { FirstName = "Eimi", LastName = "Fukada" };
            var movie = new Movie { Code = "ABW-001", Title = "Movie" };
            db.Actors.Add(actor);
            db.Movies.Add(movie);
            await db.SaveChangesAsync();
            db.MovieActors.Add(new MovieActor { ActorId = actor.Id, MovieId = movie.Id });
            await db.SaveChangesAsync();
            actorId = actor.Id;
        }

        var service = new ActorImageCacheService(client, cache, factory, maintenance);
        var sampleBytes = CreateSampleImageBytes(300, 300);
        var (savedThumb, savedFull) = await service.SaveCustomImageAsync(actorId, sampleBytes);

        // Call GetOrCreateBothAsync: should return the saved custom images directly
        var (retrievedThumb, retrievedFull) = await service.GetOrCreateBothAsync(actorId);

        Assert.Equal(savedThumb, retrievedThumb);
        Assert.Equal(savedFull, retrievedFull);
        await client.DidNotReceiveWithAnyArgs().ResolveActorImagePathAsync(default!, Arg.Any<IReadOnlyList<string>>(), default);
    }

    [Fact]
    public async Task DeleteCustomImageAsync_RemovesFilesAndDatabaseRecords()
    {
        using var factory = new TestDbContextFactory();
        using var cache = new TempCacheRoot();
        var client = Substitute.For<ILocalLibraryClient>();
        var maintenance = Substitute.For<IImageCacheMaintenanceService>();

        int actorId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var actor = new Actor { FirstName = "Rion" };
            db.Actors.Add(actor);
            await db.SaveChangesAsync();
            actorId = actor.Id;
        }

        var service = new ActorImageCacheService(client, cache, factory, maintenance);
        var sampleBytes = CreateSampleImageBytes(200, 200);
        var (thumbPath, fullPath) = await service.SaveCustomImageAsync(actorId, sampleBytes);

        Assert.True(File.Exists(thumbPath));
        Assert.True(File.Exists(fullPath));
        Assert.True(await service.HasCustomImageAsync(actorId));

        var deleted = await service.DeleteCustomImageAsync(actorId);
        Assert.True(deleted);

        Assert.False(File.Exists(thumbPath));
        Assert.False(File.Exists(fullPath));
        Assert.False(await service.HasCustomImageAsync(actorId));

        await using (var db = await factory.CreateDbContextAsync())
        {
            var count = db.ActorImages.Count(ai => ai.ActorId == actorId);
            Assert.Equal(0, count);
        }
    }

    [Fact]
    public async Task TransferImagesAsync_TransfersCustomImageToTargetWhenTargetHasNone()
    {
        using var factory = new TestDbContextFactory();
        using var cache = new TempCacheRoot();
        var client = Substitute.For<ILocalLibraryClient>();
        var maintenance = Substitute.For<IImageCacheMaintenanceService>();

        int sourceId, targetId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var source = new Actor { FirstName = "Source" };
            var target = new Actor { FirstName = "Target" };
            db.Actors.AddRange(source, target);
            await db.SaveChangesAsync();
            sourceId = source.Id;
            targetId = target.Id;
        }

        var service = new ActorImageCacheService(client, cache, factory, maintenance);
        var sampleBytes = CreateSampleImageBytes(200, 200);
        var (thumbPath, fullPath) = await service.SaveCustomImageAsync(sourceId, sampleBytes);

        Assert.True(File.Exists(thumbPath));
        Assert.True(File.Exists(fullPath));

        await service.TransferImagesAsync(sourceId, targetId);

        Assert.True(await service.HasCustomImageAsync(targetId));
        Assert.False(await service.HasCustomImageAsync(sourceId));
        Assert.True(File.Exists(thumbPath));
        Assert.True(File.Exists(fullPath));
    }

    [Fact]
    public async Task DownloadAndCacheRemoteImageAsync_DownloadsAndCachesRemoteImage()
    {
        using var factory = new TestDbContextFactory();
        using var cache = new TempCacheRoot();
        var client = Substitute.For<ILocalLibraryClient>();
        var maintenance = Substitute.For<IImageCacheMaintenanceService>();

        int actorId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var actor = new Actor
            {
                FirstName = "Noa",
                LastName = "Araki"
            };
            db.Actors.Add(actor);
            await db.SaveChangesAsync();
            actorId = actor.Id;
        }

        var sampleBytes = CreateSampleImageBytes(300, 300);
        var requestCount = 0;
        var handler = new FakeHttpMessageHandler((req, _) =>
        {
            requestCount++;
            Assert.Equal("https://pics.dmm.co.jp/mono/actjpgs/araki_noa.jpg", req.RequestUri?.ToString());
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(sampleBytes)
            });
        });
        var httpFactory = new FakeHttpClientFactory(handler);

        var service = new ActorImageCacheService(client, cache, factory, maintenance, httpFactory);
        var (thumbPath, fullPath) = await service.DownloadAndCacheRemoteImageAsync(actorId, "https://pics.dmm.co.jp/mono/actjpgs/araki_noa.jpg");

        Assert.NotNull(thumbPath);
        Assert.NotNull(fullPath);
        Assert.True(File.Exists(thumbPath));
        Assert.True(File.Exists(fullPath));
        Assert.Equal(1, requestCount);

        await using (var db = await factory.CreateDbContextAsync())
        {
            var rows = await db.ActorImages.Where(ai => ai.ActorId == actorId).ToListAsync();
            Assert.Equal(2, rows.Count);
            Assert.All(rows, r =>
            {
                Assert.Equal(ActorImageCacheService.SourceMovieCodeRemote, r.SourceMovieCode);
                Assert.Equal("https://pics.dmm.co.jp/mono/actjpgs/araki_noa.jpg", r.SourceUrl);
            });
        }
    }

    [Fact]
    public async Task DownloadAndCacheRemoteImageAsync_RejectsDefunctUrls_WithoutMakingHttpRequest()
    {
        using var factory = new TestDbContextFactory();
        using var cache = new TempCacheRoot();
        var client = Substitute.For<ILocalLibraryClient>();
        var maintenance = Substitute.For<IImageCacheMaintenanceService>();

        int actorId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var actor = new Actor { FirstName = "Noa", LastName = "Araki" };
            db.Actors.Add(actor);
            await db.SaveChangesAsync();
            actorId = actor.Id;
        }

        var httpCalled = false;
        var handler = new FakeHttpMessageHandler((_, _) =>
        {
            httpCalled = true;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        });
        var httpFactory = new FakeHttpClientFactory(handler);

        var service = new ActorImageCacheService(client, cache, factory, maintenance, httpFactory);
        var (thumbPath, fullPath) = await service.DownloadAndCacheRemoteImageAsync(actorId, "https://pics.r18.com/mono/actjpgs/araki_noa.jpg");

        Assert.Null(thumbPath);
        Assert.Null(fullPath);
        Assert.False(httpCalled);

        await using (var db = await factory.CreateDbContextAsync())
        {
            Assert.False(await db.ActorImages.AnyAsync(ai => ai.ActorId == actorId));
        }
    }

    [Fact]
    public async Task GetOrCreateBothAsync_ReturnsExistingCachedRemoteImage_WithoutMakingHttpRequests()
    {
        using var factory = new TestDbContextFactory();
        using var cache = new TempCacheRoot();
        var client = Substitute.For<ILocalLibraryClient>();
        var maintenance = Substitute.For<IImageCacheMaintenanceService>();

        int actorId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var actor = new Actor
            {
                FirstName = "Noa",
                LastName = "Araki"
            };
            db.Actors.Add(actor);
            await db.SaveChangesAsync();
            actorId = actor.Id;
        }

        var sampleBytes = CreateSampleImageBytes(300, 300);
        var requestCount = 0;
        var handler = new FakeHttpMessageHandler((_, _) =>
        {
            requestCount++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(sampleBytes)
            });
        });
        var httpFactory = new FakeHttpClientFactory(handler);

        var service = new ActorImageCacheService(client, cache, factory, maintenance, httpFactory);

        // Download at import time
        var (thumb1, full1) = await service.DownloadAndCacheRemoteImageAsync(actorId, "https://pics.dmm.co.jp/mono/actjpgs/araki_noa.jpg");
        Assert.Equal(1, requestCount);

        // Runtime GetOrCreateBothAsync should reuse the cached image with zero outbound HTTP requests
        var (thumb2, full2) = await service.GetOrCreateBothAsync(actorId);
        Assert.Equal(1, requestCount);
        Assert.Equal(thumb1, thumb2);
        Assert.Equal(full1, full2);
    }

    [Fact]
    public async Task GetOrCreateBothAsync_WhenNoImageAvailable_MakesZeroHttpRequests()
    {
        using var factory = new TestDbContextFactory();
        using var cache = new TempCacheRoot();
        var client = Substitute.For<ILocalLibraryClient>();
        var maintenance = Substitute.For<IImageCacheMaintenanceService>();

        int actorId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var actor = new Actor { FirstName = "Noa", LastName = "Araki" };
            db.Actors.Add(actor);
            await db.SaveChangesAsync();
            actorId = actor.Id;
        }

        var httpCalled = false;
        var handler = new FakeHttpMessageHandler((_, _) =>
        {
            httpCalled = true;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        });
        var httpFactory = new FakeHttpClientFactory(handler);

        var service = new ActorImageCacheService(client, cache, factory, maintenance, httpFactory);
        var (thumb, full) = await service.GetOrCreateBothAsync(actorId);

        Assert.Null(thumb);
        Assert.Null(full);
        Assert.False(httpCalled);
    }

    [Fact]
    public async Task GetOrCreateBothAsync_LocalActorsImageTakesPrecedence()
    {
        using var factory = new TestDbContextFactory();
        using var cache = new TempCacheRoot();
        var client = Substitute.For<ILocalLibraryClient>();
        var maintenance = Substitute.For<IImageCacheMaintenanceService>();

        int actorId;
        var localDir = Path.Combine(cache.RootPath, "local-actors");
        Directory.CreateDirectory(localDir);
        var localHeadshot = Path.Combine(localDir, "Noa Araki.jpg");
        File.WriteAllBytes(localHeadshot, CreateSampleImageBytes(400, 400));

        await using (var db = await factory.CreateDbContextAsync())
        {
            var actor = new Actor
            {
                FirstName = "Noa",
                LastName = "Araki"
            };
            var movie = new Movie { Code = "SIVR-505" };
            db.Actors.Add(actor);
            db.Movies.Add(movie);
            await db.SaveChangesAsync();

            db.MovieActors.Add(new MovieActor { ActorId = actor.Id, MovieId = movie.Id });
            await db.SaveChangesAsync();
            actorId = actor.Id;
        }

        client.ResolveActorImagePathAsync("SIVR-505", Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(localHeadshot);

        var httpCalled = false;
        var handler = new FakeHttpMessageHandler((_, _) =>
        {
            httpCalled = true;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        });
        var httpFactory = new FakeHttpClientFactory(handler);

        var service = new ActorImageCacheService(client, cache, factory, maintenance, httpFactory);
        var (thumbPath, fullPath) = await service.GetOrCreateBothAsync(actorId);

        Assert.NotNull(thumbPath);
        Assert.NotNull(fullPath);
        Assert.False(httpCalled, "HTTP request should not be made when a local image is found");

        await using (var db = await factory.CreateDbContextAsync())
        {
            var rows = await db.ActorImages.Where(ai => ai.ActorId == actorId).ToListAsync();
            Assert.All(rows, r =>
            {
                Assert.Equal("SIVR-505", r.SourceMovieCode);
                Assert.Null(r.SourceUrl);
            });
        }
    }

    [Fact]
    public async Task TransferImagesAsync_LocalImageOnSourceSupersedesRemoteFallbackOnTarget()
    {
        using var factory = new TestDbContextFactory();
        using var cache = new TempCacheRoot();
        var client = Substitute.For<ILocalLibraryClient>();
        var maintenance = Substitute.For<IImageCacheMaintenanceService>();

        int sourceId, targetId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var source = new Actor { FirstName = "Source" };
            var target = new Actor { FirstName = "Target" };
            db.Actors.AddRange(source, target);
            await db.SaveChangesAsync();
            sourceId = source.Id;
            targetId = target.Id;
        }

        var sampleBytes = CreateSampleImageBytes(200, 200);
        var handler = new FakeHttpMessageHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(sampleBytes) }));
        var httpFactory = new FakeHttpClientFactory(handler);

        var service = new ActorImageCacheService(client, cache, factory, maintenance, httpFactory);

        // Target gets cached from remote URL
        var (targetThumb, _) = await service.DownloadAndCacheRemoteImageAsync(targetId, "https://example.com/target.jpg");
        Assert.True(File.Exists(targetThumb));

        // Source gets a simulated local movie image in DB
        var sourceStorageIdThumb = Guid.NewGuid();
        var sourceStorageIdFull = Guid.NewGuid();
        var sourceThumbPath = cache.GetStoragePath(sourceStorageIdThumb);
        var sourceFullPath = cache.GetStoragePath(sourceStorageIdFull);
        Directory.CreateDirectory(Path.GetDirectoryName(sourceThumbPath)!);
        File.WriteAllBytes(sourceThumbPath, sampleBytes);
        File.WriteAllBytes(sourceFullPath, sampleBytes);

        await using (var db = await factory.CreateDbContextAsync())
        {
            db.ActorImages.AddRange(
                new ActorImage { ActorId = sourceId, Variant = "thumb", SourceMovieCode = "SRC-001", StorageId = sourceStorageIdThumb },
                new ActorImage { ActorId = sourceId, Variant = "full", SourceMovieCode = "SRC-001", StorageId = sourceStorageIdFull }
            );
            await db.SaveChangesAsync();
        }

        await service.TransferImagesAsync(sourceId, targetId);

        // Target's remote image should have been replaced by source's local movie image
        await using (var db = await factory.CreateDbContextAsync())
        {
            var targetRows = await db.ActorImages.Where(ai => ai.ActorId == targetId).ToListAsync();
            Assert.Equal(2, targetRows.Count);
            Assert.All(targetRows, r => Assert.Equal("SRC-001", r.SourceMovieCode));
        }
        Assert.False(File.Exists(targetThumb), "Target's superseded remote storage file should be deleted");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public async Task DownloadImageFromUrlAsync_EmptyOrWhitespaceUrl_ReturnsError(string? url)
    {
        using var factory = new TestDbContextFactory();
        var client = Substitute.For<ILocalLibraryClient>();
        var cache = Substitute.For<ILocalImageCache>();
        var maintenance = Substitute.For<IImageCacheMaintenanceService>();

        var service = new ActorImageCacheService(client, cache, factory, maintenance);
        var result = await service.DownloadImageFromUrlAsync(url!);

        Assert.False(result.Success);
        Assert.Equal("Please enter an image URL.", result.ErrorMessage);
    }

    [Theory]
    [InlineData("not-a-valid-url")]
    [InlineData("ftp://example.com/photo.jpg")]
    [InlineData("file:///C:/photo.jpg")]
    public async Task DownloadImageFromUrlAsync_InvalidUrl_ReturnsError(string url)
    {
        using var factory = new TestDbContextFactory();
        var client = Substitute.For<ILocalLibraryClient>();
        var cache = Substitute.For<ILocalImageCache>();
        var maintenance = Substitute.For<IImageCacheMaintenanceService>();

        var service = new ActorImageCacheService(client, cache, factory, maintenance);
        var result = await service.DownloadImageFromUrlAsync(url);

        Assert.False(result.Success);
        Assert.Equal("Please enter a valid HTTP or HTTPS image URL.", result.ErrorMessage);
    }

    [Fact]
    public async Task DownloadImageFromUrlAsync_DefunctDomain_ReturnsErrorWithoutMakingHttpRequest()
    {
        using var factory = new TestDbContextFactory();
        var client = Substitute.For<ILocalLibraryClient>();
        var cache = Substitute.For<ILocalImageCache>();
        var maintenance = Substitute.For<IImageCacheMaintenanceService>();

        var httpCalled = false;
        var handler = new FakeHttpMessageHandler((_, _) =>
        {
            httpCalled = true;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        });
        var httpFactory = new FakeHttpClientFactory(handler);

        var service = new ActorImageCacheService(client, cache, factory, maintenance, httpFactory);
        var result = await service.DownloadImageFromUrlAsync("https://pics.r18.com/mono/actjpgs/test.jpg");

        Assert.False(result.Success);
        Assert.Equal("The provided image URL is on a defunct domain (r18.com) and cannot be downloaded.", result.ErrorMessage);
        Assert.False(httpCalled);
    }

    [Fact]
    public async Task DownloadImageFromUrlAsync_HttpClientFactoryNull_ReturnsError()
    {
        using var factory = new TestDbContextFactory();
        var client = Substitute.For<ILocalLibraryClient>();
        var cache = Substitute.For<ILocalImageCache>();
        var maintenance = Substitute.For<IImageCacheMaintenanceService>();

        var service = new ActorImageCacheService(client, cache, factory, maintenance, httpClientFactory: null);
        var result = await service.DownloadImageFromUrlAsync("https://example.com/photo.jpg");

        Assert.False(result.Success);
        Assert.Equal("HTTP client service is not available.", result.ErrorMessage);
    }

    [Fact]
    public async Task DownloadImageFromUrlAsync_NotFound_Returns404Error()
    {
        using var factory = new TestDbContextFactory();
        var client = Substitute.For<ILocalLibraryClient>();
        var cache = Substitute.For<ILocalImageCache>();
        var maintenance = Substitute.For<IImageCacheMaintenanceService>();

        var handler = FakeHttpMessageHandler.ReturningStatus(HttpStatusCode.NotFound);
        var httpFactory = new FakeHttpClientFactory(handler);

        var service = new ActorImageCacheService(client, cache, factory, maintenance, httpFactory);
        var result = await service.DownloadImageFromUrlAsync("https://example.com/missing.jpg");

        Assert.False(result.Success);
        Assert.Equal("Image not found at the specified URL (HTTP 404).", result.ErrorMessage);
    }

    [Fact]
    public async Task DownloadImageFromUrlAsync_HttpError_ReturnsGenericHttpError()
    {
        using var factory = new TestDbContextFactory();
        var client = Substitute.For<ILocalLibraryClient>();
        var cache = Substitute.For<ILocalImageCache>();
        var maintenance = Substitute.For<IImageCacheMaintenanceService>();

        var handler = FakeHttpMessageHandler.ReturningStatus(HttpStatusCode.InternalServerError);
        var httpFactory = new FakeHttpClientFactory(handler);

        var service = new ActorImageCacheService(client, cache, factory, maintenance, httpFactory);
        var result = await service.DownloadImageFromUrlAsync("https://example.com/server-error.jpg");

        Assert.False(result.Success);
        Assert.Contains("HTTP 500", result.ErrorMessage);
    }

    [Fact]
    public async Task DownloadImageFromUrlAsync_HtmlContentType_ReturnsNotAnImageError()
    {
        using var factory = new TestDbContextFactory();
        var client = Substitute.For<ILocalLibraryClient>();
        var cache = Substitute.For<ILocalImageCache>();
        var maintenance = Substitute.For<IImageCacheMaintenanceService>();

        var handler = new FakeHttpMessageHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("<html><body>Page</body></html>", System.Text.Encoding.UTF8, "text/html")
            }));
        var httpFactory = new FakeHttpClientFactory(handler);

        var service = new ActorImageCacheService(client, cache, factory, maintenance, httpFactory);
        var result = await service.DownloadImageFromUrlAsync("https://example.com/page.html");

        Assert.False(result.Success);
        Assert.Contains("The URL did not return an image (received 'text/html')", result.ErrorMessage);
    }

    [Fact]
    public async Task DownloadImageFromUrlAsync_ContentLengthExceeds20Mb_ReturnsLimitError()
    {
        using var factory = new TestDbContextFactory();
        var client = Substitute.For<ILocalLibraryClient>();
        var cache = Substitute.For<ILocalImageCache>();
        var maintenance = Substitute.For<IImageCacheMaintenanceService>();

        var handler = new FakeHttpMessageHandler((_, _) =>
        {
            var msg = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(new byte[100])
            };
            msg.Content.Headers.ContentLength = 25 * 1024 * 1024;
            return Task.FromResult(msg);
        });
        var httpFactory = new FakeHttpClientFactory(handler);

        var service = new ActorImageCacheService(client, cache, factory, maintenance, httpFactory);
        var result = await service.DownloadImageFromUrlAsync("https://example.com/huge.jpg");

        Assert.False(result.Success);
        Assert.Equal("Image size exceeds 20 MB limit.", result.ErrorMessage);
    }

    [Fact]
    public async Task DownloadImageFromUrlAsync_ContentLengthExceedsConfiguredLimit_ReturnsLimitError()
    {
        using var factory = new TestDbContextFactory();
        var client = Substitute.For<ILocalLibraryClient>();
        var cache = Substitute.For<ILocalImageCache>();
        var maintenance = Substitute.For<IImageCacheMaintenanceService>();

        var handler = new FakeHttpMessageHandler((_, _) =>
        {
            var msg = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(new byte[100])
            };
            msg.Content.Headers.ContentLength = 6 * 1024 * 1024;
            return Task.FromResult(msg);
        });
        var httpFactory = new FakeHttpClientFactory(handler);

        var customSettings = new ImageUploadSettings(5);
        var service = new ActorImageCacheService(client, cache, factory, maintenance, httpFactory, uploadSettings: customSettings);
        var result = await service.DownloadImageFromUrlAsync("https://example.com/huge.jpg");

        Assert.False(result.Success);
        Assert.Equal("Image size exceeds 5 MB limit.", result.ErrorMessage);
    }

    [Fact]
    public async Task DownloadImageFromUrlAsync_CorruptOrUnsupportedImage_ReturnsError()
    {
        using var factory = new TestDbContextFactory();
        var client = Substitute.For<ILocalLibraryClient>();
        var cache = Substitute.For<ILocalImageCache>();
        var maintenance = Substitute.For<IImageCacheMaintenanceService>();

        var handler = new FakeHttpMessageHandler((_, _) =>
        {
            var msg = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 })
            };
            msg.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/jpeg");
            return Task.FromResult(msg);
        });
        var httpFactory = new FakeHttpClientFactory(handler);

        var service = new ActorImageCacheService(client, cache, factory, maintenance, httpFactory);
        var result = await service.DownloadImageFromUrlAsync("https://example.com/corrupt.jpg");

        Assert.False(result.Success);
        Assert.Equal("Unsupported or corrupted image file. Please provide a valid JPEG, PNG, or WebP image.", result.ErrorMessage);
    }

    [Fact]
    public async Task DownloadImageFromUrlAsync_ValidPngImage_ReturnsSuccessWithBytesAndContentType()
    {
        using var factory = new TestDbContextFactory();
        var client = Substitute.For<ILocalLibraryClient>();
        var cache = Substitute.For<ILocalImageCache>();
        var maintenance = Substitute.For<IImageCacheMaintenanceService>();

        var samplePng = CreateSampleImageBytes(200, 200);
        var handler = new FakeHttpMessageHandler((_, _) =>
        {
            var msg = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(samplePng)
            };
            msg.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");
            return Task.FromResult(msg);
        });
        var httpFactory = new FakeHttpClientFactory(handler);

        var service = new ActorImageCacheService(client, cache, factory, maintenance, httpFactory);
        var result = await service.DownloadImageFromUrlAsync("https://example.com/actor.png");

        Assert.True(result.Success);
        Assert.NotNull(result.Bytes);
        Assert.Equal("image/png", result.ContentType);
        Assert.Equal(samplePng.Length, result.Bytes.Length);
    }

    [Fact]
    public async Task DownloadImageFromUrlAsync_ValidJpegImage_ReturnsSuccessWithBytesAndContentType()
    {
        using var factory = new TestDbContextFactory();
        var client = Substitute.For<ILocalLibraryClient>();
        var cache = Substitute.For<ILocalImageCache>();
        var maintenance = Substitute.For<IImageCacheMaintenanceService>();

        using var bitmap = new SkiaSharp.SKBitmap(150, 150);
        using var image = SkiaSharp.SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SkiaSharp.SKEncodedImageFormat.Jpeg, 90);
        var sampleJpeg = data.ToArray();

        var handler = new FakeHttpMessageHandler((_, _) =>
        {
            var msg = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(sampleJpeg)
            };
            msg.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/jpeg");
            return Task.FromResult(msg);
        });
        var httpFactory = new FakeHttpClientFactory(handler);

        var service = new ActorImageCacheService(client, cache, factory, maintenance, httpFactory);
        var result = await service.DownloadImageFromUrlAsync("https://example.com/actor.jpeg");

        Assert.True(result.Success);
        Assert.NotNull(result.Bytes);
        Assert.Equal("image/jpeg", result.ContentType);
        Assert.Equal(sampleJpeg.Length, result.Bytes.Length);
    }

    [Fact]
    public async Task DownloadImageFromUrlAsync_HttpRequestException_ReturnsErrorMessage()
    {
        using var factory = new TestDbContextFactory();
        var client = Substitute.For<ILocalLibraryClient>();
        var cache = Substitute.For<ILocalImageCache>();
        var maintenance = Substitute.For<IImageCacheMaintenanceService>();

        var handler = FakeHttpMessageHandler.Throwing(new HttpRequestException("Connection reset by peer"));
        var httpFactory = new FakeHttpClientFactory(handler);

        var service = new ActorImageCacheService(client, cache, factory, maintenance, httpFactory);
        var result = await service.DownloadImageFromUrlAsync("https://example.com/broken.jpg");

        Assert.False(result.Success);
        Assert.Contains("Connection reset by peer", result.ErrorMessage);
    }

    [Fact]
    public async Task GetOrCreateAsync_VariantSource_ServesStoredSource_WhenSourceExists()
    {
        using var factory = new TestDbContextFactory();
        var sourceStorageId = Guid.NewGuid();
        var tempFile = Path.GetTempFileName();
        try
        {
            await using (var db = await factory.CreateDbContextAsync())
            {
                var actor = new Actor { FirstName = "Yua", LastName = "Mikami" };
                db.Actors.Add(actor);
                await db.SaveChangesAsync();

                db.ActorImages.Add(new ActorImage
                {
                    ActorId = actor.Id,
                    Variant = ActorImageCacheService.VariantFull,
                    SourceMovieCode = ActorImageCacheService.SourceMovieCodeCustom,
                    StorageId = Guid.NewGuid(),
                    SourceStorageId = sourceStorageId,
                    SourceExtension = ".jpg",
                    CropWidth = 0.5
                });
                db.ActorImages.Add(new ActorImage
                {
                    ActorId = actor.Id,
                    Variant = ActorImageCacheService.VariantThumb,
                    SourceMovieCode = ActorImageCacheService.SourceMovieCodeCustom,
                    StorageId = Guid.NewGuid(),
                    SourceStorageId = sourceStorageId,
                    SourceExtension = ".jpg",
                    CropWidth = 0.5
                });
                await db.SaveChangesAsync();
            }

            var client = Substitute.For<ILocalLibraryClient>();
            var cache = Substitute.For<ILocalImageCache>();
            var maintenance = Substitute.For<IImageCacheMaintenanceService>();
            var dataStore = Substitute.For<IActorImageDataStore>();
            var stored = new StoredObject($"{sourceStorageId:N}.jpg", File.OpenRead(tempFile), 0, DateTimeOffset.UnixEpoch, "\"test\"");
            dataStore.OpenReadAsync(sourceStorageId, ".jpg", Arg.Any<CancellationToken>()).Returns(stored);

            var service = new ActorImageCacheService(client, cache, factory, maintenance, dataStore: dataStore);
            var result = await service.GetOrCreateAsync(1, ActorImageCacheService.VariantSource);

            Assert.Equal(ImageServingResultKind.Stored, result.Kind);
            Assert.Same(stored, result.Object);
            await stored.DisposeAsync();
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task GetOrCreateAsync_VariantSource_FallsBackToFullPath_WhenSourceFileNotFound()
    {
        using var factory = new TestDbContextFactory();
        var thumbStorageId = Guid.NewGuid();
        var fullStorageId = Guid.NewGuid();
        var tempThumbFile = Path.GetTempFileName();
        var tempFullFile = Path.GetTempFileName();
        try
        {
            await using (var db = await factory.CreateDbContextAsync())
            {
                var actor = new Actor { FirstName = "Yua", LastName = "Mikami" };
                db.Actors.Add(actor);
                await db.SaveChangesAsync();

                db.ActorImages.Add(new ActorImage
                {
                    ActorId = actor.Id,
                    Variant = ActorImageCacheService.VariantFull,
                    SourceMovieCode = ActorImageCacheService.SourceMovieCodeCustom,
                    StorageId = fullStorageId
                });
                db.ActorImages.Add(new ActorImage
                {
                    ActorId = actor.Id,
                    Variant = ActorImageCacheService.VariantThumb,
                    SourceMovieCode = ActorImageCacheService.SourceMovieCodeCustom,
                    StorageId = thumbStorageId
                });
                await db.SaveChangesAsync();
            }

            var client = Substitute.For<ILocalLibraryClient>();
            var cache = Substitute.For<ILocalImageCache>();
            cache.GetStoragePath(thumbStorageId).Returns(tempThumbFile);
            cache.GetStoragePath(fullStorageId).Returns(tempFullFile);
            var maintenance = Substitute.For<IImageCacheMaintenanceService>();

            var service = new ActorImageCacheService(client, cache, factory, maintenance);
            var result = await service.GetOrCreateAsync(1, ActorImageCacheService.VariantSource);

            Assert.Equal(ImageServingResult.LocalFile(tempFullFile), result);
        }
        finally
        {
            if (File.Exists(tempThumbFile)) File.Delete(tempThumbFile);
            if (File.Exists(tempFullFile)) File.Delete(tempFullFile);
        }
    }

    [Theory]
    [InlineData("ABP-123")]
    [InlineData(ActorImageCacheService.SourceMovieCodeRemote)]
    [InlineData(ActorImageCacheService.SourceMovieCodeCustom)]
    public async Task SaveCustomImageAsync_DeletesPreviousSourceWhateverItsOrigin(string previousSourceCode)
    {
        using var factory = new TestDbContextFactory();
        using var cache = new TempCacheRoot();
        var dataStore = Substitute.For<IActorImageDataStore>();
        var (actorId, previousSourceId) = await SeedActorWithSourcedImageAsync(factory, previousSourceCode);

        var service = new ActorImageCacheService(Substitute.For<ILocalLibraryClient>(), cache, factory,
            Substitute.For<IImageCacheMaintenanceService>(), dataStore: dataStore);
        await service.SaveCustomImageAsync(actorId, CreateSampleImageBytes(400, 400));

        await dataStore.Received(1).DeleteAsync(previousSourceId, ".png", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SaveCustomImageAsync_RecordsSourceExtension_SoTheSourceOpensWithoutListing()
    {
        using var factory = new TestDbContextFactory();
        using var cache = new TempCacheRoot();
        var stores = new InMemoryObjectStoreProvider();
        var (actorId, _) = await SeedActorWithSourcedImageAsync(factory, "ABP-123");

        var service = new ActorImageCacheService(Substitute.For<ILocalLibraryClient>(), cache, factory,
            Substitute.For<IImageCacheMaintenanceService>(), dataStore: new ActorImageDataStore(stores, Path.Combine(Path.GetTempPath(), $"javbuddy-test-staging-{Guid.NewGuid():N}")));
        await service.SaveCustomImageAsync(actorId, CreateSampleImageBytes(400, 400));

        await using (var db = await factory.CreateDbContextAsync())
        {
            var rows = await db.ActorImages.Where(image => image.ActorId == actorId).ToListAsync();
            Assert.Equal(2, rows.Count);
            Assert.All(rows, row => Assert.Equal(".png", row.SourceExtension));
        }

        var objects = stores.Get(ObjectStoreArea.ActorImages);
        var listsBefore = objects.ListCount;
        var result = await service.GetOrCreateAsync(actorId, ActorImageCacheService.VariantSource);
        Assert.Equal(ImageServingResultKind.Stored, result.Kind);
        await result.Object!.DisposeAsync();
        Assert.Equal(listsBefore, objects.ListCount);
    }

    [Theory]
    [InlineData("ABP-123")]
    [InlineData(ActorImageCacheService.SourceMovieCodeRemote)]
    [InlineData(ActorImageCacheService.SourceMovieCodeCustom)] // custom rows whose cached files are gone get replaced
    public async Task DownloadAndCacheRemoteImageAsync_DeletesPreviousSourceWhateverItsOrigin(string previousSourceCode)
    {
        using var factory = new TestDbContextFactory();
        using var cache = new TempCacheRoot();
        var dataStore = Substitute.For<IActorImageDataStore>();
        var (actorId, previousSourceId) = await SeedActorWithSourcedImageAsync(factory, previousSourceCode);
        var sampleBytes = CreateSampleImageBytes(300, 300);
        var httpFactory = new FakeHttpClientFactory(new FakeHttpMessageHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(sampleBytes) })));

        var service = new ActorImageCacheService(Substitute.For<ILocalLibraryClient>(), cache, factory,
            Substitute.For<IImageCacheMaintenanceService>(), httpFactory, dataStore: dataStore);
        var (thumbPath, _) = await service.DownloadAndCacheRemoteImageAsync(actorId, "https://pics.dmm.co.jp/mono/actjpgs/araki_noa.jpg");

        Assert.NotNull(thumbPath);
        await dataStore.Received(1).DeleteAsync(previousSourceId, ".png", Arg.Any<CancellationToken>());
    }

    private static async Task<(int ActorId, Guid SourceStorageId)> SeedActorWithSourcedImageAsync(TestDbContextFactory factory, string sourceMovieCode)
    {
        var sourceStorageId = Guid.NewGuid();
        await using var db = await factory.CreateDbContextAsync();
        var actor = new Actor { FirstName = "Noa", LastName = "Araki" };
        db.Actors.Add(actor);
        await db.SaveChangesAsync();
        foreach (var variant in new[] { ActorImageCacheService.VariantThumb, ActorImageCacheService.VariantFull })
        {
            db.ActorImages.Add(new ActorImage
            {
                ActorId = actor.Id,
                Variant = variant,
                SourceMovieCode = sourceMovieCode,
                StorageId = Guid.NewGuid(),
                SourceStorageId = sourceStorageId,
                SourceExtension = ".png",
                SourceUrl = "https://pics.dmm.co.jp/mono/actjpgs/old.jpg",
                UpdatedAt = DateTime.UtcNow
            });
        }
        await db.SaveChangesAsync();
        return (actor.Id, sourceStorageId);
    }

    private static ActorImageCacheService DownloadService(HttpMessageHandler handler, ImageUploadSettings? uploadSettings = null) =>
        new(Substitute.For<ILocalLibraryClient>(), Substitute.For<ILocalImageCache>(), new TestDbContextFactory(),
            Substitute.For<IImageCacheMaintenanceService>(), new FakeHttpClientFactory(handler), uploadSettings: uploadSettings);

    private static FakeHttpMessageHandler Responding(Func<HttpContent> content) =>
        new((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content() }));

    private static byte[] PngBytes()
    {
        using var bitmap = new SkiaSharp.SKBitmap(4, 4);
        bitmap.Erase(SkiaSharp.SKColors.Red);
        using var data = bitmap.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    [Fact]
    public async Task DownloadImageFromUrlAsync_ValidImageWithoutContentLength_Succeeds()
    {
        var png = PngBytes();
        var service = DownloadService(Responding(() => new StreamContent(new ControlledStream(png))));

        var result = await service.DownloadImageFromUrlAsync("https://example.com/photo.png");

        Assert.True(result.Success);
        Assert.Equal("image/png", result.ContentType);
        Assert.Equal(png, result.Bytes);
    }

    [Fact]
    public async Task DownloadImageFromUrlAsync_MalformedImage_ReturnsCorruptedError()
    {
        var service = DownloadService(Responding(() => new ByteArrayContent("not an image"u8.ToArray())));

        var result = await service.DownloadImageFromUrlAsync("https://example.com/photo.png");

        Assert.False(result.Success);
        Assert.Equal("Unsupported or corrupted image file. Please provide a valid JPEG, PNG, or WebP image.", result.ErrorMessage);
    }

    [Fact]
    public async Task DownloadImageFromUrlAsync_EmptyBody_ReturnsEmptyError()
    {
        var service = DownloadService(Responding(() => new ByteArrayContent([])));

        var result = await service.DownloadImageFromUrlAsync("https://example.com/photo.png");

        Assert.Equal("The downloaded image was empty.", result.ErrorMessage);
    }

    [Fact]
    public async Task DownloadImageFromUrlAsync_DeclaredOversize_ReturnsSizeError()
    {
        var service = DownloadService(Responding(() => new ByteArrayContent(new byte[1024 * 1024 + 1])), new ImageUploadSettings(MaxSizeMb: 1));

        var result = await service.DownloadImageFromUrlAsync("https://example.com/photo.png");

        Assert.Equal("Image size exceeds 1 MB limit.", result.ErrorMessage);
    }

    [Fact]
    public async Task DownloadImageFromUrlAsync_ChunkedOversize_ReturnsSizeError()
    {
        var service = DownloadService(Responding(() => new StreamContent(new ControlledStream(new byte[1024 * 1024 + 1]))), new ImageUploadSettings(MaxSizeMb: 1));

        var result = await service.DownloadImageFromUrlAsync("https://example.com/photo.png");

        Assert.Equal("Image size exceeds 1 MB limit.", result.ErrorMessage);
    }

    [Fact]
    public async Task DownloadImageFromUrlAsync_Timeout_ReturnsTimeoutError()
    {
        var service = DownloadService(FakeHttpMessageHandler.Throwing(new TaskCanceledException("timed out")));

        var result = await service.DownloadImageFromUrlAsync("https://example.com/photo.png");

        Assert.Equal("Request timed out while downloading the image.", result.ErrorMessage);
    }

    [Fact]
    public async Task DownloadImageFromUrlAsync_MidStreamFailure_ReturnsReadError()
    {
        var service = DownloadService(Responding(() => new StreamContent(new ControlledStream(new byte[10], ControlledStream.AfterData.Throw))));

        var result = await service.DownloadImageFromUrlAsync("https://example.com/photo.png");

        Assert.Equal("Error reading image content: connection reset mid-stream", result.ErrorMessage);
    }

    [Fact]
    public async Task DownloadImageFromUrlAsync_CallerCancelledMidStream_Propagates()
    {
        var service = DownloadService(Responding(() => new StreamContent(new ControlledStream(new byte[10], ControlledStream.AfterData.BlockUntilCancelled))));
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.DownloadImageFromUrlAsync("https://example.com/photo.png", cts.Token));
    }

    [Fact]
    public async Task DownloadImageFromUrlAsync_CallerCancelledBeforeResponse_Propagates()
    {
        var service = DownloadService(new FakeHttpMessageHandler(async (_, token) =>
        {
            await Task.Delay(Timeout.Infinite, token);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }));
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.DownloadImageFromUrlAsync("https://example.com/photo.png", cts.Token));
    }

    [Fact]
    public async Task DownloadImageFromUrlAsync_UsesTheGuardedImportClient()
    {
        using var factory = new TestDbContextFactory();
        var http = Substitute.For<IHttpClientFactory>();
        http.CreateClient(Arg.Any<string>()).Returns(_ => new HttpClient(new FakeHttpMessageHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([1, 2, 3]) }))));
        var service = new ActorImageCacheService(Substitute.For<ILocalLibraryClient>(), Substitute.For<ILocalImageCache>(), factory,
            Substitute.For<IImageCacheMaintenanceService>(), http);

        await service.DownloadImageFromUrlAsync("https://example.com/a.jpg");

        http.Received(1).CreateClient(RemoteImageDownloader.ImportClientName);
    }
}
