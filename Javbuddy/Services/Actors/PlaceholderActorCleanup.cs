using Javbuddy.Data;
using Javbuddy.Services.Images;
using Microsoft.EntityFrameworkCore;

namespace Javbuddy.Services.Actors;

/// <summary>Removes the portraits and gallery photos of a movie's unidentified performers before the movie goes. Their
/// rows go with the movie (cascade), but images aren't tied to an actor by a foreign key, so they would otherwise stay
/// on disk until the scheduled cache cleanup found them.</summary>
public static class PlaceholderActorCleanup
{
    public static async Task DeleteImagesAsync(
        AppDbContext db, int movieId, IActorImageCacheService? images, IActorPhotoService? photos, CancellationToken ct = default)
    {
        if (images is null && photos is null) return;

        var placeholderIds = await db.Actors
            .Where(actor => actor.PlaceholderMovieId == movieId)
            .Select(actor => actor.Id)
            .ToListAsync(ct);
        foreach (var actorId in placeholderIds)
        {
            if (photos is not null) await photos.DeleteAllForActorAsync(actorId, ct);
            if (images is not null) await images.DeleteAllForActorAsync(actorId, ct);
        }
    }
}
