using Javbuddy.Data;
using Javbuddy.Models;
using Javbuddy.Services.SceneMedia;
using Microsoft.EntityFrameworkCore;

namespace Javbuddy.Services.Scenes;

/// <summary>A movie's apex marker as shown in the apex list, on the timeline and on Movie Detail
///. Tags are library tags, ordered by name.</summary>
public sealed record ApexItem(int Id, int Position, double Seconds, IReadOnlyList<SceneTagItem> Tags)
{
    /// <summary>Marked as a favorite.</summary>
    public bool IsFavorite { get; init; }

    /// <summary>The apex's own actors, ordered by name; empty when it inherits.</summary>
    public IReadOnlyList<SceneActorItem> Actors { get; init; } = [];

    /// <summary>Its own actors, else inherited from its parent highlight, its scene or the cast
    /// (<see cref="ClipActors"/>). What the wall, labels and filters go by.</summary>
    public EffectiveActors EffectiveActors { get; init; } = EffectiveActors.None;

    /// <summary>The effective actor names, then the tag names ("Mei — Creampie, Squirt"), or "Apex" when it
    /// has neither, for tooltips and the timeline preview. In a solo movie it leaves the actor out.</summary>
    public string DisplayLabel => ActorTagLabel.Format(ActorTagLabel.ActorsToName(LabelActors.Select(a => a.Name), CastCount), Tags.Select(t => t.Name)) ?? "Apex";

    /// <summary>How many actors the movie's cast has; unknown (int.MaxValue) names them all.</summary>
    public int CastCount { get; init; } = int.MaxValue;

    // Items built without inheritance (e.g. clip-relative copies in tests) fall back to their own actors.
    private IReadOnlyList<SceneActorItem> LabelActors => EffectiveActors.Actors.Count > 0 ? EffectiveActors.Actors : Actors;

    /// <summary>Version of the apex's current hover preview, or null while there's none.</summary>
    public long? PreviewVersion { get; init; }

    /// <summary>The apex's own lead-in and tail, null where it uses the default.</summary>
    public double? OwnLeadInSeconds { get; init; }

    public double? OwnTailSeconds { get; init; }

    /// <summary>How long it plays before and after it: its own lead-in/tail, else the default.</summary>
    public ApexWindow Window { get; init; } = ApexWindow.Default;
}

/// <summary>An apex's own lead-in and tail in seconds; a null side uses the default.</summary>
public sealed record ApexWindowOverride(double? LeadInSeconds, double? TailSeconds)
{
    public static ApexWindowOverride None { get; } = new(null, null);
}

/// <summary>What the apex and highlight forms offer as actors: the movie's cast, ordered by name. What a
/// clip inherits is computed from the loaded scenes and highlights (<see cref="ClipActors"/>).</summary>
public sealed record ApexActorOptions(IReadOnlyList<SceneActorItem> Cast);

public sealed record ApexOperationResult(bool Success, string? ErrorMessage = null, int? ApexId = null)
{
    /// <summary>The change altered the movie's tags through its clips, so a host showing
    /// them should re-read them.</summary>
    public bool MovieTagsChanged { get; init; }

    public static ApexOperationResult Ok(int apexId) => new(true, null, apexId);
    public static ApexOperationResult Fail(string error) => new(false, error);
}

public interface IMovieApexService
{
    /// <summary>The movie's apexes ordered by time.</summary>
    Task<IReadOnlyList<ApexItem>> GetApexesAsync(int movieId, CancellationToken ct = default);

    /// <summary>The cast the apex form picks actors from.</summary>
    Task<ApexActorOptions> GetActorOptionsAsync(int movieId, CancellationToken ct = default);

    /// <summary>The lead-in and tail an apex without its own plays with.</summary>
    Task<ApexWindow> GetDefaultWindowAsync(CancellationToken ct = default);

    /// <summary>Adds an apex with actorIds as its own actors; none (null or empty) means it inherits them
    ///. Actors must be in the movie's cast. Its tags roll up to the movie through ClipTagSync.
    /// window sets its own lead-in/tail; null uses the defaults.</summary>
    Task<ApexOperationResult> AddApexAsync(int movieId, double seconds, IReadOnlyCollection<int> tagIds, IReadOnlyCollection<int>? actorIds = null, ApexWindowOverride? window = null, CancellationToken ct = default);

    /// <summary>Moves the apex and replaces its tags with tagIds and, unless null, its own actors with
    /// actorIds (empty: it goes back to inheriting) and, unless null, its own lead-in/tail
    /// with window.</summary>
    Task<ApexOperationResult> UpdateApexAsync(int apexId, double seconds, IReadOnlyCollection<int> tagIds, IReadOnlyCollection<int>? actorIds = null, ApexWindowOverride? window = null, CancellationToken ct = default);

    Task<ApexOperationResult> DeleteApexAsync(int apexId, CancellationToken ct = default);

    /// <summary>Flips the apex's favorite flag and returns the new state; false when it doesn't exist.</summary>
    Task<bool> ToggleFavoriteAsync(int apexId, CancellationToken ct = default);
}

/// <summary>CRUD for per-movie apex markers; time validation lives in ApexRanges.
/// apexMedia, when present, reports each apex's hover preview and deletes it with the apex.
/// clipTags, when present, refreshes the movie's clip tags after each change. playbackSettings,
/// when present, gives the default apex window, else ApexWindow.Default. clipActors, when present,
/// wakes the stored-actor refresh after a delete the tracker can't see.</summary>
public class MovieApexService(
    IDbContextFactory<AppDbContext> dbFactory,
    IApexMediaService? apexMedia = null,
    IClipTagSyncService? clipTags = null,
    IApexPlaybackSettingsService? playbackSettings = null,
    ClipActorRefreshSignal? clipActors = null) : IMovieApexService
{
    public async Task<IReadOnlyList<ApexItem>> GetApexesAsync(int movieId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var apexes = await db.MovieApexes.AsNoTracking()
            .Include(a => a.ApexTags).ThenInclude(at => at.Tag).ThenInclude(t => t.ParentTag)
            .Include(a => a.ApexActors).ThenInclude(aa => aa.MovieActor).ThenInclude(ma => ma.Actor)
            .Where(a => a.MovieId == movieId)
            .OrderBy(a => a.Seconds).ThenBy(a => a.Id)
            .ToListAsync(ct);
        var ids = apexes.Select(a => a.Id).ToList();
        var media = apexMedia is { ServesPreview: true }
            ? await db.CachedImages.AsNoTracking()
                .Where(c => c.Role == ApexMediaService.Role && c.Variant == SceneMediaService.VariantPreview && ids.Contains(c.Index))
                .ToListAsync(ct)
            : [];

        // Only a preview /apex-image would serve counts (as for highlights).
        long? PreviewVersion(MovieApex apex) =>
            media.FirstOrDefault(m => m.Index == apex.Id) is { } row && apexMedia!.IsCurrent(row, ApexMediaService.Window(apex.Seconds))
                ? row.UpdatedAt.Ticks
                : null;

        var effective = await ClipAssignments.LoadEffectiveActorsAsync(db, movieId, ct);
        var castCount = await db.MovieActors.CountAsync(ma => ma.MovieId == movieId, ct);
        var defaultWindow = await GetDefaultWindowAsync(ct);
        return apexes
            .Select((a, i) => new ApexItem(a.Id, i + 1, a.Seconds, a.ApexTags
                .Select(at => new SceneTagItem(at.TagId, at.Tag.Name, at.Tag.ParentTag?.Name))
                .OrderBy(t => t.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList())
            {
                IsFavorite = a.IsFavorite,
                Actors = a.ApexActors
                    .Select(aa => new SceneActorItem(aa.ActorId, aa.MovieActor.Actor.DisplayName))
                    .OrderBy(actor => actor.Name, StringComparer.OrdinalIgnoreCase)
                    .ToList(),
                EffectiveActors = effective.Apexes.GetValueOrDefault(a.Id, EffectiveActors.None),
                CastCount = castCount,
                PreviewVersion = PreviewVersion(a),
                OwnLeadInSeconds = a.LeadInSeconds,
                OwnTailSeconds = a.TailSeconds,
                Window = ApexRanges.Resolve(a.LeadInSeconds, a.TailSeconds, defaultWindow),
            })
            .ToList();
    }

    public async Task<ApexWindow> GetDefaultWindowAsync(CancellationToken ct = default) =>
        playbackSettings is null ? ApexWindow.Default : await playbackSettings.GetEffectiveAsync(ct);

    public async Task<ApexActorOptions> GetActorOptionsAsync(int movieId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await ClipAssignments.LoadActorOptionsAsync(db, movieId, ct);
    }

    public async Task<ApexOperationResult> AddApexAsync(int movieId, double seconds, IReadOnlyCollection<int> tagIds, IReadOnlyCollection<int>? actorIds = null, ApexWindowOverride? window = null, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var movie = await db.Movies.Where(m => m.Id == movieId).Select(m => new { m.MediaDurationSeconds }).FirstOrDefaultAsync(ct);
        if (movie is null)
        {
            return ApexOperationResult.Fail("Movie not found.");
        }
        if (ApexRanges.ValidateTime(seconds, movie.MediaDurationSeconds) is { } error)
        {
            return ApexOperationResult.Fail(error);
        }
        window ??= ApexWindowOverride.None;
        if (ApexRanges.ValidateWindow(window.LeadInSeconds, window.TailSeconds) is { } windowError)
        {
            return ApexOperationResult.Fail(windowError);
        }
        var distinctTagIds = tagIds.Distinct().ToList();
        if (await db.Tags.CountAsync(t => distinctTagIds.Contains(t.Id), ct) != distinctTagIds.Count)
        {
            return ApexOperationResult.Fail("Tag not found.");
        }
        // None given means none of its own: it inherits.
        var distinctActorIds = actorIds?.Distinct().ToList() ?? [];
        if (await ClipAssignments.ValidateActorsAsync(db, movieId, distinctActorIds, ct) is { } actorError)
        {
            return ApexOperationResult.Fail(actorError);
        }

        var apex = new MovieApex
        {
            MovieId = movieId,
            Seconds = seconds,
            LeadInSeconds = window.LeadInSeconds,
            TailSeconds = window.TailSeconds,
            CreatedAt = DateTime.UtcNow
        };
        foreach (var tagId in distinctTagIds)
        {
            apex.ApexTags.Add(new ApexTag { TagId = tagId });
        }
        foreach (var actorId in distinctActorIds)
        {
            apex.ApexActors.Add(new ApexActor { MovieId = movieId, ActorId = actorId });
        }
        db.MovieApexes.Add(apex);
        await db.SaveChangesAsync(ct);
        var tagsChanged = clipTags is not null && await clipTags.RefreshAsync(movieId, ct);
        // Made now rather than on the next visit, so hovering it soon shows its clip.
        if (apexMedia is not null) await apexMedia.EnsureQueuedAsync(movieId, ct);
        return ApexOperationResult.Ok(apex.Id) with { MovieTagsChanged = tagsChanged };
    }

    public async Task<ApexOperationResult> UpdateApexAsync(int apexId, double seconds, IReadOnlyCollection<int> tagIds, IReadOnlyCollection<int>? actorIds = null, ApexWindowOverride? window = null, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var apex = await db.MovieApexes.Include(a => a.Movie).Include(a => a.ApexTags).Include(a => a.ApexActors).FirstOrDefaultAsync(a => a.Id == apexId, ct);
        if (apex is null)
        {
            return ApexOperationResult.Fail("Apex not found.");
        }
        if (ApexRanges.ValidateTime(seconds, apex.Movie.MediaDurationSeconds) is { } error)
        {
            return ApexOperationResult.Fail(error);
        }
        if (window is not null && ApexRanges.ValidateWindow(window.LeadInSeconds, window.TailSeconds) is { } windowError)
        {
            return ApexOperationResult.Fail(windowError);
        }
        var distinctTagIds = tagIds.Distinct().ToList();
        if (await db.Tags.CountAsync(t => distinctTagIds.Contains(t.Id), ct) != distinctTagIds.Count)
        {
            return ApexOperationResult.Fail("Tag not found.");
        }
        var distinctActorIds = actorIds?.Distinct().ToList();
        if (distinctActorIds is not null && await ClipAssignments.ValidateActorsAsync(db, apex.MovieId, distinctActorIds, ct) is { } actorError)
        {
            return ApexOperationResult.Fail(actorError);
        }

        var moved = apex.Seconds != seconds;
        apex.Seconds = seconds;
        if (window is not null)
        {
            apex.LeadInSeconds = window.LeadInSeconds;
            apex.TailSeconds = window.TailSeconds;
        }
        foreach (var removed in apex.ApexTags.Where(at => !distinctTagIds.Contains(at.TagId)).ToList())
        {
            apex.ApexTags.Remove(removed);
        }
        foreach (var tagId in distinctTagIds.Where(id => apex.ApexTags.All(at => at.TagId != id)).ToList())
        {
            apex.ApexTags.Add(new ApexTag { TagId = tagId });
        }
        if (distinctActorIds is not null)
        {
            foreach (var removed in apex.ApexActors.Where(aa => !distinctActorIds.Contains(aa.ActorId)).ToList())
            {
                apex.ApexActors.Remove(removed);
            }
            foreach (var actorId in distinctActorIds.Where(id => apex.ApexActors.All(aa => aa.ActorId != id)))
            {
                apex.ApexActors.Add(new ApexActor { MovieId = apex.MovieId, ActorId = actorId });
            }
        }
        await db.SaveChangesAsync(ct);
        var tagsChanged = clipTags is not null && await clipTags.RefreshAsync(apex.MovieId, ct);
        if (moved && apexMedia is not null) await apexMedia.EnsureQueuedAsync(apex.MovieId, ct);
        return ApexOperationResult.Ok(apex.Id) with { MovieTagsChanged = tagsChanged };
    }

    public async Task<bool> ToggleFavoriteAsync(int apexId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var apex = await db.MovieApexes.FirstOrDefaultAsync(a => a.Id == apexId, ct);
        if (apex is null) return false;

        apex.IsFavorite = !apex.IsFavorite;
        apex.FavoritedAt = apex.IsFavorite ? DateTime.UtcNow : null;
        await db.SaveChangesAsync(ct);
        return apex.IsFavorite;
    }

    public async Task<ApexOperationResult> DeleteApexAsync(int apexId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var movieId = await db.MovieApexes.Where(a => a.Id == apexId).Select(a => (int?)a.MovieId).FirstOrDefaultAsync(ct);
        var deleted = await db.MovieApexes.Where(a => a.Id == apexId).ExecuteDeleteAsync(ct);
        if (deleted == 0 || movieId is null)
        {
            return ApexOperationResult.Fail("Apex not found.");
        }
        // ExecuteDelete bypasses ClipActorStaleInterceptor; its own rows cascade away with it.
        await ClipActorStale.MarkAsync(db, movieId.Value, ct);
        clipActors?.Signal();
        if (apexMedia is not null)
        {
            await apexMedia.DeleteForApexAsync(apexId, ct);
        }
        var tagsChanged = clipTags is not null && await clipTags.RefreshAsync(movieId.Value, ct);
        return ApexOperationResult.Ok(apexId) with { MovieTagsChanged = tagsChanged };
    }
}
