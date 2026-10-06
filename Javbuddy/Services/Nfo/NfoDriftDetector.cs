using System.Globalization;
using System.Text.Json;
using System.Xml.Linq;
using Javbuddy.Models;
using Javbuddy.Services.Common;

namespace Javbuddy.Services.Nfo;

/// <summary>Every .nfo field drift detection compares, with actors as one field.</summary>
public enum NfoField
{
    Title,
    OriginalTitle,
    Plot,
    Director,
    Studio,
    Label,
    Series,
    ReleaseDate,
    Rating,
    RatingVotes,
    Runtime,
    Genres,
    Actors,
}

/// <summary>One actor as the drift check sees it: the name plus every other name the .nfo gives
/// it (altname/japanesename/japanese_name and alias elements), which actor matching also
/// accepts. Javbuddy's side uses the linked actor's DisplayName with no other names.</summary>
public sealed record NfoActorEntry(string? Name, IReadOnlyList<string> OtherNames);

/// <summary>The compared field values of one side, Javbuddy or disk — normalized (trimmed, blank
/// as null, dates without time) so a formatting-only .nfo change (whitespace, indentation,
/// element order) never reads as drift. Disk values are also cached in the baseline
/// (NfoBaselineState.LastDisk) so an unchanged .nfo never has to be re-read.</summary>
public sealed record NfoFieldValues(
    string? Title,
    string? OriginalTitle,
    string? Plot,
    string? Director,
    string? Studio,
    string? Label,
    string? Series,
    DateTime? ReleaseDate,
    double? Rating,
    int? RatingVotes,
    int? RuntimeMinutes,
    IReadOnlyList<string> Genres,
    IReadOnlyList<NfoActorEntry> Actors);

/// <summary>One field's values on each side at the last check where that field agreed, as
/// NfoDriftDetector.ValueKey strings.</summary>
public sealed record NfoFieldBaseline(string? Db, string? Disk);

/// <summary>What Movie.NfoBaselineJson stores: the per-field agreement baseline that gives
/// a drift its direction, plus the .nfo's values as last parsed. Movie.NfoBaselineLastWriteUtc/
/// NfoBaselineSize fingerprint the file LastDisk was parsed from.</summary>
public sealed record NfoBaselineState(IReadOnlyDictionary<NfoField, NfoFieldBaseline> Fields, NfoFieldValues? LastDisk)
{
    public string ToJson() => JsonSerializer.Serialize(this);

    /// <summary>Null for a missing or unreadable (e.g. pre-#376 shaped) value — a movie then just
    /// starts over as if never checked.</summary>
    public static NfoBaselineState? FromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            return JsonSerializer.Deserialize<NfoBaselineState>(json);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

public sealed record NfoDriftOutcome(NfoDriftKind Kind, string? Details, NfoBaselineState Baseline);

/// <summary>Pure drift detection: compares Javbuddy's values against the .nfo's, then
/// classifies each drifting field by which side changed since the stored baseline. No I/O —
/// NfoSyncService reads the .nfo (or reuses the cached LastDisk) and persists the outcome.</summary>
public static class NfoDriftDetector
{
    private const char Separator = '\u001f';
    private const char ActorSeparator = '\u001e';

    public static NfoFieldValues ReadNfo(XElement root) => new(
        ReadScalarElement(root, "title"),
        ReadScalarElement(root, "originaltitle"),
        ReadScalarElement(root, "plot"),
        ReadScalarElement(root, "director"),
        ReadScalarElement(root, "studio", "maker"),
        ReadScalarElement(root, "label"),
        ReadSeries(root),
        ReadReleaseDate(root)?.Date,
        ReadRating(root),
        ReadRatingVotes(root),
        ReadRuntimeMinutes(root),
        ReadGenres(root),
        root.Elements("actor").Select(ReadActor).ToList());

    /// <summary>Javbuddy's side. Requires movie.MovieActors (with Actor and its Aliases) and
    /// movie.MovieTags (with Tag and its ParentTag) to be loaded.</summary>
    public static NfoFieldValues ReadMovie(Movie movie) => new(
        movie.MetaTitle.TrimToNull(),
        movie.MetaOriginalTitle.TrimToNull(),
        movie.MetaDescription.TrimToNull(),
        movie.MetaDirector.TrimToNull(),
        movie.MetaStudio.TrimToNull(),
        movie.MetaLabel.TrimToNull(),
        movie.MetaSeries.TrimToNull(),
        movie.MetaReleaseDate?.Date,
        movie.MetaRatingScore,
        movie.MetaRatingVotes,
        movie.MetaRuntimeMinutes,
        CanonicalGenres(movie),
        LinkedActors(movie).Select(a => new NfoActorEntry(a.DisplayName, [])).ToList());

    /// <summary>Classifies drift between <paramref name="movie"/> and the .nfo's <paramref
    /// name="disk"/> values and returns the advanced baseline. Per field:
    ///
    /// - Agreeing (no drift message): the field's baseline advances to both current values.
    /// - Drifting: the baseline is kept, so the direction stays stable until the drift is
    ///   resolved. Javbuddy's value differs from its baseline value and disk's doesn't →
    ///   JavbuddyChanged; the other way round → ExternalEdit; both differ → BothChanged.
    /// - Drifting with no history (no baseline yet, e.g. a library checked for the first time, or
    ///   neither side changed since): a value the .nfo lacks entirely is something Javbuddy has
    ///   that the file doesn't → JavbuddyChanged; a differing value on disk is conservatively
    ///   treated as an outside edit → ExternalEdit.
    ///
    /// <paramref name="preWriteDisk"/> is the .nfo's values just before a Javbuddy write that
    /// produced <paramref name="disk"/>: a field that write changed has its baseline disk value
    /// moved to the written one, so Javbuddy's own write to a still-drifting field (e.g. an actor
    /// sync that fixes one actor but not another) is never mistaken for an outside edit.
    /// <paramref name="strict"/> is forwarded to AppendFieldConflicts — see its remarks.</summary>
    public static NfoDriftOutcome Evaluate(
        Movie movie,
        NfoFieldValues disk,
        NfoBaselineState? baseline,
        bool strict,
        NfoFieldValues? preWriteDisk = null)
    {
        var db = ReadMovie(movie);
        var drifts = new List<(NfoField Field, string Message)>();
        AppendActorConflicts(drifts, movie, disk);
        AppendFieldConflicts(drifts, db, disk, strict);

        var fields = baseline?.Fields.ToDictionary() ?? [];
        var directions = new Dictionary<NfoField, NfoDriftKind>();

        foreach (var field in Enum.GetValues<NfoField>())
        {
            var dbKey = ValueKey(db, field);
            var diskKey = ValueKey(disk, field);

            if (preWriteDisk is not null
                && fields.TryGetValue(field, out var written)
                && ValueKey(preWriteDisk, field) != diskKey)
            {
                fields[field] = written with { Disk = diskKey };
            }

            if (!drifts.Any(d => d.Field == field))
            {
                fields[field] = new NfoFieldBaseline(dbKey, diskKey);
                continue;
            }

            directions[field] = fields.TryGetValue(field, out var entry)
                ? Classify(entry.Db != dbKey, entry.Disk != diskKey, diskKey)
                : Classify(dbChanged: false, diskChanged: false, diskKey);
        }

        var kind = directions.Count == 0 ? NfoDriftKind.None : directions.Values.Max();
        var details = drifts.Count == 0
            ? null
            : string.Join("; ", drifts
                .OrderBy(d => d.Field == NfoField.Actors ? -1 : (int)d.Field)
                .Select(d => $"[{Label(directions[d.Field])}] {d.Message}"));

        return new NfoDriftOutcome(kind, details, new NfoBaselineState(fields, disk));
    }

    private static NfoDriftKind Classify(bool dbChanged, bool diskChanged, string? diskKey) =>
        (dbChanged, diskChanged) switch
        {
            (true, true) => NfoDriftKind.BothChanged,
            (true, _) => NfoDriftKind.JavbuddyChanged,
            (_, true) => NfoDriftKind.ExternalEdit,
            _ => string.IsNullOrEmpty(diskKey) ? NfoDriftKind.JavbuddyChanged : NfoDriftKind.ExternalEdit,
        };

    public static string Label(NfoDriftKind kind) => kind switch
    {
        NfoDriftKind.JavbuddyChanged => "Javbuddy changed",
        NfoDriftKind.ExternalEdit => "External edit",
        NfoDriftKind.BothChanged => "Both changed",
        NfoDriftKind.Unreadable => "Unreadable",
        _ => "No drift",
    };

    /// <summary>A field's value as one comparable string — what the baseline stores per side.
    /// Genres compare as a set; actors as a set of name + other names.</summary>
    private static string? ValueKey(NfoFieldValues values, NfoField field) => field switch
    {
        NfoField.Title => values.Title,
        NfoField.OriginalTitle => values.OriginalTitle,
        NfoField.Plot => values.Plot,
        NfoField.Director => values.Director,
        NfoField.Studio => values.Studio,
        NfoField.Label => values.Label,
        NfoField.Series => values.Series,
        NfoField.ReleaseDate => values.ReleaseDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        NfoField.Rating => values.Rating?.ToString("R", CultureInfo.InvariantCulture),
        NfoField.RatingVotes => values.RatingVotes?.ToString(CultureInfo.InvariantCulture),
        NfoField.Runtime => values.RuntimeMinutes?.ToString(CultureInfo.InvariantCulture),
        NfoField.Genres => string.Join(Separator, values.Genres.Order(StringComparer.OrdinalIgnoreCase)),
        NfoField.Actors => string.Join(ActorSeparator, values.Actors
            .Select(a => string.Join(Separator, [a.Name ?? "", .. a.OtherNames]))
            .Order(StringComparer.Ordinal)),
        _ => throw new ArgumentOutOfRangeException(nameof(field)),
    };

    private static List<Actor> LinkedActors(Movie movie) => movie.MovieActors
        .Select(ma => ma.Actor)
        .Where(a => a != null && !string.IsNullOrWhiteSpace(a.FirstName))
        .ToList();

    private static void AppendActorConflicts(List<(NfoField, string)> drifts, Movie movie, NfoFieldValues disk)
    {
        // Rebuilt as elements so matching stays NfoActorMatching.FindMatchingActorElements' single
        // policy, shared with proposal generation and direct sync.
        var root = new XElement("movie", disk.Actors.Select(a => new XElement(
            "actor",
            a.Name is null ? null : new XElement("name", a.Name),
            a.OtherNames.Select(n => new XElement("alias", n)))));

        var linkedActors = LinkedActors(movie);
        foreach (var actor in linkedActors)
        {
            var canonicalName = actor.DisplayName;
            var matcher = NfoActorMatching.CreateNameMatcher(actor, null);
            var matchingNfoElements = NfoActorMatching.FindMatchingActorElements(root, matcher, linkedActors.Count);

            if (matchingNfoElements.Count > 1)
            {
                drifts.Add((NfoField.Actors, $"Multiple entries in .nfo for '{canonicalName}'"));
            }
            else if (matchingNfoElements.Count == 1)
            {
                var nfoName = matchingNfoElements[0].Element("name")?.Value?.Trim();
                if (!string.IsNullOrWhiteSpace(nfoName) && !string.Equals(nfoName, canonicalName, StringComparison.Ordinal))
                {
                    drifts.Add((NfoField.Actors, $".nfo has '{nfoName}', canonical is '{canonicalName}'"));
                }
            }
            else if (root.Elements("actor").Any())
            {
                drifts.Add((NfoField.Actors, $"Actor '{canonicalName}' missing in .nfo"));
            }
        }
    }

    /// <summary>Appends field-level drift messages for every Javbuddy-managed descriptive
    /// field besides actors — title, original title, plot, director, studio, label, series,
    /// release date, rating, rating votes, runtime, and genres.
    ///
    /// <paramref name="strict"/> controls the metadata-editor-modal fields only (title, original
    /// title, plot, director, studio, label, series, release date, runtime — everything
    /// MovieService.UpdateMetadataAsync can edit): lenient (the default, used by the passive
    /// periodic rescan/CheckMovieNfoConflictAsync) only flags a field when both Javbuddy's
    /// canonical value and the corresponding .nfo element are present and non-empty — a field
    /// Javbuddy hasn't fetched yet, or an element style the on-disk .nfo simply doesn't use, isn't
    /// drift, mirroring the existing actor-conflict rule of only flagging a missing actor when the
    /// .nfo has *some* actor elements at all. Strict (CheckMovieMetadataConflictAsync only) drops
    /// that "both present" requirement for just those fields, because the metadata editor modal
    /// never writes to the .nfo itself — an edit whose target element is blank or entirely
    /// absent needs to surface as a conflict too, not silently vanish. Rating/rating
    /// votes/genres always stay lenient regardless of <paramref name="strict"/>: they aren't
    /// editable through that modal, so this method being called from it is no reason to newly
    /// flag drift in fields it never touched.</summary>
    private static void AppendFieldConflicts(List<(NfoField, string)> drifts, NfoFieldValues db, NfoFieldValues disk, bool strict)
    {
        AppendScalarConflict(drifts, NfoField.Title, "Title", db.Title, disk.Title, strict);
        AppendScalarConflict(drifts, NfoField.OriginalTitle, "Original title", db.OriginalTitle, disk.OriginalTitle, strict);
        AppendScalarConflict(drifts, NfoField.Plot, "Plot", db.Plot, disk.Plot, strict);
        AppendScalarConflict(drifts, NfoField.Director, "Director", db.Director, disk.Director, strict);
        AppendScalarConflict(drifts, NfoField.Studio, "Studio", db.Studio, disk.Studio, strict);
        AppendScalarConflict(drifts, NfoField.Label, "Label", db.Label, disk.Label, strict);
        AppendScalarConflict(drifts, NfoField.Series, "Series", db.Series, disk.Series, strict);

        if (db.ReleaseDate is { } dbDate
            && (strict ? disk.ReleaseDate != dbDate : disk.ReleaseDate is { } d && dbDate != d))
        {
            var nfoDisplay = disk.ReleaseDate is { } shown ? shown.ToString("yyyy-MM-dd") : "(none)";
            drifts.Add((NfoField.ReleaseDate, $"Release date: .nfo has '{nfoDisplay}', canonical is '{dbDate:yyyy-MM-dd}'"));
        }

        if (db.Rating is { } dbRating && disk.Rating is { } nfoRating && Math.Abs(dbRating - nfoRating) > 0.01)
        {
            drifts.Add((NfoField.Rating, $"Rating: .nfo has '{nfoRating}', canonical is '{dbRating}'"));
        }

        if (db.RatingVotes is { } dbVotes && disk.RatingVotes is { } nfoVotes && dbVotes != nfoVotes)
        {
            drifts.Add((NfoField.RatingVotes, $"Rating votes: .nfo has '{nfoVotes}', canonical is '{dbVotes}'"));
        }

        if (db.RuntimeMinutes is { } dbRuntime
            && (strict ? disk.RuntimeMinutes != dbRuntime : disk.RuntimeMinutes is { } r && dbRuntime != r))
        {
            var nfoDisplay = disk.RuntimeMinutes is { } shown ? shown.ToString(CultureInfo.InvariantCulture) : "(none)";
            drifts.Add((NfoField.Runtime, $"Runtime: .nfo has '{nfoDisplay}' minutes, canonical is '{dbRuntime}' minutes"));
        }

        if (db.Genres.Count > 0 && disk.Genres.Count > 0
            && !new HashSet<string>(db.Genres, StringComparer.OrdinalIgnoreCase).SetEquals(disk.Genres))
        {
            drifts.Add((NfoField.Genres, $"Genres: .nfo has '{string.Join(", ", disk.Genres)}', canonical is '{string.Join(", ", db.Genres)}'"));
        }
    }

    private static void AppendScalarConflict(List<(NfoField, string)> drifts, NfoField field, string label, string? canonicalValue, string? nfoValue, bool strict)
    {
        if (canonicalValue is null) return;
        if (!strict && nfoValue is null) return;
        if (string.Equals(canonicalValue, nfoValue, StringComparison.Ordinal)) return;

        drifts.Add((field, $"{label}: .nfo has '{nfoValue ?? "(none)"}', canonical is '{canonicalValue}'"));
    }

    /// <summary>Reads the first non-empty element among <paramref name="names"/> — supports the
    /// same "more than one dialect in the wild" element-name fallbacks as LocalLibraryClient's
    /// own LocalNfoParser (e.g. studio/maker).</summary>
    private static string? ReadScalarElement(XElement root, params string[] names)
    {
        foreach (var name in names)
        {
            var value = root.Element(name)?.Value?.Trim();
            if (!string.IsNullOrWhiteSpace(value)) return value;
        }
        return null;
    }

    private static string? ReadSeries(XElement root) =>
        root.Element("set")?.Element("name")?.Value?.Trim() is { Length: > 0 } nested
            ? nested
            : ReadScalarElement(root, "set");

    private static double? ReadRating(XElement root)
    {
        var raw = root.Element("ratings")?.Element("rating")?.Element("value")?.Value
            ?? root.Element("rating")?.Value;
        return double.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed) ? parsed : null;
    }

    private static int? ReadRatingVotes(XElement root) =>
        int.TryParse(root.Element("votes")?.Value, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;

    private static DateTime? ReadReleaseDate(XElement root)
    {
        var raw = ReadScalarElement(root, "releasedate", "premiered");
        if (raw is not null && DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedDate))
        {
            return parsedDate;
        }
        if (int.TryParse(ReadScalarElement(root, "year"), out var year) && year > 0)
        {
            return new DateTime(year, 1, 1);
        }
        return null;
    }

    private static int? ReadRuntimeMinutes(XElement root) =>
        int.TryParse(ReadScalarElement(root, "runtime"), NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;

    private static List<string> ReadGenres(XElement root) =>
        root.Elements("genre").Select(e => e.Value.Trim()).Where(v => v.Length > 0).ToList();

    /// <summary>Mirrors FindMatchingActorElements' reads: the name, the first of
    /// altname/japanesename/japanese_name (by element presence, as there), and every alias.</summary>
    private static NfoActorEntry ReadActor(XElement actorEl)
    {
        var otherNames = new List<string>();
        var altName = actorEl.Element("altname")?.Value?.Trim()
            ?? actorEl.Element("japanesename")?.Value?.Trim()
            ?? actorEl.Element("japanese_name")?.Value?.Trim();
        if (!string.IsNullOrWhiteSpace(altName)) otherNames.Add(altName);
        otherNames.AddRange(actorEl.Elements("alias")
            .Select(e => e.Value?.Trim())
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v => v!));

        return new NfoActorEntry(actorEl.Element("name")?.Value?.Trim(), otherNames);
    }

    /// <summary>The movie's canonical genre names sorted alphabetically, read from the real
    /// Tags/MovieTags relation — not Movie.MetaGenres's comma-joined cache string, which can't
    /// tell a Tag name containing its own comma (e.g. "Nasty, hardcore") apart from two separate
    /// genres once it's been joined into that string. Requires the caller to have
    /// loaded movie.MovieTags with its Tag navigation. The sort itself makes both the conflict
    /// message and (more importantly) the elements NfoSyncService.UpdateGenreElements writes into
    /// the .nfo deterministic.</summary>
    public static List<string> CanonicalGenres(Movie movie)
    {
        var genres = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var mt in movie.MovieTags)
        {
            if (mt.Tag.ParentTag != null)
            {
                genres.Add(mt.Tag.ParentTag.Name);
                genres.Add($"{mt.Tag.ParentTag.Name}{Tag.HierarchyDelimiter}{mt.Tag.Name}");
            }
            else
            {
                genres.Add(mt.Tag.Name);
            }
        }

        return genres.OrderBy(g => g, StringComparer.OrdinalIgnoreCase).ToList();
    }
}
