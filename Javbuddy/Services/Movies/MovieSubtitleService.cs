namespace Javbuddy.Services.Movies;

/// <summary>A subtitle track the player offers, with the URL its &lt;track&gt; loads.</summary>
public sealed record SubtitleTrack(string Url, string Label, string? Language);

/// <summary>The external subtitles beside a movie's video file. The file is the one
/// <see cref="IMovieStreamService"/> resolves inside a library root, and a subtitle is only ever picked
/// by its index among that file's siblings, never by a path from the request.</summary>
public interface IMovieSubtitleService
{
    /// <summary>The subtitles beside the movie's main file, or its version <paramref name="fileId"/>; empty when there are none.</summary>
    Task<IReadOnlyList<SubtitleTrack>> GetTracksAsync(int movieId, int? fileId = null, CancellationToken ct = default);

    /// <summary>One of those subtitles as WebVTT, or null when the index is out of range.</summary>
    Task<string?> GetWebVttAsync(int movieId, int? fileId, int index, CancellationToken ct = default);
}

public sealed class MovieSubtitleService(IMovieStreamService streamService) : IMovieSubtitleService
{
    public static string SubtitleUrl(int movieId, int? fileId, int index) =>
        fileId is null ? $"/api/movies/{movieId}/subtitles/{index}" : $"/api/movies/{movieId}/subtitles/{index}?fileId={fileId}";

    public async Task<IReadOnlyList<SubtitleTrack>> GetTracksAsync(int movieId, int? fileId = null, CancellationToken ct = default) =>
        (await FindAsync(movieId, fileId, ct))
            .Select(s => new SubtitleTrack(SubtitleUrl(movieId, fileId, s.Index), s.Label, s.Language))
            .ToList();

    public async Task<string?> GetWebVttAsync(int movieId, int? fileId, int index, CancellationToken ct = default)
    {
        var file = (await FindAsync(movieId, fileId, ct)).FirstOrDefault(s => s.Index == index);
        if (file is null) return null;
        try
        {
            return await Task.Run(() => SubtitleFiles.ToWebVtt(file.Path), ct);
        }
        catch (IOException)
        {
            // Gone between listing and reading.
            return null;
        }
    }

    private async Task<IReadOnlyList<SubtitleFile>> FindAsync(int movieId, int? fileId, CancellationToken ct)
    {
        var path = fileId is { } id
            ? await streamService.GetFilePathAsync(movieId, id, ct)
            : await streamService.GetMainFilePathAsync(movieId, ct);
        return path is null ? [] : SubtitleFiles.Find(path);
    }
}
