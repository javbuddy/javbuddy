using Javbuddy.Services.Infrastructure;
using Microsoft.Extensions.Configuration;

namespace Javbuddy.Tests.Services.Infrastructure;

public sealed class FileSystemObjectStoreTests : IDisposable
{
    private readonly string root = Directory.CreateTempSubdirectory("javbuddy-object-store-test-").FullName;

    public void Dispose()
    {
        try
        {
            Directory.Delete(root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private FileSystemObjectStore Store() => new(root);

    private static MemoryStream Bytes(params byte[] bytes) => new(bytes);

    private static async Task<byte[]> ReadAllAsync(StoredObject stored)
    {
        using var memory = new MemoryStream();
        await stored.Content.CopyToAsync(memory);
        return memory.ToArray();
    }

    [Fact]
    public void ResolveRoot_IsTheObjectStorePath_ElseUnderTheContentRoot()
    {
        static string Resolve(Dictionary<string, string?> settings) =>
            FileSystemObjectStore.ResolveRoot(new ConfigurationBuilder().AddInMemoryCollection(settings).Build(), "/app");

        Assert.Equal("/objects", Resolve(new() { ["ObjectStore:Path"] = "/objects" }));
        Assert.Equal(Path.Combine("/app", "data", "objects"), Resolve([]));
    }

    [Fact]
    public async Task Provider_GivesEachAreaAPrefixInTheOneTree()
    {
        var provider = new ObjectStoreProvider(Store(), root);
        var actorImages = provider.Get(ObjectStoreArea.ActorImages);

        await actorImages.WriteAsync("ab/abc.png", Bytes(1));
        await provider.Get(ObjectStoreArea.Trickplay).WriteAsync("ABC-123/0123/0.webp", Bytes(2));

        Assert.True(File.Exists(Path.Combine(root, "actor-images", "ab", "abc.png")));
        Assert.True(File.Exists(Path.Combine(root, "trickplay", "ABC-123", "0123", "0.webp")));
        Assert.Equal(["ab/abc.png"], (await actorImages.ListAsync("").ToListAsync()).Select(o => o.Key));
        await using (var stored = await actorImages.OpenReadAsync("ab/abc.png"))
        {
            Assert.Equal("ab/abc.png", stored?.Key);
        }
        Assert.True(await actorImages.ExistsAsync("ab/abc.png"));
        Assert.False(await actorImages.ExistsAsync("ABC-123/0123/0.webp"));

        await provider.Get(ObjectStoreArea.Trickplay).DeletePrefixAsync("ABC-123/");
        await actorImages.DeleteAsync("ab/abc.png");

        Assert.Empty(Directory.GetFileSystemEntries(root));
        Assert.Equal(root, provider.LocalRoot);
        Assert.Same(actorImages, provider.Get(ObjectStoreArea.ActorImages));
    }

    [Fact]
    public async Task WriteAsync_StoresTheKeyAsARelativePath_AndLeavesNoTempFile()
    {
        var store = Store();

        await store.WriteAsync("ABC-123/0123/0.webp", Bytes(1, 2, 3));

        Assert.Equal([1, 2, 3], await File.ReadAllBytesAsync(Path.Combine(root, "ABC-123", "0123", "0.webp")));
        Assert.Single(Directory.GetFiles(root, "*", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task WriteAsync_ReplacesAnExistingObject()
    {
        var store = Store();
        await store.WriteAsync("a/b.webp", Bytes(1, 2, 3));

        await store.WriteAsync("a/b.webp", Bytes(9));

        await using var stored = await store.OpenReadAsync("a/b.webp");
        Assert.NotNull(stored);
        Assert.Equal([9], await ReadAllAsync(stored));
        Assert.Equal(1, stored.Length);
    }

    [Fact]
    public async Task WriteAsync_ThatFails_KeepsTheOldObjectAndLeavesNoTempFile()
    {
        var store = Store();
        await store.WriteAsync("a/b.webp", Bytes(1, 2, 3));

        await Assert.ThrowsAsync<IOException>(() => store.WriteAsync("a/b.webp", new FailingStream()));

        Assert.Equal([1, 2, 3], await File.ReadAllBytesAsync(Path.Combine(root, "a", "b.webp")));
        Assert.Single(Directory.GetFiles(root, "*", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task OpenReadAsync_GivesLengthLastModifiedAndETag()
    {
        var store = Store();
        await store.WriteAsync("a/b.webp", Bytes(1, 2, 3));
        var info = new FileInfo(Path.Combine(root, "a", "b.webp"));

        await using var stored = await store.OpenReadAsync("a/b.webp");

        Assert.NotNull(stored);
        Assert.Equal("a/b.webp", stored.Key);
        Assert.Equal(3, stored.Length);
        Assert.Equal(info.LastWriteTimeUtc, stored.LastModified.UtcDateTime);
        Assert.Equal($"\"{info.LastWriteTimeUtc.Ticks:x}-3\"", stored.ETag);
    }

    [Fact]
    public async Task MissingKeys_AreNullFalseAndNoOps()
    {
        var store = Store();

        Assert.Null(await store.OpenReadAsync("no/such.webp"));
        Assert.False(await store.ExistsAsync("no/such.webp"));
        await store.DeleteAsync("no/such.webp");
        await store.DeletePrefixAsync("no/");
        Assert.Empty(await store.ListAsync("no/").ToListAsync());
    }

    [Fact]
    public async Task ListAsync_ReturnsTheKeysUnderAPrefix_ButNotInProgressWrites()
    {
        var store = Store();
        await store.WriteAsync("ab/abc1.png", Bytes(1));
        await store.WriteAsync("ab/abd2.jpg", Bytes(1, 2));
        await store.WriteAsync("cd/cde3.png", Bytes(1));
        await File.WriteAllBytesAsync(Path.Combine(root, "ab", ".tmp-0123"), [1]);

        Assert.Equal(["ab/abc1.png"], (await store.ListAsync("ab/abc1.").ToListAsync()).Select(o => o.Key));
        Assert.Equal(["ab/abc1.png", "ab/abd2.jpg"], (await store.ListAsync("ab/").ToListAsync()).Select(o => o.Key).Order());
        var all = await store.ListAsync("").ToListAsync();
        Assert.Equal(["ab/abc1.png", "ab/abd2.jpg", "cd/cde3.png"], all.Select(o => o.Key).Order());
        Assert.Equal(2, all.Single(o => o.Key == "ab/abd2.jpg").Length);
    }

    [Fact]
    public async Task DeletePrefixAsync_DeletesEverythingUnderIt_AndPrunesEmptyFolders()
    {
        var store = Store();
        await store.WriteAsync("ABC-123/1111/0.webp", Bytes(1));
        await store.WriteAsync("ABC-123/1111/1.webp", Bytes(1));
        await store.WriteAsync("ABC-123/2222/0.webp", Bytes(1));
        await store.WriteAsync("XYZ-9/3333/0.webp", Bytes(1));

        await store.DeletePrefixAsync("ABC-123/1111/");
        Assert.False(Directory.Exists(Path.Combine(root, "ABC-123", "1111")));
        Assert.True(Directory.Exists(Path.Combine(root, "ABC-123")));

        await store.DeletePrefixAsync("ABC-123/2222/");
        Assert.False(Directory.Exists(Path.Combine(root, "ABC-123")));
        Assert.Equal(["XYZ-9/3333/0.webp"], (await store.ListAsync("").ToListAsync()).Select(o => o.Key));
        Assert.True(Directory.Exists(root));
    }

    [Fact]
    public async Task DeleteAsync_PrunesTheEmptyFolder()
    {
        var store = Store();
        await store.WriteAsync("ab/abc1.png", Bytes(1));

        await store.DeleteAsync("ab/abc1.png");

        Assert.False(Directory.Exists(Path.Combine(root, "ab")));
        Assert.True(Directory.Exists(root));
    }

    [Theory]
    [InlineData("../escape.webp")]
    [InlineData("a/../../escape.webp")]
    [InlineData("/rooted.webp")]
    [InlineData("a\\b.webp")]
    [InlineData("a//b.webp")]
    [InlineData("a/.tmp-0123")]
    public async Task WriteAsync_RefusesKeysThatCouldEscapeTheRootOrHideAsATempFile(string key) =>
        await Assert.ThrowsAsync<ArgumentException>(() => Store().WriteAsync(key, Bytes(1)));

    private sealed class FailingStream : MemoryStream
    {
        public FailingStream() : base([1, 2, 3])
        {
        }

        public override Task CopyToAsync(Stream destination, int bufferSize, CancellationToken cancellationToken) =>
            throw new IOException("Read failed.");
    }
}
