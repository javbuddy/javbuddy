using System.Net;
using System.Net.Http.Headers;
using Javbuddy.Models;
using Javbuddy.Services.Images;
using Javbuddy.Services.LocalLibrary;
using Javbuddy.Services.Movies;
using Javbuddy.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using SkiaSharp;

namespace Javbuddy.Tests.Services.Movies;

public sealed class MovieExtraFanartServiceTests : IDisposable
{
    private readonly string movieDir = Path.Combine(Path.GetTempPath(), "javbuddy_test_" + Guid.NewGuid());
    private readonly string cacheDir = Path.Combine(Path.GetTempPath(), "javbuddy_cache_" + Guid.NewGuid());
    private readonly TestDbContextFactory factory = new();
    private readonly ILocalLibraryClient client = Substitute.For<ILocalLibraryClient>();
    private readonly ILocalImageCache cache = Substitute.For<ILocalImageCache>();
    private readonly int movieId;

    private string ExtraDir => Path.Combine(movieDir, "extrafanart");

    public MovieExtraFanartServiceTests()
    {
        Directory.CreateDirectory(movieDir);
        Directory.CreateDirectory(cacheDir);
        using (var db = factory.CreateDbContext())
        {
            var movie = new Movie { Code = "IPX-535", Title = "T", Status = MovieStatus.Got };
            db.Movies.Add(movie);
            db.SaveChanges();
            movieId = movie.Id;
        }
        client.ResolveMovieFolderPathAsync("IPX-535", Arg.Any<CancellationToken>()).Returns(movieDir);
        client.ListExtraFanartFileNamesAsync("IPX-535", Arg.Any<CancellationToken>()).Returns(_ => ListExtra());
        cache.GetStoragePath(Arg.Any<Guid>()).Returns(c => Path.Combine(cacheDir, c.Arg<Guid>() + ".webp"));
    }

    public void Dispose()
    {
        factory.Dispose();
        if (Directory.Exists(movieDir)) Directory.Delete(movieDir, true);
        if (Directory.Exists(cacheDir)) Directory.Delete(cacheDir, true);
    }

    private List<string> ListExtra() => Directory.Exists(ExtraDir)
        ? Directory.GetFiles(ExtraDir).Select(f => Path.GetFileName(f)).Where(n => !n.StartsWith('.')).Order().ToList()
        : [];

    private MovieExtraFanartService CreateService(IHttpClientFactory? http = null) =>
        new(factory, client, cache, http ?? Substitute.For<IHttpClientFactory>());

    private static byte[] Image(SKEncodedImageFormat format)
    {
        using var bitmap = new SKBitmap(40, 30);
        using (var canvas = new SKCanvas(bitmap)) canvas.Clear(SKColors.Teal);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(format, 90);
        return data.ToArray();
    }

    private static ExtraFanartUploadItem Item(byte[] bytes) => new(() => new MemoryStream(bytes), bytes.Length);

    private async Task SeedCacheRowsAsync(params int[] indexes)
    {
        await using var db = await factory.CreateDbContextAsync();
        foreach (var index in indexes)
        {
            var id = Guid.NewGuid();
            await File.WriteAllBytesAsync(Path.Combine(cacheDir, id + ".webp"), [1]);
            db.CachedImages.Add(new CachedImage { Code = "IPX-535", Role = "extrafanart", Index = index, Variant = "thumb", StorageId = id });
        }
        await db.SaveChangesAsync();
    }

    private async Task AssertCachedIndexesAsync(int[] expected)
    {
        await using var db = await factory.CreateDbContextAsync();
        var rows = await db.CachedImages.Where(c => c.Role == "extrafanart").OrderBy(c => c.Index).ToListAsync();
        Assert.Equal(expected, rows.Select(r => r.Index).ToArray());
        Assert.Equal(expected.Length, Directory.GetFiles(cacheDir).Length);
    }

    [Fact]
    public async Task AddAsync_NoExtrafanartFolder_CreatesItAndWritesFanart1()
    {
        var bytes = Image(SKEncodedImageFormat.Jpeg);

        var result = await CreateService().AddAsync(movieId, [Item(bytes)]);

        Assert.True(result.Success);
        Assert.Equal(1, result.AddedCount);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(Path.Combine(ExtraDir, "fanart1.jpg")));
        await client.Received(1).SyncImagesSignatureAsync(movieId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AddAsync_WithGapsAndOtherNames_NumbersFromHighestFanart()
    {
        Directory.CreateDirectory(ExtraDir);
        File.WriteAllBytes(Path.Combine(ExtraDir, "fanart1.jpg"), Image(SKEncodedImageFormat.Jpeg));
        File.WriteAllBytes(Path.Combine(ExtraDir, "fanart3.png"), Image(SKEncodedImageFormat.Png));
        File.WriteAllBytes(Path.Combine(ExtraDir, "cover.jpg"), Image(SKEncodedImageFormat.Jpeg));

        var result = await CreateService().AddAsync(movieId, [Item(Image(SKEncodedImageFormat.Jpeg)), Item(Image(SKEncodedImageFormat.Webp))]);

        Assert.True(result.Success);
        Assert.Equal(2, result.AddedCount);
        Assert.True(File.Exists(Path.Combine(ExtraDir, "fanart4.jpg")));
        Assert.True(File.Exists(Path.Combine(ExtraDir, "fanart5.webp")));
    }

    [Fact]
    public async Task AddAsync_ExtensionComesFromDetectedFormat()
    {
        var result = await CreateService().AddAsync(movieId, [Item(Image(SKEncodedImageFormat.Png))]);

        Assert.True(result.Success);
        Assert.Equal(["fanart1.png"], ListExtra());
    }

    [Fact]
    public async Task AddAsync_InvalidImageInBatch_WritesNothing()
    {
        var result = await CreateService().AddAsync(movieId,
            [Item(Image(SKEncodedImageFormat.Jpeg)), Item("not an image"u8.ToArray())]);

        Assert.False(result.Success);
        Assert.Contains("JPEG, PNG, or WebP", result.ErrorMessage);
        Assert.True(!Directory.Exists(ExtraDir) || Directory.GetFiles(ExtraDir).Length == 0);
        await client.DidNotReceive().SyncImagesSignatureAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AddAsync_StreamThrowsNonIoException_LeavesNoTmpFiles()
    {
        // e.g. the browser disconnects partway through a batch.
        var failing = new ExtraFanartUploadItem(() => throw new InvalidOperationException("circuit gone"), 10);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            CreateService().AddAsync(movieId, [Item(Image(SKEncodedImageFormat.Jpeg)), failing]));

        Assert.True(!Directory.Exists(ExtraDir) || Directory.GetFiles(ExtraDir).Length == 0);
    }

    [Fact]
    public async Task AddAsync_UnreadableSelectedFile_ReportsAReadError()
    {
        var unreadable = new ExtraFanartUploadItem(() => throw new IOException("stream reset"), 10);

        var result = await CreateService().AddAsync(movieId, [unreadable]);

        Assert.False(result.Success);
        Assert.Equal("Could not read a selected image: stream reset", result.ErrorMessage);
    }

    [Fact]
    public async Task AddAsync_FailedFirstBatch_LeavesNoEmptyExtrafanartFolder()
    {
        var result = await CreateService().AddAsync(movieId,
            [Item(Image(SKEncodedImageFormat.Jpeg)), Item("not an image"u8.ToArray())]);

        Assert.False(result.Success);
        Assert.False(Directory.Exists(ExtraDir));
    }

    [Fact]
    public async Task AddAsync_FailedBatch_KeepsAnExistingExtrafanartFolder()
    {
        Directory.CreateDirectory(ExtraDir);

        await CreateService().AddAsync(movieId, [Item("not an image"u8.ToArray())]);

        Assert.True(Directory.Exists(ExtraDir));
    }

    [Fact]
    public async Task AddAsync_ConcurrentBatches_NeverShareAFanartNumber()
    {
        var jpeg = Image(SKEncodedImageFormat.Jpeg);
        var png = Image(SKEncodedImageFormat.Png);

        // Each service gets its own database (the shared in-memory SQLite connection isn't
        // thread-safe); both resolve the same movie folder, which is what the race is about.
        using var otherFactory = new TestDbContextFactory();
        using (var db = otherFactory.CreateDbContext())
        {
            db.Movies.Add(new Movie { Id = movieId, Code = "IPX-535", Title = "T", Status = MovieStatus.Got });
            db.SaveChanges();
        }
        var first = CreateService();
        var second = new MovieExtraFanartService(otherFactory, client, cache, Substitute.For<IHttpClientFactory>());

        for (var round = 0; round < 10; round++)
        {
            await Task.WhenAll(
                Task.Run(() => first.AddAsync(movieId, [Item(jpeg), Item(jpeg)])),
                Task.Run(() => second.AddAsync(movieId, [Item(png), Item(png)])));
        }

        var numbers = ListExtra().Select(n => Path.GetFileNameWithoutExtension(n)).ToList();
        Assert.Equal(40, numbers.Count);
        Assert.Equal(numbers.Count, numbers.Distinct().Count());
    }

    [Fact]
    public async Task AddAsync_TooLarge_Fails()
    {
        var service = new MovieExtraFanartService(factory, client, cache, Substitute.For<IHttpClientFactory>(), new ImageUploadSettings(MaxSizeMb: 1));
        var big = new ExtraFanartUploadItem(() => new MemoryStream(new byte[2 * 1024 * 1024]), 2 * 1024 * 1024);

        var result = await service.AddAsync(movieId, [big]);

        Assert.False(result.Success);
        Assert.Contains("1 MB", result.ErrorMessage);
    }

    [Fact]
    public async Task AddAsync_NoLocalFolder_Fails()
    {
        client.ResolveMovieFolderPathAsync("IPX-535", Arg.Any<CancellationToken>()).Returns((string?)null);

        var result = await CreateService().AddAsync(movieId, [Item(Image(SKEncodedImageFormat.Jpeg))]);

        Assert.False(result.Success);
        Assert.Equal("This movie has no local folder.", result.ErrorMessage);
    }

    [Fact]
    public async Task AddAsync_PurgesCacheRowsFromFirstNewIndex()
    {
        Directory.CreateDirectory(ExtraDir);
        File.WriteAllBytes(Path.Combine(ExtraDir, "fanart1.jpg"), Image(SKEncodedImageFormat.Jpeg));
        // Index 1 is a stale row left over from an earlier external delete.
        await SeedCacheRowsAsync(0, 1);

        await CreateService().AddAsync(movieId, [Item(Image(SKEncodedImageFormat.Jpeg))]);

        await AssertCachedIndexesAsync([0]);
    }

    private static IHttpClientFactory HttpReturning(byte[] body, string mediaType)
    {
        var handler = new FakeHttpMessageHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(body) { Headers = { ContentType = new MediaTypeHeaderValue(mediaType) } },
        }));
        var http = Substitute.For<IHttpClientFactory>();
        http.CreateClient(Arg.Any<string>()).Returns(_ => new HttpClient(handler));
        return http;
    }

    [Fact]
    public async Task AddFromUrlAsync_ValidImage_WritesNextFanart()
    {
        var bytes = Image(SKEncodedImageFormat.Webp);

        var result = await CreateService(HttpReturning(bytes, "image/webp")).AddFromUrlAsync(movieId, "https://example.com/a.webp");

        Assert.True(result.Success);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(Path.Combine(ExtraDir, "fanart1.webp")));
    }

    [Fact]
    public async Task AddFromUrlAsync_HtmlResponse_FailsWithoutWriting()
    {
        var result = await CreateService(HttpReturning("<html/>"u8.ToArray(), "text/html")).AddFromUrlAsync(movieId, "https://example.com/page");

        Assert.False(result.Success);
        Assert.Contains("did not return an image", result.ErrorMessage);
        Assert.False(Directory.Exists(ExtraDir));
    }

    [Fact]
    public async Task AddFromUrlAsync_InvalidUrl_Fails()
    {
        var result = await CreateService().AddFromUrlAsync(movieId, "ftp://example.com/a.jpg");

        Assert.False(result.Success);
        Assert.Equal("Please enter a valid HTTP or HTTPS image URL.", result.ErrorMessage);
    }

    [Fact]
    public async Task DeleteAsync_RemovesOnlyThatFileAndPurgesFromItsIndex()
    {
        Directory.CreateDirectory(ExtraDir);
        foreach (var name in new[] { "fanart1.jpg", "fanart2.jpg", "fanart3.jpg" })
            File.WriteAllBytes(Path.Combine(ExtraDir, name), Image(SKEncodedImageFormat.Jpeg));
        client.ResolveLiveExtraFanartFilePathAsync("IPX-535", "fanart2.jpg", Arg.Any<CancellationToken>())
            .Returns(Path.Combine(ExtraDir, "fanart2.jpg"));
        await SeedCacheRowsAsync(0, 1, 2);

        var result = await CreateService().DeleteAsync(movieId, "fanart2.jpg");

        Assert.True(result.Success);
        Assert.Equal(["fanart1.jpg", "fanart3.jpg"], ListExtra());
        await AssertCachedIndexesAsync([0]);
        await client.Received(1).SyncImagesSignatureAsync(movieId, Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("../poster.jpg")]
    [InlineData("missing.jpg")]
    public async Task DeleteAsync_UnresolvableName_FailsAndTouchesNothing(string fileName)
    {
        File.WriteAllBytes(Path.Combine(movieDir, "poster.jpg"), [1]);
        client.ResolveLiveExtraFanartFilePathAsync("IPX-535", fileName, Arg.Any<CancellationToken>()).Returns((string?)null);

        var result = await CreateService().DeleteAsync(movieId, fileName);

        Assert.False(result.Success);
        Assert.Equal("That image no longer exists.", result.ErrorMessage);
        Assert.True(File.Exists(Path.Combine(movieDir, "poster.jpg")));
    }

    [Fact]
    public async Task AddFromUrlAsync_UsesTheGuardedImportClient()
    {
        var http = HttpReturning(Image(SKEncodedImageFormat.Jpeg), "image/jpeg");

        await CreateService(http).AddFromUrlAsync(movieId, "https://example.com/a.jpg");

        http.Received(1).CreateClient(RemoteImageDownloader.ImportClientName);
    }
}
