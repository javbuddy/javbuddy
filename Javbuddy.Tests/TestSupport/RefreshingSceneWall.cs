using Javbuddy.Data;
using Javbuddy.Services.Scenes;
using Microsoft.EntityFrameworkCore;

namespace Javbuddy.Tests.TestSupport;

/// <summary>The real wall, refreshing stale stored effective actors before every call — what
/// ClipActorRefreshWorker does in the app, done in line so tests see what they seeded.</summary>
public sealed class RefreshingSceneWall(IDbContextFactory<AppDbContext> factory, ISceneWallQueryService inner) : ISceneWallQueryService
{
    private async Task RefreshAsync(CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        await ClipActorSync.RefreshStaleAsync(db, ct: ct);
    }

    public async Task<SceneWallPage> GetPageAsync(SceneWallFilter filter, SceneWallSort sort, int randomSeed, int skip, int take, CancellationToken ct = default)
    {
        await RefreshAsync(ct);
        return await inner.GetPageAsync(filter, sort, randomSeed, skip, take, ct);
    }

    public async Task<SceneWallOptions> GetOptionsAsync(bool includeHidden = false, CancellationToken ct = default)
    {
        await RefreshAsync(ct);
        return await inner.GetOptionsAsync(includeHidden, ct);
    }

    public async Task<HighlightWallPage> GetHighlightPageAsync(SceneWallFilter filter, SceneWallSort sort, int randomSeed, int skip, int take, CancellationToken ct = default)
    {
        await RefreshAsync(ct);
        return await inner.GetHighlightPageAsync(filter, sort, randomSeed, skip, take, ct);
    }

    public async Task<SceneWallOptions> GetHighlightOptionsAsync(CancellationToken ct = default)
    {
        await RefreshAsync(ct);
        return await inner.GetHighlightOptionsAsync(ct);
    }

    public async Task<ApexWallPage> GetApexPageAsync(SceneWallFilter filter, SceneWallSort sort, int randomSeed, int skip, int take, CancellationToken ct = default)
    {
        await RefreshAsync(ct);
        return await inner.GetApexPageAsync(filter, sort, randomSeed, skip, take, ct);
    }

    public async Task<SceneWallOptions> GetApexOptionsAsync(CancellationToken ct = default)
    {
        await RefreshAsync(ct);
        return await inner.GetApexOptionsAsync(ct);
    }
}
