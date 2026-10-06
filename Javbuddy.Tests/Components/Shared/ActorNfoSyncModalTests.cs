using Bunit;
using Javbuddy.Components.Shared;
using Javbuddy.Models;
using Javbuddy.Services.Nfo;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Javbuddy.Tests.Components.Shared;

public class ActorNfoSyncModalTests : BunitContext
{
    [Fact]
    public void Modal_WhenShowFalse_RendersNothing()
    {
        var nfoSyncService = Substitute.For<INfoSyncService>();
        Services.AddSingleton(nfoSyncService);

        var actor = new Actor { Id = 1, FirstName = "Yua", LastName = "Mikami" };
        var cut = Render<ActorNfoSyncModal>(p => p
            .Add(x => x.Show, false)
            .Add(x => x.Actor, actor));

        Assert.Empty(cut.FindAll(".sync-modal-dialog"));
    }

    [Fact]
    public void Modal_WhenShowTrue_LoadsAndDisplaysPreview()
    {
        var nfoSyncService = Substitute.For<INfoSyncService>();
        var actor = new Actor { Id = 1, FirstName = "Yua", LastName = "Mikami" };
        var preview = new ActorNfoSyncPreviewResult(
            1,
            "Mikami Yua",
            [
                new ActorNfoMoviePreview(10, "IPX-001", "Movie 1", "/path/1.nfo", ActorNfoMovieStatus.WillUpdate, ["Yua Mikami"], "Mikami Yua"),
                new ActorNfoMoviePreview(11, "IPX-002", "Movie 2", "/path/2.nfo", ActorNfoMovieStatus.UpToDate, ["Mikami Yua"], "Mikami Yua")
            ]);
        nfoSyncService.PreviewSyncActorAsync(1, Arg.Any<IReadOnlyList<string>?>(), Arg.Any<CancellationToken>())
            .Returns(preview);
        Services.AddSingleton(nfoSyncService);

        var cut = Render<ActorNfoSyncModal>(p => p
            .Add(x => x.Show, true)
            .Add(x => x.Actor, actor));

        Assert.Contains("Sync .nfo Files — Mikami Yua", cut.Find(".sync-modal-title").TextContent);
        Assert.Contains("1 need update", cut.Find(".sync-stats-bar").TextContent);
        Assert.Contains("1 up to date", cut.Find(".sync-stats-bar").TextContent);

        var rows = cut.FindAll("tbody tr");
        Assert.Equal(2, rows.Count);
        Assert.Contains("IPX-001", rows[0].TextContent);
        Assert.Contains("Will update", rows[0].TextContent);
        Assert.Contains("IPX-002", rows[1].TextContent);
        Assert.Contains("Up to date", rows[1].TextContent);

        var syncBtn = cut.Find(".btn-primary");
        Assert.Contains("Sync 1 .nfo File", syncBtn.TextContent);
        Assert.False(syncBtn.HasAttribute("disabled"));
    }

    [Fact]
    public async Task Modal_WhenSyncClicked_CallsSyncActorAsync()
    {
        var nfoSyncService = Substitute.For<INfoSyncService>();
        var actor = new Actor { Id = 1, FirstName = "Yua", LastName = "Mikami" };
        var preview = new ActorNfoSyncPreviewResult(
            1,
            "Mikami Yua",
            [
                new ActorNfoMoviePreview(10, "IPX-001", "Movie 1", "/path/1.nfo", ActorNfoMovieStatus.WillUpdate, ["Yua Mikami"], "Mikami Yua")
            ]);
        var syncResult = new ActorNfoSyncResult(
            1,
            "Mikami Yua",
            1,
            1,
            0,
            0,
            0,
            [new ActorNfoMovieResult(10, "IPX-001", "/path/1.nfo", ActorNfoMovieStatus.WillUpdate, "Updated")]);

        nfoSyncService.PreviewSyncActorAsync(1, Arg.Any<IReadOnlyList<string>?>(), Arg.Any<CancellationToken>())
            .Returns(preview);
        nfoSyncService.SyncActorAsync(1, Arg.Any<IReadOnlyList<string>?>(), Arg.Any<CancellationToken>())
            .Returns(syncResult);
        Services.AddSingleton(nfoSyncService);

        var cut = Render<ActorNfoSyncModal>(p => p
            .Add(x => x.Show, true)
            .Add(x => x.Actor, actor));

        var syncBtn = cut.Find(".btn-primary");
        await cut.InvokeAsync(() => syncBtn.Click());

        await nfoSyncService.Received(1).SyncActorAsync(1, Arg.Any<IReadOnlyList<string>?>(), Arg.Any<CancellationToken>());
        Assert.Contains("Sync complete! Updated 1 .nfo file", cut.Find(".alert-success").TextContent);
    }

    [Fact]
    public async Task Modal_WhenCloseClicked_InvokesOnClose()
    {
        var nfoSyncService = Substitute.For<INfoSyncService>();
        var actor = new Actor { Id = 1, FirstName = "Yua", LastName = "Mikami" };
        var preview = new ActorNfoSyncPreviewResult(1, "Yua Mikami", []);
        nfoSyncService.PreviewSyncActorAsync(1, Arg.Any<IReadOnlyList<string>?>(), Arg.Any<CancellationToken>())
            .Returns(preview);
        Services.AddSingleton(nfoSyncService);

        bool closed = false;
        var cut = Render<ActorNfoSyncModal>(p => p
            .Add(x => x.Show, true)
            .Add(x => x.Actor, actor)
            .Add(x => x.OnClose, () => closed = true));

        var closeBtn = cut.Find(".btn-close");
        await cut.InvokeAsync(() => closeBtn.Click());

        Assert.True(closed);
    }
}
