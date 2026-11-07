using System.Net.Http.Headers;
using Javbuddy.Data;
using Javbuddy.Models;
using Microsoft.EntityFrameworkCore;

namespace Javbuddy.Services.Infrastructure;

/// <summary>Shared plumbing for the HTML-scraping actor-metadata clients (minnano-av.com):
/// effective settings from the single settings row, a per-instance request throttle so batch
/// enrichment doesn't hammer the site, a browser-like configured <see cref="HttpClient"/>, and the
/// connection test that is identical across sources.</summary>
public abstract class ThrottledScrapeClientBase<TSettings>(
    IHttpClientFactory httpClientFactory,
    IDbContextFactory<AppDbContext> dbFactory,
    Func<AppDbContext, DbSet<TSettings>> selectSettings,
    string defaultBaseUrl,
    int defaultRequestDelayMs,
    string sourceName,
    string configSection,
    ILogger logger,
    IChallengeSolverClient? challengeSolver = null,
    IConfiguration? configuration = null) where TSettings : class, IEnrichmentSourceSettings
{
    private readonly SemaphoreSlim throttleLock = new(1, 1);
    private DateTime? lastRequestAtUtc;

    /// <summary>Human-readable site name used in log messages.</summary>
    protected string SourceName { get; } = sourceName;

    protected ILogger Logger { get; } = logger;

    /// <summary>The Accept-Language values the site is requested with, in preference order.</summary>
    protected abstract IReadOnlyList<StringWithQualityHeaderValue> AcceptLanguages { get; }

    protected async Task<(string BaseUrl, int RequestDelayMs, bool UseChallengeSolver)> GetEffectiveSettingsAsync(CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var settings = await selectSettings(db).ReadSingleRowAsync(ct);
        var baseUrl = !string.IsNullOrWhiteSpace(settings?.BaseUrl)
            ? settings.BaseUrl.TrimEnd('/')
            : defaultBaseUrl;
        return (baseUrl, settings?.RequestDelayMs ?? defaultRequestDelayMs, // "<Section>__UseChallengeSolver" overrides the saved checkbox; unset, a source uses the
        // solver (when one is configured) unless its saved setting says otherwise.
        ScrapeSourceEnvConfig.GetUseChallengeSolver(configuration, configSection) ?? settings?.UseChallengeSolver ?? true);
    }

    /// <summary>Waits out the configured throttling delay since the previous request made through
    /// this client instance, so consecutive calls (e.g. during batch enrichment) don't hammer the site.</summary>
    protected async Task ThrottleAsync(int delayMs, CancellationToken ct)
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

    protected HttpClient CreateConfiguredClient()
    {
        var client = httpClientFactory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(15);
        client.DefaultRequestHeaders.UserAgent.Clear();
        client.DefaultRequestHeaders.UserAgent.ParseAdd(ScraperDefaults.BrowserUserAgent);
        client.DefaultRequestHeaders.Accept.Clear();
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("text/html"));
        client.DefaultRequestHeaders.AcceptLanguage.Clear();
        foreach (var language in AcceptLanguages)
        {
            client.DefaultRequestHeaders.AcceptLanguage.Add(language);
        }
        return client;
    }

    /// <summary>One page fetched by <see cref="FetchHtmlAsync"/>: <see cref="Html"/> is null when the
    /// site answered with a non-success status (<see cref="StatusCode"/>); <see cref="FinalUrl"/> is the
    /// address after redirects, whichever path fetched it.</summary>
    protected sealed record FetchedPage(string? Html, string FinalUrl, int StatusCode);

    /// <summary>GETs (or, with <paramref name="form"/>, POSTs) a page behind the request throttle. A plain
    /// request goes first; only when the response is a bot challenge, a challenge solver is configured and
    /// this source has not switched it off is the same request repeated through it. A challenge that cannot be passed
    /// throws <see cref="ScrapeBlockedException"/> rather than looking like "no results".</summary>
    protected async Task<FetchedPage> FetchHtmlAsync(
        string url,
        int delayMs,
        bool useChallengeSolver,
        IReadOnlyList<KeyValuePair<string, string>>? form,
        CancellationToken ct)
    {
        await ThrottleAsync(delayMs, ct);

        var client = CreateConfiguredClient();
        using var response = form is null
            ? await client.GetAsync(url, ct)
            : await client.PostAsync(url, new FormUrlEncodedContent(form), ct);
        var body = await response.Content.ReadAsStringAsync(ct);

        if (!ScrapeChallenge.IsChallenge(response, body))
        {
            return new FetchedPage(response.IsSuccessStatusCode ? body : null, response.RequestMessage?.RequestUri?.ToString() ?? url, (int)response.StatusCode);
        }

        if (challengeSolver is null || !await challengeSolver.IsConfiguredAsync(ct))
        {
            throw new ScrapeBlockedException($"{SourceName} is protected by a bot challenge (Cloudflare). Configure a challenge solver (FlareSolverr or Trawl) under Settings → Connections.");
        }

        if (!useChallengeSolver)
        {
            throw new ScrapeBlockedException($"{SourceName} is protected by a bot challenge (Cloudflare), and the challenge solver is switched off for this source. Turn it on under Settings → Metadata.");
        }

        var uri = new Uri(url);

        Task<SolverResult> SolveAsync() => form is null
            ? challengeSolver.GetAsync(uri, SourceName, ct)
            : challengeSolver.PostFormAsync(uri, form, SourceName, ct);

        var solved = await SolveAsync();
        if (solved.Response is { } first && IsStillChallenge(first))
        {
            // A solver's first attempt at a site can come back still challenged (a cold browser or a fresh
            // session); the next one goes through with the clearance it just earned, so try once more.
            Logger.LogInformation("{Source}: the challenge solver returned a challenge page; retrying once", SourceName);
            solved = await SolveAsync();
        }

        if (solved.Response is not { } page)
        {
            throw new ScrapeBlockedException($"{SourceName} is protected by a bot challenge (Cloudflare) and the challenge solver failed: {solved.Error}");
        }

        if (IsStillChallenge(page))
        {
            throw new ScrapeBlockedException($"{SourceName} is protected by a bot challenge (Cloudflare) that the challenge solver could not pass.");
        }

        var success = page.StatusCode is >= 200 and < 300;
        return new FetchedPage(success ? page.Html : null, string.IsNullOrEmpty(page.FinalUrl) ? url : page.FinalUrl, page.StatusCode);
    }

    private static bool IsStillChallenge(SolvedResponse page) =>
        ScrapeChallenge.IsChallenge((System.Net.HttpStatusCode)page.StatusCode, false, false, page.Html);

    public async Task<bool> TestConnectionAsync(CancellationToken ct = default)
    {
        try
        {
            var (baseUrl, _, useChallengeSolver) = await GetEffectiveSettingsAsync(ct);
            var page = await FetchHtmlAsync(baseUrl, 0, useChallengeSolver, null, ct);
            return page.Html is not null;
        }
        catch (HttpRequestException ex) when (!ct.IsCancellationRequested)
        {
            Logger.LogWarning("Failed to connect to {Source}: {Message}", SourceName, ex.Message);
            return false;
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            Logger.LogWarning(ex, "Failed to connect to {Source}", SourceName);
            return false;
        }
    }
}
