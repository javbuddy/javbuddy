using Javbuddy.Data;
using Javbuddy.Models;
using Javbuddy.Services.Infrastructure;
using Javbuddy.Services.MediaServer;
using Microsoft.EntityFrameworkCore;

namespace Javbuddy.Services.Jellyfin;

public interface IJellyfinClient : IMediaServerClient
{
    Task<JellyfinPersonDto?> LookupPersonAsync(string name, CancellationToken ct = default);
}

/// <summary>Reads Jellyfin connection settings from environment variables /
/// appsettings ("Jellyfin:BaseUrl", "Jellyfin:ExternalUrl", "Jellyfin:ApiKey" —
/// standard .NET env form "Jellyfin__BaseUrl", "Jellyfin__ExternalUrl" etc.).</summary>
public static class JellyfinEnvConfig
{
    public static bool? GetEnabled(IConfiguration configuration) => EnvConfig.GetBool(configuration, "Jellyfin:Enabled");

    public static string? GetBaseUrl(IConfiguration configuration) => configuration["Jellyfin:BaseUrl"];
    public static string? GetExternalUrl(IConfiguration configuration) => configuration["Jellyfin:ExternalUrl"];
    public static string? GetApiKey(IConfiguration configuration) => configuration["Jellyfin:ApiKey"];

    public static bool IsSet(IConfiguration configuration) => EnvConfig.AllSet(GetBaseUrl(configuration), GetApiKey(configuration));
}

public class JellyfinClient : ApiClientBase<JellyfinSettings>, IJellyfinClient
{
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(30);

    private readonly EffectiveSettingsResolver<JellyfinSettings> settingsResolver;
    private string? cachedServerId;
    private string? cachedServerIdBaseUrl;

    protected override string ServiceName => "Jellyfin";

    public JellyfinClient(IHttpClientFactory httpClientFactory, IDbContextFactory<AppDbContext> dbFactory, IConfiguration configuration)
        : base(httpClientFactory, dbFactory, configuration)
    {
        settingsResolver = new EffectiveSettingsResolver<JellyfinSettings>(
            dbFactory,
            configuration,
            config => new JellyfinSettings
            {
                Enabled = JellyfinEnvConfig.GetEnabled(config) ?? true,
                BaseUrl = JellyfinEnvConfig.GetBaseUrl(config),
                ExternalUrl = JellyfinEnvConfig.GetExternalUrl(config),
                ApiKey = JellyfinEnvConfig.GetApiKey(config)
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

    public async Task<MediaServerLookupResult> LookupMovieAsync(string query, CancellationToken ct = default)
    {
        if (!await IsEnabledAsync(ct))
        {
            return new MediaServerLookupResult(true, new List<MediaServerItemDto>(), null);
        }

        var settings = await GetSettingsAsync(ct);
        if (settings is null)
        {
            return new MediaServerLookupResult(false, null, "Jellyfin is not configured. Set a base URL and API key in Settings.");
        }

        return await ExecuteAsync(RequestTimeout, ct,
            async (client, token) =>
            {
                // Callers only read Id, ServerId and Name, so leave out what Jellyfin adds by default
                // (image tags, user data, the total count). A search returns a handful of items in one request.
                var url = CombineUrl(settings.BaseUrl!, "/Items")
                    + $"?recursive=true&includeItemTypes=Movie&enableImages=false&enableUserData=false&enableTotalRecordCount=false"
                    + $"&searchTerm={Uri.EscapeDataString(query)}&apikey={Uri.EscapeDataString(settings.ApiKey!)}";
                using var response = await client.GetAsync(url, token);

                if (!response.IsSuccessStatusCode)
                {
                    return new MediaServerLookupResult(false, null, $"Jellyfin returned {(int)response.StatusCode}: {response.ReasonPhrase}");
                }

                var body = await response.Content.ReadFromJsonAsync<JellyfinQueryResultDto>(cancellationToken: token);
                var items = (body?.Items ?? []).Cast<MediaServerItemDto>().ToList();
                return new MediaServerLookupResult(true, items, null);
            },
            message => new MediaServerLookupResult(false, null, message));
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

                    return new MediaServerTestResult(true, "Connected successfully.", version, serverId);
                }

                if (response.StatusCode is System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden)
                {
                    return new MediaServerTestResult(false, "Reached the server, but the API key was rejected.");
                }

                return new MediaServerTestResult(false, $"Server returned {(int)response.StatusCode}: {response.ReasonPhrase}");
            },
            message => new MediaServerTestResult(false, message));
    }
}
