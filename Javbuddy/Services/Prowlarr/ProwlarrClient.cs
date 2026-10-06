using Javbuddy.Data;
using Javbuddy.Models;
using Javbuddy.Services.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Javbuddy.Services.Prowlarr;

public record ProwlarrSearchResult(bool Success, List<ReleaseResourceDto>? Releases, string? ErrorMessage);
public record ProwlarrTestResult(bool Success, string Message, string? Version = null);

public interface IProwlarrClient
{
    Task<string?> GetBaseUrlAsync(CancellationToken ct = default);
    Task<string?> GetExternalUrlAsync(CancellationToken ct = default);
    Task<ProwlarrSearchResult> SearchAsync(string query, CancellationToken ct = default);
    Task<ProwlarrTestResult> TestConnectionAsync(CancellationToken ct = default);
}

/// <summary>Reads Prowlarr connection settings from environment variables /
/// appsettings ("Prowlarr:BaseUrl", "Prowlarr:ExternalUrl", "Prowlarr:ApiKey" —
/// standard .NET env form "Prowlarr__BaseUrl", "Prowlarr__ExternalUrl" etc.), which take precedence over the
/// Settings page when set.</summary>
public static class ProwlarrEnvConfig
{
    public static string? GetBaseUrl(IConfiguration configuration) => configuration["Prowlarr:BaseUrl"];
    public static string? GetExternalUrl(IConfiguration configuration) => configuration["Prowlarr:ExternalUrl"];
    public static string? GetApiKey(IConfiguration configuration) => configuration["Prowlarr:ApiKey"];

    public static bool IsSet(IConfiguration configuration) =>
        !string.IsNullOrWhiteSpace(GetBaseUrl(configuration)) && !string.IsNullOrWhiteSpace(GetApiKey(configuration));
}

public class ProwlarrClient : ApiClientBase<ProwlarrSettings>, IProwlarrClient
{
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(30);

    private readonly EffectiveSettingsResolver<ProwlarrSettings> settingsResolver;

    protected override string ServiceName => "Prowlarr";

    public ProwlarrClient(IHttpClientFactory httpClientFactory, IDbContextFactory<AppDbContext> dbFactory, IConfiguration configuration)
        : base(httpClientFactory, dbFactory, configuration)
    {
        settingsResolver = new EffectiveSettingsResolver<ProwlarrSettings>(
            dbFactory,
            configuration,
            config => new ProwlarrSettings
            {
                BaseUrl = ProwlarrEnvConfig.GetBaseUrl(config),
                ExternalUrl = ProwlarrEnvConfig.GetExternalUrl(config),
                ApiKey = ProwlarrEnvConfig.GetApiKey(config)
            },
            (db, ct) => db.ProwlarrSettings.ReadSingleRowAsync(ct),
            s => !string.IsNullOrWhiteSpace(s.BaseUrl) && !string.IsNullOrWhiteSpace(s.ApiKey));
    }

    protected override Task<ProwlarrSettings?> GetSettingsAsync(CancellationToken ct) => settingsResolver.ResolveAsync(ct);

    public async Task<ProwlarrSearchResult> SearchAsync(string query, CancellationToken ct = default)
    {
        var settings = await GetSettingsAsync(ct);
        if (settings is null)
        {
            return new ProwlarrSearchResult(false, null, "Prowlarr is not configured. Set a base URL and API key in Settings.");
        }

        return await ExecuteAsync(RequestTimeout, ct,
            async (client, token) =>
            {
                var url = CombineUrl(settings.BaseUrl!, "/api/v1/search") + $"?query={Uri.EscapeDataString(query)}&type=search";
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.Add("X-Api-Key", settings.ApiKey);

                using var response = await client.SendAsync(request, token);

                if (!response.IsSuccessStatusCode)
                {
                    var errorMessage = await ReadErrorAsync(response, token);
                    return new ProwlarrSearchResult(false, null, $"Prowlarr returned {(int)response.StatusCode}: {errorMessage}");
                }

                var releases = await response.Content.ReadFromJsonAsync<List<ReleaseResourceDto>>(cancellationToken: token);
                return new ProwlarrSearchResult(true, releases ?? new List<ReleaseResourceDto>(), null);
            },
            message => new ProwlarrSearchResult(false, null, message));
    }

    public async Task<ProwlarrTestResult> TestConnectionAsync(CancellationToken ct = default)
    {
        var settings = await GetSettingsAsync(ct);
        if (settings is null)
        {
            return new ProwlarrTestResult(false, "Set a base URL and API key first.");
        }

        return await ExecuteAsync(TimeSpan.FromSeconds(15), ct,
            async (client, token) =>
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, CombineUrl(settings.BaseUrl!, "/api/v1/system/status"));
                request.Headers.Add("X-Api-Key", settings.ApiKey);

                using var response = await client.SendAsync(request, token);
                if (response.IsSuccessStatusCode)
                {
                    string? version = null;
                    try
                    {
                        var status = await response.Content.ReadFromJsonAsync<ProwlarrSystemStatusDto>(cancellationToken: token);
                        version = status?.Version;
                    }
                    catch (System.Text.Json.JsonException)
                    {
                        // Best-effort extraction; connection itself succeeded.
                    }

                    return new ProwlarrTestResult(true, "Connected successfully.", version);
                }

                if (response.StatusCode is System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden)
                {
                    return new ProwlarrTestResult(false, "Reached the server, but the API key was rejected.");
                }

                var errorMessage = await ReadErrorAsync(response, token);
                return new ProwlarrTestResult(false, $"Server returned {(int)response.StatusCode}: {errorMessage}");
            },
            message => new ProwlarrTestResult(false, message));
    }

    private static Task<string> ReadErrorAsync(HttpResponseMessage response, CancellationToken ct) =>
        ReadJsonErrorAsync<ProwlarrErrorDto>(response, e => e.Message, ct);
}
