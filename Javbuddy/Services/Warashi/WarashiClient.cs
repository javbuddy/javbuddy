using System.Net.Http.Headers;
using Javbuddy.Data;
using Javbuddy.Models;
using Javbuddy.Services.Common;
using Javbuddy.Services.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Javbuddy.Services.Warashi;

public interface IWarashiClient
{
    Task<IReadOnlyList<WarashiSearchResult>> SearchPerformersAsync(string query, CancellationToken ct = default);

    Task<WarashiPerformerDetail?> GetPerformerDetailAsync(string performerPathOrUrl, CancellationToken ct = default);

    Task<byte[]?> DownloadImageAsync(string url, CancellationToken ct = default);

    Task<bool> TestConnectionAsync(CancellationToken ct = default);
}

public class WarashiClient(
    IHttpClientFactory httpClientFactory,
    IDbContextFactory<AppDbContext> dbFactory,
    ILogger<WarashiClient>? logger = null,
    IChallengeSolverClient? challengeSolver = null,
    IConfiguration? configuration = null)
    : ThrottledScrapeClientBase<WarashiSettings>(
        httpClientFactory, dbFactory, db => db.WarashiSettings, DefaultBaseUrl, DefaultRequestDelayMs,
        "Warashi Asian Pornstars Database", "Warashi", logger ?? NullLogger<WarashiClient>.Instance, challengeSolver, configuration), IWarashiClient
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
            var (baseUrl, delayMs, useSolver) = await GetEffectiveSettingsAsync(ct);
            var searchUrl = $"{baseUrl}/en/s-12/search";

            var form = new[]
            {
                new KeyValuePair<string, string>("recherche_critere", "f"),
                new KeyValuePair<string, string>("recherche_valeur", query.Trim())
            };

            var page = await FetchHtmlAsync(searchUrl, delayMs, useSolver, form, ct);
            if (page.Html is not { } html)
            {
                Logger.LogWarning("Warashi search for '{Query}' returned HTTP status {StatusCode}", query, page.StatusCode);
                return Array.Empty<WarashiSearchResult>();
            }

            return WarashiHtmlParser.ParseSearchResults(html, baseUrl);
        }
        catch (ScrapeBlockedException)
        {
            throw;
        }
        catch (HttpRequestException ex) when (!ct.IsCancellationRequested)
        {
            Logger.LogWarning("Warashi performer search for '{Query}' failed: {Message}", query, ex.Message);
            throw;
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
            var (baseUrl, delayMs, useSolver) = await GetEffectiveSettingsAsync(ct);
            var url = HtmlTextHelper.ResolveUrl(performerPathOrUrl, baseUrl)!;

            var page = await FetchHtmlAsync(url, delayMs, useSolver, null, ct);
            if (page.Html is not { } html)
            {
                Logger.LogWarning("Warashi detail request for '{Url}' returned HTTP status {StatusCode}", url, page.StatusCode);
                return null;
            }

            return WarashiHtmlParser.ParsePerformerDetail(html, performerPathOrUrl, baseUrl);
        }
        catch (ScrapeBlockedException)
        {
            throw;
        }
        catch (HttpRequestException ex) when (!ct.IsCancellationRequested)
        {
            Logger.LogWarning("Warashi performer detail fetch for '{Url}' failed: {Message}", performerPathOrUrl, ex.Message);
            throw;
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
            var (baseUrl, delayMs, _) = await GetEffectiveSettingsAsync(ct);
            var fullUrl = HtmlTextHelper.ResolveUrl(url, baseUrl)!;

            var client = CreateConfiguredClient();
            await ThrottleAsync(delayMs, ct);
            // Headers only, so the body is streamed through the size cap instead of buffered whole first.
            using var response = await client.GetAsync(fullUrl, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!response.IsSuccessStatusCode)
            {
                Logger.LogWarning("Warashi image download for '{Url}' returned HTTP status {StatusCode}", fullUrl, response.StatusCode);
                return null;
            }

            return await BoundedHttpContent.ReadBytesAsync(response.Content, BoundedHttpContent.MaxImageBytes, ct);
        }
        catch (HttpRequestException ex) when (!ct.IsCancellationRequested)
        {
            Logger.LogWarning("Warashi image download for '{Url}' failed: {Message}", url, ex.Message);
            return null;
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            Logger.LogWarning(ex, "Error occurred during Warashi image download for '{Url}'", url);
            return null;
        }
    }
}
