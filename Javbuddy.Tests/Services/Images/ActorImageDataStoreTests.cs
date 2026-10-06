using Javbuddy.Services.Images;
using Javbuddy.Services.Infrastructure;
using Javbuddy.Tests.TestSupport;
using SkiaSharp;

namespace Javbuddy.Tests.Services.Images;

public class ActorImageDataStoreTests : IDisposable
{
    private readonly string testDir = Path.Combine(Path.GetTempPath(), $"javbuddy-datastore-test-{Guid.NewGuid():N}");
    private readonly string stagingDir = Path.Combine(Path.GetTempPath(), $"javbuddy-datastore-staging-test-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(testDir))
        {
            Directory.Delete(testDir, recursive: true);
        }
        if (Directory.Exists(stagingDir))
        {
            Directory.Delete(stagingDir, recursive: true);
        }
    }

    [Fact]
    public async Task StageAsync_WritesToLocalOsTempStagingDirectory()
    {
        var store = CreateStore();
        var storageId = Guid.NewGuid();
        var bytes = CreateValidPngBytes();

        var extension = await store.StageAsync(storageId, bytes);

        var tempFiles = Directory.GetFiles(stagingDir);
        var file = Assert.Single(tempFiles);
        Assert.StartsWith(storageId.ToString("N"), Path.GetFileName(file));
        Assert.EndsWith(".png", file);
        Assert.Equal(".png", extension);
        Assert.False(await store.ExistsAsync(storageId, extension));
    }

    [Fact]
    public async Task Commit_MovesFileFromStagingToPermanentShard()
    {
        var store = CreateStore();
        var storageId = Guid.NewGuid();
        var bytes = CreateValidPngBytes();

        var extension = await store.StageAsync(storageId, bytes);
        await store.CommitAsync(storageId);

        Assert.Empty(Directory.GetFiles(stagingDir));

        // <object store>/actor-images/<2 hex>/<guid>.<ext>.
        var name = storageId.ToString("N");
        var path = Path.Combine(testDir, "actor-images", name[..2], name + ".png");
        Assert.Equal(bytes, await File.ReadAllBytesAsync(path));
        Assert.Equal(bytes, await store.ReadAsync(storageId, extension));
    }

    [Fact]
    public async Task DeleteStaged_RemovesFromStaging()
    {
        var store = CreateStore();
        var storageId = Guid.NewGuid();
        var bytes = CreateValidPngBytes();

        await store.StageAsync(storageId, bytes);
        store.DeleteStaged(storageId);

        Assert.Empty(Directory.GetFiles(stagingDir));
    }

    [Fact]
    public async Task Constructor_PurgesExistingStaging()
    {
        Directory.CreateDirectory(stagingDir);
        var leftoverFile = Path.Combine(stagingDir, "aborted.png");
        await File.WriteAllBytesAsync(leftoverFile, [1, 2, 3]);

        _ = CreateStore();

        Assert.False(File.Exists(leftoverFile));
    }

    [Fact]
    public async Task ExistingFile_IsFoundWhateverItsExtension()
    {
        // A file moved in from the old actor images folder keeps its extension.
        var storageId = Guid.NewGuid();
        var name = storageId.ToString("N");
        var bytes = CreateValidPngBytes();
        Directory.CreateDirectory(Path.Combine(testDir, "actor-images", name[..2]));
        await File.WriteAllBytesAsync(Path.Combine(testDir, "actor-images", name[..2], name + ".jpg"), bytes);
        var store = CreateStore();

        Assert.True(await store.ExistsAsync(storageId, extension: null));
        await using var stored = await store.OpenReadAsync(storageId, extension: null);
        Assert.NotNull(stored);
        Assert.Equal($"{name[..2]}/{name}.jpg", stored.Key);
        Assert.Equal(bytes.Length, stored.Length);
    }

    [Fact]
    public async Task WriteReadDelete_OverAFakeStore()
    {
        var stores = new InMemoryObjectStoreProvider();
        var store = new ActorImageDataStore(stores, stagingDir);
        var storageId = Guid.NewGuid();
        var name = storageId.ToString("N");
        var bytes = CreateValidPngBytes();

        var extension = await store.WriteAsync(storageId, bytes);

        Assert.Equal(".png", extension);
        Assert.Equal([$"{name[..2]}/{name}.png"], stores.Get(ObjectStoreArea.ActorImages).Keys);
        Assert.Equal(bytes, await store.ReadAsync(storageId, extension));
        Assert.True(await store.ExistsAsync(storageId, extension));

        await store.DeleteAsync(storageId, extension);

        Assert.Empty(stores.Get(ObjectStoreArea.ActorImages).Keys);
        Assert.Null(await store.ReadAsync(storageId, extension));
        Assert.Null(await store.OpenReadAsync(storageId, extension));
        await store.DeleteAsync(storageId, extension);
    }

    [Fact]
    public async Task RecordedExtension_AccessesTheExactKeyWithoutListing()
    {
        var stores = new InMemoryObjectStoreProvider();
        var objects = stores.Get(ObjectStoreArea.ActorImages);
        var store = new ActorImageDataStore(stores, stagingDir);
        var storageId = Guid.NewGuid();
        var bytes = CreateValidPngBytes();
        var extension = await store.WriteAsync(storageId, bytes);

        Assert.True(await store.ExistsAsync(storageId, extension));
        await using (var stored = await store.OpenReadAsync(storageId, extension))
        {
            Assert.NotNull(stored);
        }
        Assert.Equal(bytes, await store.ReadAsync(storageId, extension));
        await store.DeleteAsync(storageId, extension);

        Assert.Empty(objects.Keys);
        Assert.Equal(0, objects.ListCount);
    }

    [Fact]
    public async Task MissingExtension_FallsBackToListing()
    {
        // Rows from before the extension was recorded only hold the id.
        var stores = new InMemoryObjectStoreProvider();
        var objects = stores.Get(ObjectStoreArea.ActorImages);
        var store = new ActorImageDataStore(stores, stagingDir);
        var storageId = Guid.NewGuid();
        var bytes = CreateValidPngBytes();
        await store.WriteAsync(storageId, bytes);

        Assert.True(await store.ExistsAsync(storageId, extension: null));
        await using (var stored = await store.OpenReadAsync(storageId, extension: null))
        {
            Assert.NotNull(stored);
        }
        Assert.Equal(bytes, await store.ReadAsync(storageId, extension: null));
        await store.DeleteAsync(storageId, extension: null);

        Assert.Empty(objects.Keys);
        Assert.Equal(4, objects.ListCount);
    }

    [Fact]
    public async Task WriteAsync_RejectsUndecodableBytes()
    {
        var store = new ActorImageDataStore(new InMemoryObjectStoreProvider(), stagingDir);

        await Assert.ThrowsAsync<ArgumentException>(() => store.WriteAsync(Guid.NewGuid(), new byte[] { 1, 2, 3 }));
    }

    private ActorImageDataStore CreateStore() =>
        new(new ObjectStoreProvider(new FileSystemObjectStore(testDir), testDir), stagingDir);

    private static byte[] CreateValidPngBytes()
    {
        using var bitmap = new SKBitmap(2, 2);
        bitmap.Erase(SKColors.Red);
        using var image = SKImage.FromBitmap(bitmap);
        using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
        return encoded.ToArray();
    }
}
