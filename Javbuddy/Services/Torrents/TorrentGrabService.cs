using Javbuddy.Data;
using Javbuddy.Models;
using Javbuddy.Services.Metrics;
using Javbuddy.Services.Prowlarr;
using Javbuddy.Services.QBittorrent;
using Microsoft.EntityFrameworkCore;

namespace Javbuddy.Services.Torrents;

public record TorrentGrabResult(bool Success, string? ErrorMessage);

public interface ITorrentGrabService
{
    Task<TorrentGrabResult> GrabAsync(Movie movie, ReleaseResourceDto release, CancellationToken ct = default);
}

/// <summary>Sends a Prowlarr search result to qBittorrent and records it as a TorrentDownload
/// row (Status = Queued, Hash unset) so it shows up in Activity &gt; Queue right away —
/// QBittorrentSyncTask fills in the Hash/live progress on its next run.
///
/// A release's "MagnetUrl"/"DownloadUrl" fields are not trustworthy by name alone — some
/// indexers (nyaa/sukebei-based ones via Prowlarr, notably) put an http(s) proxy link in
/// *both* fields rather than a literal `magnet:` URI, because they don't host a .torrent file
/// and Prowlarr itself has to resolve the magnet server-side. Handing an http(s) URL straight
/// to qBittorrent's `urls` param asks qBittorrent to fetch it itself — which fails silently
/// (qBittorrent still reports "Ok.") whenever that URL is only reachable from this app's own
/// network position (e.g. a Prowlarr instance bound to 127.0.0.1) and not from wherever
/// qBittorrent runs. So only a URL that already starts with "magnet:" is passed straight
/// through; anything else is fetched by this app first, which either turns out to *redirect* to
/// a magnet URI (handled the same way) or serves an actual .torrent file, whose raw bytes are
/// then uploaded to qBittorrent directly instead of asking it to fetch the URL.</summary>
public class TorrentGrabService(
    IQBittorrentClient qBittorrentClient,
    IHttpClientFactory httpClientFactory,
    IDbContextFactory<AppDbContext> dbFactory,
    TorrentChangeNotifier changeNotifier,
    JavbuddyMetrics metrics) : ITorrentGrabService
{
    private const string NoRedirectClientName = "NoRedirect";
    private const int MaxRedirects = 5;
    private static readonly TimeSpan DownloadTimeout = TimeSpan.FromSeconds(30);

    private readonly IQBittorrentClient qBittorrentClient = qBittorrentClient;
    private readonly IHttpClientFactory httpClientFactory = httpClientFactory;
    private readonly IDbContextFactory<AppDbContext> dbFactory = dbFactory;
    private readonly TorrentChangeNotifier changeNotifier = changeNotifier;
    private readonly JavbuddyMetrics metrics = metrics;

    public async Task<TorrentGrabResult> GrabAsync(Movie movie, ReleaseResourceDto release, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(movie.Code))
        {
            return RecordOutcome(new TorrentGrabResult(false, "This movie has no code to tag the download with."));
        }

        var url = !string.IsNullOrWhiteSpace(release.MagnetUrl) ? release.MagnetUrl : release.DownloadUrl;
        if (string.IsNullOrWhiteSpace(url))
        {
            return RecordOutcome(new TorrentGrabResult(false, "This release has no magnet or download URL."));
        }

        QBittorrentAddResult addResult;

        if (url.StartsWith("magnet:", StringComparison.OrdinalIgnoreCase))
        {
            addResult = await qBittorrentClient.AddMagnetAsync(movie.Code, url, ct);
        }
        else
        {
            var resolved = await ResolveDownloadUrlAsync(url, release.Title, ct);
            if (!resolved.Success)
            {
                return RecordOutcome(new TorrentGrabResult(false, resolved.ErrorMessage));
            }

            addResult = resolved.MagnetUrl is not null
                ? await qBittorrentClient.AddMagnetAsync(movie.Code, resolved.MagnetUrl, ct)
                : await qBittorrentClient.AddTorrentFileAsync(movie.Code, resolved.FileName!, resolved.Bytes!, ct);
        }

        if (!addResult.Success)
        {
            return RecordOutcome(new TorrentGrabResult(false, addResult.ErrorMessage ?? "qBittorrent rejected the torrent."));
        }

        await using (var db = await dbFactory.CreateDbContextAsync(ct))
        {
            db.TorrentDownloads.Add(new TorrentDownload
            {
                MovieId = movie.Id,
                MovieCode = movie.Code,
                ReleaseTitle = release.Title,
                Indexer = release.Indexer,
                Size = release.Size,
                SourceUrl = url,
                Status = TorrentDownloadStatus.Queued
            });
            await db.SaveChangesAsync(ct);
        }

        changeNotifier.NotifyChanged();
        return RecordOutcome(new TorrentGrabResult(true, null));
    }

    private TorrentGrabResult RecordOutcome(TorrentGrabResult result)
    {
        metrics.TorrentGrabs.Add(1, new KeyValuePair<string, object?>("outcome", result.Success ? "success" : "failure"));
        return result;
    }

    /// <summary>Fetches a release's download URL, following redirects itself. Some indexers
    /// (nyaa/sukebei-based ones via Prowlarr, notably) don't host a .torrent file at all — their
    /// "download" link just 301s straight to a magnet URI. The default HttpClient can't be used
    /// for this: given AllowAutoRedirect (its default), it tries to auto-follow *any* redirect,
    /// including to a non-http(s) target, and throws deep inside SocketsHttpHandler when it
    /// can't parse "magnet:...” as a host rather than just returning the 3xx response — so this
    /// uses a dedicated named client with AllowAutoRedirect disabled and follows normal http(s)
    /// hops manually, stopping as soon as a hop redirects to a magnet: URI.</summary>
    private async Task<(bool Success, string? ErrorMessage, string? MagnetUrl, string? FileName, byte[]? Bytes)> ResolveDownloadUrlAsync(string url, string? releaseTitle, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(DownloadTimeout);

        try
        {
            var client = httpClientFactory.CreateClient(NoRedirectClientName);
            var currentUrl = url;

            for (var hop = 0; hop < MaxRedirects; hop++)
            {
                using var response = await client.GetAsync(currentUrl, cts.Token);

                if ((int)response.StatusCode is >= 300 and < 400 && response.Headers.Location is { } location)
                {
                    var resolved = location.IsAbsoluteUri ? location : new Uri(new Uri(currentUrl), location);
                    if (resolved.Scheme.Equals("magnet", StringComparison.OrdinalIgnoreCase))
                    {
                        return (true, null, resolved.OriginalString, null, null);
                    }
                    currentUrl = resolved.AbsoluteUri;
                    continue;
                }

                if (!response.IsSuccessStatusCode)
                {
                    return (false, $"Could not download the .torrent file — indexer returned {(int)response.StatusCode}.", null, null, null);
                }

                var bytes = await response.Content.ReadAsByteArrayAsync(cts.Token);
                var fileName = response.Content.Headers.ContentDisposition?.FileNameStar
                    ?? response.Content.Headers.ContentDisposition?.FileName?.Trim('"')
                    ?? SanitizeFileName(releaseTitle) + ".torrent";

                return (true, null, null, fileName, bytes);
            }

            return (false, "Too many redirects while resolving the download URL.", null, null, null);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return (false, "Timed out downloading the .torrent file from the indexer.", null, null, null);
        }
        catch (HttpRequestException ex)
        {
            return (false, $"Could not download the .torrent file: {ex.Message}", null, null, null);
        }
        catch (UriFormatException ex)
        {
            return (false, $"Indexer returned a malformed redirect: {ex.Message}", null, null, null);
        }
    }

    private static string SanitizeFileName(string? title)
    {
        var name = string.IsNullOrWhiteSpace(title) ? "release" : title;
        foreach (var c in Path.GetInvalidFileNameChars())
        {
            name = name.Replace(c, '_');
        }
        return name.Length > 100 ? name[..100] : name;
    }
}
