using Javbuddy.Data;
using Microsoft.EntityFrameworkCore;

namespace Javbuddy.Services.Infrastructure;

/// <summary>Shared plumbing for the external-integration clients (Javinizer, Jellyfin,
/// Prowlarr, QBittorrent): URL joining, base/external URL resolution, and the
/// CTS-timeout + "timed out"/"could not reach" error-mapping wrapper that used to be hand-copied
/// into every method of every client. Auth and per-endpoint request/response shape stay in the
/// derived client, since those genuinely differ per integration (Bearer token, API-key header,
/// API-key query param, cookie session).</summary>
public abstract class ApiClientBase<TSettings>(IHttpClientFactory httpClientFactory, IDbContextFactory<AppDbContext> dbFactory, IConfiguration configuration) where TSettings : class, IHasConnectionUrls
{
    protected IHttpClientFactory HttpClientFactory { get; } = httpClientFactory;
    protected IDbContextFactory<AppDbContext> DbFactory { get; } = dbFactory;
    protected IConfiguration Configuration { get; } = configuration;

    /// <summary>Display name used in generated error messages, e.g. "javinizer-go", "Jellyfin".</summary>
    protected abstract string ServiceName { get; }

    /// <summary>Named HttpClient to resolve (Program.cs registers "", "NoRedirect", "QBittorrent").
    /// Defaults to the unnamed client.</summary>
    protected virtual string HttpClientName => "";

    /// <summary>Resolves this integration's settings — env-var override or DB row, per
    /// <see cref="EffectiveSettingsResolver{TSettings}"/> where that pattern fits, or bespoke logic where
    /// it doesn't (e.g. QBittorrent's independently-overridable Category field).</summary>
    protected abstract Task<TSettings?> GetSettingsAsync(CancellationToken ct);

    public async Task<string?> GetBaseUrlAsync(CancellationToken ct = default)
    {
        var settings = await GetSettingsAsync(ct);
        return settings?.BaseUrl?.TrimEnd('/');
    }

    public async Task<string?> GetExternalUrlAsync(CancellationToken ct = default)
    {
        var settings = await GetSettingsAsync(ct);
        var url = !string.IsNullOrWhiteSpace(settings?.ExternalUrl) ? settings.ExternalUrl : settings?.BaseUrl;
        return url?.TrimEnd('/');
    }

    protected HttpClient CreateClient() => HttpClientFactory.CreateClient(HttpClientName);

    protected static string CombineUrl(string baseUrl, string path) => baseUrl.TrimEnd('/') + path;

    /// <summary>Runs <paramref name="action"/> against a fresh client under a CTS linked to
    /// <paramref name="ct"/> and canceled after <paramref name="timeout"/>. A timeout or
    /// unreachable-server failure is mapped to <paramref name="onFailure"/> with the wording
    /// every client already used ("Request to X timed out." / "Could not reach X: ...").</summary>
    protected async Task<T> ExecuteAsync<T>(
        TimeSpan timeout,
        CancellationToken ct,
        Func<HttpClient, CancellationToken, Task<T>> action,
        Func<string, T> onFailure)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);

        try
        {
            var client = CreateClient();
            return await action(client, cts.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return onFailure($"Request to {ServiceName} timed out.");
        }
        catch (HttpRequestException ex)
        {
            return onFailure($"Could not reach {ServiceName}: {ex.Message}");
        }
    }

    /// <summary>Reads a structured JSON error body for a non-success response, falling back to
    /// <see cref="HttpResponseMessage.ReasonPhrase"/> if the body isn't present/parseable or
    /// <paramref name="selectMessage"/> returns null. Cancellation is rethrown rather than treated
    /// as an unparseable body, so <see cref="ExecuteAsync{T}"/> still reports a timeout as a timeout
    /// and caller cancellation still propagates.</summary>
    protected static async Task<string> ReadJsonErrorAsync<TError>(HttpResponseMessage response, Func<TError, string?> selectMessage, CancellationToken ct)
    {
        try
        {
            var error = await response.Content.ReadFromJsonAsync<TError>(cancellationToken: ct);
            return (error is not null ? selectMessage(error) : null) ?? response.ReasonPhrase ?? "unknown error";
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return response.ReasonPhrase ?? "unknown error";
        }
    }
}
