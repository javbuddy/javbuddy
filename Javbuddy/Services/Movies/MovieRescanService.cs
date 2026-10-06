using Javbuddy.Data;
using Javbuddy.Models;
using Javbuddy.Services.Images;
using Javbuddy.Services.Javinizer;
using Javbuddy.Services.Jellyfin;
using Javbuddy.Services.LocalLibrary;
using Javbuddy.Services.Monitoring;
using Javbuddy.Services.Nfo;
using Javbuddy.Services.Tags;
using Javbuddy.Services.Trickplay;
using Microsoft.EntityFrameworkCore;

namespace Javbuddy.Services.Movies;

public sealed record MovieRescanResult(
    bool Success,
    string? Message = null,
    bool LocalFound = false,
    bool MediaInfoProbed = false,
    bool NfoApplied = false,
    bool ExternalMetadataApplied = false,
    bool ActorsSynchronized = false,
    bool JellyfinLinked = false,
    bool TrickplayQueued = false);

public interface IMovieRescanService
{
    Task<MovieRescanResult> RescanAsync(int movieId, CancellationToken ct = default);
}

/// <summary>Coordinates on-demand rescan for a single movie:
/// 1. Rescan local library files and .nfo metadata (respecting canonical metadata rules and drift handling),
///    and queue trickplay generation when the main video file has none yet.
/// 2. Query external metadata providers (javinizer-go) if configured and applicable under unified precedence rules.
/// 3. Re-synchronize actor relationships and alias mappings.
/// 4. Perform media server (Jellyfin) link and stream info check.</summary>
public class MovieRescanService(
    IDbContextFactory<AppDbContext> dbFactory,
    ILocalLibraryClient localLibraryClient,
    IJavinizerClient javinizerClient,
    IJellyfinClient jellyfinClient,
    INfoSyncService nfoSyncService,
    ITrickplayTrigger trickplayTrigger,
    MovieChangeNotifier movieChangeNotifier,
    ILogger<MovieRescanService> logger,
    TaskActivityTracker? activities = null) : IMovieRescanService
{
    public async Task<MovieRescanResult> RescanAsync(int movieId, CancellationToken ct = default)
    {
        string? code;
        await using (var db = await dbFactory.CreateDbContextAsync(ct))
        {
            var m = await db.Movies.FindAsync([movieId], ct);
            if (m is null || string.IsNullOrWhiteSpace(m.Code))
            {
                return new MovieRescanResult(false, "Movie not found.");
            }
            code = m.Code;
        }

        var activityId = activities?.Start($"Rescan · {code}", "Rescanning movie…");
        var isCompleted = false;

        try
        {
            var localFound = false;
            var mediaInfoProbed = false;
            var nfoApplied = false;
            var externalMetadataApplied = false;
            var jellyfinLinked = false;
            var trickplayQueued = false;

            // Raw genre-name list captured alongside whichever of steps 1/2 actually applies fresh
            // metadata this run, so step 3's TagNormalization call can resolve Tags directly from it
            // instead of re-splitting Movie.MetaGenres's already comma-joined value. Stays
            // null when neither step applies new metadata this run (step 3 then just reprocesses
            // the movie's current MetaGenres, e.g. to pick up a newly added rule).
            List<string>? rawGenres = null;

            // 1. Rescan local library files and .nfo metadata (respecting canonical metadata rules and drift handling)
            // RefreshMediaInfoOnlyAsync below probes and synchronizes every file. The preceding
            // metadata lookup only needs NFO/image data, so it must not perform duplicate probes.
            var local = await localLibraryClient.TryGetMetadataAsync(code, ct, includeMediaInfo: false);
            localFound = local.Found;

            if (local.Found)
            {
                if (local.FolderPath is not null)
                {
                    var posterPath = Path.Combine(local.FolderPath, "poster.jpg");
                    if (File.Exists(posterPath))
                    {
                        ImageConverter.EnsureLocalPosterCropped(posterPath);
                    }
                }

                var mediaResult = await localLibraryClient.RefreshMediaInfoOnlyAsync(movieId, ct);
                mediaInfoProbed = mediaResult.Success;
                await localLibraryClient.SyncImagesSignatureAsync(movieId, ct);

                await using var db = await dbFactory.CreateDbContextAsync(ct);
                var movie = await db.Movies.FindAsync([movieId], ct);
                if (movie is not null)
                {
                    var wasMissing = movie.Status == MovieStatus.Missing;

                    if (movie.LocalFileSizeBytes > 0 || movie.MediaVideoFileName is not null)
                    {
                        movie.Status = MovieStatus.Got;
                    }

                    // Canonical metadata rules: Javbuddy has absolute authority, with
                    // one exception: a Missing movie's descriptive metadata may only ever have
                    // come from a guess (e.g. a javinizer-go scrape run while it had no local file
                    // yet, whose title can carry a prepended code) — never from a person reviewing the
                    // real file. The moment it transitions to Got, the .nfo that shipped with the
                    // actual file becomes the best-known truth and is imported over that guess.
                    var hasExistingMetadata = !string.IsNullOrWhiteSpace(movie.MetaTitle)
                        || !string.IsNullOrWhiteSpace(movie.MetaSourceName)
                        || !string.IsNullOrWhiteSpace(movie.MetaActresses);

                    if ((wasMissing || !hasExistingMetadata) && local.Metadata is not null)
                    {
                        LocalLibraryMetadataMapper.ApplyDescriptiveMetadata(movie, local.Metadata);
                        nfoApplied = true;
                        rawGenres = local.Metadata.Genres;
                    }

                    var nfoPath = await localLibraryClient.ResolveNfoFilePathAsync(code, ct);
                    if (nfoPath is not null && File.Exists(nfoPath))
                    {
                        movie.MediaNfoLastWriteUtc = File.GetLastWriteTimeUtc(nfoPath);
                    }

                    await db.SaveChangesAsync(ct);
                }

                // The refresh above doesn't reach the new-file trigger for an unchanged file, and that
                // trigger honors the "Generate trickplay for new movie files" setting; a manual rescan
                // queues a missing set regardless, retrying one that failed earlier.
                trickplayQueued = await trickplayTrigger.OnRescanAsync(movieId, ct);
            }
            else
            {
                if (await localLibraryClient.AnyRootReachableAsync(ct))
                {
                    await using var db = await dbFactory.CreateDbContextAsync(ct);
                    var movie = await db.Movies.FindAsync([movieId], ct);
                    if (movie is not null)
                    {
                        if (movie.Status == MovieStatus.Got && movie.JellyfinItemId == null)
                        {
                            movie.Status = MovieStatus.Missing;
                        }
                        LocalLibraryMetadataMapper.ClearLocalFileData(movie);
                        await db.SaveChangesAsync(ct);
                    }
                }
            }

            // 2. Query external metadata providers (javinizer-go) if configured and applicable under unified precedence rules
            await using (var db = await dbFactory.CreateDbContextAsync(ct))
            {
                var movie = await db.Movies.FindAsync([movieId], ct);
                if (movie is not null)
                {
                    // Unified precedence: Local .nfo metadata takes precedence over external scrapers.
                    // Only query javinizer-go if the movie's metadata is not sourced from Local.
                    if (movie.MetaSourceName != "Local")
                    {
                        var javinizerBaseUrl = await javinizerClient.GetBaseUrlAsync(ct);
                        if (!string.IsNullOrWhiteSpace(javinizerBaseUrl))
                        {
                            var scrapeResult = await javinizerClient.ScrapeAsync(code, ct);
                            if (scrapeResult.Success && scrapeResult.Movie is not null)
                            {
                                MovieMetadataMapper.Apply(movie, scrapeResult.Movie);
                                externalMetadataApplied = true;
                                rawGenres = scrapeResult.Movie.Genres?.Select(g => g.Name).OfType<string>().ToList();
                                await db.SaveChangesAsync(ct);
                            }
                            else
                            {
                                logger.LogDebug("javinizer-go scrape for {Code} did not return metadata: {Error}", code, scrapeResult.ErrorMessage);
                            }
                        }
                    }
                }
            }

            // 3. Re-synchronize actor relationships and alias mappings
            await using (var db = await dbFactory.CreateDbContextAsync(ct))
            {
                var movie = await db.Movies.FindAsync([movieId], ct);
                if (movie is not null)
                {
                    await MovieActorAssociation.SynchronizeAsync(db, movie, ct);
                    if (rawGenres is not null)
                    {
                        await TagNormalization.ApplyToMovieAsync(db, movie, rawGenres, ct);
                    }
                    else
                    {
                        await TagNormalization.ApplyToMovieAsync(db, movie, ct);
                    }
                    await db.SaveChangesAsync(ct);
                }
            }

            if (localFound)
            {
                await nfoSyncService.CheckMovieNfoConflictAsync(movieId, ct);
            }

            // 4. Perform media server (Jellyfin) link and stream info check
            if (await jellyfinClient.IsEnabledAsync(ct))
            {
                var libraryNames = await jellyfinClient.GetSelectedLibraryNamesAsync(ct);
                if (libraryNames.Count > 0)
                {
                    var lookupResult = await jellyfinClient.LookupInSelectedLibrariesAsync(code, ct);
                    var match = lookupResult.Success ? lookupResult.Items?.FirstOrDefault() : null;

                    await using var db = await dbFactory.CreateDbContextAsync(ct);
                    var movie = await db.Movies.FindAsync([movieId], ct);
                    if (movie is not null)
                    {
                        if (match is not null && !string.IsNullOrWhiteSpace(match.Id))
                        {
                            JellyfinMetadataMapper.ApplyMatch(movie, match);
                            movie.Status = MovieStatus.Got;
                            jellyfinLinked = true;
                        }
                        else
                        {
                            movie.JellyfinCheckedAt = DateTime.UtcNow;
                        }
                        await db.SaveChangesAsync(ct);
                    }
                }
            }

            movieChangeNotifier.NotifyChanged();

            if (activityId.HasValue)
            {
                activities?.Complete(activityId.Value, "Rescan completed.");
            }
            isCompleted = true;

            return new MovieRescanResult(
                Success: true,
                Message: "Rescan completed.",
                LocalFound: localFound,
                MediaInfoProbed: mediaInfoProbed,
                NfoApplied: nfoApplied,
                ExternalMetadataApplied: externalMetadataApplied,
                ActorsSynchronized: true,
                JellyfinLinked: jellyfinLinked,
                TrickplayQueued: trickplayQueued);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            if (activityId.HasValue)
            {
                activities?.Complete(activityId.Value, "Rescan cancelled.");
            }
            isCompleted = true;
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Rescan failed for movie {Code} (id {MovieId})", code, movieId);
            if (activityId.HasValue)
            {
                activities?.Complete(activityId.Value, "Rescan failed.", failed: true);
            }
            isCompleted = true;
            return new MovieRescanResult(false, $"Rescan failed: {ex.Message}");
        }
        finally
        {
            if (!isCompleted && activityId.HasValue)
            {
                activities?.Complete(activityId.Value, "Rescan interrupted or failed.", failed: true);
            }
        }
    }
}
