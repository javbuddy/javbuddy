using Javbuddy.Data;
using Microsoft.EntityFrameworkCore;

namespace Javbuddy.Services.Scenes;

/// <summary>A movie's scenes, highlights and apexes, loaded together.</summary>
public sealed record MovieClips(IReadOnlyList<SceneItem> Scenes, IReadOnlyList<HighlightItem> Highlights, IReadOnlyList<ApexItem> Apexes);

public interface IMovieClipService
{
    /// <summary>The movie's scenes, highlights and apexes as the three services return them, from one load of the
    /// movie's effective actors instead of one per service (Movie Detail reads all three on every load).</summary>
    Task<MovieClips> GetClipsAsync(int movieId, CancellationToken ct = default);
}

public class MovieClipService(IDbContextFactory<AppDbContext> dbFactory, IMovieSceneService sceneService, IMovieHighlightService highlightService, IMovieApexService apexService) : IMovieClipService
{
    public async Task<MovieClips> GetClipsAsync(int movieId, CancellationToken ct = default)
    {
        int[] ids = [movieId];
        IReadOnlyDictionary<int, ClipActorsResult> effective;
        await using (var db = await dbFactory.CreateDbContextAsync(ct))
        {
            effective = await ClipAssignments.LoadEffectiveActorsAsync(db, ids, ct);
        }

        return new MovieClips(
            (await sceneService.GetScenesForMoviesAsync(ids, effective, ct))[movieId],
            (await highlightService.GetHighlightsForMoviesAsync(ids, effective, ct))[movieId],
            (await apexService.GetApexesForMoviesAsync(ids, effective, ct))[movieId]);
    }
}
