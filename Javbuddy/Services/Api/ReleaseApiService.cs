using Javbuddy.Services.Api.Contracts;
using Javbuddy.Services.Prowlarr;
using Javbuddy.Services.Settings;
using Javbuddy.Services.Torrents;

namespace Javbuddy.Services.Api;

/// <summary>Prowlarr search and grab for the public API (REST). Works for codes that aren't in the library yet; a grab then
/// adds the movie first, as the Prowlarr search modal's "Add &amp; Download" does.</summary>
public interface IReleaseApiService
{
    /// <summary>Searches Prowlarr for <paramref name="query"/> (the code when null) and remembers the results for a grab.</summary>
    Task<ApiResult<ReleaseSearchResponse>> SearchAsync(string code, string? query, CancellationToken ct = default);

    /// <summary>Sends a release from a recent search of <paramref name="code"/> to qBittorrent. Gone when the guid is unknown or expired.</summary>
    Task<ApiResult<ReleaseGrabResponse>> GrabAsync(string code, string guid, CancellationToken ct = default);
}

public sealed class ReleaseApiService(
    IProwlarrClient prowlarr,
    ITorrentGrabService grabService,
    ILibraryApiService library,
    IConnectionStatusService connectionStatus,
    ReleaseSearchCache cache) : IReleaseApiService
{
    public async Task<ApiResult<ReleaseSearchResponse>> SearchAsync(string code, string? query, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return ApiResult<ReleaseSearchResponse>.Invalid("Provide a movie code.");
        }
        if (!await IsConfiguredAsync("prowlarr", ct))
        {
            return ApiResult<ReleaseSearchResponse>.NotConfigured("Prowlarr is not configured. Set a base URL and API key in Settings.");
        }

        var disabledTask = prowlarr.GetDisabledIndexersAsync(ct);
        var result = await prowlarr.SearchAsync(string.IsNullOrWhiteSpace(query) ? code.Trim() : query.Trim(), ct);
        var disabled = await disabledTask;
        if (!result.Success || result.Releases is null)
        {
            return ApiResult<ReleaseSearchResponse>.Upstream(result.ErrorMessage ?? "The Prowlarr search failed.");
        }

        // A release without a guid can't be named in a grab, so it isn't offered.
        var releases = result.Releases
            .Where(r => !string.IsNullOrWhiteSpace(r.Guid))
            .OrderByDescending(r => r.Seeders ?? 0)
            .ToList();
        cache.Store(code, releases);

        return ApiResult<ReleaseSearchResponse>.Ok(new ReleaseSearchResponse(
            releases.Select(ToDto).ToList(),
            disabled.Select(i => $"Indexer {i.Name} is {i.Reason} and was not searched.").ToList()));
    }

    public async Task<ApiResult<ReleaseGrabResponse>> GrabAsync(string code, string guid, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(guid))
        {
            return ApiResult<ReleaseGrabResponse>.Invalid("Provide a movie code and a release guid.");
        }

        var release = cache.TryGet(code, guid.Trim());
        if (release is null)
        {
            return ApiResult<ReleaseGrabResponse>.Gone("That release is unknown or its search results have expired. Search for releases again, then grab.");
        }
        if (!await IsConfiguredAsync("qbittorrent", ct))
        {
            return ApiResult<ReleaseGrabResponse>.NotConfigured("qBittorrent is not configured. Set its connection in Settings.");
        }

        var added = false;
        var movie = await library.ResolveAsync(code, ct);
        if (movie is null)
        {
            var add = await library.AddAsync(code, ct);
            if (!add.IsOk && add.Status != ApiStatus.Conflict)
            {
                return add.AsFailure<ReleaseGrabResponse>();
            }

            // On a Conflict somebody added it meanwhile; either way it is in the library now.
            added = add.IsOk;
            movie = await library.ResolveAsync(code, ct)
                ?? throw new InvalidOperationException($"Movie {code} disappeared after being added.");
        }

        var grabbed = await grabService.GrabAsync(movie, release, ct);
        if (!grabbed.Success)
        {
            var message = grabbed.ErrorMessage ?? "qBittorrent didn't accept the release.";
            return ApiResult<ReleaseGrabResponse>.Upstream(added ? $"{message} The movie was added to the library." : message);
        }

        return ApiResult<ReleaseGrabResponse>.Ok(new ReleaseGrabResponse(MovieApiMapper.ToSummary(movie), added, release.Title));
    }

    private async Task<bool> IsConfiguredAsync(string key, CancellationToken ct) =>
        (await connectionStatus.GetStatusesAsync(ct)).Any(s => s.Key == key && s.Configured);

    private static ReleaseDto ToDto(ReleaseResourceDto r) => new(
        r.Guid!, r.Title, r.Indexer, r.Size, r.Seeders, r.Leechers, r.Grabs, r.Age,
        r.PublishDate == default ? null : r.PublishDate, r.Protocol);
}
