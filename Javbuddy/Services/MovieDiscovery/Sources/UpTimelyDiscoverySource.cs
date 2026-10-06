using System.Net.Http.Headers;
using Microsoft.Extensions.Logging.Abstractions;

namespace Javbuddy.Services.MovieDiscovery.Sources;

/// <summary>One <see cref="IStudioDiscoverySource"/> for any studio site built on the
/// up-timely.com site template/vendor. s1s1s1.com, moodyz.com, kawaiikawaii.jp, and
/// ideapocket.com all share this exact scraping shape (release/reserve listing pages, detail-page
/// gallery/actress enrichment, unhyphenated product codes) — verified against each live site
/// — so one class handles all of them, each registered in
/// Program.cs with its own domain/name/logo/studio. No age-gate cookie is needed on any of
/// them — verified live that both listing pages return the real page directly, with no prior
/// visit to an age-check interstitial required.</summary>
public class UpTimelyDiscoverySource(
    string baseUrl,
    string sourceName,
    StudioLogo? logo,
    string studio,
    IHttpClientFactory httpClientFactory,
    ILogger<UpTimelyDiscoverySource>? logger = null) : IStudioDiscoverySource
{
    private const string BrowserUserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36";
    private const int GalleryFetchConcurrency = 6;

    private readonly UpTimelyHtmlParser parser = new(baseUrl, studio);
    private readonly ILogger<UpTimelyDiscoverySource> logger = logger ?? NullLogger<UpTimelyDiscoverySource>.Instance;

    public string SourceName => sourceName;

    public StudioLogo? Logo => logo;

    public async Task<IReadOnlyList<DiscoveredMovieItem>> ScanAsync(CancellationToken ct = default)
    {
        var client = CreateConfiguredClient();
        var items = new List<DiscoveredMovieItem>();

        var releaseHtml = await FetchAsync(client, $"{baseUrl}/works/list/release", ct);
        if (releaseHtml is not null)
        {
            items.AddRange(parser.ParseReleaseListing(releaseHtml));
        }

        var reserveHtml = await FetchAsync(client, $"{baseUrl}/works/list/reserve", ct);
        if (reserveHtml is not null)
        {
            items.AddRange(parser.ParseReserveListing(reserveHtml));
        }

        var enrichedItems = new DiscoveredMovieItem[items.Count];
        await Parallel.ForEachAsync(
            Enumerable.Range(0, items.Count),
            new ParallelOptions { MaxDegreeOfParallelism = GalleryFetchConcurrency, CancellationToken = ct },
            async (index, token) =>
            {
                var item = items[index];
                var detailHtml = await FetchAsync(client, $"{baseUrl}/works/detail/{item.Code.Replace("-", string.Empty, StringComparison.Ordinal)}", token);
                enrichedItems[index] = detailHtml is null
                    ? item
                    : item with
                    {
                        GalleryImageUrls = parser.ParseGalleryImageUrls(detailHtml),
                        ActressNames = parser.ParseActressNames(detailHtml),
                    };
            });

        return enrichedItems;
    }

    private async Task<string?> FetchAsync(HttpClient client, string url, CancellationToken ct)
    {
        try
        {
            var response = await client.GetAsync(url, ct);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("{SourceName} discovery request to {Url} returned {StatusCode}", sourceName, url, response.StatusCode);
                return null;
            }
            return await response.Content.ReadAsStringAsync(ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "{SourceName} discovery request to {Url} failed", sourceName, url);
            return null;
        }
    }

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
        return client;
    }
}
