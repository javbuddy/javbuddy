using Javbuddy.Data;
using Javbuddy.Services.Images;
using Javbuddy.Services.LocalLibrary;
using Microsoft.EntityFrameworkCore;

namespace Javbuddy.Services.Movies;

public interface INewMovieArtworkService
{
    /// <summary>For each movie marked <c>NewArtworkPending</c> whose video the MediaInfo probe has finished (it has file rows
    /// and a duration), generates what the "new movie artwork" settings ask for and the movie lacks: extrafanart stills,
    /// and poster and fanart images, all from its own video. Never replaces or adds to existing images, and leaves a movie
    /// whose metadata already has a cover URL without a generated poster or fanart. The marker is cleared once a movie was
    /// attempted (a generator failure is logged, not retried), or when both settings are off. It is kept when the work is
    /// cancelled, when no library root is reachable and for a movie whose folder can't be found, so those retry on a
    /// later pass. Returns how many movies got at least one new image.</summary>
    Task<int> GeneratePendingAsync(CancellationToken ct = default);
}

public class NewMovieArtworkService(
    IDbContextFactory<AppDbContext> dbFactory,
    INewMovieArtworkSettingsService settingsService,
    ILocalLibraryClient localLibraryClient,
    IMovieExtraFanartStillsService stillsService,
    IMoviePosterGenerationService posterGeneration,
    ILogger<NewMovieArtworkService> logger) : INewMovieArtworkService
{
    public async Task<int> GeneratePendingAsync(CancellationToken ct = default)
    {
        List<PendingMovie> ids;
        await using (var db = await dbFactory.CreateDbContextAsync(ct))
        {
            // File rows appear only once the probe step ran, and with them the VR format the frames are cropped by.
            ids = await db.Movies
                .Where(m => m.NewArtworkPending && m.MediaDurationSeconds > 0 && m.Code != null && m.MovieFiles.Any())
                .OrderBy(m => m.Id)
                .Select(m => new PendingMovie(m.Id, m.Code!))
                .ToListAsync(ct);
        }
        if (ids.Count == 0) return 0;

        // An unmounted share would otherwise fail every movie at once and clear all their markers.
        if (!await localLibraryClient.AnyRootReachableAsync(ct))
        {
            logger.LogWarning("No library root is reachable, so artwork for {Count} new movie(s) waits for a later pass", ids.Count);
            return 0;
        }

        var generated = 0;
        foreach (var (movieId, code) in ids)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                // Read per movie, so switching a toggle off stops the backlog at once.
                var settings = await settingsService.GetEffectiveAsync(ct);
                if (settings.GenerateExtraFanart || settings.GeneratePosterAndFanart)
                {
                    if (await localLibraryClient.ResolveMovieFolderPathAsync(code, ct) is null)
                    {
                        logger.LogWarning("No folder found for new movie {Code}; its artwork waits for a later pass", code);
                        continue;
                    }
                }

                var any = false;
                if (settings.GeneratePosterAndFanart)
                {
                    var poster = await posterGeneration.GenerateAsync(movieId, respectRemoteArtwork: true, ct);
                    // "Already has them" is routine; anything else (no folder, ffmpeg trouble) is worth a trace.
                    if (!poster.Success) logger.LogDebug("No poster/fanart generated for movie {MovieId}: {Reason}", movieId, poster.ErrorMessage);
                    any |= poster.Success;
                }
                if (settings.GenerateExtraFanart)
                {
                    any |= await GenerateStillsAsync(movieId, ct);
                }
                if (any) generated++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Generating artwork for new movie {MovieId} failed", movieId);
            }

            await ClearPendingAsync(movieId, ct);
        }
        return generated;
    }

    private sealed record PendingMovie(int Id, string Code);

    private async Task ClearPendingAsync(int movieId, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await db.Movies.Where(m => m.Id == movieId).ExecuteUpdateAsync(s => s.SetProperty(m => m.NewArtworkPending, false), ct);
    }

    private async Task<bool> GenerateStillsAsync(int movieId, CancellationToken ct)
    {
        bool leftEyeOnly;
        await using (var db = await dbFactory.CreateDbContextAsync(ct))
        {
            var movie = await db.Movies.AsNoTracking().FirstOrDefaultAsync(m => m.Id == movieId, ct);
            if (movie is null) return false;
            leftEyeOnly = movie.IsSideBySideVr;
        }

        var preview = await stillsService.PreviewAsync(movieId, MovieExtraFanartStillsService.DefaultCount, ExtraFanartStillsMode.OnlyIfNone, leftEyeOnly, ct: ct);
        if (preview.Preview is null)
        {
            logger.LogDebug("No extrafanart generated for movie {MovieId}: {Reason}", movieId, preview.ErrorMessage);
            return false;
        }

        using (preview.Preview)
        {
            var saved = await stillsService.SaveAsync(movieId, preview.Preview, preview.Preview.Stills.Select(s => s.Id).ToList(),
                MovieExtraFanartStillsService.DefaultQuality, ExtraFanartStillsMode.OnlyIfNone, ct);
            if (!saved.Success) logger.LogWarning("Saving generated extrafanart for movie {MovieId} failed: {Reason}", movieId, saved.ErrorMessage);
            return saved.Success;
        }
    }
}
