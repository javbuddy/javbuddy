using System.Text.Json;
using System.Text.Json.Serialization;
using Javbuddy.Models;
using Javbuddy.Services.Infrastructure;

namespace Javbuddy.Components.Pages.MoviesSections;

/// <summary>The Movies grid's poster display preferences — a per-browser preference kept in its own
/// cookie, not the movie-data filter/sort state, so it's read from the cookie independently of the
/// prerender-persisted state rather than threaded through it. The record is also the cookie's JSON
/// shape.
///
/// Every parameter needs an explicit default: a cookie saved before a new option existed
/// (confirmed live — ShowSize defaulted to false, not true, for a real already-saved cookie the
/// first time this was tested) is missing that JSON property entirely, and System.Text.Json falls
/// back to the constructor parameter's own default for a missing property — bool's implicit
/// default (false) without one, whatever's declared here with one.</summary>
public sealed record MoviePosterOptions(
    string PosterSize = "medium",
    bool ShowTitle = true,
    bool ShowStatus = true,
    bool ShowSize = true,
    bool ShowQuality = true,
    bool ShowVrBadge = true,
    bool ShowVersionsBadge = true)
{
    public const string CookieName = "poster-options";

    public static MoviePosterOptions Default { get; } = new();

    /// <summary>False only when every poster-info toggle is off — PosterCard.razor's ".poster-info"
    /// wrapper still has its own padding regardless of whether title/meta/meta-sub actually render
    /// anything inside it, so with nothing left to show that padding is just dead space under the
    /// status bar. movie-poster-hide-info (see MoviePosterGrid.razor.css) collapses the whole block in that
    /// one case, rather than the padding needing to vary per individual toggle. ShowVrBadge and
    /// ShowVersionsBadge aren't among them: those corner badges sit on the artwork (".poster-image"),
    /// not in ".poster-info".</summary>
    [JsonIgnore]
    public bool AnyInfoVisible => ShowTitle || ShowStatus || ShowSize || ShowQuality;

    /// <summary>"small" and "large" as given; anything else (including a tampered cookie) is "medium".</summary>
    public static string NormalizeSize(string? size) => size is "small" or "large" ? size : "medium";

    /// <summary>Reads the cookie, keeping the defaults when it's missing or unreadable. The accessor
    /// is optional because bUnit's DI container doesn't register ASP.NET Core's real
    /// IHttpContextAccessor.</summary>
    public static MoviePosterOptions Load(IHttpContextAccessor? httpContextAccessor) =>
        Normalize(JsonCookieState.TryLoad<MoviePosterOptions>(httpContextAccessor, CookieName));

    /// <summary>The cookie's value as read from the browser (App.razor's lsPosterOptions.get).</summary>
    public static MoviePosterOptions Parse(string? json) =>
        Normalize(JsonCookieState.TryParse<MoviePosterOptions>(json));

    private static MoviePosterOptions Normalize(MoviePosterOptions? parsed) =>
        parsed is null ? Default : parsed with { PosterSize = NormalizeSize(parsed.PosterSize) };

    /// <summary>"2 versions" for a movie with more than one file version, when the badge is on.</summary>
    public string? VersionsBadge(Movie movie) =>
        ShowVersionsBadge && movie.FileCount > 1 ? $"{movie.FileCount} versions" : null;

    public string ToJson() => JsonSerializer.Serialize(this);

    public string?[] MetaSubLines(Movie movie)
    {
        var lines = new List<string?>();
        // Every line here is shown on every card (null renders as a blank row): the Movies grid is
        // virtualized and needs every card to be the same height, so a line only some cards had made
        // those rows taller and threw the grid's spacers off as it scrolled. The version count is a
        // badge on the artwork instead (VersionsBadge).
        if (ShowSize) lines.Add(movie.LocalFileSizeDisplay);
        if (ShowQuality) lines.Add(MoviePosterText.QualityLabel(movie));
        return lines.ToArray();
    }
}
