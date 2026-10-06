using System.Globalization;
using System.Xml.Linq;
using Javbuddy.Models;

namespace Javbuddy.Services.Nfo;

/// <summary>Writes a movie's canonical descriptive fields onto an .nfo's XML (title, plot, studio, series,
/// genres, release date, runtime). Only elements the movie has a value for are touched, and the return value
/// says whether anything changed.</summary>
public static class NfoFieldSync
{
    /// <summary>Reconciles the same non-actor fields NfoDriftDetector compares onto
    /// <paramref name="root"/> in place, so GenerateProposedNfoAsync's diff actually shows every
    /// field the conflict detector flags, not just actors — title, original title, plot,
    /// director, studio/label, series, genres, release date, and runtime. Only updates an
    /// element that already exists on disk with some value — never invents a brand-new element
    /// for a field the on-disk .nfo doesn't have at all (same scope boundary as
    /// NfoDriftDetector.AppendFieldConflicts). Returns whether anything actually changed, so callers that persist
    /// to disk (SyncMovieMetadataToNfoAsync) can skip an unnecessary write/timestamp bump.</summary>
    public static bool ApplyFieldUpdates(Movie movie, XElement root)
    {
        var changed = false;
        changed |= UpdateScalarElement(root, movie.MetaTitle, "title");
        changed |= UpdateScalarElement(root, movie.MetaOriginalTitle, "originaltitle");
        changed |= UpdateScalarElement(root, movie.MetaDescription, "plot");
        changed |= UpdateScalarElement(root, movie.MetaDirector, "director");
        changed |= UpdateScalarElement(root, movie.MetaStudio, "studio", "maker");
        changed |= UpdateScalarElement(root, movie.MetaLabel, "label");
        changed |= UpdateSeriesElement(root, movie.MetaSeries);
        changed |= UpdateGenreElements(root, NfoDriftDetector.CanonicalGenres(movie));
        changed |= UpdateReleaseDateElement(root, movie.MetaReleaseDate);
        changed |= UpdateRuntimeElement(root, movie.MetaRuntimeMinutes);
        return changed;
    }

    private static bool UpdateScalarElement(XElement root, string? canonicalValue, params string[] names)
    {
        if (string.IsNullOrWhiteSpace(canonicalValue)) return false;
        var trimmed = canonicalValue.Trim();

        foreach (var name in names)
        {
            var element = root.Element(name);
            if (element is null || string.IsNullOrWhiteSpace(element.Value)) continue;

            if (string.Equals(element.Value.Trim(), trimmed, StringComparison.Ordinal)) return false;

            element.Value = trimmed;
            return true;
        }

        return false;
    }

    private static bool UpdateSeriesElement(XElement root, string? canonicalValue)
    {
        if (string.IsNullOrWhiteSpace(canonicalValue)) return false;
        var trimmed = canonicalValue.Trim();

        var nestedNameEl = root.Element("set")?.Element("name");
        if (nestedNameEl is not null && !string.IsNullOrWhiteSpace(nestedNameEl.Value))
        {
            if (string.Equals(nestedNameEl.Value.Trim(), trimmed, StringComparison.Ordinal)) return false;
            nestedNameEl.Value = trimmed;
            return true;
        }

        var setEl = root.Element("set");
        if (setEl is not null && !setEl.HasElements && !string.IsNullOrWhiteSpace(setEl.Value)
            && !string.Equals(setEl.Value.Trim(), trimmed, StringComparison.Ordinal))
        {
            setEl.Value = trimmed;
            return true;
        }

        return false;
    }

    /// <summary>Replaces the on-disk &lt;genre&gt; element set with the canonical list when they
    /// differ, mirroring ConsolidateAndUpdateActorElements' primary/secondary shape: the first
    /// existing &lt;genre&gt; element becomes the anchor, extras are removed, and any additional
    /// canonical genres are inserted right after it.</summary>
    private static bool UpdateGenreElements(XElement root, List<string> canonicalGenres)
    {
        var genreElements = root.Elements("genre").ToList();
        if (canonicalGenres.Count == 0 || genreElements.Count == 0) return false;

        var currentGenres = genreElements.Select(e => e.Value.Trim()).Where(v => v.Length > 0).ToList();
        if (new HashSet<string>(currentGenres, StringComparer.OrdinalIgnoreCase).SetEquals(canonicalGenres))
        {
            return false;
        }

        var anchor = genreElements[0];
        foreach (var extra in genreElements.Skip(1))
        {
            var prevText = extra.PreviousNode as XText;
            if (prevText is not null && string.IsNullOrWhiteSpace(prevText.Value))
            {
                prevText.Remove();
            }
            extra.Remove();
        }

        anchor.Value = canonicalGenres[0];
        for (int i = canonicalGenres.Count - 1; i >= 1; i--)
        {
            anchor.AddAfterSelf(new XElement("genre", canonicalGenres[i]));
        }

        return true;
    }

    /// <summary>Writes the canonical release date into whichever of &lt;releasedate&gt;/
    /// &lt;premiered&gt; the on-disk .nfo already has a value in — matching NfoDriftDetector.ReadReleaseDate's own
    /// fallback order — formatted the same way javinizer-go's own scrapers write it
    /// (<c>yyyy-MM-dd</c>). Never invents either element, same scope boundary as the rest of
    /// ApplyFieldUpdates.</summary>
    private static bool UpdateReleaseDateElement(XElement root, DateTime? canonicalDate)
    {
        if (canonicalDate is not { } date) return false;
        var formatted = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        foreach (var name in new[] { "releasedate", "premiered" })
        {
            var element = root.Element(name);
            if (element is null || string.IsNullOrWhiteSpace(element.Value)) continue;

            if (string.Equals(element.Value.Trim(), formatted, StringComparison.Ordinal)) return false;

            element.Value = formatted;
            return true;
        }

        return false;
    }

    private static bool UpdateRuntimeElement(XElement root, int? canonicalRuntime)
    {
        if (canonicalRuntime is not { } runtime) return false;

        var element = root.Element("runtime");
        if (element is null || string.IsNullOrWhiteSpace(element.Value)) return false;

        var formatted = runtime.ToString(CultureInfo.InvariantCulture);
        if (string.Equals(element.Value.Trim(), formatted, StringComparison.Ordinal)) return false;

        element.Value = formatted;
        return true;
    }
}
