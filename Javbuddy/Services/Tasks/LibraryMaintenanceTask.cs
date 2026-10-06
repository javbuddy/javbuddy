using Javbuddy.Data;
using Javbuddy.Services.Images;
using Microsoft.EntityFrameworkCore;

namespace Javbuddy.Services.Tasks;

/// <summary>Deletes actor image originals in the durable object store that no ActorImage or
/// ActorPhoto row references: left behind by a save interrupted between writing the
/// file and its row, or by a database reset. An original is only deleted once older than a day,
/// since a save writes it before its row (or, for a photo upload, just after).</summary>
public sealed class LibraryMaintenanceTask(
    IDbContextFactory<AppDbContext> dbFactory,
    IActorImageDataStore dataStore) : IScheduledTask
{
    private static readonly TimeSpan UnreferencedSourceMaxAge = TimeSpan.FromDays(1);

    public string Name => "Library Maintenance";

    public string Description => "Deletes stored actor image originals that no actor portrait or photo uses any more.";

    public TimeSpan GetInterval() => TimeSpan.FromHours(24);

    public async Task<string?> RunAsync(CancellationToken ct, IProgress<TaskProgress> progress)
    {
        const string stage = "Deleting unreferenced actor image originals";
        progress.Report(new TaskProgress(0, null, stage));

        // Listed before the rows are read: an original saved in between then still counts as referenced.
        var stored = new List<StoredActorImageSource>();
        await foreach (var item in dataStore.ListAsync(ct))
        {
            stored.Add(item);
        }

        HashSet<Guid> referenced;
        await using (var db = await dbFactory.CreateDbContextAsync(ct))
        {
            referenced = (await db.ActorImages.Where(a => a.SourceStorageId != null).Select(a => a.SourceStorageId!.Value).ToListAsync(ct))
                .Concat(await db.ActorPhotos.Where(p => p.SourceStorageId != null).Select(p => p.SourceStorageId!.Value).ToListAsync(ct))
                .ToHashSet();
        }

        var cutoff = DateTimeOffset.UtcNow - UnreferencedSourceMaxAge;
        var deleted = 0;
        var checkedCount = 0;
        foreach (var item in stored)
        {
            ct.ThrowIfCancellationRequested();
            if (!referenced.Contains(item.StorageId) && item.LastModified < cutoff)
            {
                await dataStore.DeleteAsync(item.StorageId, item.Extension, ct);
                deleted++;
            }
            progress.Report(new TaskProgress(++checkedCount, stored.Count, stage));
        }

        return $"checked {stored.Count} actor image {(stored.Count == 1 ? "original" : "originals")}, deleted {deleted} unreferenced";
    }
}
