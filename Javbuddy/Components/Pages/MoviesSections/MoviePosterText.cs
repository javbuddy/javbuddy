using Javbuddy.Models;
using Javbuddy.Services.Movies;

namespace Javbuddy.Components.Pages.MoviesSections;

/// <summary>Text shown on a Movies grid poster that doesn't depend on the display options.</summary>
public static class MoviePosterText
{
    // The version makes a changed poster a new URL, not the browser's cached old one.
    public static string ThumbUrl(string code, string? posterVersion) =>
        $"/image-cache/{Uri.EscapeDataString(code)}/poster/thumb{(posterVersion is null ? "" : $"?v={posterVersion}")}";

    /// <summary>The poster's status border class: a Missing movie with an active or finished
    /// torrent shows as Downloading.</summary>
    public static string StatusCssClass(Movie movie, bool isDownloading) =>
        movie.Status == MovieStatus.Missing && isDownloading ? "status-downloading" : movie.Status.ToPosterCssClass();

    // The VR / 3D format joins the resolution, e.g. "4K · VR180 SBS".
    public static string? QualityLabel(Movie movie)
    {
        var quality = MediaQualityLabelFormatter.FormatPoster(movie.MediaWidth, movie.MediaHeight, movie.MediaScanType);
        return movie.VrType is null ? quality : quality is null ? movie.VrType : $"{quality} · {movie.VrType}";
    }

    /// <summary>Tooltip text for PosterCard's warning badge — unmatched cast names take priority
    /// over "no cast" since HasUnmatchedActors already implies MetaActresses is non-empty.
    /// MetaFetchedAt gates the "no cast" case so a movie that simply hasn't had metadata fetched
    /// yet (MetaActresses null for an unrelated reason) isn't flagged.</summary>
    public static string? ActorWarningTooltip(Movie movie)
    {
        if (movie.HasUnmatchedActors && !string.IsNullOrWhiteSpace(movie.UnmatchedActorNames))
        {
            var count = movie.UnmatchedActorNames.Split(", ", StringSplitOptions.RemoveEmptyEntries).Length;
            return count == 1
                ? $"1 unmatched actor: {movie.UnmatchedActorNames}"
                : $"{count} unmatched actors: {movie.UnmatchedActorNames}";
        }
        if (movie.MetaFetchedAt is not null && string.IsNullOrWhiteSpace(movie.MetaActresses))
        {
            return "No actors listed";
        }
        return null;
    }
}
