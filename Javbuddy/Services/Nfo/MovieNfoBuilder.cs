using System.Globalization;
using System.Xml.Linq;
using Javbuddy.Models;

namespace Javbuddy.Services.Nfo;

/// <summary>Builds a minimal Kodi/Jellyfin-style movie .nfo from a movie's stored metadata, for a movie whose folder
/// has none yet. Only the elements LocalNfoParser reads are written, and empty fields are left out. For the actor and genre
/// elements to match what the drift check expects, load <c>MovieActors</c> (with Actor) and <c>MovieTags</c> (with Tag and
/// ParentTag) on the movie first.</summary>
public static class MovieNfoBuilder
{
    public static string Build(Movie movie)
    {
        var root = new XElement("movie");

        void Add(string name, string? value)
        {
            if (!string.IsNullOrWhiteSpace(value)) root.Add(new XElement(name, value.Trim()));
        }

        var title = !string.IsNullOrWhiteSpace(movie.MetaTitle) ? movie.MetaTitle
            : !string.IsNullOrWhiteSpace(movie.Title) ? movie.Title
            : movie.Code;
        Add("title", title);
        Add("originaltitle", movie.MetaOriginalTitle);
        Add("id", movie.Code);
        Add("plot", movie.MetaDescription);

        if (movie.MetaReleaseDate is { } date)
        {
            root.Add(new XElement("releasedate", date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)));
            root.Add(new XElement("year", date.Year.ToString(CultureInfo.InvariantCulture)));
        }

        var runtime = movie.MetaRuntimeMinutes
            ?? (movie.MediaDurationSeconds is > 0 ? (int)Math.Round(movie.MediaDurationSeconds.Value / 60) : null);
        if (runtime is > 0) root.Add(new XElement("runtime", runtime.Value.ToString(CultureInfo.InvariantCulture)));

        Add("director", movie.MetaDirector);
        Add("studio", movie.MetaStudio);
        Add("label", movie.MetaLabel);
        if (!string.IsNullOrWhiteSpace(movie.MetaSeries))
        {
            root.Add(new XElement("set", new XElement("name", movie.MetaSeries.Trim())));
        }

        // The same values the drift check compares the .nfo against (linked actors' display names, canonical genres incl.
        // "Parent##Child"), so the new file never starts out in conflict. A movie whose actors/tags aren't linked
        // falls back to the raw Meta strings.
        var javbuddy = NfoDriftDetector.ReadMovie(movie);
        var genres = javbuddy.Genres.Count > 0 ? javbuddy.Genres : Split(movie.MetaGenres).ToList();
        var actors = javbuddy.Actors.Select(a => a.Name).OfType<string>().ToList();
        if (actors.Count == 0) actors = Split(movie.MetaActresses).ToList();

        foreach (var genre in genres) root.Add(new XElement("genre", genre));
        foreach (var actor in actors) root.Add(new XElement("actor", new XElement("name", actor)));

        return NfoXmlFormatter.SerializeXmlFormatted(new XDocument(new XDeclaration("1.0", "utf-8", "yes"), root));
    }

    private static IEnumerable<string> Split(string? joined) =>
        (joined ?? string.Empty).Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
}
