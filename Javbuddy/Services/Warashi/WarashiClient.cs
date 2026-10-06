using System.Net.Http.Headers;
using Javbuddy.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Javbuddy.Services.Warashi;

public class WarashiClient(
    IHttpClientFactory httpClientFactory,
    IDbContextFactory<AppDbContext> dbFactory,
    ILogger<WarashiClient>? logger = null) : IWarashiClient
{
    public const string DefaultBaseUrl = "https://warashi-asian-pornstars.fr";
    private const int DefaultRequestDelayMs = 750;

    private readonly IHttpClientFactory httpClientFactory = httpClientFactory;
    private readonly IDbContextFactory<AppDbContext> dbFactory = dbFactory;
    private readonly ILogger<WarashiClient> logger = logger ?? NullLogger<WarashiClient>.Instance;
    private readonly SemaphoreSlim throttleLock = new(1, 1);
    private DateTime? lastRequestAtUtc;

    private async Task<(string BaseUrl, int RequestDelayMs)> GetEffectiveSettingsAsync(CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var settings = await db.WarashiSettings.ReadSingleRowAsync(ct);
        var baseUrl = !string.IsNullOrWhiteSpace(settings?.BaseUrl)
            ? settings.BaseUrl.TrimEnd('/')
            : DefaultBaseUrl;
        var delayMs = settings?.RequestDelayMs ?? DefaultRequestDelayMs;
        return (baseUrl, delayMs);
    }

    /// <summary>Waits out the configured throttling delay since the previous request made through
    /// this client instance, so consecutive calls (e.g. during batch enrichment) don't hammer WAPdB.</summary>
    private async Task ThrottleAsync(int delayMs, CancellationToken ct)
    {
        if (delayMs <= 0) return;

        await throttleLock.WaitAsync(ct);
        try
        {
            if (lastRequestAtUtc.HasValue)
            {
                var remaining = TimeSpan.FromMilliseconds(delayMs) - (DateTime.UtcNow - lastRequestAtUtc.Value);
                if (remaining > TimeSpan.Zero)
                {
                    await Task.Delay(remaining, ct);
                }
            }
            lastRequestAtUtc = DateTime.UtcNow;
        }
        finally
        {
            throttleLock.Release();
        }
    }

    private const string BrowserUserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36";

    private HttpClient CreateConfiguredClient()
    {
        var client = httpClientFactory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(15);
        client.DefaultRequestHeaders.UserAgent.Clear();
        client.DefaultRequestHeaders.UserAgent.ParseAdd(BrowserUserAgent);
        client.DefaultRequestHeaders.Accept.Clear();
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("text/html"));
        client.DefaultRequestHeaders.AcceptLanguage.Clear();
        client.DefaultRequestHeaders.AcceptLanguage.Add(new StringWithQualityHeaderValue("en-US"));
        client.DefaultRequestHeaders.AcceptLanguage.Add(new StringWithQualityHeaderValue("en", 0.9));
        return client;
    }

    public async Task<bool> TestConnectionAsync(CancellationToken ct = default)
    {
        try
        {
            var (baseUrl, _) = await GetEffectiveSettingsAsync(ct);
            var client = CreateConfiguredClient();
            var response = await client.GetAsync(baseUrl, ct);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Failed to connect to Warashi Asian Pornstars Database");
            return false;
        }
    }

    public async Task<IReadOnlyList<WarashiSearchResult>> SearchPerformersAsync(string query, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query)) return Array.Empty<WarashiSearchResult>();

        try
        {
            var (baseUrl, delayMs) = await GetEffectiveSettingsAsync(ct);
            var searchUrl = $"{baseUrl}/en/s-12/search";
            var client = CreateConfiguredClient();

            var formContent = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("recherche_critere", "f"),
                new KeyValuePair<string, string>("recherche_valeur", query.Trim())
            });

            await ThrottleAsync(delayMs, ct);
            var response = await client.PostAsync(searchUrl, formContent, ct);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Warashi search for '{Query}' returned HTTP status {StatusCode}", query, response.StatusCode);
                return Array.Empty<WarashiSearchResult>();
            }

            var html = await response.Content.ReadAsStringAsync(ct);
            return WarashiHtmlParser.ParseSearchResults(html, baseUrl);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Error occurred during Warashi performer search for '{Query}'", query);
            return Array.Empty<WarashiSearchResult>();
        }
    }

    public async Task<WarashiPerformerDetail?> GetPerformerDetailAsync(string performerPathOrUrl, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(performerPathOrUrl)) return null;

        try
        {
            var (baseUrl, delayMs) = await GetEffectiveSettingsAsync(ct);
            var url = performerPathOrUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                      performerPathOrUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
                ? performerPathOrUrl
                : $"{baseUrl}/{performerPathOrUrl.TrimStart('/')}";

            var client = CreateConfiguredClient();
            await ThrottleAsync(delayMs, ct);
            var response = await client.GetAsync(url, ct);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Warashi detail request for '{Url}' returned HTTP status {StatusCode}", url, response.StatusCode);
                return null;
            }

            var html = await response.Content.ReadAsStringAsync(ct);
            return WarashiHtmlParser.ParsePerformerDetail(html, performerPathOrUrl, baseUrl);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Error occurred during Warashi performer detail fetch for '{Url}'", performerPathOrUrl);
            return null;
        }
    }

    public async Task<byte[]?> DownloadImageAsync(string url, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;

        try
        {
            var (baseUrl, delayMs) = await GetEffectiveSettingsAsync(ct);
            var fullUrl = url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                          url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
                ? url
                : $"{baseUrl}/{url.TrimStart('/')}";

            var client = CreateConfiguredClient();
            await ThrottleAsync(delayMs, ct);
            var response = await client.GetAsync(fullUrl, ct);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Warashi image download for '{Url}' returned HTTP status {StatusCode}", fullUrl, response.StatusCode);
                return null;
            }

            return await response.Content.ReadAsByteArrayAsync(ct);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Error occurred during Warashi image download for '{Url}'", url);
            return null;
        }
    }
}
