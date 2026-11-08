using Javbuddy.Models;
using Javbuddy.Services.Api.Contracts;
using Javbuddy.Services.Movies;

namespace Javbuddy.Services.Api;

/// <summary>Maps stored movies to the public API contracts. Pure.</summary>
public static class MovieApiMapper
{
    /// <summary>The app-relative page for a movie code (the endpoint layer makes it absolute).</summary>
    public static string WebPathFor(string code) => $"/movies/{Uri.EscapeDataString(code)}";

    public static string ContentTypeName(MovieContentType type) => type.ToString().ToLowerInvariant();

    public static string StatusName(MovieStatus status) => status.ToString().ToLowerInvariant();

    public static string DriftKindName(NfoDriftKind kind) => char.ToLowerInvariant(kind.ToString()[0]) + kind.ToString()[1..];

    public static MovieSummary ToSummary(Movie movie)
    {
        var code = movie.Code ?? string.Empty;
        return new MovieSummary(
            movie.Id,
            code,
            movie.MetaTitle,
            StatusName(movie.Status),
            movie.IsFavorite,
            ContentTypeName(movie.ContentType),
            ToDateOnly(movie.MetaReleaseDate),
            movie.MetaStudio,
            movie.FileCount,
            DriftKindName(movie.NfoDriftKind),
            WebPathFor(code));
    }

    public static MovieDetail ToDetail(Movie movie, IEnumerable<MovieTag> tags, IEnumerable<MovieCastEntry> cast, IEnumerable<MovieFile> files)
    {
        var code = movie.Code ?? string.Empty;
        return new MovieDetail(
            movie.Id,
            code,
            movie.MetaTitle,
            movie.Title,
            movie.MetaOriginalTitle,
            movie.MetaDescription,
            ToDateOnly(movie.MetaReleaseDate),
            movie.MetaStudio,
            movie.MetaLabel,
            movie.MetaSeries,
            movie.MetaDirector,
            ContentTypeName(movie.ContentType),
            StatusName(movie.Status),
            movie.IsFavorite,
            movie.MetaRuntimeMinutes,
            movie.MetaRatingScore,
            movie.MetaRatingVotes,
            movie.MetaCoverUrl,
            movie.MetaBackdropUrl,
            movie.VrType,
            tags.Select(t => new MovieTagInfo(t.TagId, t.Tag.Name, t.IsExplicit, t.FromClips, t.Tag.ParentTag?.Name))
                .OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase).ToList(),
            cast.Select(c => new MovieCastInfo(c.Name, c.ActorId, c.ActorId is not null)).ToList(),
            files.OrderByDescending(f => f.IsPrimary).ThenBy(f => f.FileName, StringComparer.OrdinalIgnoreCase)
                .Select(f => new MovieFileInfo(f.Id, f.FileName, f.VersionTag, f.FileSizeBytes, f.IsPrimary, f.VrType)).ToList(),
            new MovieNfoInfo(DriftKindName(movie.NfoDriftKind), movie.NfoConflictDetails),
            WebPathFor(code));
    }

    private static DateOnly? ToDateOnly(DateTime? value) => value is { } v ? DateOnly.FromDateTime(v) : null;
}
