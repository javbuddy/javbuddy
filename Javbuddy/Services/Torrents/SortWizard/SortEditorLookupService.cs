using Javbuddy.Data;
using Javbuddy.Models;
using Javbuddy.Services.Javinizer;
using Javbuddy.Services.Tags;
using Microsoft.EntityFrameworkCore;

namespace Javbuddy.Services.Torrents.SortWizard;

public record TrackedActorMatch(int ActorId, bool HasImage);

/// <summary>Javbuddy-DB lookups for the TorrentSort metadata editor, so the
/// component never touches the DbContext: tracked actor → javinizer actress DTO, scraped
/// actress → tracked actor (for headshots), live genre normalization and the genre palette.</summary>
public interface ISortEditorLookupService
{
    Task<ActressViewDto?> ToActressDtoAsync(int actorId, CancellationToken ct = default);

    /// <summary>One entry per input actress, in order; null when no tracked actor matches.</summary>
    Task<IReadOnlyList<TrackedActorMatch?>> MatchTrackedActorsAsync(IReadOnlyList<ActressViewDto> actresses, CancellationToken ct = default);

    Task<IReadOnlyList<GenreResolution>> ResolveGenresAsync(IEnumerable<string?> names, CancellationToken ct = default);

    Task<IReadOnlyList<string>> GetTopTagsAsync(int count, CancellationToken ct = default);
}

public class SortEditorLookupService(IDbContextFactory<AppDbContext> dbFactory, ITagService tagService) : ISortEditorLookupService
{
    public async Task<ActressViewDto?> ToActressDtoAsync(int actorId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var actor = await db.Actors.AsNoTracking().Include(a => a.Aliases).FirstOrDefaultAsync(a => a.Id == actorId, ct);
        if (actor is null) return null;

        return new ActressViewDto
        {
            FirstName = actor.FirstName,
            LastName = actor.LastName,
            JapaneseName = actor.JapaneseNameKanji,
            // javinizer-go's Actress.Aliases is pipe-separated (internal/models/movie.go).
            Aliases = actor.Aliases.Count > 0 ? string.Join('|', actor.Aliases.Select(a => a.Name)) : null
        };
    }

    public async Task<IReadOnlyList<TrackedActorMatch?>> MatchTrackedActorsAsync(IReadOnlyList<ActressViewDto> actresses, CancellationToken ct = default)
    {
        if (actresses.Count == 0) return [];

        var kanji = actresses.Select(a => Compact(a.JapaneseName)).OfType<string>().Distinct().ToList();
        var firstNames = actresses.Select(a => a.FirstName?.Trim().ToUpper()).OfType<string>().Where(s => s.Length > 0).Distinct().ToList();

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var candidates = await db.Actors.AsNoTracking()
            .Where(a => (a.JapaneseNameKanji != null && kanji.Contains(a.JapaneseNameKanji.Replace(" ", "").Replace("　", "")))
                || (a.FirstName != null && firstNames.Contains(a.FirstName.ToUpper())))
            .Select(a => new
            {
                a.Id,
                a.FirstName,
                a.LastName,
                a.JapaneseNameKanji,
                HasImage = db.ActorImages.Any(i => i.ActorId == a.Id && i.Variant == "thumb")
            })
            .ToListAsync(ct);

        return actresses.Select(actress =>
        {
            var japanese = Compact(actress.JapaneseName);
            var hit = candidates.FirstOrDefault(c => japanese is not null && Compact(c.JapaneseNameKanji) == japanese)
                ?? candidates.FirstOrDefault(c =>
                    string.Equals(c.FirstName, actress.FirstName?.Trim(), StringComparison.OrdinalIgnoreCase)
                    && string.Equals(c.LastName ?? "", actress.LastName?.Trim() ?? "", StringComparison.OrdinalIgnoreCase));
            return hit is null ? null : new TrackedActorMatch(hit.Id, hit.HasImage);
        }).ToList();
    }

    public async Task<IReadOnlyList<GenreResolution>> ResolveGenresAsync(IEnumerable<string?> names, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await TagNormalization.ResolveRawValuesAsync(db, names, ct);
    }

    public async Task<IReadOnlyList<string>> GetTopTagsAsync(int count, CancellationToken ct = default)
    {
        var tags = await tagService.GetTagsAsync(null, TagSortOrder.UsageDesc, ct);
        return tags.Where(t => !t.NeedsReview)
            .Take(count)
            .Select(t => t.ParentTagName != null ? $"{t.ParentTagName}{Tag.HierarchyDelimiter}{t.Name}" : t.Name)
            .ToList();
    }

    private static string? Compact(string? name) =>
        string.IsNullOrWhiteSpace(name) ? null : name.Replace(" ", "").Replace("　", "");
}

/// <summary>Fallback when no lookup service is wired (older tests and callers): no tracked
/// actors, every genre passes through as new, empty palette.</summary>
public sealed class NullSortEditorLookup : ISortEditorLookupService
{
    public static readonly NullSortEditorLookup Instance = new();

    public Task<ActressViewDto?> ToActressDtoAsync(int actorId, CancellationToken ct = default) =>
        Task.FromResult<ActressViewDto?>(null);

    public Task<IReadOnlyList<TrackedActorMatch?>> MatchTrackedActorsAsync(IReadOnlyList<ActressViewDto> actresses, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<TrackedActorMatch?>>(actresses.Select(_ => (TrackedActorMatch?)null).ToList());

    public Task<IReadOnlyList<GenreResolution>> ResolveGenresAsync(IEnumerable<string?> names, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<GenreResolution>>(names
            .Select(n => n?.Trim())
            .OfType<string>()
            .Where(n => n.Length > 0)
            .Select(n => new GenreResolution(n, n, false))
            .ToList());

    public Task<IReadOnlyList<string>> GetTopTagsAsync(int count, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<string>>([]);
}
