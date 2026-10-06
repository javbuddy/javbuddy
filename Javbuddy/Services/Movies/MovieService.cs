using Javbuddy.Data;
using Javbuddy.Models;
using Javbuddy.Services.Actors;
using Javbuddy.Services.Common;
using Javbuddy.Services.Javinizer;
using Javbuddy.Services.Jellyfin;
using Javbuddy.Services.LocalLibrary;
using Javbuddy.Services.Nfo;
using Javbuddy.Services.Tags;
using Javbuddy.Services.Trickplay;
using Microsoft.EntityFrameworkCore;

namespace Javbuddy.Services.Movies;

/// <summary>The metadata editor modal's edit model for MovieService.UpdateMetadataAsync — every
/// core movie field it's allowed to edit (README's "Non-goals": no MediaInfo-probed technical
/// fields, no external integration IDs). Saving only ever updates the DB — never the local .nfo
/// directly — so <see cref="Title"/> (Javbuddy-only, no .nfo counterpart at all) isn't
/// functionally different from the rest here; what differs is that the other nine feed
/// NfoSyncService's .nfo-drift check (CheckMovieMetadataConflictAsync), surfacing a "Resolve .nfo
/// Conflict" the user reviews as a diff and applies (or not) themselves, the same way the cast
/// editing and the tag editing already work. Movie.Notes isn't edited here, so saving
/// leaves it untouched.</summary>
public sealed record MovieMetadataUpdate(
    string? Title,
    string? MetaTitle,
    string? MetaOriginalTitle,
    DateTime? MetaReleaseDate,
    string? MetaStudio,
    string? MetaLabel,
    string? MetaSeries,
    string? MetaDirector,
    int? MetaRuntimeMinutes,
    string? MetaDescription);

/// <summary>Result of FetchJavinizerMetadataAsync — the "Refresh with Javinizer" field picker's
/// fetch step, which makes no DB changes; <see cref="Scraped"/> is held in memory by the
/// caller for review until the user confirms which fields to apply via
/// ApplyJavinizerMetadataAsync.</summary>
public sealed record MovieMetadataFetchResult(bool Success, string? ErrorMessage = null, MovieViewDto? Scraped = null);

/// <summary>One file in a movie's folder, as listed by the Delete dialog's "Delete files from disk"
/// preview. <see cref="RelativePath"/> is relative to the movie folder.</summary>
public sealed record MovieFolderFile(string RelativePath, long SizeBytes);

/// <summary>What deleting a movie's files from disk would remove: every file under
/// its local folder, recursively. <see cref="FolderPath"/> is null when no local folder resolves
/// (nothing on disk to delete); <see cref="ErrorMessage"/> is set when the folder couldn't be
/// listed or failed the library-root guard.</summary>
public sealed record MovieFolderDeletePreview(
    string? FolderPath,
    IReadOnlyList<MovieFolderFile> Files,
    long TotalBytes,
    string? ErrorMessage = null);

public interface IMovieService
{
    /// <summary>Flips a movie's status between Missing and Got.</summary>
    Task ToggleStatusAsync(int movieId, CancellationToken ct = default);

    /// <summary>Flips a movie's favorite flag (stamping or clearing FavoritedAt) and returns the
    /// new value; false when the movie doesn't exist.</summary>
    Task<bool> ToggleFavoriteAsync(int movieId, CancellationToken ct = default);

    /// <summary>Removes the movie's DB record (recording a <see cref="DeletedMovieHistory"/>
    /// tombstone). With <paramref name="deleteFiles"/> its whole local folder is
    /// deleted from disk first — only if that folder sits directly under a configured local library
    /// root — retrying a "Directory not empty" failure for a few seconds (see
    /// <see cref="Infrastructure.RetryingDirectoryDelete"/>); if the folder can't be deleted the DB
    /// record is kept and the error returned. No folder resolving isn't an error.</summary>
    Task<OperationResult> DeleteAsync(int movieId, bool deleteFiles = false, CancellationToken ct = default);

    /// <summary>Lists the files <see cref="DeleteAsync"/> with <c>deleteFiles</c> would remove, with
    /// their total size, for the Delete dialog to show before the user confirms.</summary>
    Task<MovieFolderDeletePreview> GetFolderDeletePreviewAsync(int movieId, CancellationToken ct = default);

    /// <summary>Applies a Jellyfin lookup match's ownership/descriptive fields onto a movie and
    /// marks it Got after Movie Detail's Jellyfin scan finds a match.</summary>
    Task LinkJellyfinItemAsync(int movieId, MediaServer.MediaServerItemDto item, CancellationToken ct = default);

    /// <summary>Links a tracked actor to a movie: appends their canonical name to
    /// <c>Movie.MetaActresses</c> (or corrects an existing unmatched entry to it), re-derives
    /// <c>MovieActor</c> links and <c>HasUnmatchedActors</c>/<c>UnmatchedActorNames</c> from that
    /// text, and immediately re-checks .nfo drift.</summary>
    Task<OperationResult> AddActorToMovieAsync(int movieId, int actorId, CancellationToken ct = default);

    /// <summary>Unlinks a tracked actor from a movie: strips their name(s) out of
    /// <c>Movie.MetaActresses</c>, re-derives <c>MovieActor</c> links and
    /// <c>HasUnmatchedActors</c>/<c>UnmatchedActorNames</c> from the remaining text, and
    /// immediately re-checks .nfo drift.</summary>
    Task<OperationResult> RemoveActorFromMovieAsync(int movieId, int actorId, CancellationToken ct = default);

    /// <summary>Removes an unmatched actor name from a movie: strips the name out of
    /// <c>Movie.MetaActresses</c>, re-derives <c>MovieActor</c> links and
    /// <c>HasUnmatchedActors</c>/<c>UnmatchedActorNames</c> from the remaining text, and
    /// immediately re-checks .nfo drift.</summary>
    Task<OperationResult> RemoveActorFromMovieAsync(int movieId, string actorName, CancellationToken ct = default);

    /// <summary>Applies the metadata editor modal's "Save" — writes every field in
    /// <paramref name="update"/> onto the movie, then immediately re-checks .nfo drift (never
    /// writes to the .nfo itself), mirroring AddActorToMovieAsync/RemoveActorFromMovieAsync's
    /// immediate-recheck pattern.</summary>
    Task<OperationResult> UpdateMetadataAsync(int movieId, MovieMetadataUpdate update, CancellationToken ct = default);

    /// <summary>Queries javinizer-go for the movie's code and returns the result for review —
    /// makes no DB changes (the "No Auto Overwrite" requirement). Fails if javinizer-go isn't
    /// configured, the movie has no code, or the scrape itself fails/finds nothing.</summary>
    Task<MovieMetadataFetchResult> FetchJavinizerMetadataAsync(int movieId, CancellationToken ct = default);

    /// <summary>Applies only the fields selected in <paramref name="options"/> from a previously
    /// fetched javinizer-go scrape (see <see cref="FetchJavinizerMetadataAsync"/>) onto the movie.
    /// Synchronizes actor associations and tag normalization only if cast or genres were among the
    /// selected fields, then flags .nfo drift via strict field matching
    /// (<see cref="INfoSyncService.CheckMovieMetadataConflictAsync"/>) without modifying the on-disk
    /// .nfo directly, mirroring <see cref="UpdateMetadataAsync"/>.</summary>
    Task<OperationResult> ApplyJavinizerMetadataAsync(int movieId, MovieViewDto scraped, MovieMetadataImportOptions options, CancellationToken ct = default);

    /// <summary>Makes one of the movie's versions its primary, pinned so a library
    /// refresh keeps it, and syncs that version's media details onto the movie the way a refresh does,
    /// queuing its trickplay like a refresh that finds a new main file. Fails when fileId isn't one of
    /// the movie's files.</summary>
    Task<OperationResult> SetPrimaryFileAsync(int movieId, int fileId, CancellationToken ct = default);

    /// <summary>Sets one version's VR / 3D format by hand: <paramref name="vrType"/> is
    /// one of VrFormat.All, or null for flat video, pinned so a library refresh keeps it. With
    /// <paramref name="detect"/> the version goes back to being detected (vrType is ignored). Only
    /// Javbuddy's own data changes; the file isn't renamed. Fails when fileId isn't one of the
    /// movie's files or vrType isn't a known format.</summary>
    Task<OperationResult> SetVrTypeAsync(int movieId, int fileId, string? vrType, bool detect = false, CancellationToken ct = default);
}

/// <summary>Mutations for MovieDetail.razor's toolbar actions — extracted so the page doesn't
/// open a DbContext directly for anything beyond its own read-only display query.</summary>
public class MovieService(
    IDbContextFactory<AppDbContext> dbFactory,
    MovieChangeNotifier? movieChangeNotifier = null,
    INfoSyncService? nfoSyncService = null,
    IJavinizerClient? javinizerClient = null,
    ITrickplayTrigger? trickplayTrigger = null,
    ILocalLibraryClient? localLibraryClient = null) : IMovieService
{
    private readonly IDbContextFactory<AppDbContext> dbFactory = dbFactory;
    private readonly MovieChangeNotifier? movieChangeNotifier = movieChangeNotifier;
    private readonly INfoSyncService? nfoSyncService = nfoSyncService;
    private readonly IJavinizerClient? javinizerClient = javinizerClient;
    private readonly ITrickplayTrigger? trickplayTrigger = trickplayTrigger;
    private readonly ILocalLibraryClient? localLibraryClient = localLibraryClient;

    public async Task<OperationResult> SetVrTypeAsync(int movieId, int fileId, string? vrType, bool detect = false, CancellationToken ct = default)
    {
        if (!detect && vrType is not null && !VrFormat.All.Contains(vrType))
        {
            return new OperationResult(false, $"Unknown VR format \"{vrType}\".");
        }

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var movie = await db.Movies.Include(m => m.MovieFiles).FirstOrDefaultAsync(m => m.Id == movieId, ct);
        var file = movie?.MovieFiles.FirstOrDefault(f => f.Id == fileId);
        if (movie is null || file is null)
        {
            return new OperationResult(false, "That version no longer exists.");
        }

        file.VrTypePinned = !detect;
        if (!detect) file.VrType = vrType;
        LocalLibraryMetadataMapper.ApplyVrFormats(movie);
        await db.SaveChangesAsync(ct);
        movieChangeNotifier?.NotifyChanged();
        return new OperationResult(true);
    }

    public async Task<OperationResult> SetPrimaryFileAsync(int movieId, int fileId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var movie = await db.Movies.Include(m => m.MovieFiles).FirstOrDefaultAsync(m => m.Id == movieId, ct);
        var primary = movie?.MovieFiles.FirstOrDefault(f => f.Id == fileId);
        if (movie is null || primary is null)
        {
            return new OperationResult(false, "That version no longer exists.");
        }

        foreach (var file in movie.MovieFiles)
        {
            file.IsPrimary = file == primary;
            file.IsPrimaryPinned = file == primary;
        }
        LocalLibraryMetadataMapper.SyncPrimaryFileToMovie(movie, primary, movie.MovieFiles.Count, movie.MovieFiles.Sum(f => f.FileSizeBytes));
        await db.SaveChangesAsync(ct);
        movieChangeNotifier?.NotifyChanged();

        // The main file changed, as when a library refresh finds a new one: its trickplay is queued
        // the same way, if it has none yet.
        if (trickplayTrigger is not null)
        {
            await trickplayTrigger.OnVideoFilesChangedAsync(movieId, ct);
        }
        return new OperationResult(true);
    }

    public async Task ToggleStatusAsync(int movieId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var tracked = await db.Movies.FindAsync([movieId], ct);
        if (tracked is not null)
        {
            tracked.Status = tracked.Status == MovieStatus.Missing ? MovieStatus.Got : MovieStatus.Missing;
            await db.SaveChangesAsync(ct);
            movieChangeNotifier?.NotifyChanged();
        }
    }

    public async Task<bool> ToggleFavoriteAsync(int movieId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var tracked = await db.Movies.FindAsync([movieId], ct);
        if (tracked is null) return false;

        tracked.IsFavorite = !tracked.IsFavorite;
        tracked.FavoritedAt = tracked.IsFavorite ? DateTime.UtcNow : null;
        await db.SaveChangesAsync(ct);
        movieChangeNotifier?.NotifyChanged();
        return tracked.IsFavorite;
    }

    public async Task<OperationResult> DeleteAsync(int movieId, bool deleteFiles = false, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var tracked = await db.Movies.FindAsync([movieId], ct);
        if (tracked is null) return OperationResult.Ok();

        if (deleteFiles)
        {
            if (localLibraryClient is null) return OperationResult.Fail(LocalLibraryUnavailable);
            if (await MovieFolderDeletion.DeleteAsync(localLibraryClient, tracked.Code, ct) is { } error) return OperationResult.Fail(error);
        }

        await DeletedMovieHistory.RecordAsync(db, tracked, ct);
        db.Movies.Remove(tracked);
        await db.SaveChangesAsync(ct);
        movieChangeNotifier?.NotifyChanged();
        return OperationResult.Ok();
    }

    public async Task<MovieFolderDeletePreview> GetFolderDeletePreviewAsync(int movieId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var code = await db.Movies.Where(m => m.Id == movieId).Select(m => m.Code).FirstOrDefaultAsync(ct);
        if (localLibraryClient is null) return new MovieFolderDeletePreview(null, [], 0, LocalLibraryUnavailable);
        var (folder, error) = await MovieFolderDeletion.ResolveAsync(localLibraryClient, code, ct);
        if (error is not null || folder is null) return new MovieFolderDeletePreview(null, [], 0, error);

        try
        {
            // AttributesToSkip = None: hidden dotfiles (.actors/, a lingering .nfsXXXX) go too, so list them.
            var options = new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.None };
            var files = new DirectoryInfo(folder).EnumerateFiles("*", options)
                .Select(f => new MovieFolderFile(Path.GetRelativePath(folder, f.FullName), f.Length))
                .OrderBy(f => f.RelativePath, StringComparer.OrdinalIgnoreCase)
                .ToList();
            return new MovieFolderDeletePreview(folder, files, files.Sum(f => f.SizeBytes));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new MovieFolderDeletePreview(folder, [], 0, $"Could not list the movie's folder: {ex.Message}");
        }
    }

    private const string LocalLibraryUnavailable = "The local library isn't available.";

    public async Task LinkJellyfinItemAsync(int movieId, MediaServer.MediaServerItemDto item, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var tracked = await db.Movies.FindAsync([movieId], ct);
        if (tracked is not null)
        {
            JellyfinMetadataMapper.ApplyMatch(tracked, item);
            tracked.Status = MovieStatus.Got;
            await db.SaveChangesAsync(ct);
            movieChangeNotifier?.NotifyChanged();
        }
    }

    public async Task<OperationResult> AddActorToMovieAsync(int movieId, int actorId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var movie = await db.Movies.FindAsync([movieId], ct);
        if (movie is null)
        {
            return OperationResult.Fail("Movie not found.");
        }

        var actor = await db.Actors.Include(a => a.Aliases).FirstOrDefaultAsync(a => a.Id == actorId, ct);
        if (actor is null || string.IsNullOrWhiteSpace(actor.FirstName))
        {
            return OperationResult.Fail("Actor not found.");
        }

        var alreadyLinked = await db.MovieActors.AnyAsync(ma => ma.MovieId == movieId && ma.ActorId == actorId, ct);
        if (alreadyLinked)
        {
            return OperationResult.Fail($"\"{actor.DisplayName}\" is already on this movie.");
        }

        var matcher = NfoActorMatching.CreateNameMatcher(actor, null);
        NfoActorMatching.UpdateMovieMetaActresses(movie, matcher, actor.DisplayName);

        await MovieActorAssociation.SynchronizeAsync(db, movie, ct);
        await db.SaveChangesAsync(ct);
        movieChangeNotifier?.NotifyChanged();

        // Re-checks .nfo drift immediately rather than waiting for the next scheduled Library
        // Rescan, mirroring TagService.AddTagToMovieAsync's matching comment.
        if (nfoSyncService is not null)
        {
            await nfoSyncService.CheckMovieNfoConflictAsync(movieId, ct);
        }

        return OperationResult.Ok();
    }

    public async Task<OperationResult> RemoveActorFromMovieAsync(int movieId, int actorId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var movie = await db.Movies.FindAsync([movieId], ct);
        if (movie is null)
        {
            return OperationResult.Fail("Movie not found.");
        }

        var link = await db.MovieActors.FirstOrDefaultAsync(ma => ma.MovieId == movieId && ma.ActorId == actorId, ct);
        if (link is null)
        {
            return OperationResult.Fail("Actor is not on this movie.");
        }

        var actor = await db.Actors.Include(a => a.Aliases).FirstAsync(a => a.Id == actorId, ct);
        var matcher = NfoActorMatching.CreateNameMatcher(actor, null);
        var remainingNames = ActorMatching.SplitNames(movie.MetaActresses)
            .Where(name => !NfoActorMatching.Matches(name, matcher))
            .ToList();
        movie.MetaActresses = remainingNames.Count > 0 ? string.Join(", ", remainingNames) : null;

        await MovieActorAssociation.SynchronizeAsync(db, movie, ct);
        await db.SaveChangesAsync(ct);
        movieChangeNotifier?.NotifyChanged();

        // See AddActorToMovieAsync's matching comment: re-check .nfo drift immediately instead of
        // only on the next scheduled Library Rescan.
        if (nfoSyncService is not null)
        {
            await nfoSyncService.CheckMovieNfoConflictAsync(movieId, ct);
        }

        return OperationResult.Ok();
    }

    public async Task<OperationResult> RemoveActorFromMovieAsync(int movieId, string actorName, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(actorName))
        {
            return OperationResult.Fail("Actor name is required.");
        }

        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var movie = await db.Movies.FindAsync([movieId], ct);
        if (movie is null)
        {
            return OperationResult.Fail("Movie not found.");
        }

        var trimmed = actorName.Trim();
        var nameMatcher = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { trimmed };
        var compact = trimmed.Replace(" ", "");
        if (compact.Length > 0)
        {
            nameMatcher.Add(compact);
        }

        var names = ActorMatching.SplitNames(movie.MetaActresses).ToList();
        var remainingNames = names
            .Where(name => !NfoActorMatching.Matches(name, nameMatcher))
            .ToList();

        if (remainingNames.Count == names.Count)
        {
            return OperationResult.Fail("Actor is not on this movie.");
        }

        movie.MetaActresses = remainingNames.Count > 0 ? string.Join(", ", remainingNames) : null;

        await MovieActorAssociation.SynchronizeAsync(db, movie, ct);
        await db.SaveChangesAsync(ct);
        movieChangeNotifier?.NotifyChanged();

        // See AddActorToMovieAsync's matching comment: re-check .nfo drift immediately instead of
        // only on the next scheduled Library Rescan.
        if (nfoSyncService is not null)
        {
            await nfoSyncService.CheckMovieNfoConflictAsync(movieId, ct);
        }

        return OperationResult.Ok();
    }

    public async Task<OperationResult> UpdateMetadataAsync(int movieId, MovieMetadataUpdate update, CancellationToken ct = default)
    {
        if (update.MetaRuntimeMinutes is < 0)
        {
            return OperationResult.Fail("Runtime must be zero or greater.");
        }

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var movie = await db.Movies.FindAsync([movieId], ct);
        if (movie is null)
        {
            return OperationResult.Fail("Movie not found.");
        }

        movie.Title = update.Title.TrimToNull();
        movie.MetaTitle = update.MetaTitle.TrimToNull();
        movie.MetaOriginalTitle = update.MetaOriginalTitle.TrimToNull();
        movie.MetaReleaseDate = update.MetaReleaseDate;
        movie.MetaStudio = update.MetaStudio.TrimToNull();
        movie.MetaLabel = update.MetaLabel.TrimToNull();
        movie.MetaSeries = update.MetaSeries.TrimToNull();
        movie.MetaDirector = update.MetaDirector.TrimToNull();
        movie.MetaRuntimeMinutes = update.MetaRuntimeMinutes;
        movie.MetaDescription = update.MetaDescription.TrimToNull();

        await db.SaveChangesAsync(ct);
        movieChangeNotifier?.NotifyChanged();

        // Never writes to the .nfo directly — CheckMovieMetadataConflictAsync's strict
        // field matching is what turns any resulting drift into a "Resolve .nfo Conflict" the
        // user reviews as a diff and applies (or not) themselves, mirroring how cast/tag editing
        // already only ever touch the DB.
        if (nfoSyncService is not null)
        {
            await nfoSyncService.CheckMovieMetadataConflictAsync(movieId, ct);
        }

        return OperationResult.Ok();
    }

    public async Task<MovieMetadataFetchResult> FetchJavinizerMetadataAsync(int movieId, CancellationToken ct = default)
    {
        if (javinizerClient is null)
        {
            return new MovieMetadataFetchResult(false, "javinizer-go is not configured.");
        }

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var movie = await db.Movies.AsNoTracking().FirstOrDefaultAsync(m => m.Id == movieId, ct);
        if (movie is null || string.IsNullOrWhiteSpace(movie.Code))
        {
            return new MovieMetadataFetchResult(false, "Movie not found.");
        }

        var scrapeResult = await javinizerClient.ScrapeAsync(movie.Code, ct);
        if (!scrapeResult.Success || scrapeResult.Movie is null)
        {
            return new MovieMetadataFetchResult(false, scrapeResult.ErrorMessage ?? $"javinizer-go found no metadata for {movie.Code}.");
        }

        return new MovieMetadataFetchResult(true, null, scrapeResult.Movie);
    }

    public async Task<OperationResult> ApplyJavinizerMetadataAsync(int movieId, MovieViewDto scraped, MovieMetadataImportOptions options, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var movie = await db.Movies.FindAsync([movieId], ct);
        if (movie is null)
        {
            return OperationResult.Fail("Movie not found.");
        }

        if (options.ImportTitle)
        {
            var title = MovieMetadataMapper.ResolveTitle(scraped, movie.Code);
            if (!string.IsNullOrWhiteSpace(title)) movie.MetaTitle = title;
        }
        if (options.ImportOriginalTitle && !string.IsNullOrWhiteSpace(scraped.OriginalTitle)) movie.MetaOriginalTitle = scraped.OriginalTitle;
        if (options.ImportReleaseDate && scraped.ReleaseDate.HasValue) movie.MetaReleaseDate = scraped.ReleaseDate;
        if (options.ImportStudio && !string.IsNullOrWhiteSpace(scraped.Maker)) movie.MetaStudio = scraped.Maker;
        if (options.ImportLabel && !string.IsNullOrWhiteSpace(scraped.Label)) movie.MetaLabel = scraped.Label;
        if (options.ImportSeries && !string.IsNullOrWhiteSpace(scraped.Series)) movie.MetaSeries = scraped.Series;
        if (options.ImportDirector && !string.IsNullOrWhiteSpace(scraped.Director)) movie.MetaDirector = scraped.Director;
        if (options.ImportRuntime && scraped.Runtime.HasValue) movie.MetaRuntimeMinutes = scraped.Runtime;
        if (options.ImportDescription && !string.IsNullOrWhiteSpace(scraped.Description)) movie.MetaDescription = scraped.Description;

        var castOrGenresChanged = false;

        if (options.ImportActresses && scraped.Actresses is { Count: > 0 })
        {
            movie.MetaActresses = MovieMetadataMapper.FormatActresses(scraped.Actresses);
            castOrGenresChanged = true;
        }

        if (options.ImportGenres && scraped.Genres is { Count: > 0 })
        {
            movie.MetaGenres = MovieMetadataMapper.FormatGenres(scraped.Genres);
            castOrGenresChanged = true;
        }

        if (options.ImportCover)
        {
            var coverUrl = MovieMetadataMapper.ResolveCoverUrl(scraped);
            if (!string.IsNullOrWhiteSpace(coverUrl))
            {
                movie.MetaCoverUrl = coverUrl;
                movie.MetaBackdropUrl = coverUrl;
            }
        }

        if (castOrGenresChanged)
        {
            await MovieActorAssociation.SynchronizeAsync(db, movie, ct);

            // Genres actually imported this call → resolve Tags straight from the raw scraped
            // list, not movie.MetaGenres's already comma-joined value. Otherwise (only
            // actresses changed) there's no fresher genre data than what's already there, so just
            // reprocess it.
            if (options.ImportGenres && scraped.Genres is { Count: > 0 })
            {
                await TagNormalization.ApplyToMovieAsync(db, movie, scraped.Genres.Select(g => g.Name), ct);
            }
            else
            {
                await TagNormalization.ApplyToMovieAsync(db, movie, ct);
            }
        }

        await db.SaveChangesAsync(ct);
        movieChangeNotifier?.NotifyChanged();

        // Never writes to the .nfo directly — CheckMovieMetadataConflictAsync's strict
        // field matching flags any resulting drift as a "Resolve .nfo Conflict" the
        // user reviews as a diff and applies (or not) themselves, mirroring UpdateMetadataAsync,
        // cast editing, and tag editing.
        if (nfoSyncService is not null)
        {
            await nfoSyncService.CheckMovieMetadataConflictAsync(movieId, ct);
        }

        return OperationResult.Ok();
    }

}
