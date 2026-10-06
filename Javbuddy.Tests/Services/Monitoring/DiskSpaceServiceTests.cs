using Javbuddy.Services.Infrastructure;
using Javbuddy.Services.LocalLibrary;
using Javbuddy.Services.Monitoring;
using NSubstitute;

namespace Javbuddy.Tests.Services.Monitoring;

public class DiskSpaceServiceTests
{
    private sealed class TempDir : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "javbuddy-tests-disk-" + Guid.NewGuid().ToString("N"));

        public TempDir() => Directory.CreateDirectory(Path);

        public void Dispose()
        {
            try
            {
                Directory.Delete(Path, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }

    private static ILocalLibraryClient CreateLocalLibraryClient(params string[] rootPaths)
    {
        var client = Substitute.For<ILocalLibraryClient>();
        client.GetRootPathsAsync(Arg.Any<CancellationToken>()).Returns(rootPaths.ToList());
        return client;
    }

    /// <summary>An object store whose local folder is the given path; null is a store with no local
    /// disk (e.g. S3).</summary>
    private static IObjectStoreProvider Stores(string? objectStore)
    {
        var provider = Substitute.For<IObjectStoreProvider>();
        provider.LocalRoot.Returns(objectStore);
        return provider;
    }

    [Fact]
    public async Task GetDiskSpaceAsync_ReturnsDataDirectoryAndConfiguredRoots()
    {
        using var dataDir = new TempDir();
        using var libraryRoot = new TempDir();
        using var imageCache = new TempDir();
        using var objects = new TempDir();
        var service = new DiskSpaceService(CreateLocalLibraryClient(libraryRoot.Path), Stores(objects.Path));

        var result = await service.GetDiskSpaceAsync(dataDir.Path, imageCache.Path);

        Assert.Equal(4, result.Count);
        Assert.Equal(dataDir.Path, result[0].Location);
        Assert.Null(result[0].Label);
        Assert.Equal(libraryRoot.Path, result[1].Location);
        Assert.Null(result[1].Label);
        Assert.Equal(imageCache.Path, result[2].Location);
        Assert.Equal("Image Cache", result[2].Label);
        Assert.Equal(objects.Path, result[3].Location);
        Assert.Equal("Object Store", result[3].Label);
        Assert.All(result, r =>
        {
            Assert.True(r.TotalBytes > 0);
            Assert.True(r.FreeBytes >= 0);
        });
    }

    [Fact]
    public async Task GetDiskSpaceAsync_SkipsUnreachableRootPath()
    {
        using var dataDir = new TempDir();
        using var imageCache = new TempDir();
        using var objects = new TempDir();
        var missingRoot = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "javbuddy-tests-missing-" + Guid.NewGuid().ToString("N"));
        var service = new DiskSpaceService(CreateLocalLibraryClient(missingRoot), Stores(objects.Path));

        var result = await service.GetDiskSpaceAsync(dataDir.Path, imageCache.Path);

        Assert.Equal(3, result.Count);
        Assert.Equal(dataDir.Path, result[0].Location);
    }

    [Fact]
    public async Task GetDiskSpaceAsync_SkipsUnreachableImageCacheAndObjectStorePaths()
    {
        using var dataDir = new TempDir();
        var missingCache = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "javbuddy-tests-missing-cache-" + Guid.NewGuid().ToString("N"));
        var missingObjects = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "javbuddy-tests-missing-objects-" + Guid.NewGuid().ToString("N"));
        var service = new DiskSpaceService(CreateLocalLibraryClient(), Stores(missingObjects));

        var result = await service.GetDiskSpaceAsync(dataDir.Path, missingCache);

        Assert.Single(result);
        Assert.Equal(dataDir.Path, result[0].Location);
    }

    [Fact]
    public async Task GetDiskSpaceAsync_DedupesIdenticalLocations()
    {
        using var dataDir = new TempDir();
        var service = new DiskSpaceService(CreateLocalLibraryClient(dataDir.Path), Stores(dataDir.Path));

        var result = await service.GetDiskSpaceAsync(dataDir.Path, dataDir.Path);

        Assert.Single(result);
        Assert.Null(result[0].Label);
    }

    [Fact]
    public async Task GetDiskSpaceAsync_LeavesOutAnObjectStoreWithNoLocalDisk()
    {
        using var dataDir = new TempDir();
        using var imageCache = new TempDir();
        var service = new DiskSpaceService(CreateLocalLibraryClient(), Stores(objectStore: null));

        var result = await service.GetDiskSpaceAsync(dataDir.Path, imageCache.Path);

        Assert.Equal([null, "Image Cache"], result.Select(r => r.Label));
    }
}
