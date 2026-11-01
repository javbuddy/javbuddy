using System.Text.Json;
using System.Text.Json.Serialization;
using Javbuddy.Data;
using Javbuddy.Models;
using Microsoft.EntityFrameworkCore;

namespace Javbuddy.Services.Infrastructure;

/// <summary>A cookie the solver's browser holds after solving a challenge.</summary>
public record SolverCookie(string Name, string Value, string? Domain, string? Path);

/// <summary>A page fetched through the solver. <see cref="FinalUrl"/> is where the browser ended up
/// after redirects (the plain client's <c>RequestMessage.RequestUri</c> equivalent).</summary>
public record SolvedResponse(string Html, int StatusCode, string FinalUrl, string? UserAgent, IReadOnlyList<SolverCookie> Cookies);

/// <summary>Either the solved page or why the solver could not deliver one.</summary>
public record SolverResult(SolvedResponse? Response, string? Error)
{
    public static SolverResult Ok(SolvedResponse response) => new(response, null);

    public static SolverResult Fail(string error) => new(null, error);
}

public record SolverTestResult(bool Success, string Message, string? Version = null);

public interface IChallengeSolverClient
{
    /// <summary>Whether a solver address is set (env var or Settings), i.e. whether sources can use one.</summary>
    Task<bool> IsConfiguredAsync(CancellationToken ct = default);

    /// <summary>GETs <paramref name="url"/> through the solver. <paramref name="sessionKey"/> keeps one
    /// browser (and its clearance cookies) alive across calls for the same key.</summary>
    Task<SolverResult> GetAsync(Uri url, string? sessionKey, CancellationToken ct = default);

    /// <summary>POSTs <paramref name="form"/> (form-urlencoded) to <paramref name="url"/> through the solver.</summary>
    Task<SolverResult> PostFormAsync(Uri url, IReadOnlyList<KeyValuePair<string, string>> form, string? sessionKey, CancellationToken ct = default);

    Task<SolverTestResult> TestConnectionAsync(CancellationToken ct = default);

    /// <summary>The solver's address, for linking to it from System &gt; Status.</summary>
    Task<string?> GetExternalUrlAsync(CancellationToken ct = default);
}

/// <summary>Reads the challenge solver address from environment variables / appsettings
/// ("FlareSolverr:BaseUrl", env form "FlareSolverr__BaseUrl"), which take precedence over the Settings page.</summary>
public static class FlareSolverrEnvConfig
{
    public static string? GetBaseUrl(IConfiguration configuration) => configuration["FlareSolverr:BaseUrl"];

    public static int? GetMaxTimeoutMs(IConfiguration configuration) =>
        int.TryParse(configuration["FlareSolverr:MaxTimeoutMs"], out var value) && value > 0 ? value : null;

    public static bool IsSet(IConfiguration configuration) => EnvConfig.AllSet(GetBaseUrl(configuration));
}

/// <summary>Speaks the FlareSolverr <c>/v1</c> protocol, which Trawl and other compatible services
/// also implement, so a user picks a solver by pointing the address at it.</summary>
public class FlareSolverrClient : ApiClientBase<FlareSolverrSettings>, IChallengeSolverClient
{
    private const string SessionPrefix = "javbuddy-";

    // The solver may take up to MaxTimeoutMs of its own; this is the margin on top for the HTTP round trip.
    private static readonly TimeSpan TimeoutMargin = TimeSpan.FromSeconds(15);

    private readonly EffectiveSettingsResolver<FlareSolverrSettings> settingsResolver;

    protected override string ServiceName => "the challenge solver";

    public FlareSolverrClient(IHttpClientFactory httpClientFactory, IDbContextFactory<AppDbContext> dbFactory, IConfiguration configuration)
        : base(httpClientFactory, dbFactory, configuration)
    {
        settingsResolver = new EffectiveSettingsResolver<FlareSolverrSettings>(
            dbFactory,
            configuration,
            config => new FlareSolverrSettings
            {
                BaseUrl = FlareSolverrEnvConfig.GetBaseUrl(config),
                MaxTimeoutMs = FlareSolverrEnvConfig.GetMaxTimeoutMs(config) ?? FlareSolverrSettings.DefaultMaxTimeoutMs
            },
            (db, ct) => db.FlareSolverrSettings.ReadSingleRowAsync(ct),
            s => !string.IsNullOrWhiteSpace(s.BaseUrl));
    }

    /// <summary>The effective settings: the env address wins over the saved row as a whole, but
    /// <c>FlareSolverr__MaxTimeoutMs</c> also overrides the timeout of a saved address on its own.</summary>
    protected override async Task<FlareSolverrSettings?> GetSettingsAsync(CancellationToken ct)
    {
        var settings = await settingsResolver.ResolveAsync(ct);
        return settings is not null && FlareSolverrEnvConfig.GetMaxTimeoutMs(Configuration) is { } timeout
            ? new FlareSolverrSettings { BaseUrl = settings.BaseUrl, MaxTimeoutMs = timeout }
            : settings;
    }

    public async Task<bool> IsConfiguredAsync(CancellationToken ct = default) => await GetSettingsAsync(ct) is not null;

    public Task<SolverResult> GetAsync(Uri url, string? sessionKey, CancellationToken ct = default) =>
        SolveAsync(new SolverRequest { Cmd = "request.get", Url = url.ToString() }, sessionKey, ct);

    public Task<SolverResult> PostFormAsync(Uri url, IReadOnlyList<KeyValuePair<string, string>> form, string? sessionKey, CancellationToken ct = default) =>
        SolveAsync(new SolverRequest
        {
            Cmd = "request.post",
            Url = url.ToString(),
            PostData = string.Join('&', form.Select(f => $"{Uri.EscapeDataString(f.Key)}={Uri.EscapeDataString(f.Value)}"))
        }, sessionKey, ct);

    private async Task<SolverResult> SolveAsync(SolverRequest request, string? sessionKey, CancellationToken ct)
    {
        var settings = await GetSettingsAsync(ct);
        if (settings is null)
        {
            return SolverResult.Fail("The challenge solver is not configured.");
        }

        request.MaxTimeout = settings.MaxTimeoutMs;
        // FlareSolverr and Trawl both create a session on first use of its name.
        request.Session = SessionName(sessionKey);

        return await ExecuteAsync(TimeSpan.FromMilliseconds(settings.MaxTimeoutMs) + TimeoutMargin, ct,
            async (client, token) =>
            {
                // The deadline is ExecuteAsync's token (MaxTimeoutMs + margin); the factory client's own
                // 100 s default would otherwise cut a longer solve short.
                client.Timeout = Timeout.InfiniteTimeSpan;
                using var response = await client.PostAsJsonAsync(CombineUrl(settings.BaseUrl!, "/v1"), request, token);
                var body = await ReadBodyAsync(response, token);
                if (!response.IsSuccessStatusCode)
                {
                    // Trawl answers a rejected request with a 400 whose JSON body says why.
                    var reason = string.IsNullOrWhiteSpace(body?.Message) ? "" : $": {body.Message}";
                    return SolverResult.Fail($"The challenge solver returned {(int)response.StatusCode} {response.ReasonPhrase}{reason}");
                }

                if (body is null) return SolverResult.Fail("The challenge solver returned an unreadable response.");
                if (!string.Equals(body.Status, "ok", StringComparison.OrdinalIgnoreCase) || body.Solution is null)
                {
                    return SolverResult.Fail(string.IsNullOrWhiteSpace(body.Message) ? "The challenge solver reported an error." : body.Message);
                }

                return SolverResult.Ok(MapSolution(body.Solution));
            },
            SolverResult.Fail);
    }

    /// <summary>The solver-side session name for a caller's key. Solvers accept only letters, digits,
    /// underscores and hyphens in a session ID (Trawl rejects anything else with a 400).</summary>
    public static string? SessionName(string? sessionKey)
    {
        if (string.IsNullOrWhiteSpace(sessionKey)) return null;
        return SessionPrefix + new string(sessionKey.Select(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-' ? c : '-').ToArray());
    }

    public async Task<SolverTestResult> TestConnectionAsync(CancellationToken ct = default)
    {
        var settings = await GetSettingsAsync(ct);
        if (settings is null)
        {
            return new SolverTestResult(false, "The challenge solver is not configured. Set its address in Settings.");
        }

        return await ExecuteAsync(TimeSpan.FromSeconds(15), ct,
            async (client, token) =>
            {
                using var response = await client.PostAsJsonAsync(CombineUrl(settings.BaseUrl!, "/v1"), new SolverRequest { Cmd = "sessions.list" }, token);
                if (!response.IsSuccessStatusCode)
                {
                    return new SolverTestResult(false, $"The challenge solver returned {(int)response.StatusCode} {response.ReasonPhrase}.");
                }

                var body = await ReadBodyAsync(response, token);
                return string.Equals(body?.Status, "ok", StringComparison.OrdinalIgnoreCase)
                    ? new SolverTestResult(true, "Connected to the challenge solver.", body!.Version)
                    : new SolverTestResult(false, "The address answered, but not like a FlareSolverr-compatible /v1 API.");
            },
            message => new SolverTestResult(false, message));
    }

    /// <summary>Maps a <c>solution</c> object onto <see cref="SolvedResponse"/>; the solver's
    /// <c>url</c> is the post-redirect address.</summary>
    internal static SolvedResponse MapSolution(SolverSolution solution) => new(
        solution.Response ?? "",
        solution.Status,
        solution.Url ?? "",
        solution.UserAgent,
        (solution.Cookies ?? []).Select(c => new SolverCookie(c.Name ?? "", c.Value ?? "", c.Domain, c.Path)).ToList());

    private static async Task<SolverResponseBody?> ReadBodyAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            return await response.Content.ReadFromJsonAsync<SolverResponseBody>(cancellationToken: ct);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    internal sealed class SolverRequest
    {
        [JsonPropertyName("cmd")] public string Cmd { get; set; } = "";
        [JsonPropertyName("url"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Url { get; set; }
        [JsonPropertyName("postData"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? PostData { get; set; }
        [JsonPropertyName("session"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Session { get; set; }
        [JsonPropertyName("maxTimeout"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public int? MaxTimeout { get; set; }
    }

    internal sealed class SolverResponseBody
    {
        [JsonPropertyName("status")] public string? Status { get; set; }
        [JsonPropertyName("message")] public string? Message { get; set; }
        [JsonPropertyName("version")] public string? Version { get; set; }
        [JsonPropertyName("solution")] public SolverSolution? Solution { get; set; }
    }

    internal sealed class SolverSolution
    {
        [JsonPropertyName("url")] public string? Url { get; set; }
        [JsonPropertyName("status")] public int Status { get; set; }
        [JsonPropertyName("response")] public string? Response { get; set; }
        [JsonPropertyName("userAgent")] public string? UserAgent { get; set; }
        [JsonPropertyName("cookies")] public List<SolverCookieDto>? Cookies { get; set; }
    }

    internal sealed class SolverCookieDto
    {
        [JsonPropertyName("name")] public string? Name { get; set; }
        [JsonPropertyName("value")] public string? Value { get; set; }
        [JsonPropertyName("domain")] public string? Domain { get; set; }
        [JsonPropertyName("path")] public string? Path { get; set; }
    }
}
