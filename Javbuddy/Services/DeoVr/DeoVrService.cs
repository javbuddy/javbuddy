using Javbuddy.Data;
using Javbuddy.Models;
using Javbuddy.Services.LocalLibrary;
using Javbuddy.Services.Movies;
using Javbuddy.Services.Scenes;
using Javbuddy.Services.Trickplay;
using Microsoft.EntityFrameworkCore;

namespace Javbuddy.Services.DeoVr;

/// <summary>Where a movie's DeoVR thumbnail comes from: a local file, else a remote URL.</summary>
public sealed record DeoVrThumbnail(string? FilePath, string? RedirectUrl);

/// <summary>Builds DeoVR's JSON documents: the /deovr list (one list per group, in
/// order) and one movie's document, with every version as an encoding and the
/// movie's scenes as chapters and its trickplay as the timeline preview.</summary>
public interface IDeoVrService
{
    /// <summary>The effective settings, or null while the integration is off.</summary>
    Task<DeoVrSettings?> GetEnabledSettingsAsync(CancellationToken ct = default);

    /// <param name="baseUrl">The address the request came in on, without a trailing slash.</param>
    Task<DeoVrScenesResponse> GetScenesAsync(string baseUrl, CancellationToken ct = default);

    /// <summary>Null when the movie doesn't exist or has no local video file.</summary>
    Task<DeoVrVideoResponse?> GetVideoAsync(int movieId, string baseUrl, CancellationToken ct = default);

    Task<DeoVrThumbnail?> GetThumbnailAsync(int movieId, CancellationToken ct = default);
}

public sealed class DeoVrService(
    IDbContextFactory<AppDbContext> dbFactory,
    IDeoVrSettingsService settingsService,
    IDeoVrGroupService groupService,
    IMovieGridQueryService gridQuery,
    ILocalLibraryClient localLibraryClient,
    ITrickplayStore trickplayStore) : IDeoVrService
{
    // Local fanart DeoVR can be expected to decode; a WebP fanart falls through to the remote cover.
    private static readonly string[] ThumbnailCandidates = ["fanart.jpg", "fanart.jpeg", "fanart.png"];

    public async Task<DeoVrSettings?> GetEnabledSettingsAsync(CancellationToken ct = default)
    {
        var settings = await settingsService.GetEffectiveAsync(ct);
        return settings.Enabled ? settings : null;
    }

    public async Task<DeoVrScenesResponse> GetScenesAsync(string baseUrl, CancellationToken ct = default)
    {
        // With every group deleted this is an empty list; DeoVR then shows nothing to browse.
        var scenes = new List<DeoVrScene>();
        foreach (var group in await groupService.ListAsync(ct))
        {
            // A new seed per request, so a Random group reshuffles each time DeoVR reloads the list.
            var sort = group.Sort with { RandomSeed = Random.Shared.Next(1, int.MaxValue) };
            scenes.Add(new DeoVrScene(group.Name, ToListItems(await gridQuery.GetWithFilesAsync(group.Filter, sort, ct), baseUrl)));
        }
        return new DeoVrScenesResponse(scenes);
    }

    public async Task<DeoVrVideoResponse?> GetVideoAsync(int movieId, string baseUrl, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var movie = await db.Movies.AsNoTracking().Include(m => m.MovieFiles).FirstOrDefaultAsync(m => m.Id == movieId, ct);
        if (movie is null || movie.MovieFiles.Count == 0) return null;

        var scenes = await db.Scenes.AsNoTracking().Where(s => s.MovieId == movieId).ToListAsync(ct);
        var files = movie.MovieFiles
            .OrderByDescending(f => f.IsPrimary)
            .ThenBy(f => f.FileName, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var primary = files[0];
        var projection = DeoVrProjection.FromVrType(primary.VrType, primary.FileName);

        // DeoVR takes one timeline per video, so every version shares the main file's trickplay
        //; only a locally generated set has sheets to build it from.
        var identity = TrickplayIdentity.For(primary.FileName, primary.DurationSeconds, primary.Width, primary.Height);
        var timeline = identity is not null && !string.IsNullOrWhiteSpace(movie.Code) && await trickplayStore.GetSetAsync(movie.Code, identity, ct) is not null
            ? DeoVrUrls.Timeline(baseUrl, movie.Id, identity)
            : null;

        return new DeoVrVideoResponse(
            Id: movie.Id,
            Title: Title(movie),
            VideoLength: (int)(primary.DurationSeconds ?? LengthSeconds(movie)),
            ScreenType: projection.ScreenType,
            StereoMode: projection.StereoMode,
            ThumbnailUrl: DeoVrUrls.Thumbnail(baseUrl, movie.Id),
            Encodings: files
                .Select(f => new DeoVrEncoding(
                    MovieVersionParser.FormatVersionLabel(f),
                    [new DeoVrVideoSource(f.Height ?? 0, DeoVrUrls.Stream(baseUrl, movie.Id, f.Id, f.FileName))]))
                .ToList(),
            TimeStamps: SceneRanges.ResolveEffectiveRanges(scenes, null)
                .Select(s => new DeoVrTimestamp((int)s.Scene.StartSeconds, s.DisplayTitle))
                .ToList(),
            TimelinePreview: timeline);
    }

    public async Task<DeoVrThumbnail?> GetThumbnailAsync(int movieId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var movie = await db.Movies.AsNoTracking()
            .Where(m => m.Id == movieId)
            .Select(m => new { m.Code, m.MetaCoverUrl })
            .FirstOrDefaultAsync(ct);
        if (movie is null || string.IsNullOrWhiteSpace(movie.Code)) return null;

        var local = await localLibraryClient.ResolveFirstExistingLocalFilePathAsync(movie.Code, ThumbnailCandidates, ct);
        if (local is not null) return new DeoVrThumbnail(local, null);
        return Uri.TryCreate(movie.MetaCoverUrl, UriKind.Absolute, out var remote) && (remote.Scheme == Uri.UriSchemeHttp || remote.Scheme == Uri.UriSchemeHttps)
            ? new DeoVrThumbnail(null, remote.AbsoluteUri)
            : null;
    }

    private static List<DeoVrListItem> ToListItems(IEnumerable<Movie> movies, string baseUrl) =>
        movies
            .Select(m => new DeoVrListItem(Title(m), (int)LengthSeconds(m), DeoVrUrls.Video(baseUrl, m.Id), DeoVrUrls.Thumbnail(baseUrl, m.Id)))
            .ToList();

    /// <summary>"CODE Title": the title as-is when it already starts with the code (some scrapers
    /// put it there), just the code when there's no title.</summary>
    private static string Title(Movie movie)
    {
        var name = movie.DisplayName;
        return string.IsNullOrWhiteSpace(movie.Code) || name.StartsWith(movie.Code, StringComparison.OrdinalIgnoreCase)
            ? name
            : $"{movie.Code} {name}";
    }

    private static double LengthSeconds(Movie movie) =>
        movie.MediaDurationSeconds ?? (movie.MetaRuntimeMinutes ?? 0) * 60.0;
}
