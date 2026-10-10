using Javbuddy.Data;
using Microsoft.EntityFrameworkCore;

namespace Javbuddy.Services.Scenes;

/// <summary>One stored effective actor tag of a clip, by the tag's name.</summary>
public sealed record ActorTagRow(int OwnerId, int ActorId, string TagName);

/// <summary>The wall cards' actor tags: the stored effective ones of the cards on a page (not the rolled-up ones), as
/// "Mei (Blonde, Tattoo)" labels beside each actor's name.</summary>
public sealed class ActorTagLookup(IReadOnlyDictionary<int, IReadOnlyDictionary<string, IReadOnlyList<string>>> byOwner)
{
    public static async Task<ActorTagLookup> LoadAsync(AppDbContext db, Func<List<int>, IQueryable<ActorTagRow>> rowsOf, IEnumerable<int> ownerIds, CancellationToken ct)
    {
        var ids = ownerIds.ToList();
        if (ids.Count == 0) return new ActorTagLookup(new Dictionary<int, IReadOnlyDictionary<string, IReadOnlyList<string>>>());

        var found = await rowsOf(ids).ToListAsync(ct);
        var actorIds = found.Select(r => r.ActorId).Distinct().ToList();
        var names = (await db.Actors.AsNoTracking().Where(a => actorIds.Contains(a.Id)).ToListAsync(ct)).ToDictionary(a => a.Id, a => a.DisplayName);
        var byOwner = found
            .GroupBy(r => r.OwnerId)
            .ToDictionary(
                owner => owner.Key,
                owner => (IReadOnlyDictionary<string, IReadOnlyList<string>>)owner
                    .GroupBy(r => names.GetValueOrDefault(r.ActorId) ?? string.Empty)
                    .ToDictionary(actor => actor.Key, actor => (IReadOnlyList<string>)actor.Select(r => r.TagName).Order(StringComparer.OrdinalIgnoreCase).ToList()));
        return new ActorTagLookup(byOwner);
    }

    /// <summary>The actors' names with their tags in parentheses, or null when none has any.</summary>
    public IReadOnlyList<string>? Labels(int ownerId, IReadOnlyList<string> actors)
    {
        if (!byOwner.TryGetValue(ownerId, out var tags)) return null;
        return [.. actors.Select(actor => tags.TryGetValue(actor, out var actorTags) && actorTags.Count > 0 ? $"{actor} ({string.Join(", ", actorTags)})" : actor)];
    }
}
