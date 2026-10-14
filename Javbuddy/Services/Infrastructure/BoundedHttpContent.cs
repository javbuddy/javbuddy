namespace Javbuddy.Services.Infrastructure;

/// <summary>Reads a response body into memory with a size cap, so a server (or a wrong URL) can't make a caller buffer
/// an unbounded download.</summary>
public static class BoundedHttpContent
{
    /// <summary>Cap for an image fetched from a provider: the same 20 MB as the upload limit, far above any real cover.</summary>
    public const long MaxImageBytes = 20L * 1024 * 1024;

    /// <summary>Reads <paramref name="stream"/> to the end, or returns null as soon as it holds more than
    /// <paramref name="maxBytes"/>.</summary>
    public static async Task<byte[]?> ReadStreamAsync(Stream stream, long maxBytes, CancellationToken ct)
    {
        using var ms = new MemoryStream();
        var buffer = new byte[81920];
        int read;
        long totalRead = 0;
        while ((read = await stream.ReadAsync(buffer, ct)) > 0)
        {
            totalRead += read;
            if (totalRead > maxBytes) return null;
            await ms.WriteAsync(buffer.AsMemory(0, read), ct);
        }

        return ms.ToArray();
    }

    /// <summary>Reads a response body for the callers that only need the bytes and already handle
    /// <see cref="HttpRequestException"/>: throws one when the declared or streamed length passes
    /// <paramref name="maxBytes"/>.</summary>
    public static async Task<byte[]> ReadBytesAsync(HttpContent content, long maxBytes, CancellationToken ct)
    {
        if (content.Headers.ContentLength > maxBytes)
        {
            throw new HttpRequestException($"The response is {content.Headers.ContentLength} bytes, over the {maxBytes}-byte limit.");
        }

        await using var stream = await content.ReadAsStreamAsync(ct);
        return await ReadStreamAsync(stream, maxBytes, ct)
            ?? throw new HttpRequestException($"The response is over the {maxBytes}-byte limit.");
    }
}
