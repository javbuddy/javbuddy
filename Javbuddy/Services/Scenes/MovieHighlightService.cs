using Javbuddy.Data;
using Javbuddy.Models;
using Javbuddy.Services.Common;
using Javbuddy.Services.SceneMedia;
using Microsoft.EntityFrameworkCore;

namespace Javbuddy.Services.Scenes;

/// <summary>A movie's highlight as shown in the highlight list and timeline track.
/// Lane is the timeline row it's drawn in, so overlapping highlights stack.</summary>
public sealed record HighlightItem(
    int Id,
    int Position,
    string DisplayTitle,
    string? Title,
    double StartSeconds,
    double EndSeconds,
    bool IsFavorite,
    int Lane)
{
    public double DurationSeconds => EndSeconds - StartSeconds;

    /// <summary>The highlight's own actors, ordered by name; empty when it inherits.</summary>
    public IReadOnlyList<SceneActorItem> Actors { get; init; } = [];

    /// <summary>Its own actors, else inherited from the scene it starts in or the cast (
    /// <see cref="ClipActors"/>). What the wall, labels and filters go by.</summary>
    public EffectiveActors EffectiveActors { get; init; } = EffectiveActors.None;

    /// <summary>The highlight's library tags, ordered by name.</summary>
    public IReadOnlyList<SceneTagItem> Tags { get; init; } = [];

    /// <summary>Version (cache-buster) of the highlight's screenshot, null when there is no current
    /// one. Served at <c>/highlight-image/{Id}/thumb?v=…</c>.</summary>
    public long? ThumbVersion { get; init; }

    /// <summary>Like ThumbVersion, for the animated hover preview (<c>/highlight-image/{Id}/preview</c>).</summary>
    public long? PreviewVersion { get; init; }
}

public sealed record HighlightOperationResult(bool Success, string? ErrorMessage = null, int? HighlightId = null)
{
    /// <summary>The change altered the movie's tags through its clips, so a host showing
    /// them should re-read them.</summary>
    public bool MovieTagsChanged { get; init; }

    public static HighlightOperationResult Ok(int highlightId) => new(true, null, highlightId);
    public static HighlightOperationResult Fail(string error) => new(false, error);
}

public interface IMovieHighlightService
{
    /// <summary>The movie's highlights ordered by start time.</summary>
    Task<IReadOnlyList<HighlightItem>> GetHighlightsAsync(int movieId, CancellationToken ct = default);

    /// <summary>The cast the highlight form picks actors from.</summary>
    Task<ApexActorOptions> GetActorOptionsAsync(int movieId, CancellationToken ct = default);

    /// <summary>Adds a highlight with tagIds (none when null) and actorIds as its own actors; none (null or
    /// empty) means it inherits them. Actors must be in the movie's cast. Its tags roll up to
    /// the movie through ClipTagSync.</summary>
    Task<HighlightOperationResult> AddHighlightAsync(int movieId, double startSeconds, double endSeconds, string? title, IReadOnlyCollection<int>? tagIds = null, IReadOnlyCollection<int>? actorIds = null, CancellationToken ct = default);

    /// <summary>Changes the range and title and, unless null, replaces the tags with tagIds and its own
    /// actors with actorIds (empty: it goes back to inheriting).</summary>
    Task<HighlightOperationResult> UpdateHighlightAsync(int highlightId, double startSeconds, double endSeconds, string? title, IReadOnlyCollection<int>? tagIds = null, IReadOnlyCollection<int>? actorIds = null, CancellationToken ct = default);

    Task<HighlightOperationResult> DeleteHighlightAsync(int highlightId, CancellationToken ct = default);

    /// <summary>Flips the highlight's favorite flag; returns the new value (false when not found).</summary>
    Task<bool> ToggleFavoriteAsync(int highlightId, CancellationToken ct = default);
}

/// <summary>CRUD for per-movie highlights; validation rules live in HighlightRanges.
/// highlightMedia, when present, deletes a deleted highlight's screenshot/preview and is told about
/// range edits so media generation waits for them to settle. clipTags, when present,
/// refreshes the movie's clip tags after each change. clipActors, when present, wakes the stored-actor
/// refresh after a delete the tracker can't see.</summary>
public class MovieHighlightService(IDbContextFactory<AppDbContext> dbFactory, IHighlightMediaService? highlightMedia = null, IClipTagSyncService? clipTags = null, ClipActorRefreshSignal? clipActors = null) : IMovieHighlightService
{
    private const int TitleMaxLength = 200;

    public async Task<IReadOnlyList<HighlightItem>> GetHighlightsAsync(int movieId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var ordered = HighlightRanges.Order(await db.MovieHighlights.AsNoTracking()
            .Include(h => h.HighlightTags).ThenInclude(ht => ht.Tag).ThenInclude(t => t.ParentTag)
            .Include(h => h.HighlightActors).ThenInclude(ha => ha.MovieActor).ThenInclude(ma => ma.Actor)
            .Where(h => h.MovieId == movieId)
            .ToListAsync(ct));
        var lanes = HighlightRanges.AssignLanes(ordered.Select(h => (h.StartSeconds, h.EndSeconds)).ToList());
        var ids = ordered.Select(h => h.Id).ToList();
        var media = await db.CachedImages.AsNoTracking()
            .Where(c => c.Role == HighlightMediaService.Role && ids.Contains(c.Index))
            .ToListAsync(ct);

        // Only media /highlight-image would serve counts (as for scenes).
        long? MediaVersion(MovieHighlight highlight, string variant)
        {
            if (highlightMedia is not null && !highlightMedia.ServesVariant(variant)) return null;
            var window = HighlightMediaService.Window(highlight.StartSeconds, highlight.EndSeconds);
            var row = media.FirstOrDefault(m => m.Index == highlight.Id && m.Variant == variant);
            var current = row is not null && (highlightMedia?.IsCurrent(row, window) ?? (row.SceneStartMs == window.StartMs && row.SceneEndMs == window.EndMs));
            return current ? row!.UpdatedAt.Ticks : null;
        }

        var effective = await ClipAssignments.LoadEffectiveActorsAsync(db, movieId, ct);
        var castCount = await db.MovieActors.CountAsync(ma => ma.MovieId == movieId, ct);
        return ordered
            .Select((h, i) =>
            {
                var effectiveActors = effective.Highlights.GetValueOrDefault(h.Id, EffectiveActors.None);
                var actors = h.HighlightActors
                    .Select(ha => new SceneActorItem(ha.ActorId, ha.MovieActor.Actor.DisplayName))
                    .OrderBy(actor => actor.Name, StringComparer.OrdinalIgnoreCase)
                    .ToList();
                var tags = h.HighlightTags
                    .Select(ht => new SceneTagItem(ht.TagId, ht.Tag.Name, ht.Tag.ParentTag?.Name))
                    .OrderBy(t => t.Name, StringComparer.CurrentCultureIgnoreCase)
                    .ToList();
                return new HighlightItem(h.Id, i + 1,
                    HighlightRanges.DisplayTitle(h.Title, i + 1, ActorTagLabel.ActorsToName(effectiveActors.Actors.Select(a => a.Name), castCount), tags.Select(t => t.Name)), h.Title,
                    h.StartSeconds, h.EndSeconds, h.IsFavorite, lanes[i])
                {
                    Actors = actors,
                    EffectiveActors = effectiveActors,
                    Tags = tags,
                    ThumbVersion = MediaVersion(h, SceneMediaService.VariantThumb),
                    PreviewVersion = MediaVersion(h, SceneMediaService.VariantPreview)
                };
            })
            .ToList();
    }

    public async Task<ApexActorOptions> GetActorOptionsAsync(int movieId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await ClipAssignments.LoadActorOptionsAsync(db, movieId, ct);
    }

    /// <summary>The tag ids, deduplicated, or an error when one isn't a library tag.</summary>
    private static async Task<(List<int>? TagIds, string? Error)> ValidateTagsAsync(AppDbContext db, IReadOnlyCollection<int>? tagIds, CancellationToken ct)
    {
        if (tagIds is null) return (null, null);
        var distinct = tagIds.Distinct().ToList();
        return await db.Tags.CountAsync(t => distinct.Contains(t.Id) && !t.IsActorTag, ct) == distinct.Count ? (distinct, null) : (null, "Tag not found.");
    }

    public async Task<HighlightOperationResult> AddHighlightAsync(int movieId, double startSeconds, double endSeconds, string? title, IReadOnlyCollection<int>? tagIds = null, IReadOnlyCollection<int>? actorIds = null, CancellationToken ct = default)
    {
        var normalizedTitle = title.TrimToNull();
        if (normalizedTitle?.Length > TitleMaxLength)
        {
            return HighlightOperationResult.Fail($"Title cannot exceed {TitleMaxLength} characters.");
        }

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var movie = await db.Movies.Where(m => m.Id == movieId).Select(m => new { m.MediaDurationSeconds }).FirstOrDefaultAsync(ct);
        if (movie is null)
        {
            return HighlightOperationResult.Fail("Movie not found.");
        }
        if (HighlightRanges.Validate(startSeconds, endSeconds, movie.MediaDurationSeconds) is { } error)
        {
            return HighlightOperationResult.Fail(error);
        }
        var (distinctTagIds, tagError) = await ValidateTagsAsync(db, tagIds, ct);
        if (tagError is not null)
        {
            return HighlightOperationResult.Fail(tagError);
        }
        // None given means none of its own: it inherits.
        var distinctActorIds = actorIds?.Distinct().ToList() ?? [];
        if (await ClipAssignments.ValidateActorsAsync(db, movieId, distinctActorIds, ct) is { } actorError)
        {
            return HighlightOperationResult.Fail(actorError);
        }

        var highlight = new MovieHighlight
        {
            MovieId = movieId,
            StartSeconds = startSeconds,
            EndSeconds = endSeconds,
            Title = normalizedTitle,
            CreatedAt = DateTime.UtcNow
        };
        foreach (var tagId in distinctTagIds ?? [])
        {
            highlight.HighlightTags.Add(new HighlightTag { TagId = tagId });
        }
        foreach (var actorId in distinctActorIds)
        {
            highlight.HighlightActors.Add(new HighlightActor { MovieId = movieId, ActorId = actorId });
        }
        db.MovieHighlights.Add(highlight);
        await db.SaveChangesAsync(ct);
        highlightMedia?.NoteHighlightsChanged(movieId);
        var tagsChanged = clipTags is not null && await clipTags.RefreshAsync(movieId, ct);
        return HighlightOperationResult.Ok(highlight.Id) with { MovieTagsChanged = tagsChanged };
    }

    public async Task<HighlightOperationResult> UpdateHighlightAsync(int highlightId, double startSeconds, double endSeconds, string? title, IReadOnlyCollection<int>? tagIds = null, IReadOnlyCollection<int>? actorIds = null, CancellationToken ct = default)
    {
        var normalizedTitle = title.TrimToNull();
        if (normalizedTitle?.Length > TitleMaxLength)
        {
            return HighlightOperationResult.Fail($"Title cannot exceed {TitleMaxLength} characters.");
        }

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var highlight = await db.MovieHighlights.Include(h => h.Movie).Include(h => h.HighlightTags).Include(h => h.HighlightActors)
            .FirstOrDefaultAsync(h => h.Id == highlightId, ct);
        if (highlight is null)
        {
            return HighlightOperationResult.Fail("Highlight not found.");
        }
        if (HighlightRanges.Validate(startSeconds, endSeconds, highlight.Movie.MediaDurationSeconds) is { } error)
        {
            return HighlightOperationResult.Fail(error);
        }
        var (distinctTagIds, tagError) = await ValidateTagsAsync(db, tagIds, ct);
        if (tagError is not null)
        {
            return HighlightOperationResult.Fail(tagError);
        }
        var distinctActorIds = actorIds?.Distinct().ToList();
        if (distinctActorIds is not null && await ClipAssignments.ValidateActorsAsync(db, highlight.MovieId, distinctActorIds, ct) is { } actorError)
        {
            return HighlightOperationResult.Fail(actorError);
        }

        // A title-only edit doesn't move the range, so it doesn't hold back media generation.
        var rangeChanged = highlight.StartSeconds != startSeconds || highlight.EndSeconds != endSeconds;
        highlight.StartSeconds = startSeconds;
        highlight.EndSeconds = endSeconds;
        highlight.Title = normalizedTitle;
        if (distinctTagIds is not null)
        {
            foreach (var removed in highlight.HighlightTags.Where(ht => !distinctTagIds.Contains(ht.TagId)).ToList())
            {
                highlight.HighlightTags.Remove(removed);
            }
            foreach (var tagId in distinctTagIds.Where(id => highlight.HighlightTags.All(ht => ht.TagId != id)).ToList())
            {
                highlight.HighlightTags.Add(new HighlightTag { TagId = tagId });
            }
        }
        if (distinctActorIds is not null)
        {
            foreach (var removed in highlight.HighlightActors.Where(ha => !distinctActorIds.Contains(ha.ActorId)).ToList())
            {
                highlight.HighlightActors.Remove(removed);
            }
            foreach (var actorId in distinctActorIds.Where(id => highlight.HighlightActors.All(ha => ha.ActorId != id)))
            {
                highlight.HighlightActors.Add(new HighlightActor { MovieId = highlight.MovieId, ActorId = actorId });
            }
        }
        await db.SaveChangesAsync(ct);
        if (rangeChanged) highlightMedia?.NoteHighlightsChanged(highlight.MovieId);
        var tagsChanged = clipTags is not null && await clipTags.RefreshAsync(highlight.MovieId, ct);
        return HighlightOperationResult.Ok(highlight.Id) with { MovieTagsChanged = tagsChanged };
    }

    public async Task<HighlightOperationResult> DeleteHighlightAsync(int highlightId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var movieId = await db.MovieHighlights.Where(h => h.Id == highlightId).Select(h => (int?)h.MovieId).FirstOrDefaultAsync(ct);
        var deleted = await db.MovieHighlights.Where(h => h.Id == highlightId).ExecuteDeleteAsync(ct);
        if (deleted == 0 || movieId is null)
        {
            return HighlightOperationResult.Fail("Highlight not found.");
        }
        // ExecuteDelete bypasses ClipActorStaleInterceptor: apexes inheriting from it change.
        await ClipActorStale.MarkAsync(db, movieId.Value, ct);
        clipActors?.Signal();
        if (highlightMedia is not null)
        {
            await highlightMedia.DeleteForHighlightAsync(highlightId, ct);
            highlightMedia.NoteHighlightsChanged(movieId.Value);
        }
        var tagsChanged = clipTags is not null && await clipTags.RefreshAsync(movieId.Value, ct);
        return HighlightOperationResult.Ok(highlightId) with { MovieTagsChanged = tagsChanged };
    }

    public async Task<bool> ToggleFavoriteAsync(int highlightId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var highlight = await db.MovieHighlights.FirstOrDefaultAsync(h => h.Id == highlightId, ct);
        if (highlight is null) return false;

        highlight.IsFavorite = !highlight.IsFavorite;
        highlight.FavoritedAt = highlight.IsFavorite ? DateTime.UtcNow : null;
        await db.SaveChangesAsync(ct);
        return highlight.IsFavorite;
    }
}
