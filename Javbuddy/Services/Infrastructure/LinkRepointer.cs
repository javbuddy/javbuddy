using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;

namespace Javbuddy.Services.Infrastructure;

/// <summary>Moves the join rows of a merged-away entity onto the surviving one. A row the target already has
/// (same <paramref name="key"/>) is just dropped, so the merge never creates a duplicate link. Changes are
/// staged on the context; the caller saves.</summary>
public static class LinkRepointer
{
    /// <param name="links">The join table.</param>
    /// <param name="source">Selects the rows of the entity being merged away.</param>
    /// <param name="target">Selects the rows the surviving entity already has.</param>
    /// <param name="key">What identifies a row once the owner is factored out (the movie, scene, …).</param>
    /// <param name="repointed">The source row's copy owned by the target.</param>
    public static async Task RepointAsync<TLink, TKey>(
        DbSet<TLink> links,
        Expression<Func<TLink, bool>> source,
        Expression<Func<TLink, bool>> target,
        Func<TLink, TKey> key,
        Func<TLink, TLink> repointed,
        CancellationToken ct)
        where TLink : class
        where TKey : notnull
    {
        var targetKeys = (await links.AsNoTracking().Where(target).ToListAsync(ct)).Select(key).ToHashSet();
        foreach (var link in await links.Where(source).ToListAsync(ct))
        {
            links.Remove(link);
            if (targetKeys.Add(key(link)))
            {
                links.Add(repointed(link));
            }
        }
    }
}
