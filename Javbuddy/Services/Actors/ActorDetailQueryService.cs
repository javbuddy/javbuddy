using Javbuddy.Data;
using Javbuddy.Models;
using Javbuddy.Services.Images;
using Javbuddy.Services.Movies;
using Microsoft.EntityFrameworkCore;

namespace Javbuddy.Services.Actors;

public interface IActorDetailQueryService
{
    /// <summary>The movies the actor is linked to, newest first, with tags (and parent tags) and scenes.</summary>
    Task<List<Movie>> GetMoviesAsync(int actorId, CancellationToken ct = default);

    Task<int> GetPhotoCountAsync(int actorId, CancellationToken ct = default);

    /// <summary>Codes with an active or finished torrent, for the poster grid's Downloading status.</summary>
    Task<HashSet<string>> GetDownloadingCodesAsync(CancellationToken ct = default);
}

/// <summary>Actor Detail's reads, kept out of the page so it doesn't open a DbContext itself.</summary>
public class ActorDetailQueryService(IDbContextFactory<AppDbContext> dbFactory) : IActorDetailQueryService
{
    public async Task<List<Movie>> GetMoviesAsync(int actorId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var movies = await db.Movies
            .AsNoTrackingWithIdentityResolution()
            .Where(m => m.MovieActors.Any(link => link.ActorId == actorId))
            .Include(m => m.MovieTags).ThenInclude(mt => mt.Tag).ThenInclude(t => t.ParentTag)
            .Include(m => m.Scenes)
            .OrderByDescending(m => m.CreatedAt)
            .ToListAsync(ct);

        // Same poster versions as the Movies grid, so a cropped cover isn't the browser's cached old one.
        await PosterVersion.ApplyAsync(db, movies, ct);
        return movies;
    }

    public async Task<int> GetPhotoCountAsync(int actorId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.ActorPhotos.CountAsync(photo => photo.ActorId == actorId, ct);
    }

    public async Task<HashSet<string>> GetDownloadingCodesAsync(CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await MovieGridQueryService.QueryDownloadingCodesAsync(db, ct);
    }
}
