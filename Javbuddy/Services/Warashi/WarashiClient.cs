using System.Net.Http.Headers;
using Javbuddy.Data;
using Javbuddy.Models;
using Javbuddy.Services.Common;
using Javbuddy.Services.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Javbuddy.Services.Warashi;

public class WarashiClient(
    IHttpClientFactory httpClientFactory,
    IDbContextFactory<AppDbContext> dbFactory,
    ILogger<WarashiClient>? logger = null)
    : ThrottledScrapeClientBase<WarashiSettings>(
        httpClientFactory, dbFactory, db => db.WarashiSettings, DefaultBaseUrl, DefaultRequestDelayMs,
        "Warashi Asian Pornstars Database", logger ?? NullLogger<WarashiClient>.Instance), IWarashiClient
{
    public const string DefaultBaseUrl = "https://warashi-asian-pornstars.fr";
    private const int DefaultRequestDelayMs = 750;

    protected override IReadOnlyList<StringWithQualityHeaderValue> AcceptLanguages { get; } =
    [
        new("en-US"),
        new("en", 0.9),
    ];

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
                Logger.LogWarning("Warashi search for '{Query}' returned HTTP status {StatusCode}", query, response.StatusCode);
                return Array.Empty<WarashiSearchResult>();
            }

            var html = await response.Content.ReadAsStringAsync(ct);
            return WarashiHtmlParser.ParseSearchResults(html, baseUrl);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            Logger.LogWarning(ex, "Error occurred during Warashi performer search for '{Query}'", query);
            return Array.Empty<WarashiSearchResult>();
        }
    }

    public async Task<WarashiPerformerDetail?> GetPerformerDetailAsync(string performerPathOrUrl, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(performerPathOrUrl)) return null;

        try
        {
            var (baseUrl, delayMs) = await GetEffectiveSettingsAsync(ct);
            var url = HtmlTextHelper.ResolveUrl(performerPathOrUrl, baseUrl)!;

            var client = CreateConfiguredClient();
            await ThrottleAsync(delayMs, ct);
            var response = await client.GetAsync(url, ct);
            if (!response.IsSuccessStatusCode)
            {
                Logger.LogWarning("Warashi detail request for '{Url}' returned HTTP status {StatusCode}", url, response.StatusCode);
                return null;
            }

            var html = await response.Content.ReadAsStringAsync(ct);
            return WarashiHtmlParser.ParsePerformerDetail(html, performerPathOrUrl, baseUrl);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            Logger.LogWarning(ex, "Error occurred during Warashi performer detail fetch for '{Url}'", performerPathOrUrl);
            return null;
        }
    }

    public async Task<byte[]?> DownloadImageAsync(string url, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;

        try
        {
            var (baseUrl, delayMs) = await GetEffectiveSettingsAsync(ct);
            var fullUrl = HtmlTextHelper.ResolveUrl(url, baseUrl)!;

            var client = CreateConfiguredClient();
            await ThrottleAsync(delayMs, ct);
            var response = await client.GetAsync(fullUrl, ct);
            if (!response.IsSuccessStatusCode)
            {
                Logger.LogWarning("Warashi image download for '{Url}' returned HTTP status {StatusCode}", fullUrl, response.StatusCode);
                return null;
            }

            return await BoundedHttpContent.ReadBytesAsync(response.Content, BoundedHttpContent.MaxImageBytes, ct);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            Logger.LogWarning(ex, "Error occurred during Warashi image download for '{Url}'", url);
            return null;
        }
    }
}
