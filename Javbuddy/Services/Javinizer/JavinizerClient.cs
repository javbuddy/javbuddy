using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Javbuddy.Data;
using Javbuddy.Models;
using Javbuddy.Services.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Javbuddy.Services.Javinizer;

public record JavinizerScrapeResult(bool Success, MovieViewDto? Movie, string? ErrorMessage);
public record JavinizerTestResult(bool Success, string Message, string? Version = null);

public record JavinizerScanResult(bool Success, ScanResponseDto? Data, string? ErrorMessage);
public record JavinizerBatchScrapeResult(bool Success, BatchScrapeResponseDto? Data, string? ErrorMessage);
public record JavinizerBatchJobResult(bool Success, BatchJobResponseDto? Data, string? ErrorMessage);
public record JavinizerOrganizePreviewResult(bool Success, OrganizePreviewResponseDto? Data, string? ErrorMessage);
public record JavinizerOrganizeResult(bool Success, string? ErrorMessage);
public record JavinizerRescrapeResult(bool Success, string? ErrorMessage);
public record JavinizerExcludeResult(bool Success, string? ErrorMessage);
public record JavinizerUpdateResult(bool Success, string? ErrorMessage);
public record JavinizerPosterFromUrlResult(bool Success, string? ErrorMessage);
public record JavinizerSourcesResult(bool Success, List<ScraperSourceResultDto>? Results, string? ErrorMessage);
public record JavinizerFieldOverrideResult(bool Success, FieldOverrideResponseDto? Data, string? ErrorMessage);
public record JavinizerScrapersResult(bool Success, List<ScraperInfoDto>? Scrapers, string? ErrorMessage);
public record JavinizerPosterCropResult(bool Success, PosterCropResponseDto? Data, string? ErrorMessage);
public record JavinizerBytesResult(bool Success, byte[]? Bytes, string? ContentType, string? ErrorMessage, bool NotFound = false);

/// <summary>A named organize destination offered to the user in TorrentSort's Destination step,
/// e.g. Name "jav", Path "/media/jav" (as javinizer-go sees it).</summary>
public record DestinationAlias(string Name, string Path);

/// <summary>Reads javinizer-go connection settings from environment variables /
/// appsettings ("Javinizer:BaseUrl", "Javinizer:ExternalUrl", "Javinizer:ApiToken", "Javinizer:DestinationAliases" —
/// standard .NET env form "Javinizer__BaseUrl", "Javinizer__ExternalUrl", "Javinizer__DestinationAliases__0__Name" etc.),
/// which take precedence over the Settings page when set.</summary>
public static class JavinizerEnvConfig
{
    public static string? GetBaseUrl(IConfiguration configuration) => configuration["Javinizer:BaseUrl"];
    public static string? GetExternalUrl(IConfiguration configuration) => configuration["Javinizer:ExternalUrl"];
    public static string? GetApiToken(IConfiguration configuration) => configuration["Javinizer:ApiToken"];

    public static List<DestinationAlias> GetDestinationAliases(IConfiguration configuration) =>
        configuration.GetSection("Javinizer:DestinationAliases").Get<List<DestinationAlias>>()?
            .Where(a => !string.IsNullOrWhiteSpace(a.Name) && !string.IsNullOrWhiteSpace(a.Path))
            .ToList()
        ?? [];

    public static bool IsSet(IConfiguration configuration) =>
        !string.IsNullOrWhiteSpace(GetBaseUrl(configuration)) && !string.IsNullOrWhiteSpace(GetApiToken(configuration));
}

public interface IJavinizerClient
{
    Task<string?> GetBaseUrlAsync(CancellationToken ct = default);
    Task<string?> GetExternalUrlAsync(CancellationToken ct = default);
    Task<JavinizerScrapeResult> ScrapeAsync(string code, CancellationToken ct = default);
    Task<JavinizerTestResult> TestConnectionAsync(CancellationToken ct = default);

    Task<JavinizerScanResult> ScanAsync(string path, bool recursive, CancellationToken ct = default);
    Task<JavinizerBatchScrapeResult> BatchScrapeAsync(BatchScrapeRequestDto request, CancellationToken ct = default);
    Task<JavinizerBatchJobResult> GetBatchJobAsync(string jobId, bool includeData, CancellationToken ct = default);
    Task<JavinizerOrganizePreviewResult> PreviewOrganizeAsync(string jobId, string resultId, OrganizePreviewRequestDto request, CancellationToken ct = default);
    Task<JavinizerOrganizeResult> OrganizeAsync(string jobId, OrganizePreviewRequestDto request, CancellationToken ct = default);
    Task<JavinizerRescrapeResult> RescrapeAsync(string jobId, string resultId, BatchRescrapeRequestDto request, CancellationToken ct = default);
    Task<JavinizerExcludeResult> ExcludeResultAsync(string jobId, string resultId, CancellationToken ct = default);
    Task<JavinizerUpdateResult> UpdateResultAsync(string jobId, string resultId, MovieViewDto movie, CancellationToken ct = default, ulong? expectedResultRevision = null);
    Task<JavinizerPosterFromUrlResult> SetPosterFromUrlAsync(string jobId, string resultId, string url, CancellationToken ct = default);
    Task<JavinizerSourcesResult> GetResultSourcesAsync(string jobId, string resultId, CancellationToken ct = default);
    Task<JavinizerFieldOverrideResult> OverrideFieldAsync(string jobId, string resultId, string field, string source, CancellationToken ct = default);
    Task<JavinizerScrapersResult> GetScrapersAsync(CancellationToken ct = default);
    /// <summary>A job's temp poster: <c>{movieId}-full.jpg</c> (the uncropped source) when
    /// <paramref name="fullSize"/>, else the cropped <c>{movieId}.jpg</c>.</summary>
    Task<JavinizerBytesResult> GetTempPosterAsync(string jobId, string movieId, bool fullSize, CancellationToken ct = default);
    Task<JavinizerPosterCropResult> CropPosterAsync(string jobId, string resultId, PosterCropRequestDto request, CancellationToken ct = default);
}

public class JavinizerClient : ApiClientBase<JavinizerSettings>, IJavinizerClient
{
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(60);

    private readonly EffectiveSettingsResolver<JavinizerSettings> settingsResolver;

    protected override string ServiceName => "javinizer-go";

    public JavinizerClient(IHttpClientFactory httpClientFactory, IDbContextFactory<AppDbContext> dbFactory, IConfiguration configuration)
        : base(httpClientFactory, dbFactory, configuration)
    {
        settingsResolver = new EffectiveSettingsResolver<JavinizerSettings>(
            dbFactory,
            configuration,
            config => new JavinizerSettings
            {
                BaseUrl = JavinizerEnvConfig.GetBaseUrl(config),
                ExternalUrl = JavinizerEnvConfig.GetExternalUrl(config),
                ApiToken = JavinizerEnvConfig.GetApiToken(config)
            },
            (db, ct) => db.JavinizerSettings.ReadSingleRowAsync(ct),
            s => !string.IsNullOrWhiteSpace(s.BaseUrl) && !string.IsNullOrWhiteSpace(s.ApiToken));
    }

    protected override Task<JavinizerSettings?> GetSettingsAsync(CancellationToken ct) => settingsResolver.ResolveAsync(ct);

    public async Task<JavinizerScrapeResult> ScrapeAsync(string code, CancellationToken ct = default)
    {
        var settings = await GetSettingsAsync(ct);
        if (settings is null)
        {
            return new JavinizerScrapeResult(false, null, "javinizer-go is not configured. Set a base URL and API token in Settings.");
        }

        return await ExecuteAsync(RequestTimeout, ct,
            async (client, token) =>
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, CombineUrl(settings.BaseUrl!, "/api/v1/scrape"))
                {
                    Content = JsonContent.Create(new ScrapeRequestDto { Id = code })
                };
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiToken);

                using var response = await client.SendAsync(request, token);

                if (!response.IsSuccessStatusCode)
                {
                    var errorMessage = await ReadErrorAsync(response, token);
                    return new JavinizerScrapeResult(false, null, $"javinizer-go returned {(int)response.StatusCode}: {errorMessage}");
                }

                var body = await response.Content.ReadFromJsonAsync<ScrapeResponseDto>(cancellationToken: token);
                if (body?.Movie is null)
                {
                    return new JavinizerScrapeResult(false, null, "javinizer-go returned no metadata for this code.");
                }

                return new JavinizerScrapeResult(true, body.Movie, null);
            },
            message => new JavinizerScrapeResult(false, null, message));
    }

    public async Task<JavinizerTestResult> TestConnectionAsync(CancellationToken ct = default)
    {
        var settings = await GetSettingsAsync(ct);
        if (settings is null)
        {
            return new JavinizerTestResult(false, "Set a base URL and API token first.");
        }

        return await ExecuteAsync(TimeSpan.FromSeconds(15), ct,
            async (client, token) =>
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, CombineUrl(settings.BaseUrl!, "/api/v1/version"));
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiToken);

                using var response = await client.SendAsync(request, token);
                if (response.IsSuccessStatusCode)
                {
                    string? version = null;
                    try
                    {
                        var status = await response.Content.ReadFromJsonAsync<JavinizerVersionResponseDto>(cancellationToken: token);
                        version = status?.EffectiveVersion;
                    }
                    catch (JsonException)
                    {
                        // Best-effort extraction; connection itself succeeded.
                    }

                    return new JavinizerTestResult(true, "Connected successfully.", version);
                }

                if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    // Fallback to /api/v1/config for older instances or mocks without /api/v1/version
                    using var fallbackRequest = new HttpRequestMessage(HttpMethod.Get, CombineUrl(settings.BaseUrl!, "/api/v1/config"));
                    fallbackRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiToken);

                    using var fallbackResponse = await client.SendAsync(fallbackRequest, token);
                    if (fallbackResponse.IsSuccessStatusCode)
                    {
                        return new JavinizerTestResult(true, "Connected successfully.");
                    }
                }

                if (response.StatusCode is System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden)
                {
                    return new JavinizerTestResult(false, "Reached the server, but the API token was rejected.");
                }

                var errorMessage = await ReadErrorAsync(response, token);
                return new JavinizerTestResult(false, $"Server returned {(int)response.StatusCode}: {errorMessage}");
            },
            message => new JavinizerTestResult(false, message));
    }

    public async Task<JavinizerScanResult> ScanAsync(string path, bool recursive, CancellationToken ct = default)
    {
        var (success, body, error) = await SendAsync<ScanResponseDto>(
            HttpMethod.Post, "/api/v1/scan", new ScanRequestDto { Path = path, Recursive = recursive }, ct);
        return new JavinizerScanResult(success, body, error);
    }

    public async Task<JavinizerBatchScrapeResult> BatchScrapeAsync(BatchScrapeRequestDto request, CancellationToken ct = default)
    {
        var (success, body, error) = await SendAsync<BatchScrapeResponseDto>(HttpMethod.Post, "/api/v1/batch/scrape", request, ct);
        return new JavinizerBatchScrapeResult(success, body, error);
    }

    public async Task<JavinizerBatchJobResult> GetBatchJobAsync(string jobId, bool includeData, CancellationToken ct = default)
    {
        var path = $"/api/v1/batch/{Uri.EscapeDataString(jobId)}" + (includeData ? "?include_data=true" : "");
        var (success, body, error) = await SendAsync<BatchJobResponseDto>(HttpMethod.Get, path, null, ct);
        return new JavinizerBatchJobResult(success, body, error);
    }

    public async Task<JavinizerOrganizePreviewResult> PreviewOrganizeAsync(string jobId, string resultId, OrganizePreviewRequestDto request, CancellationToken ct = default)
    {
        var path = $"/api/v1/batch/{Uri.EscapeDataString(jobId)}/results/{Uri.EscapeDataString(resultId)}/preview";
        var (success, body, error) = await SendAsync<OrganizePreviewResponseDto>(HttpMethod.Post, path, request, ct);
        return new JavinizerOrganizePreviewResult(success, body, error);
    }

    public async Task<JavinizerOrganizeResult> OrganizeAsync(string jobId, OrganizePreviewRequestDto request, CancellationToken ct = default)
    {
        var path = $"/api/v1/batch/{Uri.EscapeDataString(jobId)}/organize";
        var (success, _, error) = await SendAsync<JsonElement>(HttpMethod.Post, path, request, ct);
        return new JavinizerOrganizeResult(success, error);
    }

    public async Task<JavinizerRescrapeResult> RescrapeAsync(string jobId, string resultId, BatchRescrapeRequestDto request, CancellationToken ct = default)
    {
        var path = $"/api/v1/batch/{Uri.EscapeDataString(jobId)}/results/{Uri.EscapeDataString(resultId)}/rescrape";
        var (success, _, error) = await SendAsync<JsonElement>(HttpMethod.Post, path, request, ct);
        return new JavinizerRescrapeResult(success, error);
    }

    public async Task<JavinizerExcludeResult> ExcludeResultAsync(string jobId, string resultId, CancellationToken ct = default)
    {
        var path = $"/api/v1/batch/{Uri.EscapeDataString(jobId)}/results/{Uri.EscapeDataString(resultId)}/exclude";
        var (success, _, error) = await SendAsync<JsonElement>(HttpMethod.Post, path, null, ct);
        return new JavinizerExcludeResult(success, error);
    }

    public async Task<JavinizerUpdateResult> UpdateResultAsync(string jobId, string resultId, MovieViewDto movie, CancellationToken ct = default, ulong? expectedResultRevision = null)
    {
        var path = $"/api/v1/batch/{Uri.EscapeDataString(jobId)}/results/{Uri.EscapeDataString(resultId)}";
        var request = new UpdateMovieRequestDto { Movie = movie, ExpectedResultRevision = expectedResultRevision };
        var (success, _, error) = await SendAsync<JsonElement>(HttpMethod.Patch, path, request, ct);
        return new JavinizerUpdateResult(success, error);
    }

    public async Task<JavinizerPosterFromUrlResult> SetPosterFromUrlAsync(string jobId, string resultId, string url, CancellationToken ct = default)
    {
        var path = $"/api/v1/batch/{Uri.EscapeDataString(jobId)}/results/{Uri.EscapeDataString(resultId)}/poster-from-url";
        var request = new PosterFromUrlRequestDto { Url = url };
        var (success, _, error) = await SendAsync<JsonElement>(HttpMethod.Post, path, request, ct);
        return new JavinizerPosterFromUrlResult(success, error);
    }

    public async Task<JavinizerSourcesResult> GetResultSourcesAsync(string jobId, string resultId, CancellationToken ct = default)
    {
        var path = $"/api/v1/batch/{Uri.EscapeDataString(jobId)}/results/{Uri.EscapeDataString(resultId)}/sources";
        var (success, body, error) = await SendAsync<SourceResultsResponseDto>(HttpMethod.Get, path, null, ct);
        return new JavinizerSourcesResult(success, success ? body?.Results ?? [] : null, error);
    }

    public async Task<JavinizerFieldOverrideResult> OverrideFieldAsync(string jobId, string resultId, string field, string source, CancellationToken ct = default)
    {
        var path = $"/api/v1/batch/{Uri.EscapeDataString(jobId)}/results/{Uri.EscapeDataString(resultId)}/field-override";
        var request = new FieldOverrideRequestDto { Field = field, Source = source };
        var (success, body, error) = await SendAsync<FieldOverrideResponseDto>(HttpMethod.Post, path, request, ct);
        return new JavinizerFieldOverrideResult(success, body, error);
    }

    public async Task<JavinizerScrapersResult> GetScrapersAsync(CancellationToken ct = default)
    {
        var (success, body, error) = await SendAsync<AvailableScrapersResponseDto>(HttpMethod.Get, "/api/v1/scrapers", null, ct);
        return new JavinizerScrapersResult(success, success ? body?.Scrapers ?? [] : null, error);
    }

    /// <summary>The full-size poster source javinizer-go measures manual crops against (its own
    /// review UI loads the same file). The temp route needs the API token, so the bytes come
    /// through Javbuddy rather than straight to the browser.</summary>
    public async Task<JavinizerBytesResult> GetTempPosterAsync(string jobId, string movieId, bool fullSize, CancellationToken ct = default)
    {
        var settings = await GetSettingsAsync(ct);
        if (settings is null)
        {
            return new JavinizerBytesResult(false, null, null, "javinizer-go is not configured. Set a base URL and API token in Settings.");
        }

        var path = $"/api/v1/temp/posters/{Uri.EscapeDataString(jobId)}/{Uri.EscapeDataString(movieId)}{(fullSize ? "-full" : "")}.jpg";
        return await ExecuteAsync(RequestTimeout, ct,
            async (client, token) =>
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, CombineUrl(settings.BaseUrl!, path));
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiToken);
                using var response = await client.SendAsync(request, token);
                if (!response.IsSuccessStatusCode)
                {
                    var errorMessage = await ReadErrorAsync(response, token);
                    return new JavinizerBytesResult(false, null, null, $"javinizer-go returned {(int)response.StatusCode}: {errorMessage}", response.StatusCode == HttpStatusCode.NotFound);
                }

                var bytes = await response.Content.ReadAsByteArrayAsync(token);
                return new JavinizerBytesResult(true, bytes, response.Content.Headers.ContentType?.MediaType ?? "image/jpeg", null);
            },
            message => new JavinizerBytesResult(false, null, null, message));
    }

    public async Task<JavinizerPosterCropResult> CropPosterAsync(string jobId, string resultId, PosterCropRequestDto request, CancellationToken ct = default)
    {
        var path = $"/api/v1/batch/{Uri.EscapeDataString(jobId)}/results/{Uri.EscapeDataString(resultId)}/poster-crop";
        var (success, body, error) = await SendAsync<PosterCropResponseDto>(HttpMethod.Post, path, request, ct);
        return new JavinizerPosterCropResult(success, body, error);
    }

    /// <summary>Shared plumbing for the batch-job endpoints above: settings lookup, Bearer auth,
    /// and the existing ReadErrorAsync error path — the same pattern ScrapeAsync/TestConnectionAsync
    /// use, factored out because every batch-job call shares it.</summary>
    private async Task<(bool Success, T? Body, string? ErrorMessage)> SendAsync<T>(HttpMethod method, string path, object? requestBody, CancellationToken ct)
    {
        var settings = await GetSettingsAsync(ct);
        if (settings is null)
        {
            return (false, default, "javinizer-go is not configured. Set a base URL and API token in Settings.");
        }

        return await ExecuteAsync<(bool Success, T? Body, string? ErrorMessage)>(RequestTimeout, ct,
            async (client, token) =>
            {
                using var request = new HttpRequestMessage(method, CombineUrl(settings.BaseUrl!, path));
                if (requestBody is not null)
                {
                    request.Content = JsonContent.Create(requestBody);
                }
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiToken);

                using var response = await client.SendAsync(request, token);

                if (!response.IsSuccessStatusCode)
                {
                    var errorMessage = await ReadErrorAsync(response, token);
                    return (false, default(T), $"javinizer-go returned {(int)response.StatusCode}: {errorMessage}");
                }

                var body = await response.Content.ReadFromJsonAsync<T>(cancellationToken: token);
                return (true, body, (string?)null);
            },
            message => (false, default(T), message));
    }

    private static Task<string> ReadErrorAsync(HttpResponseMessage response, CancellationToken ct) =>
        ReadJsonErrorAsync<ErrorResponseDto>(response, e => e.Message ?? e.Error, ct);
}
