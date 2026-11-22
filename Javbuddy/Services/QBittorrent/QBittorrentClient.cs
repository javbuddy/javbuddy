using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using Javbuddy.Data;
using Javbuddy.Models;
using Javbuddy.Services.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Javbuddy.Services.QBittorrent;

public record QBittorrentTestResult(bool Success, string Message, string? Version = null);
public record QBittorrentAddResult(bool Success, string? ErrorMessage);

/// <summary>Outcome of listing torrents. <see cref="Success"/> false means qBittorrent couldn't be
/// queried (not configured, login rejected, non-2xx, timeout/unreachable) — distinct from a
/// successful query that returned no torrents, so callers must not treat it as "nothing exists".</summary>
public record QBittorrentTorrentsResult(bool Success, List<QBittorrentTorrentDto> Torrents, string? ErrorMessage = null)
{
    public static QBittorrentTorrentsResult Failure(string message) => new(false, [], message);
}

public interface IQBittorrentClient
{
    Task<string?> GetBaseUrlAsync(CancellationToken ct = default);
    Task<string?> GetExternalUrlAsync(CancellationToken ct = default);
    Task<QBittorrentTestResult> TestConnectionAsync(CancellationToken ct = default);

    /// <summary>For magnet links only — qBittorrent resolves these itself via DHT/trackers, so
    /// handing it the URL is safe regardless of qBittorrent's network position.</summary>
    Task<QBittorrentAddResult> AddMagnetAsync(string movieCode, string magnetUrl, CancellationToken ct = default);

    /// <summary>For .torrent files fetched from an indexer/Prowlarr download URL: the caller
    /// must download the file itself (from wherever it can reach that URL) and upload the raw
    /// bytes here, rather than passing the URL through for qBittorrent to fetch — that URL is
    /// often only reachable from this app's network position, not qBittorrent's (e.g. a
    /// Prowlarr instance bound to 127.0.0.1 when qBittorrent runs on a different host).</summary>
    Task<QBittorrentAddResult> AddTorrentFileAsync(string movieCode, string fileName, byte[] torrentFileBytes, CancellationToken ct = default);

    Task<QBittorrentTorrentsResult> GetTorrentsAsync(CancellationToken ct = default);
    Task<bool> DeleteTorrentAsync(string hash, bool deleteFiles = false, CancellationToken ct = default);
}

/// <summary>Reads qBittorrent connection settings from environment variables / appsettings
/// ("QBittorrent:BaseUrl", "QBittorrent:ExternalUrl", "QBittorrent:Username", "QBittorrent:Password" —
/// standard .NET env form "QBittorrent__BaseUrl", "QBittorrent__ExternalUrl" etc.), which take precedence over the Settings page when set.</summary>
public static class QBittorrentEnvConfig
{
    public static string? GetBaseUrl(IConfiguration configuration) => configuration["QBittorrent:BaseUrl"];
    public static string? GetExternalUrl(IConfiguration configuration) => configuration["QBittorrent:ExternalUrl"];
    public static string? GetUsername(IConfiguration configuration) => configuration["QBittorrent:Username"];
    public static string? GetPassword(IConfiguration configuration) => configuration["QBittorrent:Password"];

    /// <summary>Optional — unlike BaseUrl/Username/Password, a category isn't required for the
    /// connection to work, and can be set here even when the rest of the settings row comes
    /// from the Settings page (or vice versa).</summary>
    public static string? GetCategory(IConfiguration configuration) => configuration["QBittorrent:Category"];

    public static bool IsSet(IConfiguration configuration) =>
        EnvConfig.AllSet(GetBaseUrl(configuration), GetUsername(configuration), GetPassword(configuration));
}

/// <summary>Talks to qBittorrent's WebUI API v2. Unlike Prowlarr/Jellyfin this isn't API-key
/// auth — qBittorrent uses a cookie-based session (POST /api/v2/auth/login returns a SID
/// cookie), so every operation here logs in fresh and manually attaches the cookie to its one
/// follow-up request, matching the rest of the codebase's stateless-per-call client style. Uses
/// the "QBittorrent" named HttpClient (UseCookies = false — see Program.cs) so the framework's
/// own automatic cookie jar never interferes with this manual cookie handling; a pooled handler
/// silently resending an old SID on a later login made qBittorrent skip issuing a new one,
/// which read as a rejected login.
/// Newly added torrents aren't returned with a hash, so grabs are correlated to a live torrent
/// later by tagging them with the movie's code (see QBittorrentSyncTask). If a Category is
/// configured, every torrent added here also gets tagged with it and torrents/info calls are
/// scoped to it, so this app's downloads don't mix with unrelated torrents in the same
/// qBittorrent instance.
/// GetSettingsAsync stays bespoke (not EffectiveSettingsResolver) because Category is independently
/// overridable regardless of whether the connection fields (BaseUrl/Username/Password) come
/// from env or the DB row — a shape EffectiveSettingsResolver's single env-or-DB switch doesn't fit.</summary>
public class QBittorrentClient(IHttpClientFactory httpClientFactory, IDbContextFactory<AppDbContext> dbFactory, IConfiguration configuration, QBittorrentSessionCache? sessionCache = null) : ApiClientBase<QBittorrentSettings>(httpClientFactory, dbFactory, configuration), IQBittorrentClient
{
    private readonly QBittorrentSessionCache sessions = sessionCache ?? new();

    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(30);

    protected override string ServiceName => "qBittorrent";
    protected override string HttpClientName => "QBittorrent";

    public async Task<QBittorrentTestResult> TestConnectionAsync(CancellationToken ct = default)
    {
        var settings = await GetSettingsAsync(ct);
        if (settings is null)
        {
            return new QBittorrentTestResult(false, "Set a base URL, username and password first.");
        }

        return await ExecuteAsync(TimeSpan.FromSeconds(15), ct,
            async (client, token) =>
            {
                // An explicit test always tries a real login, so it also lifts a "login rejected" block.
                sessions.Forget(settings);
                using var response = await SendAuthenticatedAsync(client, settings, sid =>
                    BuildRequest(HttpMethod.Get, settings, "/api/v2/app/version", sid), token);
                if (response is null)
                {
                    return new QBittorrentTestResult(false, "Reached the server, but the login was rejected.");
                }

                if (response.IsSuccessStatusCode)
                {
                    string? version = null;
                    try
                    {
                        var rawVersion = (await response.Content.ReadAsStringAsync(token)).Trim();
                        if (!string.IsNullOrWhiteSpace(rawVersion))
                        {
                            version = rawVersion;
                        }
                    }
                    catch
                    {
                        // Best-effort extraction; connection itself succeeded.
                    }

                    return new QBittorrentTestResult(true, "Connected successfully.", version);
                }

                return new QBittorrentTestResult(false, $"Server returned {(int)response.StatusCode}.");
            },
            message => new QBittorrentTestResult(false, message));
    }

    public Task<QBittorrentAddResult> AddMagnetAsync(string movieCode, string magnetUrl, CancellationToken ct = default) =>
        AddAsync(movieCode, settings =>
        {
            var fields = new Dictionary<string, string> { ["urls"] = magnetUrl, ["tags"] = movieCode };
            if (!string.IsNullOrWhiteSpace(settings.Category)) fields["category"] = settings.Category;
            return new FormUrlEncodedContent(fields);
        }, ct);

    public Task<QBittorrentAddResult> AddTorrentFileAsync(string movieCode, string fileName, byte[] torrentFileBytes, CancellationToken ct = default) =>
        AddAsync(movieCode, settings =>
        {
            var content = new MultipartFormDataContent();
            var fileContent = new ByteArrayContent(torrentFileBytes);
            fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/x-bittorrent");
            content.Add(fileContent, "torrents", fileName);
            content.Add(new StringContent(movieCode), "tags");
            if (!string.IsNullOrWhiteSpace(settings.Category)) content.Add(new StringContent(settings.Category), "category");
            return content;
        }, ct);

    /// <summary>Shared add flow: log in, make sure the movie-code tag (and category, if
    /// configured) exist, then submit the caller-built content — either a magnet URL or an
    /// uploaded .torrent file, already including the tags/category fields — to
    /// /api/v2/torrents/add.</summary>
    private async Task<QBittorrentAddResult> AddAsync(string movieCode, Func<QBittorrentSettings, HttpContent> buildAddContent, CancellationToken ct)
    {
        var settings = await GetSettingsAsync(ct);
        if (settings is null)
        {
            return new QBittorrentAddResult(false, "qBittorrent is not configured. Set a base URL, username and password in Settings.");
        }

        return await ExecuteAsync(RequestTimeout, ct,
            async (client, token) =>
            {
                const string LoginFailed = "Could not log in to qBittorrent — check the username/password.";

                // Idempotent — makes sure the movie-code tag exists before tagging the new torrent
                // with it, so QBittorrentSyncTask can later correlate this grab to a live torrent.
                using (var createTagsResponse = await SendAuthenticatedAsync(client, settings, sid =>
                    BuildRequest(HttpMethod.Post, settings, "/api/v2/torrents/createTags", sid,
                        new FormUrlEncodedContent(new Dictionary<string, string> { ["tags"] = movieCode })), token))
                {
                    // Best-effort — some qBittorrent versions auto-create tags on add anyway.
                    if (createTagsResponse is null) return new QBittorrentAddResult(false, LoginFailed);
                }

                var category = settings.Category;
                if (!string.IsNullOrWhiteSpace(category))
                {
                    // Idempotent — qBittorrent errors if the category doesn't exist yet on add, and
                    // simply no-ops (or 409s) if it's already there.
                    using var createCategoryResponse = await SendAuthenticatedAsync(client, settings, sid =>
                        BuildRequest(HttpMethod.Post, settings, "/api/v2/torrents/createCategory", sid,
                            new FormUrlEncodedContent(new Dictionary<string, string> { ["category"] = category })), token);
                    if (createCategoryResponse is null) return new QBittorrentAddResult(false, LoginFailed);
                }

                // The request (and so its Content) is rebuilt if the session has to be renewed.
                using var addResponse = await SendAuthenticatedAsync(client, settings, sid =>
                    BuildRequest(HttpMethod.Post, settings, "/api/v2/torrents/add", sid, buildAddContent(settings)), token);
                if (addResponse is null) return new QBittorrentAddResult(false, LoginFailed);

                var body = await addResponse.Content.ReadAsStringAsync(token);

                if (!addResponse.IsSuccessStatusCode)
                {
                    return new QBittorrentAddResult(false, $"qBittorrent returned {(int)addResponse.StatusCode}: {body}");
                }

                if (body.Contains("Fails", StringComparison.OrdinalIgnoreCase))
                {
                    return new QBittorrentAddResult(false, "qBittorrent rejected the torrent.");
                }

                return new QBittorrentAddResult(true, null);
            },
            message => new QBittorrentAddResult(false, message));
    }

    public async Task<QBittorrentTorrentsResult> GetTorrentsAsync(CancellationToken ct = default)
    {
        var settings = await GetSettingsAsync(ct);
        if (settings is null) return QBittorrentTorrentsResult.Failure("qBittorrent is not configured.");

        return await ExecuteAsync(RequestTimeout, ct,
            async (client, token) =>
            {
                var infoPath = "/api/v2/torrents/info";
                if (!string.IsNullOrWhiteSpace(settings.Category))
                {
                    // Scoped to our category (when one is configured) so torrents added outside
                    // this app don't show up in the Queue/History pages or get touched by the sync.
                    infoPath += $"?category={Uri.EscapeDataString(settings.Category)}";
                }

                using var response = await SendAuthenticatedAsync(client, settings, sid =>
                    BuildRequest(HttpMethod.Get, settings, infoPath, sid), token);
                if (response is null) return QBittorrentTorrentsResult.Failure("qBittorrent login was rejected.");
                if (!response.IsSuccessStatusCode) return QBittorrentTorrentsResult.Failure($"qBittorrent returned {(int)response.StatusCode}.");

                var torrents = await response.Content.ReadFromJsonAsync<List<QBittorrentTorrentDto>>(cancellationToken: token);
                return new QBittorrentTorrentsResult(true, torrents ?? []);
            },
            QBittorrentTorrentsResult.Failure);
    }

    public async Task<bool> DeleteTorrentAsync(string hash, bool deleteFiles = false, CancellationToken ct = default)
    {
        var settings = await GetSettingsAsync(ct);
        if (settings is null) return false;

        return await ExecuteAsync(RequestTimeout, ct,
            async (client, token) =>
            {
                using var response = await SendAuthenticatedAsync(client, settings, sid =>
                    BuildRequest(HttpMethod.Post, settings, "/api/v2/torrents/delete", sid,
                        new FormUrlEncodedContent(new Dictionary<string, string>
                        {
                            ["hashes"] = hash,
                            ["deleteFiles"] = deleteFiles ? "true" : "false"
                        })), token);
                return response is { IsSuccessStatusCode: true };
            },
            _ => false);
    }

    private static HttpRequestMessage BuildRequest(HttpMethod method, QBittorrentSettings settings, string pathAndQuery, string sid, HttpContent? content = null)
    {
        var request = new HttpRequestMessage(method, CombineUrl(settings.BaseUrl!, pathAndQuery)) { Content = content };
        AddAuthHeaders(request, settings.BaseUrl!, sid);
        return request;
    }

    /// <summary>Sends a request under the cached session for these credentials, logging in only when
    /// there is none. qBittorrent answers 403 once a session has expired, so a 403 is retried once
    /// after a fresh login. Returns null when the login is rejected; that is remembered, and no
    /// further login is attempted (a wrong password would trip qBittorrent's WebUI ban) until the
    /// connection settings change or <see cref="TestConnectionAsync"/> is run. The caller disposes the response.</summary>
    private async Task<HttpResponseMessage?> SendAuthenticatedAsync(HttpClient client, QBittorrentSettings settings, Func<string, HttpRequestMessage> buildRequest, CancellationToken ct)
    {
        if (sessions.IsLoginRejected(settings)) return null;

        var sid = sessions.TryGetSid(settings);
        var fresh = sid is null;
        if (sid is null)
        {
            sid = await LoginAsync(client, settings, ct);
            if (sid is null)
            {
                sessions.MarkLoginRejected(settings);
                return null;
            }
            sessions.SetSid(settings, sid);
        }

        using (var request = buildRequest(sid))
        {
            var response = await client.SendAsync(request, ct);
            if (response.StatusCode != HttpStatusCode.Forbidden || fresh) return response;
            response.Dispose();
        }

        sessions.Forget(settings);
        sid = await LoginAsync(client, settings, ct);
        if (sid is null)
        {
            sessions.MarkLoginRejected(settings);
            return null;
        }
        sessions.SetSid(settings, sid);

        using var retry = buildRequest(sid);
        return await client.SendAsync(retry, ct);
    }

    /// <summary>Logs in and returns the SID cookie value, or null if the login was rejected.</summary>
    private static async Task<string?> LoginAsync(HttpClient client, QBittorrentSettings settings, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, CombineUrl(settings.BaseUrl!, "/api/v2/auth/login"));
        request.Headers.Referrer = new Uri(settings.BaseUrl!);
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["username"] = settings.Username ?? "",
            ["password"] = settings.Password ?? ""
        });

        using var response = await client.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode) return null;

        if (!response.Headers.TryGetValues("Set-Cookie", out var cookies)) return null;
        foreach (var cookie in cookies)
        {
            var sidPart = cookie.Split(';')[0];
            if (sidPart.StartsWith("SID=", StringComparison.OrdinalIgnoreCase))
            {
                return sidPart["SID=".Length..];
            }
        }
        return null;
    }

    /// <summary>qBittorrent requires a Referer/Origin header matching its own host on
    /// state-changing requests (CSRF protection) in addition to the session cookie.</summary>
    private static void AddAuthHeaders(HttpRequestMessage request, string baseUrl, string sid)
    {
        request.Headers.Add("Cookie", $"SID={sid}");
        request.Headers.Referrer = new Uri(baseUrl);
    }

    protected override async Task<QBittorrentSettings?> GetSettingsAsync(CancellationToken ct)
    {
        if (!QBittorrentEnvConfig.IsSet(Configuration))
        {
            await using var db = await DbFactory.CreateDbContextAsync(ct);
            var settings = await db.QBittorrentSettings.ReadSingleRowAsync(ct);
            return string.IsNullOrWhiteSpace(settings?.BaseUrl)
                || string.IsNullOrWhiteSpace(settings?.Username)
                || string.IsNullOrWhiteSpace(settings?.Password)
                ? null
                : settings;
        }

        // Connection is env-configured, but Category is independent — fall back to whatever
        // is saved on the DB row (if any) when there's no QBittorrent:Category env override.
        var category = QBittorrentEnvConfig.GetCategory(Configuration);
        if (string.IsNullOrWhiteSpace(category))
        {
            await using var db = await DbFactory.CreateDbContextAsync(ct);
            var row = await db.QBittorrentSettings.ReadSingleRowAsync(ct);
            category = row?.Category;
        }

        return new QBittorrentSettings
        {
            BaseUrl = QBittorrentEnvConfig.GetBaseUrl(Configuration),
            ExternalUrl = QBittorrentEnvConfig.GetExternalUrl(Configuration),
            Username = QBittorrentEnvConfig.GetUsername(Configuration),
            Password = QBittorrentEnvConfig.GetPassword(Configuration),
            Category = category
        };
    }
}

/// <summary>Process-wide qBittorrent session state, keyed by the connection credentials so changing
/// the URL, username or password starts from scratch. Singleton because <see cref="QBittorrentClient"/>
/// is scoped, and the Queue page polls every few seconds.</summary>
public class QBittorrentSessionCache
{
    private readonly ConcurrentDictionary<(string, string, string), string> sids = new();
    private readonly ConcurrentDictionary<(string, string, string), bool> rejected = new();

    private static (string, string, string) KeyOf(QBittorrentSettings s) => (s.BaseUrl ?? "", s.Username ?? "", s.Password ?? "");

    public string? TryGetSid(QBittorrentSettings settings) => sids.GetValueOrDefault(KeyOf(settings));

    public void SetSid(QBittorrentSettings settings, string sid)
    {
        var key = KeyOf(settings);
        sids[key] = sid;
        rejected.TryRemove(key, out _);
    }

    public bool IsLoginRejected(QBittorrentSettings settings) => rejected.ContainsKey(KeyOf(settings));

    public void MarkLoginRejected(QBittorrentSettings settings)
    {
        var key = KeyOf(settings);
        sids.TryRemove(key, out _);
        rejected[key] = true;
    }

    /// <summary>Drops the cached session and any "login rejected" mark, so the next call logs in again.</summary>
    public void Forget(QBittorrentSettings settings)
    {
        var key = KeyOf(settings);
        sids.TryRemove(key, out _);
        rejected.TryRemove(key, out _);
    }
}
