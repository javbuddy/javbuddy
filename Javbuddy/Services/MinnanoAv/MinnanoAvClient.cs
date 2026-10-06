using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using Javbuddy.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Javbuddy.Services.MinnanoAv;

public partial class MinnanoAvClient(
    IHttpClientFactory httpClientFactory,
    IDbContextFactory<AppDbContext> dbFactory,
    ILogger<MinnanoAvClient>? logger = null) : IMinnanoAvClient
{
    public const string DefaultBaseUrl = "https://www.minnano-av.com";
    private const int DefaultRequestDelayMs = 750;

    private readonly IHttpClientFactory httpClientFactory = httpClientFactory;
    private readonly IDbContextFactory<AppDbContext> dbFactory = dbFactory;
    private readonly ILogger<MinnanoAvClient> logger = logger ?? NullLogger<MinnanoAvClient>.Instance;
    private readonly SemaphoreSlim throttleLock = new(1, 1);
    private DateTime? lastRequestAtUtc;

    [GeneratedRegex(@"/actress\d+\.html$", RegexOptions.IgnoreCase)]
    private static partial Regex DetailPagePathRegex();

    private async Task<(string BaseUrl, int RequestDelayMs)> GetEffectiveSettingsAsync(CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var settings = await db.MinnanoAvSettings.ReadSingleRowAsync(ct);
        var baseUrl = !string.IsNullOrWhiteSpace(settings?.BaseUrl)
            ? settings.BaseUrl.TrimEnd('/')
            : DefaultBaseUrl;
        var delayMs = settings?.RequestDelayMs ?? DefaultRequestDelayMs;
        return (baseUrl, delayMs);
    }

    /// <summary>Waits out the configured throttling delay since the previous request made through
    /// this client instance, so consecutive calls (e.g. during batch enrichment) don't hammer minnano-av.com.</summary>
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
        client.DefaultRequestHeaders.AcceptLanguage.Add(new StringWithQualityHeaderValue("ja"));
        client.DefaultRequestHeaders.AcceptLanguage.Add(new StringWithQualityHeaderValue("en-US", 0.9));
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
            logger.LogWarning(ex, "Failed to connect to minnano-av.com");
            return false;
        }
    }

    public async Task<IReadOnlyList<MinnanoAvSearchResult>> SearchPerformersAsync(string query, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query)) return Array.Empty<MinnanoAvSearchResult>();

        try
        {
            var (baseUrl, delayMs) = await GetEffectiveSettingsAsync(ct);
            var searchUrl = $"{baseUrl}/search_result.php?search_scope=actress&search_word={Uri.EscapeDataString(query.Trim())}";
            var client = CreateConfiguredClient();

            await ThrottleAsync(delayMs, ct);
            var response = await client.GetAsync(searchUrl, ct);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("minnano-av.com search for '{Query}' returned HTTP status {StatusCode}", query, response.StatusCode);
                return Array.Empty<MinnanoAvSearchResult>();
            }

            var html = await response.Content.ReadAsStringAsync(ct);
            var finalAbsolutePath = response.RequestMessage?.RequestUri?.AbsolutePath ?? string.Empty;

            // A single exact match redirects straight from search_result.php to the actress's own
            // detail page — the response we already have IS the detail page, so parse it as one
            // pre-loaded, exact-match candidate rather than issuing a second request.
            if (DetailPagePathRegex().IsMatch(finalAbsolutePath))
            {
                var finalPath = finalAbsolutePath.TrimStart('/');
                var detail = MinnanoAvHtmlParser.ParsePerformerDetail(html, finalPath, baseUrl);
                if (detail is null) return Array.Empty<MinnanoAvSearchResult>();

                return new[]
                {
                    new MinnanoAvSearchResult(
                        Name: detail.Name,
                        Kana: detail.Kana,
                        Romaji: detail.Romaji,
                        PathOrUrl: finalPath,
                        ImageUrl: MinnanoAvHtmlParser.ParseOgImage(html, baseUrl),
                        DebutInfo: null,
                        IsExactMatch: true,
                        KnownAliases: detail.Aliases)
                };
            }

            return MinnanoAvHtmlParser.ParseSearchResults(html, baseUrl);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Error occurred during minnano-av.com performer search for '{Query}'", query);
            return Array.Empty<MinnanoAvSearchResult>();
        }
    }

    public async Task<MinnanoAvPerformerDetail?> GetPerformerDetailAsync(string performerPathOrUrl, CancellationToken ct = default)
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
                logger.LogWarning("minnano-av.com detail request for '{Url}' returned HTTP status {StatusCode}", url, response.StatusCode);
                return null;
            }

            var html = await response.Content.ReadAsStringAsync(ct);
            return MinnanoAvHtmlParser.ParsePerformerDetail(html, performerPathOrUrl, baseUrl);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Error occurred during minnano-av.com performer detail fetch for '{Url}'", performerPathOrUrl);
            return null;
        }
    }
}
