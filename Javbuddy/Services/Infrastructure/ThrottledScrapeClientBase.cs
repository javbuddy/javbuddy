using System.Net.Http.Headers;
using Javbuddy.Data;
using Javbuddy.Models;
using Microsoft.EntityFrameworkCore;

namespace Javbuddy.Services.Infrastructure;

/// <summary>Shared plumbing for the HTML-scraping actor-metadata clients (Warashi, minnano-av.com):
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
    ILogger logger) where TSettings : class, IEnrichmentSourceSettings
{
    private readonly SemaphoreSlim throttleLock = new(1, 1);
    private DateTime? lastRequestAtUtc;

    /// <summary>Human-readable site name used in log messages.</summary>
    protected string SourceName { get; } = sourceName;

    protected ILogger Logger { get; } = logger;

    /// <summary>The Accept-Language values the site is requested with, in preference order.</summary>
    protected abstract IReadOnlyList<StringWithQualityHeaderValue> AcceptLanguages { get; }

    protected async Task<(string BaseUrl, int RequestDelayMs)> GetEffectiveSettingsAsync(CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var settings = await selectSettings(db).ReadSingleRowAsync(ct);
        var baseUrl = !string.IsNullOrWhiteSpace(settings?.BaseUrl)
            ? settings.BaseUrl.TrimEnd('/')
            : defaultBaseUrl;
        return (baseUrl, settings?.RequestDelayMs ?? defaultRequestDelayMs);
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
            Logger.LogWarning(ex, "Failed to connect to {Source}", SourceName);
            return false;
        }
    }
}
