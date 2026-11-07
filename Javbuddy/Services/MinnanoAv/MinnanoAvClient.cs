using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using Javbuddy.Data;
using Javbuddy.Models;
using Javbuddy.Services.Common;
using Javbuddy.Services.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Javbuddy.Services.MinnanoAv;

public interface IMinnanoAvClient
{
    Task<IReadOnlyList<MinnanoAvSearchResult>> SearchPerformersAsync(string query, CancellationToken ct = default);

    Task<MinnanoAvPerformerDetail?> GetPerformerDetailAsync(string performerPathOrUrl, CancellationToken ct = default);

    Task<bool> TestConnectionAsync(CancellationToken ct = default);
}

public partial class MinnanoAvClient(
    IHttpClientFactory httpClientFactory,
    IDbContextFactory<AppDbContext> dbFactory,
    ILogger<MinnanoAvClient>? logger = null,
    IChallengeSolverClient? challengeSolver = null,
    IConfiguration? configuration = null)
    : ThrottledScrapeClientBase<MinnanoAvSettings>(
        httpClientFactory, dbFactory, db => db.MinnanoAvSettings, DefaultBaseUrl, DefaultRequestDelayMs,
        "minnano-av.com", "MinnanoAv", logger ?? NullLogger<MinnanoAvClient>.Instance, challengeSolver, configuration), IMinnanoAvClient
{
    public const string DefaultBaseUrl = "https://www.minnano-av.com";
    private const int DefaultRequestDelayMs = 750;

    protected override IReadOnlyList<StringWithQualityHeaderValue> AcceptLanguages { get; } =
    [
        new("ja"),
        new("en-US", 0.9),
    ];

    [GeneratedRegex(@"/actress\d+\.html$", RegexOptions.IgnoreCase)]
    private static partial Regex DetailPagePathRegex();

    public async Task<IReadOnlyList<MinnanoAvSearchResult>> SearchPerformersAsync(string query, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query)) return Array.Empty<MinnanoAvSearchResult>();

        try
        {
            var (baseUrl, delayMs, useSolver) = await GetEffectiveSettingsAsync(ct);
            var searchUrl = $"{baseUrl}/search_result.php?search_scope=actress&search_word={Uri.EscapeDataString(query.Trim())}";

            var page = await FetchHtmlAsync(searchUrl, delayMs, useSolver, null, ct);
            if (page.Html is not { } html)
            {
                Logger.LogWarning("minnano-av.com search for '{Query}' returned HTTP status {StatusCode}", query, page.StatusCode);
                return Array.Empty<MinnanoAvSearchResult>();
            }

            var finalAbsolutePath = Uri.TryCreate(page.FinalUrl, UriKind.Absolute, out var finalUri) ? finalUri.AbsolutePath : string.Empty;

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
        catch (ScrapeBlockedException)
        {
            throw;
        }
        catch (HttpRequestException ex) when (!ct.IsCancellationRequested)
        {
            Logger.LogWarning("minnano-av.com performer search for '{Query}' failed: {Message}", query, ex.Message);
            throw;
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            Logger.LogWarning(ex, "Error occurred during minnano-av.com performer search for '{Query}'", query);
            return Array.Empty<MinnanoAvSearchResult>();
        }
    }

    public async Task<MinnanoAvPerformerDetail?> GetPerformerDetailAsync(string performerPathOrUrl, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(performerPathOrUrl)) return null;

        try
        {
            var (baseUrl, delayMs, useSolver) = await GetEffectiveSettingsAsync(ct);
            var url = HtmlTextHelper.ResolveUrl(performerPathOrUrl, baseUrl)!;

            var page = await FetchHtmlAsync(url, delayMs, useSolver, null, ct);
            if (page.Html is not { } html)
            {
                Logger.LogWarning("minnano-av.com detail request for '{Url}' returned HTTP status {StatusCode}", url, page.StatusCode);
                return null;
            }

            return MinnanoAvHtmlParser.ParsePerformerDetail(html, performerPathOrUrl, baseUrl);
        }
        catch (ScrapeBlockedException)
        {
            throw;
        }
        catch (HttpRequestException ex) when (!ct.IsCancellationRequested)
        {
            Logger.LogWarning("minnano-av.com performer detail fetch for '{Url}' failed: {Message}", performerPathOrUrl, ex.Message);
            throw;
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            Logger.LogWarning(ex, "Error occurred during minnano-av.com performer detail fetch for '{Url}'", performerPathOrUrl);
            return null;
        }
    }
}
