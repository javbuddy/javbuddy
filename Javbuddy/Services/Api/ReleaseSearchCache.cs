using Javbuddy.Services.Movies;
using Javbuddy.Services.Prowlarr;
using Microsoft.Extensions.Caching.Memory;

namespace Javbuddy.Services.Api;

/// <summary>
/// Remembers the releases of recent API searches, per (movie code, guid), for a grab to look up. This is what stops a client
/// from passing an arbitrary URL to qBittorrent: a grab names only a guid, and the download URL comes from here. Entries
/// expire 15 minutes after the search (checked against <see cref="TimeProvider"/> as well as evicted by the memory cache).
/// </summary>
public sealed class ReleaseSearchCache(IMemoryCache cache, TimeProvider timeProvider)
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(15);

    private sealed record Entry(ReleaseResourceDto Release, DateTimeOffset ExpiresAt);

    /// <summary>The key for a code: zero-padding and case tolerant, so a search for "abp123" is found by a grab for "ABP-123".</summary>
    public static string Normalize(string code) =>
        CodeNormalization.GetZeroPaddingToleranceKey(code) ?? CodeNormalization.Normalize(code);

    public void Store(string code, IEnumerable<ReleaseResourceDto> releases)
    {
        var expiresAt = timeProvider.GetUtcNow().Add(Lifetime);
        foreach (var release in releases.Where(r => !string.IsNullOrWhiteSpace(r.Guid)))
        {
            cache.Set(Key(code, release.Guid!), new Entry(release, expiresAt), Lifetime);
        }
    }

    public ReleaseResourceDto? TryGet(string code, string guid) =>
        cache.TryGetValue(Key(code, guid), out Entry? entry) && entry is not null && entry.ExpiresAt > timeProvider.GetUtcNow()
            ? entry.Release
            : null;

    private static string Key(string code, string guid) => $"api-release|{Normalize(code)}|{guid}";
}
