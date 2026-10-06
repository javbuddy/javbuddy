using System.Linq.Expressions;
using Javbuddy.Data;
using Microsoft.EntityFrameworkCore;

namespace Javbuddy.Services.Infrastructure;

/// <summary>Collapses the "single-row settings table" upsert used by every Settings page: load
/// the one existing row (ordered by Id for determinism, matching every hand-written version of
/// this pattern), copy the form model's fields onto it, or insert the form model as the first
/// row if the table is still empty.</summary>
public sealed class SingleRowSettingsRepository<TSettings>(
    IDbContextFactory<AppDbContext> dbFactory,
    Func<AppDbContext, DbSet<TSettings>> selectDbSet,
    Expression<Func<TSettings, int>> idSelector,
    Action<TSettings, TSettings> copyFields) where TSettings : class
{
    private readonly IDbContextFactory<AppDbContext> dbFactory = dbFactory;
    private readonly Func<AppDbContext, DbSet<TSettings>> selectDbSet = selectDbSet;
    private readonly Expression<Func<TSettings, int>> idSelector = idSelector;
    private readonly Action<TSettings, TSettings> copyFields = copyFields;

    /// <summary>The single settings row (untracked), or <paramref name="createDefault"/>'s unsaved
    /// defaults if the table is still empty.</summary>
    public async Task<TSettings> GetAsync(Func<TSettings> createDefault, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await selectDbSet(db).AsNoTracking().OrderBy(idSelector).FirstOrDefaultAsync(ct) ?? createDefault();
    }

    /// <summary>Upserts <paramref name="formModel"/> as the single settings row: copies its
    /// fields (via the constructor's copyFields) onto whatever row already exists, or inserts
    /// formModel itself as the first row if the table is empty.</summary>
    public async Task SaveAsync(TSettings formModel, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var dbSet = selectDbSet(db);
        var existing = await dbSet.OrderBy(idSelector).FirstOrDefaultAsync(ct);
        if (existing is null)
        {
            dbSet.Add(formModel);
        }
        else
        {
            copyFields(existing, formModel);
        }
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Applies <paramref name="apply"/> to the single settings row (inserting
    /// <paramref name="createIfMissing"/>'s row first if the table is empty) and saves only the
    /// columns it changed — for background work recording its own outcome without overwriting
    /// settings a user edited while it ran.</summary>
    public async Task UpdateAsync(Func<TSettings> createIfMissing, Action<TSettings> apply, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var dbSet = selectDbSet(db);
        var row = await dbSet.OrderBy(idSelector).FirstOrDefaultAsync(ct);
        if (row is null)
        {
            row = createIfMissing();
            dbSet.Add(row);
        }
        apply(row);
        await db.SaveChangesAsync(ct);
    }
}
