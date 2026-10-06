using Javbuddy.Data;
using Microsoft.EntityFrameworkCore;

namespace Javbuddy.Services.Trickplay;

/// <summary>Queues trickplay generation when a library refresh records a new or changed main video
/// file, when the "Generate trickplay for new movie files" setting is on. A remux
/// that keeps the file's identity (a chapter write, a repair) already has its set, so nothing is
/// queued for it. A manual rescan on Movie Detail queues a missing set whatever the
/// setting, and retries one whose generation failed earlier.</summary>
public interface ITrickplayTrigger
{
    Task OnVideoFilesChangedAsync(int movieId, CancellationToken ct = default);

    /// <summary>Queues generation when the movie's main file has no set; true when it was queued.</summary>
    Task<bool> OnRescanAsync(int movieId, CancellationToken ct = default);
}

public sealed class TrickplayTrigger(
    IDbContextFactory<AppDbContext> dbFactory,
    ITrickplaySettingsService settingsService,
    ITrickplayStore store,
    TrickplayQueue queue) : ITrickplayTrigger
{
    public async Task OnVideoFilesChangedAsync(int movieId, CancellationToken ct = default)
    {
        if (!(await settingsService.GetEffectiveAsync(ct)).GenerateForNewFiles) return;
        await QueueIfMissingAsync(movieId, retryFailed: false, ct);
    }

    public Task<bool> OnRescanAsync(int movieId, CancellationToken ct = default) =>
        QueueIfMissingAsync(movieId, retryFailed: true, ct);

    private async Task<bool> QueueIfMissingAsync(int movieId, bool retryFailed, CancellationToken ct)
    {
        TrickplayMainFile? main;
        await using (var db = await dbFactory.CreateDbContextAsync(ct))
        {
            main = await TrickplayMainFiles.LoadAsync(db, movieId, ct);
        }
        if (main?.Identity is not { } identity) return false;
        if (await store.GetSetAsync(main.Code, identity, ct) is not null) return false;

        if (retryFailed) queue.ForgetFailure(movieId, identity);
        return queue.Enqueue(movieId, main.Code, identity, main.DurationSeconds);
    }
}
