using System.Collections.Concurrent;
using System.Runtime.CompilerServices;

namespace Javbuddy.Services.Infrastructure;

/// <summary>One area of durable storage: a key prefix inside the one object store
/// (<c>actor-images/</c>, <c>trickplay/</c>). The disposable image cache isn't one of these —
/// it can be recreated at any time and stays a plain local folder.</summary>
public sealed record ObjectStoreArea(string Prefix)
{
    public static readonly ObjectStoreArea ActorImages = new("actor-images");

    public static readonly ObjectStoreArea Trickplay = new("trickplay");

    public static IReadOnlyList<ObjectStoreArea> All { get; } = [ActorImages, Trickplay];
}

/// <summary>Durable object storage addressed by string keys (<c>/</c>-separated, like S3), working
/// with streams rather than paths, so a backend without a local filesystem can implement it.
/// Each write replaces the whole object atomically: a reader sees the old object or the new one,
/// never a partial write.</summary>
public interface IObjectStore
{
    Task WriteAsync(string key, Stream content, CancellationToken ct = default);

    /// <summary>Null when the key doesn't exist. The caller disposes the result.</summary>
    Task<StoredObject?> OpenReadAsync(string key, CancellationToken ct = default);

    Task<bool> ExistsAsync(string key, CancellationToken ct = default);

    /// <summary>A missing key is not an error.</summary>
    Task DeleteAsync(string key, CancellationToken ct = default);

    /// <summary>Deletes every object whose key starts with prefix.</summary>
    Task DeletePrefixAsync(string prefix, CancellationToken ct = default);

    /// <summary>Every object whose key starts with prefix ("" for all), in no particular order.</summary>
    IAsyncEnumerable<StoredObjectInfo> ListAsync(string prefix, CancellationToken ct = default);
}

/// <summary>The one object store and its per-area views.</summary>
public interface IObjectStoreProvider
{
    /// <summary>The local folder holding the whole store, for disk-space reporting only; null when
    /// the backend has no local disk (disk space is then not applicable).</summary>
    string? LocalRoot { get; }

    /// <summary>The area's view of the store: its keys are relative to the area's prefix.</summary>
    IObjectStore Get(ObjectStoreArea area);
}

/// <summary>Hands out prefixed views of one backend store, whichever backend it is.</summary>
public sealed class ObjectStoreProvider(IObjectStore root, string? localRoot) : IObjectStoreProvider
{
    private readonly ConcurrentDictionary<ObjectStoreArea, IObjectStore> views = new();

    public string? LocalRoot { get; } = localRoot;

    public IObjectStore Get(ObjectStoreArea area) => views.GetOrAdd(area, a => new PrefixedObjectStore(root, a.Prefix + "/"));
}

/// <summary>A view of another store under a key prefix, the way one S3 bucket holds several areas.</summary>
public sealed class PrefixedObjectStore(IObjectStore inner, string prefix) : IObjectStore
{
    public Task WriteAsync(string key, Stream content, CancellationToken ct = default) => inner.WriteAsync(prefix + key, content, ct);

    public async Task<StoredObject?> OpenReadAsync(string key, CancellationToken ct = default)
    {
        var stored = await inner.OpenReadAsync(prefix + key, ct);
        return stored is null ? null : new StoredObject(key, stored.Content, stored.Length, stored.LastModified, stored.ETag);
    }

    public Task<bool> ExistsAsync(string key, CancellationToken ct = default) => inner.ExistsAsync(prefix + key, ct);

    public Task DeleteAsync(string key, CancellationToken ct = default) => inner.DeleteAsync(prefix + key, ct);

    public Task DeletePrefixAsync(string keyPrefix, CancellationToken ct = default) => inner.DeletePrefixAsync(prefix + keyPrefix, ct);

    public async IAsyncEnumerable<StoredObjectInfo> ListAsync(string keyPrefix, [EnumeratorCancellation] CancellationToken ct = default)
    {
        await foreach (var item in inner.ListAsync(prefix + keyPrefix, ct))
        {
            yield return item with { Key = item.Key[prefix.Length..] };
        }
    }
}

public sealed record StoredObjectInfo(string Key, long Length, DateTimeOffset LastModified);

/// <summary>An object opened for reading. <see cref="ETag"/> is quoted, ready for an HTTP header.</summary>
public sealed class StoredObject(string key, Stream content, long length, DateTimeOffset lastModified, string eTag) : IAsyncDisposable, IDisposable
{
    public string Key { get; } = key;

    public Stream Content { get; } = content;

    public long Length { get; } = length;

    public DateTimeOffset LastModified { get; } = lastModified;

    public string ETag { get; } = eTag;

    public void Dispose() => Content.Dispose();

    public ValueTask DisposeAsync() => Content.DisposeAsync();
}
