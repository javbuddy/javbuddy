using Javbuddy.Data;
using Javbuddy.Models;
using Javbuddy.Services.Javinizer;
using Javbuddy.Services.Jellyfin;
using Javbuddy.Services.LocalLibrary;
using Javbuddy.Services.MediaServer;
using Javbuddy.Services.Tags;
using Microsoft.EntityFrameworkCore;

namespace Javbuddy.Services.Movies;

public sealed record MovieAddResult(Movie Movie, bool OwnedViaJellyfin, bool MetadataFound, string? MetadataError);

public interface IMovieAddService
{
    /// <summary>Adds a new Missing movie by code and runs the same lookup chain as the manual
    /// "Add a movie" page: media server ownership check (marks it Got on a match), then metadata from
    /// the local library first, falling back to javinizer-go.</summary>
    Task<MovieAddResult> AddAsync(string code, CancellationToken ct = default);
}

/// <summary>Shared by MovieAdd.razor (manual add) and MovieDetail.razor's r18.dev preview branch
/// ("Add to Library" on a movie not yet in the library) — both need the identical
/// ownership-check-then-metadata-fallback pipeline, not two copies that can drift apart.</summary>
public class MovieAddService(
    IDbContextFactory<AppDbContext> dbFactory,
    IMediaServerClient mediaServerClient,
    ILocalLibraryClient localLibraryClient,
    IJavinizerClient javinizerClient,
    MovieChangeNotifier movieChangeNotifier) : IMovieAddService
{
    private readonly IDbContextFactory<AppDbContext> dbFactory = dbFactory;
    private readonly IMediaServerClient mediaServerClient = mediaServerClient;
    private readonly ILocalLibraryClient localLibraryClient = localLibraryClient;
    private readonly IJavinizerClient javinizerClient = javinizerClient;
    private readonly MovieChangeNotifier movieChangeNotifier = movieChangeNotifier;

    public async Task<MovieAddResult> AddAsync(string code, CancellationToken ct = default)
    {
        var movie = new Movie { Code = code };
        await using (var db = await dbFactory.CreateDbContextAsync(ct))
        {
            db.Movies.Add(movie);
            await DeletedMovieHistory.ClearAsync(db, code, ct);
            await db.SaveChangesAsync(ct);
        }
        movieChangeNotifier.NotifyChanged();

        // Ownership only (only within the libraries configured on the Connections page — none
        // selected means media server isn't checked at all). Metadata no longer comes from media server —
        // see the local/javinizer-go lookup below.
        var serverResult = await mediaServerClient.LookupInSelectedLibrariesAsync(code, ct);
        var match = serverResult.Success ? serverResult.Items?.FirstOrDefault() : null;

        if (match is not null)
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            var tracked = await db.Movies.FindAsync([movie.Id], ct);
            if (tracked is not null)
            {
                JellyfinMetadataMapper.ApplyMatch(tracked, match);
                tracked.Status = MovieStatus.Got;
                await db.SaveChangesAsync(ct);
            }
        }

        // Metadata: local library folders first, javinizer-go as fallback.
        var local = await localLibraryClient.TryGetMetadataAsync(code);
        if (local.Found && local.Metadata is not null)
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            var tracked = await db.Movies.FindAsync([movie.Id], ct);
            if (tracked is not null)
            {
                LocalLibraryMetadataMapper.Apply(tracked, local.Metadata);
                await MovieActorAssociation.SynchronizeAsync(db, tracked, ct);
                await TagNormalization.ApplyToMovieAsync(db, tracked, local.Metadata.Genres, ct);
                await db.SaveChangesAsync(ct);
            }
            return new MovieAddResult(movie, match is not null, true, null);
        }

        var scrapeResult = await javinizerClient.ScrapeAsync(code, ct);
        if (scrapeResult.Success && scrapeResult.Movie is not null)
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            var tracked = await db.Movies.FindAsync([movie.Id], ct);
            if (tracked is not null)
            {
                MovieMetadataMapper.Apply(tracked, scrapeResult.Movie);
                await MovieActorAssociation.SynchronizeAsync(db, tracked, ct);
                await TagNormalization.ApplyToMovieAsync(db, tracked, scrapeResult.Movie.Genres?.Select(g => g.Name) ?? [], ct);
                await db.SaveChangesAsync(ct);
            }
            return new MovieAddResult(movie, match is not null, true, null);
        }

        return new MovieAddResult(movie, match is not null, false, scrapeResult.ErrorMessage);
    }
}
