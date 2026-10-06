using System.Runtime.CompilerServices;
using Javbuddy.Services.Infrastructure;
using SkiaSharp;

namespace Javbuddy.Services.Images;

/// <summary>Stores canonical actor-image bytes in the durable object store under
/// <c>actor-images/&lt;2 hex&gt;/&lt;guid&gt;.&lt;ext&gt;</c>, separately from the disposable
/// WebP cache. Staging for in-flight
/// uploads always lives under the local OS temp directory, never the store, so staged writes don't
/// hit a network-mounted volume; committing writes the staged bytes to the store.
/// Writing and staging return the extension the bytes are kept under; callers record it next to the
/// storage id and pass it back so each access is a single request against the exact key
///. A null extension (rows from before it was recorded) falls back to listing
/// the key's prefix.</summary>
public interface IActorImageDataStore
{
    /// <summary>Returns the extension the bytes were stored under, e.g. ".jpg".</summary>
    Task<string> WriteAsync(Guid storageId, ReadOnlyMemory<byte> bytes, CancellationToken ct = default);

    /// <summary>Returns the extension the bytes will be stored under on commit, e.g. ".jpg".</summary>
    Task<string> StageAsync(Guid storageId, ReadOnlyMemory<byte> bytes, CancellationToken ct = default);

    Task CommitAsync(Guid storageId, CancellationToken ct = default);

    void DeleteStaged(Guid storageId);

    void PurgeStaging();

    Task<byte[]?> ReadAsync(Guid storageId, string? extension, CancellationToken ct = default);

    /// <summary>Null when there are no stored bytes for storageId. The caller disposes the result.</summary>
    Task<StoredObject?> OpenReadAsync(Guid storageId, string? extension, CancellationToken ct = default);

    Task<bool> ExistsAsync(Guid storageId, string? extension, CancellationToken ct = default);

    Task DeleteAsync(Guid storageId, string? extension, CancellationToken ct = default);

    /// <summary>Every stored original, in no particular order, for finding ones no row references
    ///. Keys that aren't a storage id are skipped.</summary>
    IAsyncEnumerable<StoredActorImageSource> ListAsync(CancellationToken ct = default);
}

public sealed record StoredActorImageSource(Guid StorageId, string Extension, DateTimeOffset LastModified);

public sealed class ActorImageDataStore : IActorImageDataStore
{
    private readonly IObjectStore store;
    private string TempPath { get; }

    /// <summary>stagingPath defaults to a fixed folder under the OS temp directory; tests pass their
    /// own, since the constructor purges it.</summary>
    public ActorImageDataStore(IObjectStoreProvider stores, string? stagingPath = null)
    {
        store = stores.Get(ObjectStoreArea.ActorImages);
        TempPath = stagingPath ?? Path.Combine(Path.GetTempPath(), "javbuddy-actor-image-staging");
        PurgeStaging();
    }

    public async Task<string> StageAsync(Guid storageId, ReadOnlyMemory<byte> bytes, CancellationToken ct = default)
    {
        var ext = GetExtension(bytes.Span);
        var name = storageId.ToString("N");
        var tempFile = Path.Combine(TempPath, name + ext);
        Directory.CreateDirectory(TempPath);
        await File.WriteAllBytesAsync(tempFile, bytes, ct);
        return ext;
    }

    public async Task CommitAsync(Guid storageId, CancellationToken ct = default)
    {
        var staged = FindStaged(storageId);
        if (staged is null) return;

        await using (var stream = File.OpenRead(staged))
        {
            await store.WriteAsync(Key(storageId, Path.GetExtension(staged)), stream, ct);
        }
        DeleteStaged(storageId);
    }

    public void DeleteStaged(Guid storageId)
    {
        try
        {
            var staged = FindStaged(storageId);
            if (staged is not null)
            {
                File.Delete(staged);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    public void PurgeStaging()
    {
        try
        {
            if (Directory.Exists(TempPath))
            {
                Directory.Delete(TempPath, recursive: true);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    public async Task<string> WriteAsync(Guid storageId, ReadOnlyMemory<byte> bytes, CancellationToken ct = default)
    {
        var ext = GetExtension(bytes.Span);
        using var stream = new MemoryStream(bytes.ToArray(), writable: false);
        await store.WriteAsync(Key(storageId, ext), stream, ct);
        return ext;
    }

    public async Task<byte[]?> ReadAsync(Guid storageId, string? extension, CancellationToken ct = default)
    {
        await using var stored = await OpenReadAsync(storageId, extension, ct);
        if (stored is null) return null;

        using var memory = new MemoryStream();
        await stored.Content.CopyToAsync(memory, ct);
        return memory.ToArray();
    }

    public async Task<StoredObject?> OpenReadAsync(Guid storageId, string? extension, CancellationToken ct = default)
    {
        if (extension is not null) return await store.OpenReadAsync(Key(storageId, extension), ct);
        return await FindKeyAsync(storageId, ct) is { } key ? await store.OpenReadAsync(key, ct) : null;
    }

    public async Task<bool> ExistsAsync(Guid storageId, string? extension, CancellationToken ct = default) =>
        extension is not null
            ? await store.ExistsAsync(Key(storageId, extension), ct)
            : await FindKeyAsync(storageId, ct) is not null;

    public async Task DeleteAsync(Guid storageId, string? extension, CancellationToken ct = default)
    {
        try
        {
            if (extension is not null) await store.DeleteAsync(Key(storageId, extension), ct);
            else if (await FindKeyAsync(storageId, ct) is { } key) await store.DeleteAsync(key, ct);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    public async IAsyncEnumerable<StoredActorImageSource> ListAsync([EnumeratorCancellation] CancellationToken ct = default)
    {
        await foreach (var item in store.ListAsync("", ct))
        {
            var name = item.Key[(item.Key.LastIndexOf('/') + 1)..];
            if (name.Length >= 32 && Guid.TryParseExact(name[..32], "N", out var storageId))
            {
                yield return new StoredActorImageSource(storageId, name[32..], item.LastModified);
            }
        }
    }

    private static string Key(Guid storageId, string extension)
    {
        var name = storageId.ToString("N");
        return $"{name[..2]}/{name}{extension}";
    }

    /// <summary>The stored key, whichever extension the bytes were saved with, for rows that
    /// don't record the extension.</summary>
    private async Task<string?> FindKeyAsync(Guid storageId, CancellationToken ct)
    {
        await foreach (var item in store.ListAsync(Key(storageId, "."), ct))
        {
            return item.Key;
        }
        return null;
    }

    private string? FindStaged(Guid storageId) =>
        Directory.Exists(TempPath)
            ? Directory.EnumerateFiles(TempPath, storageId.ToString("N") + ".*", SearchOption.TopDirectoryOnly).FirstOrDefault()
            : null;

    private static string GetExtension(ReadOnlySpan<byte> bytes)
    {
        using var stream = new MemoryStream(bytes.ToArray());
        using var codec = SKCodec.Create(stream);
        if (codec is null)
        {
            throw new ArgumentException("Actor image could not be decoded.", nameof(bytes));
        }

        return codec.EncodedFormat switch
        {
            SKEncodedImageFormat.Jpeg => ".jpg",
            SKEncodedImageFormat.Png => ".png",
            SKEncodedImageFormat.Webp => ".webp",
            _ => throw new ArgumentException("Actor image must be JPEG, PNG, or WebP.", nameof(bytes)),
        };
    }
}
