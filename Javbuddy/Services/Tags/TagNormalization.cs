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
            var before = movie.Id != 0 ? await SnapshotRepointedExclusionsAsync(db, context, movie.Id, ct) : [];
            await ApplyToMovieAsync(db, movie, normalized, context, existingLinks: null, ct);
            await db.SaveChangesAsync(ct);
            await CopyExclusionsToRuleTargetsAsync(db, context, before, ct);
            await db.SaveChangesAsync(ct);
        }
        finally
        {
            NewTagCreationLock.Release();
        }
    }

    private const int LibraryChunkSize = 500;

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
        // Only the id and the genre text are needed: the movies are not tracked, and the changed MetaGenres
        // values are written back with ExecuteUpdate below.
        var movies = await db.Movies
            .AsNoTracking()
            .Where(m => m.MetaGenres != null && m.MetaGenres != "")
            .Select(m => new { m.Id, m.MetaGenres })
            .ToListAsync(ct);

        // One query for every movie's current links instead of one query per movie — the same
        // upfront-load-then-look-up-in-memory shape MovieActorAssociation.SynchronizeAllAsync
        // uses, since this loop can run against the whole library at once.
        var linksByMovie = (await db.MovieTags.ToListAsync(ct))
            .GroupBy(link => link.MovieId)
            .ToDictionary(group => group.Key, group => group.ToList());

        var exclusionsBefore = await SnapshotRepointedExclusionsAsync(db, context, movieId: null, ct);

        // Chunked, each chunk in its own short transaction, so other writers get in between chunks instead of
        // waiting out one library-sized batch.
        foreach (var chunk in movies.Chunk(LibraryChunkSize))
        {
            await using var transaction = await db.Database.BeginTransactionAsync(ct);

            var newGenres = new List<(int Id, string? MetaGenres)>();
            foreach (var row in chunk)
            {
                var stub = new Movie { Id = row.Id, MetaGenres = row.MetaGenres };
                await ApplyToMovieAsync(db, stub, SplitGenres(row.MetaGenres), context, linksByMovie.GetValueOrDefault(row.Id, []), ct);
                if (stub.MetaGenres != row.MetaGenres)
                {
                    newGenres.Add((row.Id, stub.MetaGenres));
                }
            }

            await db.SaveChangesAsync(ct);
            foreach (var (id, metaGenres) in newGenres)
            {
                await db.Movies.Where(m => m.Id == id).ExecuteUpdateAsync(set => set.SetProperty(m => m.MetaGenres, metaGenres), ct);
            }

            await transaction.CommitAsync(ct);
        }

        await ApplyToSceneTagsAsync(db, context, ct);
        await db.SaveChangesAsync(ct);
        await CopyExclusionsToRuleTargetsAsync(db, context, exclusionsBefore, ct);

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
                        // An actor tag's subtag ("Hair/Long" from a .nfo this app wrote) is never a genre, and a plain
                        // tag never nests under an actor tag.
                        if (parentTag is { IsActorTag: true }) continue;

                        // Tag names are unique across all parents (ignoring case), so a child named like a tag elsewhere
                        // reuses that tag rather than inserting one the unique index would reject.
                        tag = context.KnownTags.FirstOrDefault(t => string.Equals(t.Name, childName, StringComparison.OrdinalIgnoreCase));

                        if (tag is null)
                        {
                            if (parentTag is null)
                            {
                                parentTag = new Tag { Name = parentName, NeedsReview = true };
                                db.Tags.Add(parentTag);
                                context.KnownTags.Add(parentTag);
                            }

                            // "Foo##foo": the parent just created already has the child's name.
                            if (string.Equals(childName, parentName, StringComparison.OrdinalIgnoreCase))
                            {
                                tag = parentTag;
                            }
                            else
                            {
                                tag = new Tag { Name = childName, ParentTag = parentTag, NeedsReview = true };
                                db.Tags.Add(tag);
                                context.KnownTags.Add(tag);
                            }
                        }
                    }
                }
            }

            // Actor tags come from the actor-tag editors only (they reach the movie as FromClips rows), never from metadata.
            if (tag is { IsActorTag: true }) continue;

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

        // A link that passed down whose tag a replacement rule now repoints hands its toggle to the rule's target.
        var knownById = context.KnownTags.Where(t => t.Id != 0).ToDictionary(t => t.Id);
        var passingIds = existingLinks
            .Where(link => link.PropagatesDown && link.IsExplicit && !resolvedIds.Contains(link.TagId) && knownById.ContainsKey(link.TagId))
            .Select(link => knownById[link.TagId])
            .Select(tag => FindRule(tag.ParentTag != null ? $"{tag.ParentTag.Name}{Tag.HierarchyDelimiter}{tag.Name}" : tag.Name, context.Rules)?.TargetTagId)
            .OfType<int>()
            .Where(resolvedIds.Contains)
            .ToHashSet();

        // A tag the metadata no longer lists stops being explicit; the row stays while a scene, highlight
        // or apex still carries it. A row the metadata does list keeps its flags: a tag the
        // movie only has through its clips comes back here from MetaGenres and from an .nfo that lists clip
        // tags, and must not become the movie's own.
        foreach (var link in existingLinks.Where(link => !resolvedIds.Contains(link.TagId)))
        {
            if (link.FromClips)
            {
                link.IsExplicit = false;
                // Only the movie's own tag passes down to its clips.
                link.PropagatesDown = false;
            }
            else
            {
                db.MovieTags.Remove(link);
            }
        }

        var existingTagIds = existingLinks.Select(link => link.TagId).ToHashSet();
        foreach (var link in existingLinks.Where(link => passingIds.Contains(link.TagId) && link.IsExplicit && !link.PropagatesDown))
        {
            link.PropagatesDown = true;
        }
        foreach (var tagId in resolvedIds.Where(id => !existingTagIds.Contains(id)))
        {
            db.MovieTags.Add(new MovieTag { MovieId = movie.Id, TagId = tagId, PropagatesDown = passingIds.Contains(tagId) });
        }
        foreach (var newTag in resolvedNewTags)
        {
            // A movie that already has an Id (the library pass hands over an untracked stub) is linked by id, so
            // the link doesn't attach it to the change tracker.
            db.MovieTags.Add(movie.Id != 0 ? new MovieTag { MovieId = movie.Id, Tag = newTag } : new MovieTag { Movie = movie, Tag = newTag });
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

    /// <summary>What each clip inherits, before the pass, in every movie (or just <paramref name="movieId"/>) with a clip
    /// opted out of a tag a replacement rule repoints: <see cref="CopyExclusionsToRuleTargetsAsync"/> compares it with
    /// what they inherit afterwards.</summary>
    private static async Task<Dictionary<int, ClipTagInheritanceResult>> SnapshotRepointedExclusionsAsync(AppDbContext db, NormalizationContext context, int? movieId, CancellationToken ct)
    {
        var rows = await db.SceneTagExclusions.Where(e => movieId == null || e.MovieId == movieId).Select(e => new { e.MovieId, e.TagId })
            .Concat(db.HighlightTagExclusions.Where(e => movieId == null || e.MovieId == movieId).Select(e => new { e.MovieId, e.TagId }))
            .Concat(db.ApexTagExclusions.Where(e => movieId == null || e.MovieId == movieId).Select(e => new { e.MovieId, e.TagId }))
            .Distinct()
            .ToListAsync(ct);
        var tagsById = context.KnownTags.Where(t => t.Id != 0).ToDictionary(t => t.Id);
        var snapshot = new Dictionary<int, ClipTagInheritanceResult>();
        foreach (var id in rows.Where(r => RuleTargetOf(r.TagId, tagsById, context) is not null).Select(r => r.MovieId).Distinct())
        {
            snapshot[id] = await ClipAssignments.LoadInheritedTagsAsync(db, id, ct);
        }
        return snapshot;
    }

    /// <summary>Copies a clip's opt-out of a tag onto the tag a replacement rule repoints it to, when the pass moved what
    /// passed that tag down onto the target and so the clip only now inherits it. A clip that already inherited
    /// the target (from another source) keeps it: its opt-out of the old tag never covered that one. Copied, not moved: not
    /// every source is repointed (highlight tags never are, and the single-movie path only rewrites movie tags), so
    /// the original must keep blocking the old tag; it cascades away if the tag is pruned. Needs the pass saved, so
    /// the stored links are the new ones. <paramref name="before"/> is <see cref="SnapshotRepointedExclusionsAsync"/>'s.</summary>
    private static async Task CopyExclusionsToRuleTargetsAsync(AppDbContext db, NormalizationContext context, Dictionary<int, ClipTagInheritanceResult> before, CancellationToken ct)
    {
        if (before.Count == 0) return;
        var tagsById = context.KnownTags.Where(t => t.Id != 0).ToDictionary(t => t.Id);
        var movieIds = before.Keys.ToList();
        var sceneRows = await db.SceneTagExclusions.Where(e => movieIds.Contains(e.MovieId)).ToListAsync(ct);
        var highlightRows = await db.HighlightTagExclusions.Where(e => movieIds.Contains(e.MovieId)).ToListAsync(ct);
        var apexRows = await db.ApexTagExclusions.Where(e => movieIds.Contains(e.MovieId)).ToListAsync(ct);

        foreach (var (movieId, old) in before)
        {
            var now = await ClipAssignments.LoadInheritedTagsAsync(db, movieId, ct);
            Copy(sceneRows.Where(e => e.MovieId == movieId).ToList(), old.Scenes, now.Scenes, e => e.SceneId, e => e.TagId,
                (e, target) => new SceneTagExclusion { SceneId = e.SceneId, MovieId = e.MovieId, TagId = target });
            Copy(highlightRows.Where(e => e.MovieId == movieId).ToList(), old.Highlights, now.Highlights, e => e.HighlightId, e => e.TagId,
                (e, target) => new HighlightTagExclusion { HighlightId = e.HighlightId, MovieId = e.MovieId, TagId = target });
            Copy(apexRows.Where(e => e.MovieId == movieId).ToList(), old.Apexes, now.Apexes, e => e.ApexId, e => e.TagId,
                (e, target) => new ApexTagExclusion { ApexId = e.ApexId, MovieId = e.MovieId, TagId = target });
        }

        void Copy<TRow>(List<TRow> rows, IReadOnlyDictionary<int, IReadOnlyList<InheritedTag>> old, IReadOnlyDictionary<int, IReadOnlyList<InheritedTag>> now,
            Func<TRow, int> owner, Func<TRow, int> tagOf, Func<TRow, int, TRow> copied) where TRow : class
        {
            bool Inherits(IReadOnlyDictionary<int, IReadOnlyList<InheritedTag>> pools, int clipId, int tagId) =>
                pools.TryGetValue(clipId, out var pool) && pool.Any(t => t.Tag.TagId == tagId);

            var present = rows.Select(r => (owner(r), tagOf(r))).ToHashSet();
            foreach (var row in rows)
            {
                if (RuleTargetOf(tagOf(row), tagsById, context) is not { } target) continue;
                var clipId = owner(row);
                // Only the rows the pass repointed: the target wasn't reaching the clip before and is now.
                if (Inherits(old, clipId, target) || !Inherits(now, clipId, target)) continue;
                if (present.Add((clipId, target))) db.Add(copied(row, target));
            }
        }
    }

    /// <summary>The tag a replacement rule repoints the tag to, or null (no rule, the rule targets the tag itself,
    /// or the tag is ignored).</summary>
    private static int? RuleTargetOf(int tagId, Dictionary<int, Tag> tagsById, NormalizationContext context)
    {
        if (!tagsById.TryGetValue(tagId, out var tag)) return null;
        var value = tag.ParentTag != null ? $"{tag.ParentTag.Name}{Tag.HierarchyDelimiter}{tag.Name}" : tag.Name;
        if (IsIgnored(value, context)) return null;
        return FindRule(value, context.Rules)?.TargetTagId is { } target && target != tagId ? target : null;
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
            // Resolved tags any of whose source links passed down: a repointed link keeps its toggle.
            var passingIds = new HashSet<int>();
            foreach (var link in sceneLinks)
            {
                int resolvedId;
                if (!tagsById.TryGetValue(link.TagId, out var tag))
                {
                    resolvedId = link.TagId;
                }
                else
                {
                    var value = tag.ParentTag != null ? $"{tag.ParentTag.Name}{Tag.HierarchyDelimiter}{tag.Name}" : tag.Name;
                    if (IsIgnored(value, context)) continue;
                    resolvedId = FindRule(value, context.Rules)?.TargetTagId ?? link.TagId;
                }
                resolvedIds.Add(resolvedId);
                if (link.PropagatesDown) passingIds.Add(resolvedId);
            }

            var existingIds = sceneLinks.Select(link => link.TagId).ToHashSet();
            db.SceneTags.RemoveRange(sceneLinks.Where(link => !resolvedIds.Contains(link.TagId)));
            foreach (var link in sceneLinks.Where(link => passingIds.Contains(link.TagId) && !link.PropagatesDown))
            {
                link.PropagatesDown = true;
            }
            foreach (var tagId in resolvedIds.Where(id => !existingIds.Contains(id)))
            {
                db.SceneTags.Add(new SceneTag { SceneId = sceneLinks.Key, TagId = tagId, PropagatesDown = passingIds.Contains(tagId) });
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
        var settings = await db.TagSettings.ReadSingleRowAsync(ct);
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
