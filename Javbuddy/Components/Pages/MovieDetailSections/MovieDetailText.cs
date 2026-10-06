using Javbuddy.Models;
using Javbuddy.Services.Actors;
using Javbuddy.Services.Movies;
using Javbuddy.Services.Nfo;

namespace Javbuddy.Components.Pages.MovieDetailSections;

/// <summary>Text and links the Movie Detail page derives from a movie and its cast.</summary>
public static class MovieDetailText
{
    public static readonly string UnderageAgeTooltip =
        $"Under {ActorPhysicalAttributesHelper.MinimumActorAge} at release: the release date, the actor's birthdate, or the actor match is likely wrong.";

    /// <summary>Truncates like Jellyfin's own title display — a hard character cap with an
    /// ellipsis, not word-wrapping the whole (sometimes 200+ character) title across lines.</summary>
    public static string Truncate(string text, int maxLength)
    {
        if (string.IsNullOrEmpty(text) || text.Length <= maxLength) return text;
        return text[..maxLength].TrimEnd() + "…";
    }

    /// <summary>The code up to its last hyphen ("ABC" for "ABC-123"), or null when there's none to
    /// split on.</summary>
    public static string? CodePrefix(string? code)
    {
        var separatorIndex = code?.LastIndexOf('-') ?? -1;
        return separatorIndex > 0 ? code![..separatorIndex] : null;
    }

    /// <summary>What the delete confirmation names: "ABC-123 - Title", or just the
    /// display name when it already leads with the code (fetched titles usually do, and an
    /// unfetched movie's display name is the code itself).</summary>
    public static string DeleteLabel(Movie movie) =>
        !string.IsNullOrWhiteSpace(movie.Code) && !movie.DisplayName.StartsWith(movie.Code, StringComparison.OrdinalIgnoreCase)
            ? $"{movie.Code} - {movie.DisplayName}"
            : movie.DisplayName;

    public static string ActorThumbUrl(int? actorId, long? imageVersion) =>
        $"/actor-image/{actorId}/thumb{(imageVersion is { } v ? $"?v={v}" : "")}";

    public static string? MetaFetchedTooltip(Movie movie) => movie.MetaFetchedAt is not null
        ? $"{movie.MetaSourceName ?? "unknown"} · fetched {movie.MetaFetchedAt.Value.ToLocalTime():yyyy-MM-dd HH:mm}"
        : null;

    // Direction first, then the per-field detail lines (each already direction-prefixed).
    public static string NfoDriftTooltip(Movie movie) =>
        $"NFO drift: {NfoDriftDetector.Label(movie.NfoDriftKind)}"
        + (string.IsNullOrWhiteSpace(movie.NfoConflictDetails) ? "" : $" — {movie.NfoConflictDetails}");

    // Movie-level warning: null when no cast member's age at release is under MinimumActorAge.
    public static string? UnderageCastTooltip(IEnumerable<MovieCastEntry> cast)
    {
        var underage = cast.Where(c => c.IsUnderage).Select(c => $"{c.Name} ({c.Age})").ToList();
        return underage.Count == 0
            ? null
            : $"Cast age at release is under {ActorPhysicalAttributesHelper.MinimumActorAge}: {string.Join(", ", underage)}. The release date or a birthdate is likely wrong.";
    }

    public static string JellyfinLinkedTooltip(Movie movie)
    {
        var text = "Linked to a library item";
        if (!string.IsNullOrWhiteSpace(movie.JellyfinLibraryName))
        {
            text += $" in {movie.JellyfinLibraryName}";
        }
        if (movie.JellyfinCheckedAt is not null)
        {
            text += $" (checked {movie.JellyfinCheckedAt.Value.ToLocalTime():yyyy-MM-dd HH:mm})";
        }
        return text;
    }
}
