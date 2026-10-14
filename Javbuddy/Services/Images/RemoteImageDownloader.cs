using System.Net;
using Javbuddy.Services.Infrastructure;

namespace Javbuddy.Services.Images;

public enum RemoteImageDownloadFailure
{
    None,
    MissingUrl,
    DefunctHost,
    InvalidUrl,
    Timeout,
    SendFailed,
    HttpStatus,
    DeclaredTooLarge,
    UnacceptableMediaType,
    TooLarge,
    ReadFailed,
    Empty,
    /// <summary>The URL (or a redirect) resolved only to a local/private address — see
    /// <see cref="PublicAddressGuard"/>.</summary>
    BlockedAddress
}

public sealed record RemoteImageDownloadResult(
    RemoteImageDownloadFailure Failure,
    byte[]? Bytes = null,
    string? MediaType = null,
    HttpStatusCode? StatusCode = null,
    string? ReasonPhrase = null,
    long? DeclaredLength = null,
    Exception? Error = null)
{
    public bool Success => Failure == RemoteImageDownloadFailure.None;
}

/// <summary>Downloads a user-supplied remote image URL with a byte cap — the transport shared by
/// the actor portrait and movie cover "from URL" flows: URL and defunct-host checks, a timeout
/// linked to the caller's token, declared and streamed size limits, and empty-body detection.
/// Callers keep their own image validation and error wording. Caller cancellation propagates;
/// any other cancellation (the timeout, or HttpClient's own) is reported as
/// <see cref="RemoteImageDownloadFailure.Timeout"/>.</summary>
public static class RemoteImageDownloader
{
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);

    /// <summary>True for an image content type, or a missing / generic binary one, which image CDNs do send.</summary>
    public static bool IsImageMediaType(string? mediaType) =>
        mediaType is null
        || mediaType.StartsWith("image/", StringComparison.Ordinal)
        || mediaType is "application/octet-stream" or "binary/octet-stream";

    /// <summary>Downloads an image a metadata provider handed us (<paramref name="url"/> is not user-typed, so this takes the
    /// plain client rather than the public-address-only import one) with the size cap, timeout and media-type check.</summary>
    public static Task<RemoteImageDownloadResult> DownloadProviderImageAsync(HttpClient client, string? url, CancellationToken ct)
    {
        var failure = TryParseUrl(url, out var uri);
        return failure != RemoteImageDownloadFailure.None
            ? Task.FromResult(new RemoteImageDownloadResult(failure))
            : DownloadAsync(client, uri!, BoundedHttpContent.MaxImageBytes, ct, isAcceptableMediaType: IsImageMediaType);
    }

    /// <summary>Validates a user-supplied image URL: present, not on a defunct host, and an
    /// absolute HTTP(S) URL. Returns <see cref="RemoteImageDownloadFailure.None"/> with the parsed
    /// <paramref name="uri"/> when valid.</summary>
    public static RemoteImageDownloadFailure TryParseUrl(string? url, out Uri? uri)
    {
        uri = null;
        if (string.IsNullOrWhiteSpace(url))
        {
            return RemoteImageDownloadFailure.MissingUrl;
        }

        var trimmed = url.Trim();
        if (ActorImageUrlHelper.IsDefunctUrl(trimmed))
        {
            return RemoteImageDownloadFailure.DefunctHost;
        }

        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            uri = null;
            return RemoteImageDownloadFailure.InvalidUrl;
        }

        return RemoteImageDownloadFailure.None;
    }

    /// <summary>Name of the IHttpClientFactory client for user-supplied image URLs; its handler
    /// refuses local and private addresses (<see cref="PublicAddressGuard"/>).</summary>
    public const string ImportClientName = "UserImageImport";

    private static bool IsBlockedAddress(Exception? ex)
    {
        for (; ex is not null; ex = ex.InnerException)
        {
            if (ex is BlockedAddressException) return true;
        }
        return false;
    }

    public static async Task<RemoteImageDownloadResult> DownloadAsync(
        HttpClient client,
        Uri uri,
        long maxBytes,
        CancellationToken ct,
        Action<HttpRequestMessage>? configureRequest = null,
        Func<string?, bool>? isAcceptableMediaType = null,
        TimeSpan? timeout = null)
    {
        using var timeoutCts = new CancellationTokenSource(timeout ?? DefaultTimeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);
        var requestCt = linkedCts.Token;

        HttpResponseMessage response;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            configureRequest?.Invoke(request);
            response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, requestCt);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return new(RemoteImageDownloadFailure.Timeout);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new(IsBlockedAddress(ex) ? RemoteImageDownloadFailure.BlockedAddress : RemoteImageDownloadFailure.SendFailed, Error: ex);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                return new(RemoteImageDownloadFailure.HttpStatus, StatusCode: response.StatusCode, ReasonPhrase: response.ReasonPhrase);
            }

            var declaredLength = response.Content.Headers.ContentLength;
            if (declaredLength > maxBytes)
            {
                return new(RemoteImageDownloadFailure.DeclaredTooLarge, DeclaredLength: declaredLength);
            }

            var mediaType = response.Content.Headers.ContentType?.MediaType?.ToLowerInvariant();
            if (isAcceptableMediaType is not null && !isAcceptableMediaType(mediaType))
            {
                return new(RemoteImageDownloadFailure.UnacceptableMediaType, MediaType: mediaType);
            }

            byte[] bytes;
            try
            {
                await using var stream = await response.Content.ReadAsStreamAsync(requestCt);
                var read = await BoundedHttpContent.ReadStreamAsync(stream, maxBytes, requestCt);
                if (read is null) return new(RemoteImageDownloadFailure.TooLarge);
                bytes = read;
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                return new(RemoteImageDownloadFailure.Timeout);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                return new(RemoteImageDownloadFailure.ReadFailed, Error: ex);
            }

            if (bytes.Length == 0)
            {
                return new(RemoteImageDownloadFailure.Empty, MediaType: mediaType);
            }

            return new(RemoteImageDownloadFailure.None, bytes, mediaType);
        }
    }
}
