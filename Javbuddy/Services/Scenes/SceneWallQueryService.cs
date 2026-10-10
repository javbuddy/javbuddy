using Javbuddy.Data;
using Javbuddy.Models;
using Javbuddy.Services.Movies;
using Javbuddy.Services.SceneMedia;
using Javbuddy.Services.Tags;
using Microsoft.EntityFrameworkCore;

namespace Javbuddy.Services.Scenes;

/// <summary>Filters for the scene wall. Within Tags, Actors and Studios a scene
/// matches any selected value (TagIds also match subtags); the filters combine with AND. Text
/// matches the scene title, movie code or movie title. Scenes hidden from the overview
/// are left out unless IncludeHidden. ApexOnly keeps clips whose range contains an apex of their movie;
/// ApexTagIds keeps those containing an apex with one of the tags (or a subtag), implying ApexOnly
///. ActorTagIds keeps clips where an actor has one of the actor tags (Tag.IsActorTag), own, inherited or rolled
/// up; with ActorIds, it must be one of those actors who has it ("Mei, brunette").</summary>
public sealed record SceneWallFilter(
    IReadOnlyCollection<int>? TagIds = null,
    IReadOnlyCollection<int>? ActorIds = null,
    IReadOnlyCollection<string>? Studios = null,
    bool FavoritesOnly = false,
    string? Text = null,
    bool IncludeHidden = false,
    ActorAttributeSelection? ActorAttributes = null,
    bool ApexOnly = false,
    IReadOnlyCollection<int>? ApexTagIds = null,
    IReadOnlyCollection<int>? ActorTagIds = null);

public enum SceneWallSort
{
    ReleaseDate,
    DateAdded,
    // 2 was Rating; a remembered one falls back to the default sort.
    Random = 3,
}

public sealed record SceneWallCard(
    int SceneId,
    int MovieId,
    string Code,
    string MovieTitle,
    string DisplayTitle,
    double StartSeconds,
    double? EffectiveEndSeconds,
    IReadOnlyList<string> Actors,
    IReadOnlyList<SceneTagItem> Tags,
    bool IsFavorite,
    long? ThumbVersion,
    long? PreviewVersion)
{
    /// <summary>Whether the movie has a local video file the clip player can stream.</summary>
    public bool HasLocalVideo { get; init; }

    /// <summary>For the clip player's "Open in Jellyfin" link.</summary>
    public string? JellyfinItemId { get; init; }

    public string? JellyfinServerId { get; init; }

    /// <summary>The movie's media duration.</summary>
    public double? DurationSeconds { get; init; }

    /// <summary>How many of the movie's apexes lie in the scene's effective range.</summary>
    public int ApexCount { get; init; }

    /// <summary>Tags rolled up from its highlights and apexes that it doesn't hold itself.</summary>
    public IReadOnlyList<ImplicitTag> ImplicitTags { get; init; } = [];
}

public sealed record SceneWallPage(IReadOnlyList<SceneWallCard> Cards, int TotalCount);

/// <summary>A highlight on the wall, with its own actors and tags.</summary>
public sealed record HighlightWallCard(
    int HighlightId,
    int MovieId,
    string Code,
    string MovieTitle,
    string DisplayTitle,
    double StartSeconds,
    double EndSeconds,
    IReadOnlyList<string> Actors,
    IReadOnlyList<SceneTagItem> Tags,
    bool IsFavorite,
    long? ThumbVersion,
    long? PreviewVersion)
{
    /// <summary>Whether the movie has a local video file the clip player can stream.</summary>
    public bool HasLocalVideo { get; init; }

    /// <summary>For the clip player's "Open in Jellyfin" link.</summary>
    public string? JellyfinItemId { get; init; }

    public string? JellyfinServerId { get; init; }

    /// <summary>The movie's media duration.</summary>
    public double? DurationSeconds { get; init; }

    /// <summary>How many of the movie's apexes lie in the highlight's range.</summary>
    public int ApexCount { get; init; }

    /// <summary>Tags rolled up from its apexes that it doesn't hold itself.</summary>
    public IReadOnlyList<ImplicitTag> ImplicitTags { get; init; } = [];
}

public sealed record HighlightWallPage(IReadOnlyList<HighlightWallCard> Cards, int TotalCount);

/// <summary>An apex on the wall: its own actors and tags. DisplayTitle is its label
/// ("Mei — Creampie"), which only the clip player shows. It has no screenshot, only the hover preview
/// around it.</summary>
public sealed record ApexWallCard(
    int ApexId,
    int MovieId,
    string Code,
    string MovieTitle,
    string DisplayTitle,
    double Seconds,
    IReadOnlyList<string> Actors,
    IReadOnlyList<SceneTagItem> Tags,
    bool IsFavorite,
    long? PreviewVersion)
{
    /// <summary>Whether the movie has a local video file the clip player can stream.</summary>
    public bool HasLocalVideo { get; init; }

    /// <summary>For the clip player's "Open in Jellyfin" link.</summary>
    public string? JellyfinItemId { get; init; }

    public string? JellyfinServerId { get; init; }

    /// <summary>The movie's media duration.</summary>
    public double? DurationSeconds { get; init; }

    /// <summary>How long the clip player plays before and after it.</summary>
    public ApexWindow Window { get; init; } = ApexWindow.Default;
}

public sealed record ApexWallPage(IReadOnlyList<ApexWallCard> Cards, int TotalCount);

public sealed record SceneWallOption(int Id, string Name);

/// <summary>What the wall's filters can offer: only tags, actors and studios that scenes actually use.</summary>
public sealed record SceneWallOptions(
    IReadOnlyList<SceneWallOption> Tags,
    IReadOnlyList<SceneWallOption> Actors,
    IReadOnlyList<string> Studios,
    ActorAttributeOptions ActorAttributes)
{
    /// <summary>Every tag on an apex, for the Apex tag filter.</summary>
    public IReadOnlyList<SceneWallOption> ApexTags { get; init; } = [];

    /// <summary>Every actor tag (Tag.IsActorTag) some clip of the view has for an actor, for the Actor tag filter.</summary>
    public IReadOnlyList<SceneWallOption> ActorTags { get; init; } = [];
}

public interface ISceneWallQueryService
{
    Task<SceneWallPage> GetPageAsync(SceneWallFilter filter, SceneWallSort sort, int randomSeed, int skip, int take, CancellationToken ct = default);

    /// <summary>includeHidden also offers what only hidden scenes use, matching SceneWallFilter.IncludeHidden.</summary>
    Task<SceneWallOptions> GetOptionsAsync(bool includeHidden = false, CancellationToken ct = default);

    /// <summary>The wall's highlight view: Tag, Actor, favorite and text match the
    /// highlight's own values, and IncludeHidden is ignored — hiding a scene never
    /// hides a highlight.</summary>
    Task<HighlightWallPage> GetHighlightPageAsync(SceneWallFilter filter, SceneWallSort sort, int randomSeed, int skip, int take, CancellationToken ct = default);

    /// <summary>Tags and actors on highlights, and studios of movies with highlights.</summary>
    Task<SceneWallOptions> GetHighlightOptionsAsync(CancellationToken ct = default);

    /// <summary>The wall's apex view: ApexTagIds, Actor and the actress attributes match the
    /// apex's own tags and actors, favorite its own flag, text the movie's code and title. TagIds,
    /// ApexOnly and IncludeHidden don't apply.</summary>
    Task<ApexWallPage> GetApexPageAsync(SceneWallFilter filter, SceneWallSort sort, int randomSeed, int skip, int take, CancellationToken ct = default);

    /// <summary>Actors of apexes and studios of movies with apexes; the apex tags are in ApexTags, Tags is empty.</summary>
    Task<SceneWallOptions> GetApexOptionsAsync(CancellationToken ct = default);
}

/// <summary>The library-wide scene wall's query. Filtering, ordering and paging run in
/// SQL; only the page's movies are then loaded to resolve each card's position ("Scene N") and
/// effective end the same way MovieSceneService does. highlightMedia, when present, decides which
/// highlight media variants the cards advertise. Apex cards resolve their position, tags,
/// actors and preview through apexService the way Movie Detail does. Scene and highlight
/// cards carry their rolled-up tags, computed per movie by ClipRollup as the clip editor does.</summary>
public sealed class SceneWallQueryService(
    IDbContextFactory<AppDbContext> dbFactory,
    IMovieSceneService sceneService,
    IHighlightMediaService? highlightMedia = null,
    IMovieApexService? apexService = null,
    IMovieHighlightService? highlightService = null) : ISceneWallQueryService
{
    private readonly IMovieApexService apexes = apexService ?? new MovieApexService(dbFactory);
    private readonly IMovieHighlightService highlights = highlightService ?? new MovieHighlightService(dbFactory);

    // Same seeded shuffle as the Movies grid's "random" sort (MovieGridQueryService).
    private const long RandomSortModulus = int.MaxValue;

    public async Task<SceneWallPage> GetPageAsync(SceneWallFilter filter, SceneWallSort sort, int randomSeed, int skip, int take, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var query = Filter(db, filter);
        var total = await query.CountAsync(ct);

        var page = await Order(query, sort, randomSeed)
            .Skip(skip)
            .Take(take)
            .Select(s => new { s.Id, s.MovieId, s.Movie.Code, Title = s.Movie.MetaTitle ?? s.Movie.Title, s.Movie.JellyfinItemId, s.Movie.JellyfinServerId, s.Movie.MediaDurationSeconds, s.Movie.LocalFileSizeBytes })
            .ToListAsync(ct);

        // Resolved per movie (positions and effective ends depend on the movie's other scenes).
        var itemsByMovie = new Dictionary<int, IReadOnlyList<SceneItem>>();
        foreach (var movieId in page.Select(p => p.MovieId).Distinct())
        {
            itemsByMovie[movieId] = await sceneService.GetScenesAsync(movieId, ct);
        }
        var apexSeconds = await ApexSecondsByMovieAsync(db, itemsByMovie.Keys, ct);
        var rollups = await RollupsAsync(itemsByMovie, ct);

        var cards = page
            .Select(p => (Row: p, Item: itemsByMovie[p.MovieId].FirstOrDefault(i => i.Id == p.Id)))
            .Where(x => x.Item is not null)
            .Select(x => new SceneWallCard(
                x.Item!.Id,
                x.Row.MovieId,
                x.Row.Code ?? string.Empty,
                MovieTitleWithoutCode(x.Row.Title, x.Row.Code),
                x.Item.DisplayTitle,
                x.Item.StartSeconds,
                x.Item.EffectiveEndSeconds,
                x.Item.EffectiveActors.Actors.Select(a => a.Name).ToList(),
                x.Item.Tags,
                x.Item.IsFavorite,
                x.Item.ThumbVersion,
                x.Item.PreviewVersion)
            {
                HasLocalVideo = x.Row.LocalFileSizeBytes != null,
                JellyfinItemId = x.Row.JellyfinItemId,
                JellyfinServerId = x.Row.JellyfinServerId,
                DurationSeconds = x.Row.MediaDurationSeconds,
                ApexCount = apexSeconds[x.Row.MovieId].Count(a => ApexRanges.IsWithin(x.Item.StartSeconds, x.Item.EffectiveEndSeconds, a)),
                ImplicitTags = rollups[x.Row.MovieId].Scenes.GetValueOrDefault(x.Item.Id, []),
            })
            .ToList();
        return new SceneWallPage(cards, total);
    }

    public async Task<SceneWallOptions> GetOptionsAsync(bool includeHidden = false, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        // Reached through Scenes, so only links of scenes that exist (and are shown) count. Tags include
        // those rolled up from highlights and apexes, actors their stored effective ones.
        var shown = db.Scenes.Where(s => includeHidden || !s.IsHiddenFromOverview);
        var shownSceneIds = shown.Select(s => s.Id);
        var tags = await TagOptionsQuery(db, ClipRollupQueries.SceneTags(db).Where(r => shownSceneIds.Contains(r.OwnerId))).ToListAsync(ct);
        var actors = await ActorsOf(db, ClipRollupQueries.SceneActors(db).Where(r => shownSceneIds.Contains(r.OwnerId))).ToListAsync(ct);
        var studios = await shown
            .Where(s => s.Movie.MetaStudio != null && s.Movie.MetaStudio != "")
            .Select(s => s.Movie.MetaStudio!)
            .Distinct()
            .ToListAsync(ct);
        var attributes = await ActorAttributeOptionsQuery.LoadAsync(
            CastAppearances(db.MovieActors.Where(ma => db.SceneEffectiveActors.Any(r =>
                r.MovieId == ma.MovieId && r.ActorId == ma.ActorId && (includeHidden || !r.Scene.IsHiddenFromOverview)))),
            DateOnly.FromDateTime(DateTime.UtcNow), ct);

        return new SceneWallOptions(
            TagOptions(tags),
            actors.Select(a => new SceneWallOption(a.Id, a.DisplayName))
                .OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase).ToList(),
            studios.OrderBy(s => s, StringComparer.OrdinalIgnoreCase).ToList(),
            attributes)
        {
            ApexTags = await ApexTagOptionsAsync(db, ct),
            ActorTags = await ActorTagOptionsAsync(db.SceneEffectiveActorTags.Select(r => r.Tag), ct),
        };
    }

    public async Task<HighlightWallPage> GetHighlightPageAsync(SceneWallFilter filter, SceneWallSort sort, int randomSeed, int skip, int take, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var query = FilterHighlights(db, filter);
        var total = await query.CountAsync(ct);

        var page = await OrderHighlights(query, sort, randomSeed)
            .Skip(skip)
            .Take(take)
            .Select(h => new
            {
                h.Id,
                h.MovieId,
                h.Movie.Code,
                MovieTitle = h.Movie.MetaTitle ?? h.Movie.Title,
                h.Title,
                h.StartSeconds,
                h.EndSeconds,
                h.IsFavorite,
                h.Movie.JellyfinItemId,
                h.Movie.JellyfinServerId,
                h.Movie.MediaDurationSeconds,
                h.Movie.LocalFileSizeBytes,
            })
            .ToListAsync(ct);

        // Positions ("Highlight N"), per movie on the page.
        var movieIds = page.Select(p => p.MovieId).Distinct().ToList();
        var siblings = await db.MovieHighlights.AsNoTracking()
            .Where(h => movieIds.Contains(h.MovieId))
            .Select(h => new { h.Id, h.MovieId, h.StartSeconds })
            .ToListAsync(ct);
        var positions = siblings
            .GroupBy(h => h.MovieId)
            .SelectMany(g => g.OrderBy(h => h.StartSeconds).ThenBy(h => h.Id).Select((h, i) => (h.Id, Position: i + 1)))
            .ToDictionary(x => x.Id, x => x.Position);
        var ids = page.Select(p => p.Id).ToList();
        // Effective actors: its own, else inherited.
        var actorsById = (await (from r in ClipRollupQueries.HighlightActors(db)
                                 where ids.Contains(r.OwnerId)
                                 join a in db.Actors on r.ActorId equals a.Id
                                 select new { r.OwnerId, Actor = a })
                .AsNoTracking()
                .ToListAsync(ct))
            .ToLookup(x => x.OwnerId, x => x.Actor.DisplayName);
        var tagsById = (await db.HighlightTags.AsNoTracking()
                .Where(ht => ids.Contains(ht.HighlightId))
                .Select(ht => new { ht.HighlightId, ht.TagId, ht.Tag.Name, Parent = ht.Tag.ParentTag != null ? ht.Tag.ParentTag.Name : null })
                .ToListAsync(ct))
            .ToLookup(x => x.HighlightId, x => new SceneTagItem(x.TagId, x.Name, x.Parent));

        var apexSeconds = await ApexSecondsByMovieAsync(db, movieIds, ct);
        // Only the apexes matter to a highlight's roll-up, so no scenes are loaded.
        var rollups = await RollupsAsync(movieIds.ToDictionary(id => id, _ => (IReadOnlyList<SceneItem>)[]), ct);
        var media = await db.CachedImages.AsNoTracking()
            .Where(c => c.Role == HighlightMediaService.Role && ids.Contains(c.Index))
            .ToListAsync(ct);

        // Only media /highlight-image would serve counts (as for scene cards).
        long? MediaVersion(int id, double start, double end, string variant)
        {
            if (highlightMedia is not null && !highlightMedia.ServesVariant(variant)) return null;
            var window = HighlightMediaService.Window(start, end);
            var row = media.FirstOrDefault(m => m.Index == id && m.Variant == variant);
            var current = row is not null && (highlightMedia?.IsCurrent(row, window) ?? (row.SceneStartMs == window.StartMs && row.SceneEndMs == window.EndMs));
            return current ? row!.UpdatedAt.Ticks : null;
        }

        var cards = page.Select(p =>
        {
            // Ordered by name, as Movie Detail lists them.
            var actors = actorsById[p.Id].OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToList();
            var tags = tagsById[p.Id].OrderBy(t => t.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
            return new HighlightWallCard(
                p.Id,
                p.MovieId,
                p.Code ?? string.Empty,
                MovieTitleWithoutCode(p.MovieTitle, p.Code),
                HighlightRanges.DisplayTitle(p.Title, positions.GetValueOrDefault(p.Id, 1), actors, tags.Select(t => t.Name)),
                p.StartSeconds,
                p.EndSeconds,
                actors,
                tags,
                p.IsFavorite,
                MediaVersion(p.Id, p.StartSeconds, p.EndSeconds, SceneMediaService.VariantThumb),
                MediaVersion(p.Id, p.StartSeconds, p.EndSeconds, SceneMediaService.VariantPreview))
            {
                HasLocalVideo = p.LocalFileSizeBytes != null,
                JellyfinItemId = p.JellyfinItemId,
                JellyfinServerId = p.JellyfinServerId,
                DurationSeconds = p.MediaDurationSeconds,
                ApexCount = apexSeconds[p.MovieId].Count(a => a >= p.StartSeconds && a < p.EndSeconds),
                ImplicitTags = rollups[p.MovieId].Highlights.GetValueOrDefault(p.Id, []),
            };
        }).ToList();
        return new HighlightWallPage(cards, total);
    }

    public async Task<ApexWallPage> GetApexPageAsync(SceneWallFilter filter, SceneWallSort sort, int randomSeed, int skip, int take, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var query = FilterApexes(db, filter);
        var total = await query.CountAsync(ct);

        var page = await OrderApexes(query, sort, randomSeed)
            .Skip(skip)
            .Take(take)
            .Select(a => new { a.Id, a.MovieId, a.Movie.Code, Title = a.Movie.MetaTitle ?? a.Movie.Title, a.Movie.JellyfinItemId, a.Movie.JellyfinServerId, a.Movie.MediaDurationSeconds, a.Movie.LocalFileSizeBytes })
            .ToListAsync(ct);

        // Resolved per movie, the way Movie Detail lists them.
        var itemsById = new Dictionary<int, ApexItem>();
        foreach (var movieId in page.Select(p => p.MovieId).Distinct())
        {
            foreach (var item in await apexes.GetApexesAsync(movieId, ct))
            {
                itemsById[item.Id] = item;
            }
        }

        var cards = page
            .Where(p => itemsById.ContainsKey(p.Id))
            .Select(p =>
            {
                var item = itemsById[p.Id];
                return new ApexWallCard(
                    p.Id,
                    p.MovieId,
                    p.Code ?? string.Empty,
                    MovieTitleWithoutCode(p.Title, p.Code),
                    // The wall names the actors even in a solo movie, unlike Movie Detail.
                    (item with { CastCount = int.MaxValue }).DisplayLabel,
                    item.Seconds,
                    item.EffectiveActors.Actors.Select(a => a.Name).ToList(),
                    item.Tags,
                    item.IsFavorite,
                    item.PreviewVersion)
                {
                    HasLocalVideo = p.LocalFileSizeBytes != null,
                    JellyfinItemId = p.JellyfinItemId,
                    JellyfinServerId = p.JellyfinServerId,
                    DurationSeconds = p.MediaDurationSeconds,
                    Window = item.Window,
                };
            })
            .ToList();
        return new ApexWallPage(cards, total);
    }

    public async Task<SceneWallOptions> GetApexOptionsAsync(CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var actors = await ActorsOf(db, ClipRollupQueries.ApexActors(db)).ToListAsync(ct);
        var studios = await db.MovieApexes
            .Where(a => a.Movie.MetaStudio != null && a.Movie.MetaStudio != "")
            .Select(a => a.Movie.MetaStudio!)
            .Distinct()
            .ToListAsync(ct);
        var appearances = CastAppearances(db.MovieActors.Where(ma => db.ApexEffectiveActors.Any(r => r.MovieId == ma.MovieId && r.ActorId == ma.ActorId)));
        var attributes = await ActorAttributeOptionsQuery.LoadAsync(appearances, DateOnly.FromDateTime(DateTime.UtcNow), ct);

        return new SceneWallOptions(
            [],
            actors.Select(a => new SceneWallOption(a.Id, a.DisplayName))
                .OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase).ToList(),
            studios.OrderBy(s => s, StringComparer.OrdinalIgnoreCase).ToList(),
            attributes)
        {
            ApexTags = await ApexTagOptionsAsync(db, ct),
            ActorTags = await ActorTagOptionsAsync(db.HighlightEffectiveActorTags.Select(r => r.Tag), ct),
        };
    }

    // The tags some clip of the view has for an actor.
    private static async Task<IReadOnlyList<SceneWallOption>> ActorTagOptionsAsync(IQueryable<Tag> usedTags, CancellationToken ct) =>
        [.. (await ActorTagOptions.LoadAsync(usedTags, ct)).Select(o => new SceneWallOption(o.Id, o.Label))];

    private static async Task<IReadOnlyList<SceneWallOption>> ApexTagOptionsAsync(AppDbContext db, CancellationToken ct)
    {
        var tags = await db.ApexTags
            .Select(at => new { at.Tag.Id, at.Tag.Name, Parent = at.Tag.ParentTag != null ? at.Tag.ParentTag.Name : null })
            .Distinct()
            .ToListAsync(ct);
        return tags.Select(t => new SceneWallOption(t.Id, t.Parent is null ? t.Name : $"{t.Parent} › {t.Name}"))
            .OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>Each movie's tag roll-up over the given scenes and all its highlights and apexes,
    /// so the sources ("highlight 2", "apex 3") number them as Movie Detail does.</summary>
    private async Task<Dictionary<int, ClipRollupResult>> RollupsAsync(IReadOnlyDictionary<int, IReadOnlyList<SceneItem>> scenesByMovie, CancellationToken ct)
    {
        var rollups = new Dictionary<int, ClipRollupResult>();
        foreach (var (movieId, scenes) in scenesByMovie)
        {
            rollups[movieId] = ClipRollup.Compute(scenes, await highlights.GetHighlightsAsync(movieId, ct), await apexes.GetApexesAsync(movieId, ct));
        }
        return rollups;
    }

    /// <summary>The times of every apex of the given movies, for the cards' apex counts.</summary>
    private static async Task<ILookup<int, double>> ApexSecondsByMovieAsync(AppDbContext db, IEnumerable<int> movieIds, CancellationToken ct)
    {
        var ids = movieIds.ToList();
        var rows = await db.MovieApexes.AsNoTracking()
            .Where(a => ids.Contains(a.MovieId))
            .Select(a => new { a.MovieId, a.Seconds })
            .ToListAsync(ct);
        return rows.ToLookup(r => r.MovieId, r => r.Seconds);
    }

    public async Task<SceneWallOptions> GetHighlightOptionsAsync(CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var tags = await TagOptionsQuery(db, ClipRollupQueries.HighlightTags(db)).ToListAsync(ct);
        var actors = await ActorsOf(db, ClipRollupQueries.HighlightActors(db)).ToListAsync(ct);
        var studios = await db.MovieHighlights
            .Where(h => h.Movie.MetaStudio != null && h.Movie.MetaStudio != "")
            .Select(h => h.Movie.MetaStudio!)
            .Distinct()
            .ToListAsync(ct);
        var appearances = CastAppearances(db.MovieActors.Where(ma => db.HighlightEffectiveActors.Any(r => r.MovieId == ma.MovieId && r.ActorId == ma.ActorId)));
        var attributes = await ActorAttributeOptionsQuery.LoadAsync(appearances, DateOnly.FromDateTime(DateTime.UtcNow), ct);

        return new SceneWallOptions(
            TagOptions(tags),
            actors.Select(a => new SceneWallOption(a.Id, a.DisplayName))
                .OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase).ToList(),
            studios.OrderBy(s => s, StringComparer.OrdinalIgnoreCase).ToList(),
            attributes)
        {
            ApexTags = await ApexTagOptionsAsync(db, ct),
            ActorTags = await ActorTagOptionsAsync(db.ApexEffectiveActorTags.Select(r => r.Tag), ct),
        };
    }

    /// <summary>Metadata titles usually start with the code ("MIH-006 If You Want…"), which the card
    /// already shows next to the title.</summary>
    public static string MovieTitleWithoutCode(string? title, string? code)
    {
        if (string.IsNullOrWhiteSpace(title)) return code ?? string.Empty;
        if (!string.IsNullOrEmpty(code) && title.StartsWith(code, StringComparison.OrdinalIgnoreCase))
        {
            var rest = title[code.Length..].TrimStart(' ', '-', ':', '|', '·');
            if (rest.Length > 0) return rest;
        }
        return title;
    }

    /// <summary>The cast links behind a view's effective actors, each once with its movie's release date — her age
    /// and cup size depend only on the movie, so one appearance per (movie, actor) is enough.</summary>
    private static IQueryable<ActorAppearance> CastAppearances(IQueryable<MovieActor> links) =>
        links.Select(ma => new ActorAppearance { Actor = ma.Actor, Release = ma.Movie.MetaReleaseDate });

    private sealed record TagOption(int Id, string Name, string? Parent);

    /// <summary>The distinct tags behind a set of (owner, tag) rows, with their parent's name.</summary>
    private static IQueryable<TagOption> TagOptionsQuery(AppDbContext db, IQueryable<OwnerTag> rows) =>
        db.Tags
            .Where(t => rows.Select(r => r.TagId).Contains(t.Id))
            .Select(t => new TagOption(t.Id, t.Name, t.ParentTag != null ? t.ParentTag.Name : null));

    private static List<SceneWallOption> TagOptions(IEnumerable<TagOption> tags) =>
        tags.Select(t => new SceneWallOption(t.Id, t.Parent is null ? t.Name : $"{t.Parent} › {t.Name}"))
            .OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase).ToList();

    /// <summary>The distinct actors behind a set of (owner, actor) rows.</summary>
    private static IQueryable<Actor> ActorsOf(AppDbContext db, IQueryable<OwnerActor> rows) =>
        db.Actors.Where(a => rows.Select(r => r.ActorId).Contains(a.Id));

    /// <summary>Ids of the actors meeting the measurement criteria, or null when there are none. Cup size and
    /// age need the scene's movie, so the filters below add them per owner: one actress must meet everything.</summary>
    private static IQueryable<int>? MatchingActorIds(AppDbContext db, ActorAttributeSelection selection) =>
        selection.Height.IsSet || selection.Bust.IsSet || selection.Waist.IsSet || selection.Hips.IsSet
            ? MovieFilterPredicates.WhereActors(db.Actors, selection).Select(a => a.Id)
            : null;

    private sealed class OwnerRelease
    {
        public int OwnerId { get; init; }
        public DateTime? Release { get; init; }
    }

    /// <summary>Owners (scenes, highlights or apexes) with one effective actress meeting every selected
    /// attribute; age is hers on the owner's movie's release date, today without one, and cup size
    /// hers on that release date, her regular cup size without one.</summary>
    private static IQueryable<int> OwnersWithMatchingActress(AppDbContext db, IQueryable<OwnerActor> rows, IQueryable<OwnerRelease> releases, ActorAttributeSelection attributes)
    {
        var ids = MatchingActorIds(db, attributes) ?? db.Actors.Select(a => a.Id); // cup/age alone: everyone is a candidate
        var cups = attributes.CupSizes.Count > 0 ? attributes.CupSizes.Select(c => c.Trim().ToUpperInvariant()).ToList() : null;
        var hasAge = attributes.Age.IsSet;
        var (ageLo, ageHi) = (attributes.Age.Min ?? int.MinValue, attributes.Age.Max ?? int.MaxValue);
        var todayDate = DateTime.UtcNow.Date;
        return from r in rows
               where ids.Contains(r.ActorId)
               join x in db.Actors on r.ActorId equals x.Id
               join o in releases on r.OwnerId equals o.OwnerId
               where cups == null || cups.Contains((x.CupSizePeriods
                   .Where(p => o.Release != null && p.EffectiveFrom <= o.Release)
                   .OrderByDescending(p => p.EffectiveFrom)
                   .Select(p => p.CupSize)
                   .FirstOrDefault() ?? x.CupSize ?? "").Trim().ToUpper())
               where !hasAge || (x.BirthDate != null &&
                   ((o.Release ?? todayDate).Year - x.BirthDate.Value.Year - (((o.Release ?? todayDate).Month < x.BirthDate.Value.Month || ((o.Release ?? todayDate).Month == x.BirthDate.Value.Month && (o.Release ?? todayDate).Day < x.BirthDate.Value.Day)) ? 1 : 0)) >= ageLo &&
                   ((o.Release ?? todayDate).Year - x.BirthDate.Value.Year - (((o.Release ?? todayDate).Month < x.BirthDate.Value.Month || ((o.Release ?? todayDate).Month == x.BirthDate.Value.Month && (o.Release ?? todayDate).Day < x.BirthDate.Value.Day)) ? 1 : 0)) <= ageHi)
               select r.OwnerId;
    }

    /// <summary>The selected tags and their subtags (a filter on a parent tag matches its subtags).</summary>
    private static IQueryable<int> MatchingTagIds(AppDbContext db, IReadOnlyCollection<int> tagIds) =>
        db.Tags.Where(t => tagIds.Contains(t.Id) || (t.ParentTagId != null && tagIds.Contains(t.ParentTagId.Value))).Select(t => t.Id);

    private static IQueryable<Scene> Filter(AppDbContext db, SceneWallFilter filter)
    {
        var query = db.Scenes.AsNoTracking();
        if (!filter.IncludeHidden)
        {
            query = query.Where(s => !s.IsHiddenFromOverview);
        }
        // Tags rolled up from highlights and apexes, actors falling back to the cast.
        var sceneActors = ClipRollupQueries.SceneActors(db);
        if (filter.TagIds is { Count: > 0 } tagIds)
        {
            var matching = MatchingTagIds(db, tagIds);
            var sceneTags = ClipRollupQueries.SceneTags(db);
            // Non-correlated IN (…) so SQLite evaluates the roll-up once, not once per scene.
            var withTag = sceneTags.Where(r => matching.Contains(r.TagId)).Select(r => r.OwnerId);
            query = query.Where(s => withTag.Contains(s.Id));
        }
        if (filter.ActorIds is { Count: > 0 } actorIds)
        {
            var withActor = sceneActors.Where(r => actorIds.Contains(r.ActorId)).Select(r => r.OwnerId);
            query = query.Where(s => withActor.Contains(s.Id));
        }
        if (filter.ActorTagIds is { Count: > 0 } actorTagIds)
        {
            var (anyActor, onlyActors) = ActorTagActors(filter);
            var matchingActorTags = MatchingTagIds(db, actorTagIds);
            var withActorTag = db.SceneEffectiveActorTags.Where(r => matchingActorTags.Contains(r.TagId) && (anyActor || onlyActors.Contains(r.ActorId))).Select(r => r.SceneId);
            query = query.Where(s => withActorTag.Contains(s.Id));
        }
        if (filter.ActorAttributes is { IsEmpty: false } attributes)
        {
            // The scene's effective actresses count (like the Actor filter), and one of them must meet
            // every criterion; age is hers on the scene's movie's release date (today without one).
            var releases = db.Scenes.Select(x => new OwnerRelease { OwnerId = x.Id, Release = x.Movie.MetaReleaseDate });
            var matched = OwnersWithMatchingActress(db, sceneActors, releases, attributes);
            query = query.Where(s => matched.Contains(s.Id));
        }
        if (filter.Studios is { Count: > 0 } studios)
        {
            query = query.Where(s => s.Movie.MetaStudio != null && studios.Contains(s.Movie.MetaStudio));
        }
        if (!string.IsNullOrWhiteSpace(filter.Text))
        {
            // LIKE, as on the Movies grid, for SQLite's case-insensitive ASCII matching.
            var pattern = $"%{filter.Text.Trim()}%";
            query = query.Where(s =>
                (s.Title != null && EF.Functions.Like(s.Title, pattern))
                || (s.Movie.Code != null && EF.Functions.Like(s.Movie.Code, pattern))
                || (s.Movie.MetaTitle != null && EF.Functions.Like(s.Movie.MetaTitle, pattern))
                || (s.Movie.Title != null && EF.Functions.Like(s.Movie.Title, pattern)));
        }
        if (filter.FavoritesOnly)
        {
            query = query.Where(s => s.IsFavorite);
        }
        if (WantsApex(filter))
        {
            // In the scene's effective range, by the same rule the roll-up uses (ClipRollupQueries.ApexScenes).
            var apexes = MatchingApexes(db, filter);
            var apexScenes = ClipRollupQueries.ApexScenes(db);
            var apexIds = apexes.Select(a => a.Id);
            var withApex = apexScenes.Where(p => apexIds.Contains(p.OwnerId)).Select(p => p.OtherId);
            query = query.Where(s => withApex.Contains(s.Id));
        }
        return query;
    }

    private static IQueryable<MovieHighlight> FilterHighlights(AppDbContext db, SceneWallFilter filter)
    {
        var query = db.MovieHighlights.AsNoTracking();
        // Tags rolled up from its apexes, actors inherited when it has none.
        var highlightActors = ClipRollupQueries.HighlightActors(db);
        if (filter.TagIds is { Count: > 0 } tagIds)
        {
            var matching = MatchingTagIds(db, tagIds);
            var highlightTags = ClipRollupQueries.HighlightTags(db);
            var withTag = highlightTags.Where(r => matching.Contains(r.TagId)).Select(r => r.OwnerId);
            query = query.Where(h => withTag.Contains(h.Id));
        }
        if (filter.ActorIds is { Count: > 0 } actorIds)
        {
            var withActor = highlightActors.Where(r => actorIds.Contains(r.ActorId)).Select(r => r.OwnerId);
            query = query.Where(h => withActor.Contains(h.Id));
        }
        if (filter.ActorTagIds is { Count: > 0 } actorTagIds)
        {
            var (anyActor, onlyActors) = ActorTagActors(filter);
            var matchingActorTags = MatchingTagIds(db, actorTagIds);
            var withActorTag = db.HighlightEffectiveActorTags.Where(r => matchingActorTags.Contains(r.TagId) && (anyActor || onlyActors.Contains(r.ActorId))).Select(r => r.HighlightId);
            query = query.Where(h => withActorTag.Contains(h.Id));
        }
        if (filter.ActorAttributes is { IsEmpty: false } attributes)
        {
            // Like the scene filter, but over the highlight's effective actors.
            var releases = db.MovieHighlights.Select(x => new OwnerRelease { OwnerId = x.Id, Release = x.Movie.MetaReleaseDate });
            var matched = OwnersWithMatchingActress(db, highlightActors, releases, attributes);
            query = query.Where(h => matched.Contains(h.Id));
        }
        if (filter.Studios is { Count: > 0 } studios)
        {
            query = query.Where(h => h.Movie.MetaStudio != null && studios.Contains(h.Movie.MetaStudio));
        }
        if (!string.IsNullOrWhiteSpace(filter.Text))
        {
            var pattern = $"%{filter.Text.Trim()}%";
            query = query.Where(h =>
                (h.Title != null && EF.Functions.Like(h.Title, pattern))
                || (h.Movie.Code != null && EF.Functions.Like(h.Movie.Code, pattern))
                || (h.Movie.MetaTitle != null && EF.Functions.Like(h.Movie.MetaTitle, pattern))
                || (h.Movie.Title != null && EF.Functions.Like(h.Movie.Title, pattern)));
        }
        if (filter.FavoritesOnly)
        {
            query = query.Where(h => h.IsFavorite);
        }
        if (WantsApex(filter))
        {
            var apexes = MatchingApexes(db, filter);
            var apexHighlights = ClipRollupQueries.ApexHighlights(db);
            var apexIds = apexes.Select(a => a.Id);
            var withApex = apexHighlights.Where(p => apexIds.Contains(p.OwnerId)).Select(p => p.OtherId);
            query = query.Where(h => withApex.Contains(h.Id));
        }
        return query;
    }

    /// <summary>Whether an actor-tag filter matches any actor, else the actors it is limited to (the Actor filter's).</summary>
    private static (bool AnyActor, IReadOnlyCollection<int> OnlyActors) ActorTagActors(SceneWallFilter filter) =>
        filter.ActorIds is { Count: > 0 } actorIds ? (false, actorIds) : (true, []);

    private static bool WantsApex(SceneWallFilter filter) => filter.ApexOnly || filter.ApexTagIds is { Count: > 0 };

    /// <summary>The apexes a scene or highlight must contain one of: all of them, or those with a selected
    /// apex tag or a subtag of one.</summary>
    private static IQueryable<MovieApex> MatchingApexes(AppDbContext db, SceneWallFilter filter)
    {
        IQueryable<MovieApex> apexes = db.MovieApexes;
        if (filter.ApexTagIds is { Count: > 0 } tagIds)
        {
            apexes = apexes.Where(a => a.ApexTags.Any(at => tagIds.Contains(at.TagId) || (at.Tag.ParentTagId != null && tagIds.Contains(at.Tag.ParentTagId.Value))));
        }
        return apexes;
    }

    private static IQueryable<MovieApex> FilterApexes(AppDbContext db, SceneWallFilter filter)
    {
        var query = MatchingApexes(db, filter).AsNoTracking();
        // Actors inherited when it has none.
        var apexActors = ClipRollupQueries.ApexActors(db);
        if (filter.ActorIds is { Count: > 0 } actorIds)
        {
            var withActor = apexActors.Where(r => actorIds.Contains(r.ActorId)).Select(r => r.OwnerId);
            query = query.Where(a => withActor.Contains(a.Id));
        }
        if (filter.ActorTagIds is { Count: > 0 } actorTagIds)
        {
            var (anyActor, onlyActors) = ActorTagActors(filter);
            var matchingActorTags = MatchingTagIds(db, actorTagIds);
            var withActorTag = db.ApexEffectiveActorTags.Where(r => matchingActorTags.Contains(r.TagId) && (anyActor || onlyActors.Contains(r.ActorId))).Select(r => r.ApexId);
            query = query.Where(a => withActorTag.Contains(a.Id));
        }
        if (filter.ActorAttributes is { IsEmpty: false } attributes)
        {
            // Like the scene filter, but over the apex's effective actors.
            var releases = db.MovieApexes.Select(x => new OwnerRelease { OwnerId = x.Id, Release = x.Movie.MetaReleaseDate });
            var matched = OwnersWithMatchingActress(db, apexActors, releases, attributes);
            query = query.Where(a => matched.Contains(a.Id));
        }
        if (filter.Studios is { Count: > 0 } studios)
        {
            query = query.Where(a => a.Movie.MetaStudio != null && studios.Contains(a.Movie.MetaStudio));
        }
        if (!string.IsNullOrWhiteSpace(filter.Text))
        {
            var pattern = $"%{filter.Text.Trim()}%";
            query = query.Where(a =>
                (a.Movie.Code != null && EF.Functions.Like(a.Movie.Code, pattern))
                || (a.Movie.MetaTitle != null && EF.Functions.Like(a.Movie.MetaTitle, pattern))
                || (a.Movie.Title != null && EF.Functions.Like(a.Movie.Title, pattern)));
        }
        if (filter.FavoritesOnly)
        {
            query = query.Where(a => a.IsFavorite);
        }
        return query;
    }

    // Same orders as the scene wall's, with apexes in playback order within a movie.
    private static IQueryable<MovieApex> OrderApexes(IQueryable<MovieApex> query, SceneWallSort sort, int randomSeed) => sort switch
    {
        SceneWallSort.DateAdded => query
            .OrderByDescending(a => a.Movie.FileAddedAt ?? a.Movie.CreatedAt)
            .ThenBy(a => a.MovieId).ThenBy(a => a.Seconds).ThenBy(a => a.Id),
        SceneWallSort.Random => query.OrderBy(a => (long)a.Id * randomSeed % RandomSortModulus).ThenBy(a => a.Id),
        _ => query
            .OrderByDescending(a => a.Movie.MetaReleaseDate)
            .ThenBy(a => a.MovieId).ThenBy(a => a.Seconds).ThenBy(a => a.Id),
    };

    // Same orders as the scene wall's, with highlights in playback order within a movie.
    private static IQueryable<MovieHighlight> OrderHighlights(IQueryable<MovieHighlight> query, SceneWallSort sort, int randomSeed) => sort switch
    {
        SceneWallSort.DateAdded => query
            .OrderByDescending(h => h.Movie.FileAddedAt ?? h.Movie.CreatedAt)
            .ThenBy(h => h.MovieId).ThenBy(h => h.StartSeconds).ThenBy(h => h.Id),
        SceneWallSort.Random => query.OrderBy(h => (long)h.Id * randomSeed % RandomSortModulus).ThenBy(h => h.Id),
        _ => query
            .OrderByDescending(h => h.Movie.MetaReleaseDate)
            .ThenBy(h => h.MovieId).ThenBy(h => h.StartSeconds).ThenBy(h => h.Id),
    };

    // Newest first for the date sorts; within a movie, scenes stay in playback order.
    private static IQueryable<Scene> Order(IQueryable<Scene> query, SceneWallSort sort, int randomSeed) => sort switch
    {
        SceneWallSort.DateAdded => query
            .OrderByDescending(s => s.Movie.FileAddedAt ?? s.Movie.CreatedAt)
            .ThenBy(s => s.MovieId).ThenBy(s => s.StartSeconds),
        SceneWallSort.Random => query.OrderBy(s => (long)s.Id * randomSeed % RandomSortModulus).ThenBy(s => s.Id),
        _ => query
            .OrderByDescending(s => s.Movie.MetaReleaseDate)
            .ThenBy(s => s.MovieId).ThenBy(s => s.StartSeconds),
    };
}
