using System.Runtime.CompilerServices;
using Javbuddy.Services.Infrastructure;

namespace Javbuddy.Tests.TestSupport;

/// <summary>An in-memory <see cref="IObjectStore"/> for testing the domain wrappers over the object
/// store without a filesystem. LastModified can be set per key to test age-based
/// cleanup; ListCount counts listings, to test that a lookup by exact key doesn't list.</summary>
public sealed class InMemoryObjectStore : IObjectStore
{
    private readonly Dictionary<string, (byte[] Bytes, DateTimeOffset LastModified)> objects = new(StringComparer.Ordinal);

    public IReadOnlyCollection<string> Keys => objects.Keys;

    public byte[] this[string key] => objects[key].Bytes;

    public int ListCount { get; private set; }

    public void SetLastModified(string key, DateTimeOffset lastModified) => objects[key] = (objects[key].Bytes, lastModified);

    public async Task WriteAsync(string key, Stream content, CancellationToken ct = default)
    {
        using var memory = new MemoryStream();
        await content.CopyToAsync(memory, ct);
        objects[key] = (memory.ToArray(), DateTimeOffset.UtcNow);
    }

    public Task<StoredObject?> OpenReadAsync(string key, CancellationToken ct = default) =>
        Task.FromResult(objects.TryGetValue(key, out var item)
            ? new StoredObject(key, new MemoryStream(item.Bytes, writable: false), item.Bytes.Length, item.LastModified, $"\"{item.Bytes.Length:x}\"")
            : null);

    public Task<bool> ExistsAsync(string key, CancellationToken ct = default) => Task.FromResult(objects.ContainsKey(key));

    public Task DeleteAsync(string key, CancellationToken ct = default)
    {
        objects.Remove(key);
        return Task.CompletedTask;
    }

    public Task DeletePrefixAsync(string prefix, CancellationToken ct = default)
    {
        foreach (var key in objects.Keys.Where(k => k.StartsWith(prefix, StringComparison.Ordinal)).ToList())
        {
            objects.Remove(key);
        }
        return Task.CompletedTask;
    }

    public async IAsyncEnumerable<StoredObjectInfo> ListAsync(string prefix, [EnumeratorCancellation] CancellationToken ct = default)
    {
        ListCount++;
        foreach (var (key, item) in objects.Where(o => o.Key.StartsWith(prefix, StringComparison.Ordinal)).ToList())
        {
            yield return new StoredObjectInfo(key, item.Bytes.Length, item.LastModified);
        }
        await Task.CompletedTask;
    }
}

/// <summary>Hands out one <see cref="InMemoryObjectStore"/> per area, standing in for the area's
/// prefixed view, so a test sees the area's keys without the prefix. No local disk.</summary>
public sealed class InMemoryObjectStoreProvider : IObjectStoreProvider
{
    public string? LocalRoot => null;

    private readonly Dictionary<ObjectStoreArea, InMemoryObjectStore> stores = [];

    public InMemoryObjectStore Get(ObjectStoreArea area)
    {
        if (!stores.TryGetValue(area, out var store)) stores[area] = store = new InMemoryObjectStore();
        return store;
    }

    IObjectStore IObjectStoreProvider.Get(ObjectStoreArea area) => Get(area);
}
