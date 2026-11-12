using Bunit;
using Javbuddy.Services.SceneMedia;
using Javbuddy.Services.Scenes;
using Javbuddy.Services.Tags;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Javbuddy.Tests.Components.Shared;

/// <summary>What ClipEditor re-reads when a media job finishes: the scene, highlight and apex services
/// are spies forwarding to the real ones, so the calls can be counted.</summary>
public class ClipEditorMediaReloadTests : ClipEditorTestBase
{
    private readonly IMovieSceneService scenesSpy = Substitute.For<IMovieSceneService>();
    private readonly IMovieHighlightService highlightsSpy = Substitute.For<IMovieHighlightService>();
    private readonly IMovieApexService apexesSpy = Substitute.For<IMovieApexService>();
    private readonly IActorTagService actorTagsSpy = Substitute.For<IActorTagService>();

    public ClipEditorMediaReloadTests()
    {
        // Forward to the real services, but count the calls.
        scenesSpy.GetScenesAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(ci => sceneService.GetScenesAsync(ci.ArgAt<int>(0)));
        scenesSpy.GetCastOptionsAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(ci => sceneService.GetCastOptionsAsync(ci.ArgAt<int>(0)));
        highlightsSpy.GetHighlightsAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(ci => highlightService.GetHighlightsAsync(ci.ArgAt<int>(0)));
        apexesSpy.GetApexesAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(ci => apexService.GetApexesAsync(ci.ArgAt<int>(0)));
        apexesSpy.GetDefaultWindowAsync(Arg.Any<CancellationToken>()).Returns(ci => apexService.GetDefaultWindowAsync());
        var realActorTags = new ActorTagService(factory);
        actorTagsSpy.GetEffectiveAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(ci => realActorTags.GetEffectiveAsync(ci.ArgAt<int>(0)));
        actorTagsSpy.GetActorTagsAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(ci => realActorTags.GetActorTagsAsync());
        Services.AddSingleton(scenesSpy);
        Services.AddSingleton(highlightsSpy);
        Services.AddSingleton(apexesSpy);
        Services.AddSingleton(actorTagsSpy);
    }

    [Fact]
    public async Task GeneratedMedia_RereadsOnlyTheThreeLists_AndShowsTheNewThumbnails()
    {
        var sceneId = (await sceneService.AddSceneAsync(movieId, 600, null, "Bath")).SceneId!.Value;
        var cut = RenderClipEditor();
        Assert.Empty(cut.FindAll(".scene-editor-thumb img"));
        scenesSpy.ClearReceivedCalls();
        highlightsSpy.ClearReceivedCalls();
        apexesSpy.ClearReceivedCalls();
        actorTagsSpy.ClearReceivedCalls();
        sceneMedia.ClearReceivedCalls();

        await using (var db = await factory.CreateDbContextAsync())
        {
            db.CachedImages.Add(new Javbuddy.Models.CachedImage { Code = "ABC-123", Role = "scene", Index = sceneId, Variant = "thumb", StorageId = Guid.NewGuid(), SceneStartMs = 600_000, SceneEndMs = 3_600_000 });
            await db.SaveChangesAsync();
        }
        workerMedia.GenerateAsync(sceneId, Arg.Any<CancellationToken>()).Returns(movieId);
        await mediaQueue.StartAsync(CancellationToken.None);
        mediaQueue.Enqueue(sceneId, movieId, new SceneMediaWindow(600_000, 3_600_000));

        cut.WaitForAssertion(() => Assert.Single(cut.FindAll(".scene-editor-thumb img")), TimeSpan.FromSeconds(5));
        await mediaQueue.StopAsync(CancellationToken.None);

        // The lists carry the media versions; the cast, default window, actor tags and library don't change,
        // and the queue already holds the missing media.
        await scenesSpy.Received(1).GetScenesAsync(movieId, Arg.Any<CancellationToken>());
        await highlightsSpy.Received(1).GetHighlightsAsync(movieId, Arg.Any<CancellationToken>());
        await apexesSpy.Received(1).GetApexesAsync(movieId, Arg.Any<CancellationToken>());
        await scenesSpy.DidNotReceive().GetCastOptionsAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
        await apexesSpy.DidNotReceive().GetDefaultWindowAsync(Arg.Any<CancellationToken>());
        await actorTagsSpy.DidNotReceive().GetEffectiveAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
        await actorTagsSpy.DidNotReceive().GetActorTagsAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>());
        await sceneMedia.DidNotReceive().EnsureQueuedAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ABurstOfGeneratedMedia_IsCoalescedIntoFewerReloads()
    {
        await sceneService.AddSceneAsync(movieId, 600, null, "Bath");
        var gate = new TaskCompletionSource();
        var cut = RenderClipEditor();
        scenesSpy.ClearReceivedCalls();
        // The first reload is held open while the burst arrives.
        scenesSpy.GetScenesAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(async ci =>
        {
            await gate.Task;
            return await sceneService.GetScenesAsync(ci.ArgAt<int>(0));
        });

        const int burst = 10;
        for (var i = 0; i < burst; i++) RaiseGenerated(movieId);
        gate.SetResult();

        cut.WaitForAssertion(() => Assert.True(scenesSpy.ReceivedCalls().Count(c => c.GetMethodInfo().Name == nameof(IMovieSceneService.GetScenesAsync)) >= 1));
        await Task.Delay(300);
        var calls = scenesSpy.ReceivedCalls().Count(c => c.GetMethodInfo().Name == nameof(IMovieSceneService.GetScenesAsync));
        Assert.InRange(calls, 1, 2);
    }

    private void RaiseGenerated(int id)
    {
        var field = typeof(SceneMediaQueue).GetField(nameof(SceneMediaQueue.Generated), System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        ((Action<int>?)field.GetValue(mediaQueue))?.Invoke(id);
    }
}
