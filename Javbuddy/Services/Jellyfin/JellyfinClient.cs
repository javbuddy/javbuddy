using Javbuddy.Data;
using Javbuddy.Models;
using Javbuddy.Services.Infrastructure;
using Javbuddy.Services.MediaServer;
using Javbuddy.Services.Trickplay;
using Microsoft.EntityFrameworkCore;

namespace Javbuddy.Services.Jellyfin;

public record JellyfinLookupResult(bool Success, List<JellyfinItemDto>? Items, string? ErrorMessage);
public record JellyfinTestResult(bool Success, string Message, string? Version = null, string? ServerId = null) : MediaServerTestResult(Success, Message, Version, ServerId);
public record JellyfinLibrariesResult(bool Success, List<JellyfinVirtualFolderDto>? Libraries, string? ErrorMessage);

public interface IJellyfinClient : IMediaServerClient
{
    Task<string?> GetBaseUrlAsync(CancellationToken ct = default);
    Task<JellyfinLookupResult> LookupAsync(string query, string? parentId = null, CancellationToken ct = default);

    /// <summary>Lists every Movie-type item across the configured "libraries to check" (no
    /// search term — the full library contents). Empty selection returns an empty result
    /// without contacting the server, same as LookupInSelectedLibrariesAsync.</summary>
    Task<JellyfinLookupResult> ListMoviesInSelectedLibrariesAsync(CancellationToken ct = default);

    Task<List<string>> GetSelectedLibraryNamesAsync(CancellationToken ct = default);
    Task<JellyfinLibrariesResult> GetLibrariesAsync(CancellationToken ct = default);
    Task<string?> GetServerIdAsync(CancellationToken ct = default);
    Task<JellyfinPersonDto?> LookupPersonAsync(string name, CancellationToken ct = default);

    /// <summary>Looks up the item's trickplay (scrub-preview thumbnail) layout for Movie Cleanup's
    /// scrub bar and builds a browser-reachable tile URL template (ExternalUrl
    /// preferred over BaseUrl, since the browser loads the tiles directly; API key embedded). Null if
    /// Jellyfin is disabled/unconfigured, the request fails, or the item has no usable trickplay
    /// data (trickplay generation is opt-in per Jellyfin library) — callers just skip the scrub bar.</summary>
    Task<TrickplayLayout?> GetTrickplayAsync(string itemId, CancellationToken ct = default);
}

/// <summary>Reads Jellyfin connection settings from environment variables /
/// appsettings ("Jellyfin:BaseUrl", "Jellyfin:ExternalUrl", "Jellyfin:ApiKey", "Jellyfin:SelectedLibraryNames" —
/// standard .NET env form "Jellyfin__BaseUrl", "Jellyfin__ExternalUrl" etc.).
/// SelectedLibraryNames supports standard .NET array configuration (Jellyfin__SelectedLibraryNames__0,
/// Jellyfin__SelectedLibraryNames__1, JSON arrays, and comma/newline-delimited strings).</summary>
public static class JellyfinEnvConfig
{
    public static bool? GetEnabled(IConfiguration configuration)
    {
        var raw = configuration["Jellyfin:Enabled"];
        return bool.TryParse(raw, out var enabled) ? enabled : null;
    }

    public static string? GetBaseUrl(IConfiguration configuration) => configuration["Jellyfin:BaseUrl"];
    public static string? GetExternalUrl(IConfiguration configuration) => configuration["Jellyfin:ExternalUrl"];
    public static string? GetApiKey(IConfiguration configuration) => configuration["Jellyfin:ApiKey"];

    public static List<string> GetSelectedLibraryNames(IConfiguration configuration)
    {
        var section = configuration.GetSection("Jellyfin:SelectedLibraryNames");
        var children = section.GetChildren()
            .Select(c => c.Value)
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v => v!.Trim())
            .ToList();

        if (children.Count > 0)
        {
            return children;
        }

        var scalar = configuration["Jellyfin:SelectedLibraryNames"];
        if (string.IsNullOrWhiteSpace(scalar))
        {
            return [];
        }

        return JellyfinClient.ParseLibraryNames(scalar);
    }

    public static bool IsSet(IConfiguration configuration) =>
        !string.IsNullOrWhiteSpace(GetBaseUrl(configuration)) && !string.IsNullOrWhiteSpace(GetApiKey(configuration));
}

public class JellyfinClient : ApiClientBase<JellyfinSettings>, IJellyfinClient
{
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(30);
    private const int ItemsPageSize = 500;

    private readonly EffectiveSettingsResolver<JellyfinSettings> settingsResolver;
    private string? cachedServerId;
    private string? cachedServerIdBaseUrl;

    protected override string ServiceName => "Jellyfin";
    public string DisplayName => "Jellyfin";

    public JellyfinClient(IHttpClientFactory httpClientFactory, IDbContextFactory<AppDbContext> dbFactory, IConfiguration configuration)
        : base(httpClientFactory, dbFactory, configuration)
    {
        settingsResolver = new EffectiveSettingsResolver<JellyfinSettings>(
            dbFactory,
            configuration,
            config =>
            {
                var envLibraryNames = JellyfinEnvConfig.GetSelectedLibraryNames(config);
                return new JellyfinSettings
                {
                    Enabled = JellyfinEnvConfig.GetEnabled(config) ?? true,
                    BaseUrl = JellyfinEnvConfig.GetBaseUrl(config),
                    ExternalUrl = JellyfinEnvConfig.GetExternalUrl(config),
                    ApiKey = JellyfinEnvConfig.GetApiKey(config),
                    SelectedLibraryNames = envLibraryNames.Count > 0 ? string.Join(",", envLibraryNames) : null
                };
            },
            (db, ct) => db.JellyfinSettings.ReadSingleRowAsync(ct),
            s => !string.IsNullOrWhiteSpace(s.BaseUrl) && !string.IsNullOrWhiteSpace(s.ApiKey));
    }

    protected override Task<JellyfinSettings?> GetSettingsAsync(CancellationToken ct) => settingsResolver.ResolveAsync(ct);

    public async Task<bool> IsEnabledAsync(CancellationToken ct = default)
    {
        var envEnabled = JellyfinEnvConfig.GetEnabled(Configuration);
        if (envEnabled == false) return false;

        if (JellyfinEnvConfig.IsSet(Configuration))
        {
            return envEnabled ?? true;
        }

        await using var db = await DbFactory.CreateDbContextAsync(ct);
        var settings = await db.JellyfinSettings.ReadSingleRowAsync(ct);
        if (settings is null) return false;

        var isConfigured = !string.IsNullOrWhiteSpace(settings.BaseUrl) && !string.IsNullOrWhiteSpace(settings.ApiKey);
        var isEnabled = envEnabled ?? settings.Enabled;
        return isEnabled && isConfigured;
    }

    public Task<JellyfinLookupResult> LookupAsync(string query, string? parentId = null, CancellationToken ct = default) =>
        QueryItemsAsync(query, parentId, ct);

    private async Task<JellyfinLookupResult> QueryItemsAsync(string? searchTerm, string? parentId, CancellationToken ct)
    {
        var settings = await GetSettingsAsync(ct);
        if (settings is null)
        {
            return new JellyfinLookupResult(false, null, "Jellyfin is not configured. Set a base URL and API key in Settings.");
        }

        return await ExecuteAsync(RequestTimeout, ct,
            async (client, token) =>
            {
                // Callers only read Id, ServerId, Name and ProductionYear, so leave out what Jellyfin adds by default
                // (image tags, user data, the total count). A library listing is read a page at a time instead of as
                // one response holding every movie; a search returns a handful and stays one request.
                var searchFilter = string.IsNullOrWhiteSpace(searchTerm) ? "" : $"&searchTerm={Uri.EscapeDataString(searchTerm)}";
                var parentFilter = string.IsNullOrWhiteSpace(parentId) ? "" : $"&parentId={Uri.EscapeDataString(parentId)}";
                var baseUrl = CombineUrl(settings.BaseUrl!, "/Items")
                    + $"?recursive=true&includeItemTypes=Movie&enableImages=false&enableUserData=false&enableTotalRecordCount=false{searchFilter}{parentFilter}"
                    + $"&apikey={Uri.EscapeDataString(settings.ApiKey!)}";
                var paged = string.IsNullOrWhiteSpace(searchTerm);

                var items = new List<JellyfinItemDto>();
                var seenIds = new HashSet<string>();
                for (var startIndex = 0; ; startIndex += ItemsPageSize)
                {
                    var url = paged ? $"{baseUrl}&sortBy=SortName&startIndex={startIndex}&limit={ItemsPageSize}" : baseUrl;
                    using var response = await client.GetAsync(url, token);

                    if (!response.IsSuccessStatusCode)
                    {
                        return new JellyfinLookupResult(false, null, $"Jellyfin returned {(int)response.StatusCode}: {response.ReasonPhrase}");
                    }

                    var body = await response.Content.ReadFromJsonAsync<JellyfinQueryResultDto>(cancellationToken: token);
                    var page = body?.Items ?? new List<JellyfinItemDto>();
                    var added = 0;
                    foreach (var item in page)
                    {
                        // A server that ignores paging would repeat the same items forever; an item without an Id can't be de-duplicated.
                        if (item.Id is null || seenIds.Add(item.Id))
                        {
                            items.Add(item);
                            added++;
                        }
                    }

                    if (!paged || page.Count < ItemsPageSize || added == 0)
                    {
                        break;
                    }
                }

                if (!string.IsNullOrWhiteSpace(parentId))
                {
                    foreach (var item in items)
                    {
                        item.LibraryId = parentId;
                    }
                }
                return new JellyfinLookupResult(true, items, null);
            },
            message => new JellyfinLookupResult(false, null, message));
    }

    /// <summary>Resolves the selected library names to the actual libraries (Name + ItemId),
    /// since Jellyfin's /Items endpoint only accepts an ItemId as parentId.</summary>
    private async Task<List<JellyfinVirtualFolderDto>> ResolveSelectedLibrariesAsync(CancellationToken ct)
    {
        var selectedNames = await GetSelectedLibraryNamesAsync(ct);
        if (selectedNames.Count == 0)
        {
            return [];
        }

        var librariesResult = await GetLibrariesAsync(ct);
        if (!librariesResult.Success || librariesResult.Libraries is null)
        {
            return [];
        }

        var selectedNameSet = new HashSet<string>(selectedNames, StringComparer.Ordinal);
        return librariesResult.Libraries
            .Where(l => !string.IsNullOrWhiteSpace(l.ItemId) && !string.IsNullOrWhiteSpace(l.Name) && selectedNameSet.Contains(l.Name!))
            .ToList();
    }

    public async Task<JellyfinLookupResult> ListMoviesInSelectedLibrariesAsync(CancellationToken ct = default)
    {
        if (!await IsEnabledAsync(ct))
        {
            return new JellyfinLookupResult(true, new List<JellyfinItemDto>(), null);
        }

        var libraries = await ResolveSelectedLibrariesAsync(ct);
        if (libraries.Count == 0)
        {
            return new JellyfinLookupResult(true, new List<JellyfinItemDto>(), null);
        }

        var seenIds = new HashSet<string>();
        var combined = new List<JellyfinItemDto>();

        foreach (var library in libraries)
        {
            var result = await QueryItemsAsync(null, library.ItemId, ct);
            if (!result.Success)
            {
                return result;
            }

            foreach (var item in result.Items ?? Enumerable.Empty<JellyfinItemDto>())
            {
                if (!string.IsNullOrWhiteSpace(item.Id) && seenIds.Add(item.Id))
                {
                    item.LibraryName = library.Name;
                    combined.Add(item);
                }
            }
        }

        return new JellyfinLookupResult(true, combined, null);
    }

    public async Task<MediaServerLookupResult> LookupInSelectedLibrariesAsync(string query, CancellationToken ct = default)
    {
        if (!await IsEnabledAsync(ct))
        {
            return new MediaServerLookupResult(true, new List<MediaServerItemDto>(), null);
        }

        var libraries = await ResolveSelectedLibrariesAsync(ct);
        if (libraries.Count == 0)
        {
            return new MediaServerLookupResult(true, new List<MediaServerItemDto>(), null);
        }

        foreach (var library in libraries)
        {
            var result = await LookupAsync(query, library.ItemId, ct);
            if (!result.Success)
            {
                return new MediaServerLookupResult(false, null, result.ErrorMessage);
            }
            if (result.Items is { Count: > 0 })
            {
                foreach (var item in result.Items)
                {
                    item.LibraryName = library.Name;
                }
                return new MediaServerLookupResult(true, result.Items.Cast<MediaServerItemDto>().ToList(), null);
            }
        }

        return new MediaServerLookupResult(true, new List<MediaServerItemDto>(), null);
    }

    public async Task<List<string>> GetSelectedLibraryNamesAsync(CancellationToken ct = default)
    {
        var envNames = JellyfinEnvConfig.GetSelectedLibraryNames(Configuration);
        if (envNames.Count > 0)
        {
            return envNames;
        }

        await using var db = await DbFactory.CreateDbContextAsync(ct);
        var settings = await db.JellyfinSettings.ReadSingleRowAsync(ct);
        return ParseLibraryNames(settings?.SelectedLibraryNames);
    }

    public static List<string> ParseLibraryNames(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return [];

        var trimmed = raw.Trim();
        if (trimmed.StartsWith('[') && trimmed.EndsWith(']'))
        {
            try
            {
                var parsed = System.Text.Json.JsonSerializer.Deserialize<List<string>>(trimmed);
                if (parsed is { Count: > 0 })
                {
                    return parsed.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p.Trim()).ToList();
                }
            }
            catch (System.Text.Json.JsonException)
            {
                // Fall back to delimiter splitting
            }
        }

        return trimmed.Split([',', '\n', '\r', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
    }

    public async Task<MediaServerActionResult> RefreshItemAsync(string itemId, CancellationToken ct = default)
    {
        var settings = await GetSettingsAsync(ct);
        if (settings is null)
        {
            return new MediaServerActionResult(false, "Jellyfin is not configured. Set a base URL and API key in Settings.");
        }

        return await ExecuteAsync(RequestTimeout, ct,
            async (client, token) =>
            {
                var url = CombineUrl(settings.BaseUrl!, $"/Items/{Uri.EscapeDataString(itemId)}/Refresh")
                    + $"?metadataRefreshMode=Default&imageRefreshMode=Default&apikey={Uri.EscapeDataString(settings.ApiKey!)}";

                using var response = await client.PostAsync(url, null, token);
                if (response.IsSuccessStatusCode)
                {
                    return new MediaServerActionResult(true, null);
                }

                return new MediaServerActionResult(false, $"Jellyfin returned {(int)response.StatusCode}: {response.ReasonPhrase}");
            },
            message => new MediaServerActionResult(false, message));
    }

    public async Task<string?> GetServerIdAsync(CancellationToken ct = default)
    {
        if (!await IsEnabledAsync(ct)) return null;
        var settings = await GetSettingsAsync(ct);
        if (settings is null || string.IsNullOrWhiteSpace(settings.BaseUrl)) return null;

        if (cachedServerId is not null && cachedServerIdBaseUrl == settings.BaseUrl)
        {
            return cachedServerId;
        }

        var result = await ExecuteAsync(TimeSpan.FromSeconds(15), ct,
            async (client, token) =>
            {
                var url = CombineUrl(settings.BaseUrl!, "/System/Info") + $"?apikey={Uri.EscapeDataString(settings.ApiKey!)}";
                using var response = await client.GetAsync(url, token);
                if (!response.IsSuccessStatusCode)
                {
                    return null;
                }

                try
                {
                    var info = await response.Content.ReadFromJsonAsync<JellyfinSystemInfoDto>(cancellationToken: token);
                    return info?.Id;
                }
                catch (System.Text.Json.JsonException)
                {
                    return null;
                }
            },
            _ => null);

        if (!string.IsNullOrWhiteSpace(result))
        {
            cachedServerId = result;
            cachedServerIdBaseUrl = settings.BaseUrl;
            return result;
        }

        return null;
    }

    public async Task<string?> GetWebUrlAsync(string itemId, string? serverId = null, CancellationToken ct = default)
    {
        if (!await IsEnabledAsync(ct)) return null;
        var settings = await GetSettingsAsync(ct);
        if (settings is null) return null;
        var webBase = !string.IsNullOrWhiteSpace(settings.ExternalUrl) ? settings.ExternalUrl : settings.BaseUrl;
        var resolvedServerId = !string.IsNullOrWhiteSpace(serverId) ? serverId : await GetServerIdAsync(ct);
        if (string.IsNullOrWhiteSpace(resolvedServerId)) return null;
        return $"{webBase!.TrimEnd('/')}/web/index.html#!/details?id={Uri.EscapeDataString(itemId)}&serverId={Uri.EscapeDataString(resolvedServerId)}";
    }

    public async Task<TrickplayLayout?> GetTrickplayAsync(string itemId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(itemId)) return null;
        if (!await IsEnabledAsync(ct)) return null;
        var settings = await GetSettingsAsync(ct);
        if (settings is null || string.IsNullOrWhiteSpace(settings.BaseUrl) || string.IsNullOrWhiteSpace(settings.ApiKey)) return null;
        var tileBase = !string.IsNullOrWhiteSpace(settings.ExternalUrl) ? settings.ExternalUrl : settings.BaseUrl;

        return await ExecuteAsync(RequestTimeout, ct,
            async (client, token) =>
            {
                var url = CombineUrl(settings.BaseUrl!, "/Items")
                    + $"?ids={Uri.EscapeDataString(itemId)}&fields=Trickplay&apikey={Uri.EscapeDataString(settings.ApiKey!)}";

                try
                {
                    using var response = await client.GetAsync(url, token);
                    if (!response.IsSuccessStatusCode) return null;

                    var body = await response.Content.ReadFromJsonAsync<JellyfinTrickplayQueryResultDto>(cancellationToken: token);
                    return BuildTrickplay(body?.Items?.FirstOrDefault(), itemId, tileBase!, settings.ApiKey!);
                }
                catch (System.Text.Json.JsonException)
                {
                    return null;
                }
            },
            _ => null);
    }

    /// <summary>Picks the item's own media source (falling back to the first) and its smallest
    /// tile-sheet width, and returns null unless the layout and runtime are all usable.</summary>
    private static TrickplayLayout? BuildTrickplay(JellyfinTrickplayItemDto? item, string itemId, string tileBase, string apiKey)
    {
        if (item?.Trickplay is not { Count: > 0 } sources) return null;
        if (item.RunTimeTicks is not > 0) return null;

        var mediaSourceId = sources.ContainsKey(itemId) ? itemId : sources.Keys.First();
        var info = sources[mediaSourceId]
            .Values
            .Where(i => i.Width > 0 && i.Height > 0 && i.TileWidth > 0 && i.TileHeight > 0 && i.ThumbnailCount > 0 && i.Interval > 0)
            .OrderBy(i => i.Width)
            .FirstOrDefault();
        if (info is null) return null;

        var template = $"{tileBase.TrimEnd('/')}/Videos/{Uri.EscapeDataString(itemId)}/Trickplay/{info.Width}/{{index}}.jpg"
            + $"?mediaSourceId={Uri.EscapeDataString(mediaSourceId)}&api_key={Uri.EscapeDataString(apiKey)}";
        return new TrickplayLayout(
            info.Width, info.Height, info.TileWidth, info.TileHeight, info.ThumbnailCount, info.Interval,
            item.RunTimeTicks.Value / (double)TimeSpan.TicksPerSecond, template);
    }

    public async Task<JellyfinPersonDto?> LookupPersonAsync(string name, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        if (!await IsEnabledAsync(ct)) return null;
        var settings = await GetSettingsAsync(ct);
        if (settings is null || string.IsNullOrWhiteSpace(settings.BaseUrl)) return null;

        return await ExecuteAsync(RequestTimeout, ct,
            async (client, token) =>
            {
                var trimmed = name.Trim();
                var url = CombineUrl(settings.BaseUrl!, $"/Persons/{Uri.EscapeDataString(trimmed)}")
                    + $"?apikey={Uri.EscapeDataString(settings.ApiKey!)}";

                try
                {
                    using var response = await client.GetAsync(url, token);
                    if (response.IsSuccessStatusCode)
                    {
                        return await response.Content.ReadFromJsonAsync<JellyfinPersonDto>(cancellationToken: token);
                    }

                    var searchUrl = CombineUrl(settings.BaseUrl!, "/Persons")
                        + $"?searchTerm={Uri.EscapeDataString(trimmed)}&limit=5&apikey={Uri.EscapeDataString(settings.ApiKey!)}";

                    using var searchResponse = await client.GetAsync(searchUrl, token);
                    if (searchResponse.IsSuccessStatusCode)
                    {
                        var queryResult = await searchResponse.Content.ReadFromJsonAsync<JellyfinQueryResultDto>(cancellationToken: token);
                        var matched = queryResult?.Items?
                            .FirstOrDefault(i => string.Equals(i.Name, trimmed, StringComparison.OrdinalIgnoreCase));
                        if (matched is not null && !string.IsNullOrWhiteSpace(matched.Id))
                        {
                            return new JellyfinPersonDto
                            {
                                Id = matched.Id,
                                Name = matched.Name,
                                ServerId = matched.ServerId
                            };
                        }
                    }
                }
                catch (System.Text.Json.JsonException)
                {
                    return null;
                }

                return null;
            },
            _ => null);
    }

    public async Task<JellyfinLibrariesResult> GetLibrariesAsync(CancellationToken ct = default)
    {
        if (!await IsEnabledAsync(ct))
        {
            return new JellyfinLibrariesResult(false, null, "Jellyfin integration is disabled.");
        }

        var settings = await GetSettingsAsync(ct);
        if (settings is null)
        {
            return new JellyfinLibrariesResult(false, null, "Jellyfin is not configured. Set a base URL and API key in Settings.");
        }

        return await ExecuteAsync(RequestTimeout, ct,
            async (client, token) =>
            {
                var url = CombineUrl(settings.BaseUrl!, "/Library/VirtualFolders") + $"?apikey={Uri.EscapeDataString(settings.ApiKey!)}";
                using var response = await client.GetAsync(url, token);

                if (!response.IsSuccessStatusCode)
                {
                    return new JellyfinLibrariesResult(false, null, $"Jellyfin returned {(int)response.StatusCode}: {response.ReasonPhrase}");
                }

                var libraries = await response.Content.ReadFromJsonAsync<List<JellyfinVirtualFolderDto>>(cancellationToken: token);
                return new JellyfinLibrariesResult(true, libraries ?? new List<JellyfinVirtualFolderDto>(), null);
            },
            message => new JellyfinLibrariesResult(false, null, message));
    }

    public async Task<MediaServerTestResult> TestConnectionAsync(CancellationToken ct = default)
    {
        var settings = await GetSettingsAsync(ct);
        if (settings is null)
        {
            return new MediaServerTestResult(false, "Set a base URL and API key first.");
        }

        return await ExecuteAsync(TimeSpan.FromSeconds(15), ct,
            async (client, token) =>
            {
                var url = CombineUrl(settings.BaseUrl!, "/System/Info") + $"?apikey={Uri.EscapeDataString(settings.ApiKey!)}";
                using var response = await client.GetAsync(url, token);

                if (response.IsSuccessStatusCode)
                {
                    string? serverId = null;
                    string? version = null;
                    try
                    {
                        var info = await response.Content.ReadFromJsonAsync<JellyfinSystemInfoDto>(cancellationToken: token);
                        if (!string.IsNullOrWhiteSpace(info?.Id))
                        {
                            cachedServerId = info.Id;
                            cachedServerIdBaseUrl = settings.BaseUrl;
                            serverId = info.Id;
                        }
                        if (!string.IsNullOrWhiteSpace(info?.Version))
                        {
                            version = info.Version;
                        }
                    }
                    catch (System.Text.Json.JsonException)
                    {
                        // Server info deserialization is best-effort for caching server ID;
                        // the connection itself succeeded.
                    }

                    return new JellyfinTestResult(true, "Connected successfully.", version, serverId);
                }

                if (response.StatusCode is System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden)
                {
                    return new JellyfinTestResult(false, "Reached the server, but the API key was rejected.");
                }

                return new JellyfinTestResult(false, $"Server returned {(int)response.StatusCode}: {response.ReasonPhrase}");
            },
            message => new JellyfinTestResult(false, message));
    }
}
