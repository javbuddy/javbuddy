using Javbuddy.Data;
using Javbuddy.Models;
using Javbuddy.Services.Scenes;
using Microsoft.EntityFrameworkCore;

namespace Javbuddy.Services.Tags;

/// <summary>Applies tag replacement rules and the ignore list to a movie's raw scraped/imported
/// genre values, maintaining the canonical Tag/MovieTag catalog and keeping Movie.MetaGenres (the
/// Movies grid's fast-filter cache) in sync with it. Mirrors MovieActorAssociation's shape: pure
/// static methods operating on a caller-supplied AppDbContext so they compose with an existing
/// SaveChanges flow rather than owning their own.</summary>
public static class TagNormalization
{
    // Serializes "does this brand-new tag name already exist" across concurrent callers.
    // LibraryRescanTask/ImportNewMoviesAsync imports up to MaxConcurrency movies in parallel,
    // each on its own AppDbContext — without this, two imports discovering the same never-seen
    // genre value at once could both decide to insert a new Tag with that Name and race on the
    // unique index, throwing a DbUpdateException that aborts the whole batch's Task.WhenAll.
    private static readonly SemaphoreSlim NewTagCreationLock = new(1, 1);

    /// <summary>Reprocessing hook: re-resolves movie.MetaGenres's raw comma-joined value into
    /// canonical Tags. Only usable when there's no fresher raw genre list available (e.g. a rescan
    /// step re-running normalization against unchanged metadata to pick up newly added rules) —
    /// splitting an already comma-joined value can't distinguish one genre whose own name contains
    /// a comma from two separate genres, so any caller that just applied fresh metadata and
    /// still has the original raw genre-name list in hand should call the
    /// <see cref="ApplyToMovieAsync(AppDbContext, Movie, IEnumerable{string?}, CancellationToken)"/>
    /// overload below instead.</summary>
    public static Task ApplyToMovieAsync(AppDbContext db, Movie movie, CancellationToken ct = default) =>
        ApplyToMovieAsync(db, movie, SplitGenres(movie.MetaGenres), ct);

    /// <summary>Ingestion hook: resolves canonical Tags directly from <paramref name="rawGenres"/>
    /// (applying the ignore list then replacement rules, creating any genuinely new Tag with
    /// NeedsReview set for the Tags page's discovery review), syncs MovieTag links to match, and
    /// rewrites MetaGenres from the resolved canonical names. Call right after
    /// MovieMetadataMapper.Apply / LocalLibraryMetadataMapper.Apply set movie.MetaGenres, passing
    /// the same raw genre-name list the mapper was given — not movie.MetaGenres's already
    /// comma-joined value, which can no longer tell a genre named e.g. "Nasty, hardcore" apart from
    /// two separate genres.</summary>
    public static async Task ApplyToMovieAsync(AppDbContext db, Movie movie, IEnumerable<string?> rawGenres, CancellationToken ct = default)
    {
        await NewTagCreationLock.WaitAsync(ct);
        try
        {
            // Loading the current tag catalog and resolving/creating against it both happen
            // while holding the lock, then it's flushed before releasing — so the next caller's
            // own LoadContextAsync (which can't run until it acquires the lock) always sees
            // whatever this call just committed, instead of working from a stale snapshot.
            var context = await LoadContextAsync(db, ct);
            var normalized = rawGenres
                .Select(v => v?.Trim())
                .Where(v => !string.IsNullOrEmpty(v))
                .Select(v => v!)
                .ToList();
            await ApplyToMovieAsync(db, movie, normalized, context, existingLinks: null, ct);
            await db.SaveChangesAsync(ct);
        }
        finally
        {
            NewTagCreationLock.Release();
        }
    }

    /// <summary>Reprocesses every movie's current Movie.MetaGenres against the current rules/
    /// ignore list. Backs three distinct Tags-page actions that all reduce to the same
    /// operation: the initial library discovery/backfill pass, "Apply Rules to Library" (a newly
    /// added rule matches text already resolved into a Tag with that exact name, since
    /// MetaGenres always reflects the current canonical name), and "Strip Ignored Tags from
    /// Library". Applies the same rules to scene tags. Also deletes any
    /// now-orphaned Tag matching the ignore list or superseded by a replacement rule's source
    /// value. Returns the number of movies reprocessed.</summary>
    public static async Task<int> ApplyToLibraryAsync(AppDbContext db, CancellationToken ct = default)
    {
        var context = await LoadContextAsync(db, ct);
        var movies = await db.Movies
            .Where(m => m.MetaGenres != null && m.MetaGenres != "")
            .ToListAsync(ct);

        // One query for every movie's current links instead of one query per movie — the same
        // upfront-load-then-look-up-in-memory shape MovieActorAssociation.SynchronizeAllAsync
        // uses, since this loop can run against the whole library at once.
        var linksByMovie = (await db.MovieTags.ToListAsync(ct))
            .GroupBy(link => link.MovieId)
            .ToDictionary(group => group.Key, group => group.ToList());

        foreach (var movie in movies)
        {
            await ApplyToMovieAsync(db, movie, SplitGenres(movie.MetaGenres), context, linksByMovie.GetValueOrDefault(movie.Id, []), ct);
        }

        await ApplyToSceneTagsAsync(db, context, ct);

        // Flush the per-movie MovieTag and SceneTag adds/removes above before the orphan check below queries
        // MovieTags — that query hits the database directly, so it would otherwise still see
        // now-removed links that only exist as pending deletes in the change tracker.
        await db.SaveChangesAsync(ct);

        await RefreshClipTagsAsync(db, ct);
        // Self-heal for the stored effective actors too: every movie is recomputed by the worker,
        // which the caller wakes.
        await ClipActorStale.MarkAllAsync(db, ct);

        // A tag banned by the ignore list, or superseded by a replacement rule matching its own
        // name, is unlinked from every movie above — but its now-orphaned catalog row would
        // otherwise linger. Only prune ones that are actually unused now: a same-named Tag some
        // other movie still legitimately carries (not reachable through this reprocessing pass,
        // e.g. one with a blank MetaGenres) should survive.
        var supersededCandidates = context.KnownTags
            .Where(t => t.Id != 0 && (
                context.Ignored.Any(i => Matches(i.Value, i.MatchMode, t.Name))
                || context.Rules.Any(r => Matches(r.SourceValue, r.MatchMode, t.Name))))
            .ToList();

        if (supersededCandidates.Count > 0)
        {
            var candidateIds = supersededCandidates.Select(t => t.Id).ToHashSet();
            // A tag still on a scene is in use too, even with no movie carrying it:
            // scene links were normalized above by "Parent##Name", but candidates match bare names,
            // so e.g. an ignored "Sample" leaves a scene's "Play##Sample" link in place.
            var stillLinkedIds = await db.MovieTags
                .Where(mt => candidateIds.Contains(mt.TagId))
                .Select(mt => mt.TagId)
                .Union(db.SceneTags.Where(st => candidateIds.Contains(st.TagId)).Select(st => st.TagId))
                .Distinct()
                .ToListAsync(ct);

            var orphaned = supersededCandidates.Where(t => !stillLinkedIds.Contains(t.Id)).ToList();
            if (orphaned.Count > 0)
            {
                db.Tags.RemoveRange(orphaned);
            }
        }

        return movies.Count;
    }

    /// <summary>Re-syncs every movie's FromClips flags with its clips' tags after the scene
    /// tag pass above, and doubles as a self-heal: a movie whose flags drifted (a clip tag written by a path
    /// that skipped ClipTagSync) is fixed by "Apply Rules to Library". MetaGenres follows for each movie
    /// that changed.</summary>
    private static async Task RefreshClipTagsAsync(AppDbContext db, CancellationToken ct)
    {
        var movieIds = await db.SceneTags.Select(st => st.Scene.MovieId)
            .Concat(db.HighlightTags.Select(ht => ht.Highlight.MovieId))
            .Concat(db.ApexTags.Select(at => at.Apex.MovieId))
            .Concat(db.MovieTags.Where(mt => mt.FromClips).Select(mt => mt.MovieId))
            .Distinct()
            .ToListAsync(ct);

        var changed = new List<int>();
        foreach (var movieId in movieIds)
        {
            if (await ClipTagSync.RefreshAsync(db, movieId, ct)) changed.Add(movieId);
        }
        if (changed.Count > 0)
        {
            await SyncMetaGenresAsync(db, changed, ct);
            await db.SaveChangesAsync(ct);
        }
    }

    /// <summary>Read-only variant for the TorrentSort review step, before a Movie row even
    /// exists yet: applies ignore/replacement rules to a raw genre-name list and returns the
    /// resolved names, without creating any Tag/MovieTag rows (nothing to link to yet — a
    /// genuinely new value just passes through unchanged and becomes a real Tag later, when the
    /// organized movie is actually saved and ApplyToMovieAsync runs for real).</summary>
    public static async Task<IReadOnlyList<string>> NormalizeRawValuesAsync(AppDbContext db, IEnumerable<string?> rawValues, CancellationToken ct = default)
    {
        var result = new List<string>();
        foreach (var resolution in await ResolveRawValuesAsync(db, rawValues, ct))
        {
            if (resolution.Resolved is { } name && !result.Contains(name, StringComparer.OrdinalIgnoreCase))
            {
                result.Add(name);
            }
        }

        return result;
    }

    /// <summary>Per-value form of <see cref="NormalizeRawValuesAsync"/> for the sort editor's live
    /// genre chips: the same rules, but one entry per non-blank input (no dedupe),
    /// so each chip can show its own outcome. Read-only — creates no Tag rows.</summary>
    public static async Task<IReadOnlyList<GenreResolution>> ResolveRawValuesAsync(AppDbContext db, IEnumerable<string?> rawValues, CancellationToken ct = default)
    {
        var context = await LoadContextAsync(db, ct);

        var result = new List<GenreResolution>();
        foreach (var value in rawValues)
        {
            var raw = value?.Trim();
            if (string.IsNullOrEmpty(raw)) continue;
            result.Add(Resolve(raw, context));
        }

        return result;
    }

    private static GenreResolution Resolve(string raw, NormalizationContext context)
    {
        if (IsIgnored(raw, context)) return new GenreResolution(raw, null, true);

        if (FindRule(raw, context.Rules) is { } rule)
        {
            var target = rule.TargetTag;
            return new GenreResolution(raw, target.ParentTag != null ? $"{target.ParentTag.Name}{Tag.HierarchyDelimiter}{target.Name}" : target.Name, true);
        }

        var known = context.KnownTags.FirstOrDefault(t => string.Equals(t.Name, raw, StringComparison.OrdinalIgnoreCase));
        var resolved = known?.ParentTag != null ? $"{known.ParentTag.Name}{Tag.HierarchyDelimiter}{known.Name}" : raw;
        return new GenreResolution(raw, resolved, known is not null);
    }

    /// <summary>Rebuilds Movie.MetaGenres from each movie's current MovieTag links — call after
    /// any Tag rename/merge/delete/ignore so the display cache matches the canonical relational
    /// data. Re-syncs every movie when movieIds is null.</summary>
    public static async Task SyncMetaGenresAsync(AppDbContext db, IReadOnlyCollection<int>? movieIds = null, CancellationToken ct = default)
    {
        var query = db.Movies.Include(m => m.MovieTags).ThenInclude(mt => mt.Tag).ThenInclude(t => t.ParentTag).AsQueryable();
        if (movieIds is not null)
        {
            query = query.Where(m => movieIds.Contains(m.Id));
        }

        var movies = await query.ToListAsync(ct);
        foreach (var movie in movies)
        {
            movie.MetaGenres = FormatGenres(movie.MovieTags.Select(mt => mt.Tag));
        }
    }

    private static async Task ApplyToMovieAsync(AppDbContext db, Movie movie, List<string> rawValues, NormalizationContext context, List<MovieTag>? existingLinks, CancellationToken ct)
    {
        var resolved = new List<Tag>();

        foreach (var raw in rawValues)
        {
            if (IsIgnored(raw, context)) continue;

            var rule = FindRule(raw, context.Rules);
            Tag? tag = null;
            if (rule is not null)
            {
                tag = context.KnownTags.First(t => t.Id == rule.TargetTagId);
            }
            else
            {
                tag = context.KnownTags.FirstOrDefault(t => string.Equals(t.Name, raw, StringComparison.OrdinalIgnoreCase));

                if (tag is null && raw.Contains(Tag.HierarchyDelimiter))
                {
                    var parts = raw.Split(Tag.HierarchyDelimiter, 2, StringSplitOptions.TrimEntries);
                    var parentName = parts[0];
                    var childName = parts[1];

                    if (!string.IsNullOrEmpty(parentName) && !string.IsNullOrEmpty(childName))
                    {
                        var parentTag = context.KnownTags.FirstOrDefault(t => string.Equals(t.Name, parentName, StringComparison.OrdinalIgnoreCase));
                        if (parentTag is null)
                        {
                            parentTag = new Tag { Name = parentName, NeedsReview = true };
                            db.Tags.Add(parentTag);
                            context.KnownTags.Add(parentTag);
                        }

                        tag = context.KnownTags.FirstOrDefault(t =>
                            string.Equals(t.Name, childName, StringComparison.OrdinalIgnoreCase) &&
                            (t.ParentTagId == parentTag.Id || t.ParentTag == parentTag));

                        if (tag is null)
                        {
                            tag = new Tag { Name = childName, ParentTag = parentTag, NeedsReview = true };
                            db.Tags.Add(tag);
                            context.KnownTags.Add(tag);
                        }
                    }
                }
            }

            if (tag is null)
            {
                tag = new Tag { Name = raw, NeedsReview = true };
                db.Tags.Add(tag);
                context.KnownTags.Add(tag);
            }

            if (!resolved.Contains(tag))
            {
                resolved.Add(tag);
            }
        }

        // Deduplicate parent tags when a more specific subtag under that parent is present
        var parentIdsPresent = resolved
            .Where(t => t.ParentTagId.HasValue || t.ParentTag != null)
            .Select(t => t.ParentTagId ?? t.ParentTag?.Id ?? 0)
            .Where(id => id != 0)
            .ToHashSet();

        if (parentIdsPresent.Count > 0)
        {
            resolved.RemoveAll(t => t.Id != 0 && parentIdsPresent.Contains(t.Id));
        }

        // Single-movie callers pass null and get a targeted per-movie query; ApplyToLibraryAsync
        // preloads every movie's links in one query upfront and passes its slice in directly.
        existingLinks ??= await db.MovieTags.Where(mt => mt.MovieId == movie.Id).ToListAsync(ct);

        // Pre-existing tags always have a real (nonzero) Id already; only a tag just created
        // above in this pass can still be unsaved (Id == 0), so it's added via the Tag navigation
        // instead of TagId — EF resolves the FK at SaveChanges once the new Tag gets its real Id.
        var resolvedIds = resolved.Where(t => t.Id != 0).Select(t => t.Id).ToHashSet();
        var resolvedNewTags = resolved.Where(t => t.Id == 0).ToList();

        // A tag the metadata no longer lists stops being explicit; the row stays while a scene, highlight
        // or apex still carries it. A row the metadata does list keeps its flags: a tag the
        // movie only has through its clips comes back here from MetaGenres and from an .nfo that lists clip
        // tags, and must not become the movie's own.
        foreach (var link in existingLinks.Where(link => !resolvedIds.Contains(link.TagId)))
        {
            if (link.FromClips)
            {
                link.IsExplicit = false;
            }
            else
            {
                db.MovieTags.Remove(link);
            }
        }

        var existingTagIds = existingLinks.Select(link => link.TagId).ToHashSet();
        foreach (var tagId in resolvedIds.Where(id => !existingTagIds.Contains(id)))
        {
            db.MovieTags.Add(new MovieTag { MovieId = movie.Id, TagId = tagId });
        }
        foreach (var newTag in resolvedNewTags)
        {
            db.MovieTags.Add(new MovieTag { Movie = movie, Tag = newTag });
        }

        var tagsById = context.KnownTags.Where(t => t.Id != 0).ToDictionary(t => t.Id);
        var clipOnlyTags = existingLinks
            .Where(link => link.FromClips && !resolvedIds.Contains(link.TagId) && tagsById.ContainsKey(link.TagId))
            .Select(link => tagsById[link.TagId]);
        movie.MetaGenres = FormatGenres(resolved.Concat(clipOnlyTags));
    }

    /// <summary>MetaGenres's value for a movie's effective tags: canonical names ("Parent##Name" for a
    /// subtag), sorted, comma-joined; null when there are none.</summary>
    private static string? FormatGenres(IEnumerable<Tag> tags)
    {
        var names = tags
            .Select(t => t.ParentTag != null ? $"{t.ParentTag.Name}{Tag.HierarchyDelimiter}{t.Name}" : t.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToList();
        return names.Count > 0 ? string.Join(", ", names) : null;
    }

    /// <summary>Applies the ignore list and replacement rules to every scene's tags:
    /// an ignored tag is unlinked, a tag matching a rule is repointed to the rule's target (once,
    /// even when the scene already has it or several of its tags map there). A tag is matched by
    /// the same value MetaGenres holds for movies ("Parent##Name" for a subtag). Scene tags are
    /// never copied onto the movie here, and no new tags are created.</summary>
    private static async Task ApplyToSceneTagsAsync(AppDbContext db, NormalizationContext context, CancellationToken ct)
    {
        var tagsById = context.KnownTags.Where(t => t.Id != 0).ToDictionary(t => t.Id);
        var links = await db.SceneTags.ToListAsync(ct);

        foreach (var sceneLinks in links.GroupBy(link => link.SceneId))
        {
            var resolvedIds = new HashSet<int>();
            foreach (var link in sceneLinks)
            {
                if (!tagsById.TryGetValue(link.TagId, out var tag))
                {
                    resolvedIds.Add(link.TagId);
                    continue;
                }

                var value = tag.ParentTag != null ? $"{tag.ParentTag.Name}{Tag.HierarchyDelimiter}{tag.Name}" : tag.Name;
                if (IsIgnored(value, context)) continue;
                resolvedIds.Add(FindRule(value, context.Rules)?.TargetTagId ?? link.TagId);
            }

            var existingIds = sceneLinks.Select(link => link.TagId).ToHashSet();
            db.SceneTags.RemoveRange(sceneLinks.Where(link => !resolvedIds.Contains(link.TagId)));
            foreach (var tagId in resolvedIds.Where(id => !existingIds.Contains(id)))
            {
                db.SceneTags.Add(new SceneTag { SceneId = sceneLinks.Key, TagId = tagId });
            }
        }
    }

    private static bool IsIgnored(string raw, NormalizationContext context) =>
        context.Ignored.Any(i => Matches(i.Value, i.MatchMode, raw))
        || (context.AutoIgnoreNonLatin && TagScriptDetector.ContainsNonLatinScript(raw));

    private static TagReplacementRule? FindRule(string raw, List<TagReplacementRule> rules) =>
        rules.FirstOrDefault(r => Matches(r.SourceValue, r.MatchMode, raw));

    private static bool Matches(string pattern, TagMatchMode mode, string value) => mode switch
    {
        TagMatchMode.Exact => string.Equals(pattern, value, StringComparison.Ordinal),
        _ => string.Equals(pattern, value, StringComparison.OrdinalIgnoreCase)
    };

    private static List<string> SplitGenres(string? metaGenres) =>
        string.IsNullOrWhiteSpace(metaGenres)
            ? []
            : metaGenres.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

    private static async Task<NormalizationContext> LoadContextAsync(AppDbContext db, CancellationToken ct)
    {
        var settings = await db.TagSettings.AsNoTracking().OrderBy(s => s.Id).FirstOrDefaultAsync(ct);
        return new NormalizationContext(
            await db.IgnoredTags.AsNoTracking().ToListAsync(ct),
            await db.TagReplacementRules.AsNoTracking().Include(r => r.TargetTag).ThenInclude(t => t.ParentTag).ToListAsync(ct),
            await db.Tags.Include(t => t.ParentTag).ToListAsync(ct),
            settings?.AutoIgnoreNonLatinTags ?? false);
    }

    private sealed record NormalizationContext(
        List<IgnoredTag> Ignored,
        List<TagReplacementRule> Rules,
        List<Tag> KnownTags,
        bool AutoIgnoreNonLatin);
}

/// <summary>What the sort editor's genre chips show for one raw value:
/// <see cref="Resolved"/> is the name it will be saved as (null = dropped by an ignore rule),
/// <see cref="IsKnown"/> is false when saving it would create a new, unreviewed tag.</summary>
public record GenreResolution(string Raw, string? Resolved, bool IsKnown)
{
    public bool IsIgnored => Resolved is null;
    public bool IsRenamed => Resolved is not null && !string.Equals(Raw, Resolved, StringComparison.Ordinal);
}
