using Microsoft.EntityFrameworkCore;

namespace Javbuddy.Data;

public static class SingleRowSettingsQueryExtensions
{
    /// <summary>The one settings row (lowest Id, so it's deterministic) read untracked, or null while the
    /// table is still empty. Use a tracked query instead when the caller writes the row back.</summary>
    public static Task<TSettings?> ReadSingleRowAsync<TSettings>(this DbSet<TSettings> set, CancellationToken ct = default)
        where TSettings : class =>
        set.AsNoTracking().OrderBy(s => EF.Property<int>(s, "Id")).FirstOrDefaultAsync(ct);
}
