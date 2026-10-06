using System.Globalization;
using Javbuddy.Data;
using Javbuddy.Services.Infrastructure;
using Javbuddy.Services.Jellyfin;
using Microsoft.EntityFrameworkCore;

namespace Javbuddy.Services.Trickplay;

/// <summary>Finds the trickplay for a movie's player, whichever source has it: the
/// locally generated set for its main video file first, then — when the Jellyfin fallback setting
/// is on and the movie is matched in Jellyfin — Jellyfin's, else none (the player then shows the
/// stand-in scene timeline).</summary>
public interface ITrickplayService
{
    Task<TrickplayLayout?> GetAsync(int movieId, CancellationToken ct = default);

    /// <summary>One of the movie's local tile sheets, for the tile endpoint; null when the movie,
    /// identity or sheet doesn't exist. The caller disposes the result.</summary>
    Task<StoredObject?> OpenTileAsync(int movieId, string identity, int index, CancellationToken ct = default);
}

public sealed class TrickplayService(
    IDbContextFactory<AppDbContext> dbFactory,
    ITrickplayStore store,
    ITrickplaySettingsService settingsService,
    IJellyfinClient jellyfinClient) : ITrickplayService
{
    /// <summary>The browser's URL template for a local set's tile sheets.</summary>
    public static string TileUrlTemplate(int movieId, string identity) =>
        string.Create(CultureInfo.InvariantCulture, $"/trickplay/{movieId}/{identity}/{{index}}.webp");

    public async Task<TrickplayLayout?> GetAsync(int movieId, CancellationToken ct = default)
    {
        TrickplayMainFile? main;
        await using (var db = await dbFactory.CreateDbContextAsync(ct))
        {
            main = await TrickplayMainFiles.LoadAsync(db, movieId, ct);
        }
        if (main is null) return null;
        var (code, jellyfinItemId, identity) = (main.Code, main.JellyfinItemId, main.Identity);

        if (identity is not null && await store.GetSetAsync(code, identity, ct) is { } set)
        {
            return new TrickplayLayout(
                set.Width, set.Height, set.TileWidth, set.TileHeight, set.ThumbnailCount,
                set.IntervalMs, set.DurationSeconds, TileUrlTemplate(movieId, identity));
        }

        if (string.IsNullOrWhiteSpace(jellyfinItemId)) return null;
        var settings = await settingsService.GetEffectiveAsync(ct);
        return settings.JellyfinFallback ? await jellyfinClient.GetTrickplayAsync(jellyfinItemId, ct) : null;
    }

    public async Task<StoredObject?> OpenTileAsync(int movieId, string identity, int index, CancellationToken ct = default)
    {
        if (!TrickplayIdentity.IsValid(identity)) return null;

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var code = await db.Movies.AsNoTracking().Where(m => m.Id == movieId).Select(m => m.Code).FirstOrDefaultAsync(ct);
        return string.IsNullOrWhiteSpace(code) ? null : await store.OpenSheetAsync(code, identity, index, ct);
    }
}
