using System.Globalization;
using System.Xml.Linq;
using Javbuddy.Models;

namespace Javbuddy.Services.Nfo;

/// <summary>Writes a movie's canonical descriptive fields onto an .nfo's XML (title, original title, plot, director,
/// studio, label, series, genres, release date, runtime). Only fields the movie has a value for are touched, and the
/// return value says whether anything changed. An element the file lacks (or has empty) is created.</summary>
public static class NfoFieldSync
{
    /// <summary>Reconciles the same non-actor fields NfoDriftDetector compares onto
    /// <paramref name="root"/> in place, so GenerateProposedNfoAsync's diff actually shows every
    /// field the conflict detector flags, not just actors. A field the movie has a value for is
    /// written to its existing element, or the element is created when the file has none (or an
    /// empty one) — the drift check flags exactly those cases. Returns whether anything actually
    /// changed, so callers that persist to disk (SyncMovieMetadataToNfoAsync) can skip an
    /// unnecessary write/timestamp bump.</summary>
    public static bool ApplyFieldUpdates(Movie movie, XElement root)
    {
        var changed = false;
        // What the detector already reads from the file: a value that parses equal (a <year>-only date, "120.0"
        // minutes, a date with a time) is in sync, so it is not rewritten.
        var disk = NfoDriftDetector.ReadNfo(root);
        changed |= UpdateOrCreateElement(root, movie.MetaTitle, ["title"], ["id"]);
        changed |= UpdateOrCreateElement(root, movie.MetaOriginalTitle, ["originaltitle"], ["title", "id"]);
        changed |= UpdateOrCreateElement(root, movie.MetaDescription, ["plot"], ["originaltitle", "title", "id"]);
        changed |= UpdateOrCreateElement(root, movie.MetaDirector, ["director"], ["runtime", "plot", "title"]);
        changed |= UpdateOrCreateElement(root, movie.MetaStudio, ["studio", "maker"], ["director", "runtime", "plot", "title"]);
        changed |= UpdateOrCreateElement(root, movie.MetaLabel, ["label"], ["studio", "maker", "director", "plot", "title"]);
        changed |= UpdateSeriesElement(root, movie.MetaSeries);
        changed |= UpdateGenreElements(root, NfoDriftDetector.CanonicalGenres(movie));
        changed |= UpdateOrCreateElement(root,
            disk.ReleaseDate == movie.MetaReleaseDate?.Date
                ? null
                : movie.MetaReleaseDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["releasedate", "premiered"], ["plot", "originaltitle", "title"]);
        changed |= UpdateOrCreateElement(root,
            disk.RuntimeMinutes == movie.MetaRuntimeMinutes ? null : movie.MetaRuntimeMinutes?.ToString(CultureInfo.InvariantCulture),
            ["runtime"], ["releasedate", "premiered", "plot", "title"]);
        return changed;
    }

    /// <summary>Writes a scalar field onto the first of <paramref name="names"/> (the dialects an .nfo may use, e.g.
    /// studio/maker) that has a value. A file with none of them filled gets it: an empty element is filled in
    /// place, a missing one is created as <c>names[0]</c> right after the first of <paramref name="anchors"/> the
    /// file has, else as the root's first child. A value is added or fetched in Javbuddy after an import from a
    /// file that lacked it, and must reach disk.</summary>
    private static bool UpdateOrCreateElement(XElement root, string? canonicalValue, string[] names, string[] anchors)
    {
        if (string.IsNullOrWhiteSpace(canonicalValue)) return false;
        var trimmed = canonicalValue.Trim().Replace("\r\n", "\n");

        foreach (var name in names)
        {
            var filled = root.Element(name);
            if (filled is null || string.IsNullOrWhiteSpace(filled.Value)) continue;
            if (string.Equals(filled.Value.Trim(), trimmed, StringComparison.Ordinal)) return false;
            filled.Value = trimmed;
            return true;
        }

        var empty = names.Select(n => root.Element(n)).FirstOrDefault(e => e is not null);
        if (empty is not null)
        {
            empty.Value = trimmed;
            return true;
        }

        InsertScalar(root, new XElement(names[0], trimmed), anchors);
        return true;
    }

    /// <summary>Inserts <paramref name="created"/> after the first of <paramref name="anchors"/> the file has, else
    /// as the root's first child, copying the file's own indentation so a document loaded with whitespace
    /// preserved keeps its shape.</summary>
    private static void InsertScalar(XElement root, XElement created, string[] anchors)
    {
        var after = anchors.Select(a => root.Element(a)).FirstOrDefault(e => e is not null);
        if (after is not null)
        {
            var indent = after.PreviousNode is XText text && string.IsNullOrWhiteSpace(text.Value) ? text.Value : null;
            after.AddAfterSelf(created);
            if (indent is not null) after.AddAfterSelf(new XText(indent));
            return;
        }

        var first = root.Elements().FirstOrDefault();
        if (first is null)
        {
            root.AddFirst(created);
            return;
        }

        var firstIndent = first.PreviousNode is XText t && string.IsNullOrWhiteSpace(t.Value) ? t.Value : null;
        first.AddBeforeSelf(created);
        if (firstIndent is not null) first.AddBeforeSelf(new XText(firstIndent));
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

        if (setEl is null)
        {
            InsertScalar(root, new XElement("set", new XElement("name", trimmed)), ["label", "studio", "maker", "director", "plot", "title"]);
            return true;
        }

        // An empty <name/> is filled in place; a <set> with other children but no <name> gets one; an empty <set/>
        // gets the flat text value the detector also reads.
        if (nestedNameEl is not null)
        {
            nestedNameEl.Value = trimmed;
            return true;
        }

        if (setEl.HasElements)
        {
            setEl.AddFirst(new XElement("name", trimmed));
            return true;
        }

        if (!string.IsNullOrWhiteSpace(setEl.Value)) return false;
        setEl.Value = trimmed;
        return true;
    }

    /// <summary>Replaces the on-disk &lt;genre&gt; element set with the canonical list when they
    /// differ, mirroring ConsolidateAndUpdateActorElements' primary/secondary shape: the first
    /// existing &lt;genre&gt; element becomes the anchor, extras are removed, and any additional
    /// canonical genres are inserted right after it. An .nfo with no &lt;genre&gt; element at all gets them
    /// created: tags are added in Javbuddy, so a file that
    /// has none yet must be able to receive them.</summary>
    private static bool UpdateGenreElements(XElement root, List<string> canonicalGenres)
    {
        var genreElements = root.Elements("genre").ToList();
        if (canonicalGenres.Count == 0) return false;
        if (genreElements.Count == 0)
        {
            InsertGenreElements(root, canonicalGenres);
            return true;
        }

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

    /// <summary>Adds a &lt;genre&gt; per name to an .nfo that has none: right before the first &lt;actor&gt; (where
    /// scrapers write them), else after the last element, copying the file's own indentation so a document loaded
    /// with whitespace preserved keeps its shape.</summary>
    private static void InsertGenreElements(XElement root, List<string> genres)
    {
        var before = root.Element("actor");
        var reference = before ?? root.Elements().LastOrDefault();
        if (reference is null)
        {
            root.Add(genres.Select(g => new XElement("genre", g)));
            return;
        }

        var indent = reference.PreviousNode is XText text && string.IsNullOrWhiteSpace(text.Value) ? text.Value : null;
        if (before is not null)
        {
            foreach (var genre in genres)
            {
                before.AddBeforeSelf(new XElement("genre", genre));
                if (indent is not null) before.AddBeforeSelf(new XText(indent));
            }
            return;
        }

        for (var i = genres.Count - 1; i >= 0; i--)
        {
            reference.AddAfterSelf(new XElement("genre", genres[i]));
            if (indent is not null) reference.AddAfterSelf(new XText(indent));
        }
    }
}
