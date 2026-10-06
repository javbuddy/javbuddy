using Bunit;
using Javbuddy.Components.Shared;
using Javbuddy.Models;
using Javbuddy.Services.Ffmpeg;
using Javbuddy.Services.SceneMedia;
using Javbuddy.Services.Scenes;
using Javbuddy.Services.Trickplay;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Javbuddy.Tests.Components.Shared;

/// <summary>ClipEditor's scenes, ported from SceneEditorTests.</summary>
public class ClipEditorSceneTests : ClipEditorTestBase
{
    private IRenderedComponent<ClipEditor> RenderEditor(bool shortcuts = false, Action<IReadOnlyList<SceneItem>>? onChanged = null, TrickplayLayout? trickplay = null, bool hasLocalVideo = true) =>
        RenderClipEditor(shortcuts, onScenes: onChanged, trickplay: trickplay, hasLocalVideo: hasLocalVideo);

    private async Task<List<Scene>> StoredScenesAsync()
    {
        await using var db = await factory.CreateDbContextAsync();
        return await db.Scenes.OrderBy(s => s.StartSeconds).ToListAsync();
    }

    [Fact]
    public async Task ToolsRow_ComesLast_AfterTheHostsContent()
    {
        await sceneService.AddSceneAsync(movieId, 0, 750, "Interview");

        var cut = Render<ClipEditor>(p => p
            .Add(x => x.MovieId, movieId)
            .Add(x => x.VideoSelector, Selector)
            .Add(x => x.BeforeTools, "<p class=\"host-slot\">Highlights</p>"));

        // Scene list, then the host's content (the highlight editor), then Detect / Write side by side.
        var children = cut.Find(".scene-editor").Children.Select(c => c.ClassName).ToList();
        Assert.True(children.IndexOf("scene-editor-list") < children.IndexOf("host-slot"));
        Assert.Equal("scene-editor-tools", children.Last());
        var tools = cut.Find(".scene-editor-tools");
        Assert.Equal(["scene-detector", "scene-chapter-writer"], tools.Children.Select(c => c.ClassName!.Trim()));
    }

    [Fact]
    public void NoScenes_ShowsEmptyState()
    {
        var cut = RenderEditor();

        Assert.Equal("No scenes yet.", cut.Find(".scene-editor-empty").TextContent);
        Assert.Equal("Scenes", cut.Find(".scene-editor-heading").TextContent);
    }

    [Fact]
    public void Header_HasAnInfoIconBesideTheHeading()
    {
        var cut = RenderEditor();

        var button = cut.Find(".scene-editor-header .scene-editor-title .section-info-btn");
        Assert.Equal("What are scenes, highlights and apexes?", button.GetAttribute("aria-label"));
    }

    [Fact]
    public async Task ExistingScenes_ListWithResolvedRangesAndReportToHost()
    {
        await sceneService.AddSceneAsync(movieId, 0, 750, "Interview");
        await sceneService.AddSceneAsync(movieId, 750, null, null);
        IReadOnlyList<SceneItem>? reported = null;

        var cut = RenderEditor(onChanged: s => reported = s);

        var rows = cut.FindAll(".scene-editor-row");
        Assert.Equal(2, rows.Count);
        Assert.Contains("0:00–12:30", rows[0].TextContent);
        Assert.Contains("Interview", rows[0].TextContent);
        Assert.Contains("12:30–1:00:00", rows[1].TextContent);
        Assert.Equal("Scene 2", rows[1].QuerySelector(".scene-editor-name")!.TextContent);
        Assert.Equal("Scenes (2)", cut.Find(".scene-editor-heading").TextContent);
        Assert.Equal(2, reported!.Count);
    }

    [Fact]
    public async Task AddScene_PrefillsStartFromPlayer_AndSaves()
    {
        PlayerAt(125.5);
        IReadOnlyList<SceneItem>? reported = null;
        var cut = RenderEditor(onChanged: s => reported = s);

        cut.Find(".scene-editor-add-btn").Click();
        Assert.Equal("2:05.5", cut.Find(".scene-editor-start-input").GetAttribute("value"));
        cut.Find(".scene-editor-title-input").Input("Bath");
        cut.Find(".scene-editor-form").Submit();

        var stored = Assert.Single(await StoredScenesAsync());
        Assert.Equal(125.5, stored.StartSeconds);
        Assert.Null(stored.EndSeconds);
        Assert.Equal("Bath", stored.Title);
        Assert.Empty(cut.FindAll(".scene-editor-form"));
        Assert.Contains("Bath", cut.Find(".scene-editor-row").TextContent);
        Assert.Single(reported!);
    }

    [Fact]
    public async Task SetEndFromPlayer_ClampsToDuration()
    {
        PlayerAt(3600.4);
        var cut = RenderEditor();

        cut.Find(".scene-editor-add-btn").Click();
        cut.Find(".scene-editor-start-input").Input("59:00");
        cut.Find(".scene-editor-end-now-btn").Click();
        Assert.Equal("1:00:00", cut.Find(".scene-editor-end-input").GetAttribute("value"));
        cut.Find(".scene-editor-form").Submit();

        Assert.Equal(3600d, Assert.Single(await StoredScenesAsync()).EndSeconds);
    }

    [Fact]
    public void SetFromPlayer_WithoutPlayback_ShowsError()
    {
        PlayerAt(null);
        var cut = RenderEditor();

        cut.Find(".scene-editor-add-btn").Click();
        Assert.Equal("", cut.Find(".scene-editor-start-input").GetAttribute("value") ?? "");
        cut.Find(".scene-editor-start-now-btn").Click();

        Assert.Equal("Start playback to use the player's time.", cut.Find(".scene-editor-error").TextContent);
    }

    [Fact]
    public async Task InvalidInput_ShowsErrorAndKeepsFormOpen()
    {
        await sceneService.AddSceneAsync(movieId, 0, 600, null);
        PlayerAt(null);
        var cut = RenderEditor();

        cut.Find(".scene-editor-add-btn").Click();
        cut.Find(".scene-editor-start-input").Input("abc");
        cut.Find(".scene-editor-form").Submit();
        Assert.Equal("Enter the start as m:ss or h:mm:ss.", cut.Find(".scene-editor-error").TextContent);

        cut.Find(".scene-editor-start-input").Input("5:00");
        cut.Find(".scene-editor-form").Submit();
        Assert.Equal("Overlaps \"Scene 1\".", cut.Find(".scene-editor-error").TextContent);

        Assert.Single(cut.FindAll(".scene-editor-form"));
        Assert.Single(await StoredScenesAsync());
    }

    [Fact]
    public async Task EditScene_PrefillsAndUpdates_ClearingTheEnd()
    {
        await sceneService.AddSceneAsync(movieId, 60, 120.25, "Old");
        var cut = RenderEditor();

        cut.Find(".scene-editor-edit-btn").Click();
        Assert.Equal("1:00", cut.Find(".scene-editor-start-input").GetAttribute("value"));
        Assert.Equal("2:00.25", cut.Find(".scene-editor-end-input").GetAttribute("value"));
        Assert.Equal("Old", cut.Find(".scene-editor-title-input").GetAttribute("value"));

        cut.Find(".scene-editor-title-input").Input("New");
        cut.Find(".scene-editor-clear-end-btn").Click();
        cut.Find(".scene-editor-form").Submit();

        var stored = Assert.Single(await StoredScenesAsync());
        Assert.Equal("New", stored.Title);
        Assert.Null(stored.EndSeconds);
    }

    [Fact]
    public async Task DeleteScene_AsksForConfirmation_ThenRemovesIt()
    {
        await sceneService.AddSceneAsync(movieId, 60, null, "Interview");
        var cut = RenderEditor();

        cut.Find(".scene-editor-delete-btn").Click();

        Assert.Single(await StoredScenesAsync());
        Assert.Equal("Delete \"Interview\"?", cut.Find(".delete-confirm-dialog h2").TextContent);

        cut.Find(".delete-confirm-confirm-btn").Click();

        Assert.Empty(await StoredScenesAsync());
        Assert.Empty(cut.FindAll(".delete-confirm-dialog"));
        Assert.NotNull(cut.Find(".scene-editor-empty"));
    }

    [Fact]
    public async Task DeleteScene_CancelledByButtonEscapeOrBackdrop_KeepsIt()
    {
        await sceneService.AddSceneAsync(movieId, 60, null, null);
        var cut = RenderEditor();

        cut.Find(".scene-editor-delete-btn").Click();
        cut.Find(".delete-confirm-cancel-btn").Click();
        Assert.Empty(cut.FindAll(".delete-confirm-dialog"));

        cut.Find(".scene-editor-delete-btn").Click();
        cut.Find(".delete-confirm-backdrop").KeyDown(new KeyboardEventArgs { Key = "Escape" });
        Assert.Empty(cut.FindAll(".delete-confirm-dialog"));

        cut.Find(".scene-editor-delete-btn").Click();
        cut.Find(".delete-confirm-backdrop").Click();
        Assert.Empty(cut.FindAll(".delete-confirm-dialog"));

        Assert.Single(await StoredScenesAsync());
    }

    [Fact]
    public async Task ClickingAScene_SeeksThePlayerToItsStart()
    {
        await sceneService.AddSceneAsync(movieId, 750, null, null);
        var cut = RenderEditor();

        cut.Find(".scene-editor-row").Click();

        var seek = Assert.Single(module.Invocations, i => i.Identifier == "seek");
        Assert.Equal(Selector, seek.Arguments[0]);
        Assert.Equal(750d, seek.Arguments[1]);
    }

    [Fact]
    public void Shortcuts_AreOnlyRegisteredWhenEnabled()
    {
        RenderEditor(shortcuts: false);
        Assert.DoesNotContain(module.Invocations, i => i.Identifier == "initShortcuts");

        RenderEditor(shortcuts: true);
        var init = Assert.Single(module.Invocations, i => i.Identifier == "initShortcuts");
        Assert.Equal(Selector, init.Arguments[2]);
    }

    [Fact]
    public async Task ShortcutM_AddsAnUntitledSceneAtTheCurrentTime()
    {
        var cut = RenderEditor(shortcuts: true);

        await cut.InvokeAsync(() => cut.Instance.AddSceneAtAsync(300));

        var stored = Assert.Single(await StoredScenesAsync());
        Assert.Equal(300d, stored.StartSeconds);
        Assert.Null(stored.Title);
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll(".scene-editor-row")));
    }

    [Fact]
    public async Task ShortcutShiftM_EndsTheSceneThePlayheadIsIn()
    {
        await sceneService.AddSceneAsync(movieId, 0, null, "First");
        await sceneService.AddSceneAsync(movieId, 600, null, "Second");
        var cut = RenderEditor(shortcuts: true);

        await cut.InvokeAsync(() => cut.Instance.EndSceneAtAsync(900));

        var stored = await StoredScenesAsync();
        Assert.Null(stored[0].EndSeconds);
        Assert.Equal(900d, stored[1].EndSeconds);
        Assert.Equal("Second", stored[1].Title);
    }

    [Fact]
    public async Task ShortcutShiftM_BeforeAnyScene_ShowsError()
    {
        await sceneService.AddSceneAsync(movieId, 600, null, null);
        var cut = RenderEditor(shortcuts: true);

        await cut.InvokeAsync(() => cut.Instance.EndSceneAtAsync(30));

        cut.WaitForAssertion(() => Assert.Equal("No scene starts before 0:30.", cut.Find(".scene-editor-error").TextContent));
    }

    [Fact]
    public async Task NoScenes_FileWithChapters_OffersImport_AndImports()
    {
        chapterImport.GetFileChaptersAsync(movieId, Arg.Any<CancellationToken>())
            .Returns(new FileChapters("DEVR-041.mp4", [new(0, 1800, "DEVR-041-A"), new(1800, 3600, "DEVR-041-B")]));
        chapterImport.ImportFileChaptersAsync(movieId, Arg.Any<CancellationToken>()).Returns(async _ =>
        {
            await sceneService.AddSceneAsync(movieId, 0, null, "DEVR-041-A");
            await sceneService.AddSceneAsync(movieId, 1800, null, "DEVR-041-B");
            return SceneImportResult.Ok(2);
        });
        var cut = RenderEditor();

        var import = cut.WaitForElement(".scene-editor-import-btn");
        Assert.Equal("Import 2 chapters from file", import.TextContent.Trim());
        Assert.Equal("DEVR-041.mp4: DEVR-041-A, DEVR-041-B", import.GetAttribute("title"));

        import.Click();

        cut.WaitForAssertion(() => Assert.Equal(2, cut.FindAll(".scene-editor-row").Count));
        Assert.Empty(cut.FindAll(".scene-editor-import-btn"));
    }

    [Fact]
    public async Task ImportFailure_ShowsTheError()
    {
        chapterImport.GetFileChaptersAsync(movieId, Arg.Any<CancellationToken>())
            .Returns(new FileChapters("a.mp4", [new FfprobeChapterInfo(0, 10, null)]));
        chapterImport.ImportFileChaptersAsync(movieId, Arg.Any<CancellationToken>())
            .Returns(SceneImportResult.Fail("Chapter 1 can't be imported: Overlaps \"A\"."));
        var cut = RenderEditor();

        cut.WaitForElement(".scene-editor-import-btn").Click();

        Assert.Equal("Chapter 1 can't be imported: Overlaps \"A\".", cut.Find(".scene-editor-error").TextContent);
        await Task.CompletedTask;
    }

    [Fact]
    public async Task ExistingScenes_DoNotProbeTheFile()
    {
        await sceneService.AddSceneAsync(movieId, 0, null, null);

        RenderEditor();

        await chapterImport.DidNotReceive().GetFileChaptersAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void FileWithoutChapters_OffersNoImport()
    {
        chapterImport.GetFileChaptersAsync(movieId, Arg.Any<CancellationToken>()).Returns(new FileChapters("a.mp4", []));

        var cut = RenderEditor();

        Assert.NotNull(cut.Find(".scene-editor-empty"));
        Assert.Empty(cut.FindAll(".scene-editor-import-btn"));
    }

    private async Task<(int SceneId, int ActorId, int TagId)> SeedCastSceneAndTagAsync()
    {
        await using var db = await factory.CreateDbContextAsync();
        var actor = new Actor { FirstName = "Aika" };
        var tag = new Tag { Name = "Bath" };
        db.AddRange(actor, tag);
        await db.SaveChangesAsync();
        db.MovieActors.Add(new MovieActor { MovieId = movieId, ActorId = actor.Id });
        var scene = new Scene { MovieId = movieId, StartSeconds = 60, Title = "Tub" };
        db.Scenes.Add(scene);
        await db.SaveChangesAsync();
        return (scene.Id, actor.Id, tag.Id);
    }

    [Fact]
    public async Task EditingAScene_PicksCastActorsAndRemovesTags_Immediately()
    {
        var (sceneId, aikaId, tagId) = await SeedCastSceneAndTagAsync();
        int beaId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var bea = new Actor { FirstName = "Bea" };
            db.Actors.Add(bea);
            await db.SaveChangesAsync();
            db.MovieActors.Add(new MovieActor { MovieId = movieId, ActorId = bea.Id });
            await db.SaveChangesAsync();
            beaId = bea.Id;
        }
        await sceneService.AddSceneTagAsync(sceneId, tagId);
        var cut = RenderEditor();
        Assert.Contains("Bath", cut.Find(".scene-editor-meta").TextContent);

        // No actors of its own: it inherits the cast, shown dimmed.
        cut.Find(".scene-editor-edit-btn").Click();
        Assert.Equal(["Aika", "Bea"], cut.FindAll(".scene-editor-actor-chip.clip-actor-chip-inherited").Select(c => c.TextContent));
        Assert.Equal("Inherited from the cast", cut.Find(".clip-actor-chip-inherited").GetAttribute("title"));

        // Clicking picks Bea alone, applied at once.
        await cut.FindAll(".scene-editor-actor-chip")[1].ClickAsync();
        Assert.Equal(["Bea"], cut.FindAll(".scene-editor-actor-chip[aria-pressed=true]").Select(c => c.TextContent));
        Assert.Equal([beaId], Assert.Single(await sceneService.GetScenesAsync(movieId)).Actors.Select(a => a.ActorId));

        // Unpicking the last one goes back to inheriting.
        await cut.FindAll(".scene-editor-actor-chip")[1].ClickAsync();
        Assert.Empty(Assert.Single(await sceneService.GetScenesAsync(movieId)).Actors);
        Assert.Equal(2, cut.FindAll(".scene-editor-actor-chip.clip-actor-chip-inherited").Count);

        // Customize copies the whole cast to unpick from.
        await cut.Find(".clip-actor-customize").ClickAsync();
        await cut.FindAll(".scene-editor-actor-chip")[1].ClickAsync();
        Assert.Equal([aikaId], Assert.Single(await sceneService.GetScenesAsync(movieId)).Actors.Select(a => a.ActorId));

        cut.Find(".scene-editor-chip-remove").Click();
        Assert.Empty(Assert.Single(await sceneService.GetScenesAsync(movieId)).Tags);
        // Still editing: link changes don't close the form or touch the times/title.
        Assert.Single(cut.FindAll(".scene-editor-form"));
        Assert.Single(cut.FindAll(".scene-editor-links"));
    }

    [Fact]
    public async Task TagSearchAdd_AddsTheTagToTheEditedScene()
    {
        var (sceneId, _, tagId) = await SeedCastSceneAndTagAsync();
        var cut = RenderEditor();
        cut.Find(".scene-editor-edit-btn").Click();

        var search = cut.FindComponent<TagSearchAdd>();
        await cut.InvokeAsync(() => search.Instance.OnAdd.InvokeAsync(tagId));

        Assert.Equal("Bath", Assert.Single((await sceneService.GetScenesAsync(movieId))[0].Tags).Name);
        cut.WaitForAssertion(() => Assert.Contains("Bath", cut.Find(".scene-editor-tag-chip").TextContent));
        Assert.Equal(sceneId, (await sceneService.GetScenesAsync(movieId))[0].Id);
    }

    [Fact]
    public void NewSceneForm_ExplainsActorsAndTagsComeAfterSaving()
    {
        PlayerAt(null);
        var cut = RenderEditor();

        cut.Find(".scene-editor-add-btn").Click();

        Assert.Empty(cut.FindAll(".scene-editor-links"));
        Assert.Contains("then edit it to set actors and tags", cut.Find(".scene-editor-links-hint").TextContent);
    }

    private async Task<int> SeedSceneWithMediaAsync(double start = 600)
    {
        await sceneService.AddSceneAsync(movieId, start, null, null);
        await using var db = await factory.CreateDbContextAsync();
        var scene = await db.Scenes.SingleAsync();
        foreach (var variant in new[] { "thumb", "preview" })
        {
            db.CachedImages.Add(new CachedImage
            {
                Code = "ABC-123",
                Role = "scene",
                Index = scene.Id,
                Variant = variant,
                StorageId = Guid.NewGuid(),
                SceneStartMs = (long)(start * 1000),
                // The only scene, so it runs to the end of the 3600 s movie.
                SceneEndMs = 3_600_000,
                UpdatedAt = new DateTime(2026, 9, 25, 0, 0, 0, DateTimeKind.Utc)
            });
        }
        await db.SaveChangesAsync();
        return scene.Id;
    }

    [Fact]
    public async Task SceneWithMedia_ShowsScreenshot_AndThePreviewWhileHovered()
    {
        var sceneId = await SeedSceneWithMediaAsync();
        var version = new DateTime(2026, 9, 25, 0, 0, 0, DateTimeKind.Utc).Ticks;
        var cut = RenderEditor();

        Assert.Equal($"/scene-image/{sceneId}/thumb?v={version}", cut.Find(".scene-editor-thumb img").GetAttribute("src"));
        Assert.Empty(cut.FindAll(".scene-editor-thumb video"));

        // The preview is a WebM video, so it plays in a <video> over the screenshot
        // rather than as the <img>'s src.
        cut.Find(".scene-editor-item").MouseEnter();
        Assert.Equal($"/scene-image/{sceneId}/preview?v={version}", cut.Find(".scene-editor-thumb video").GetAttribute("src"));
        Assert.Equal($"/scene-image/{sceneId}/thumb?v={version}", cut.Find(".scene-editor-thumb img").GetAttribute("src"));

        cut.Find(".scene-editor-item").MouseLeave();
        Assert.Empty(cut.FindAll(".scene-editor-thumb video"));
        await sceneMedia.DidNotReceive().EnsureQueuedAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SceneWithoutMedia_QueuesGeneration_AndShowsTheTrickplayTile()
    {
        await sceneService.AddSceneAsync(movieId, 1234, null, null);
        var trickplay = new TrickplayLayout(320, 180, 10, 10, 250, 10000, 2500, "https://jf/{index}.jpg");

        var cut = RenderEditor(trickplay: trickplay);

        Assert.Empty(cut.FindAll(".scene-editor-thumb img"));
        Assert.Equal(TrickplayTiles.Style(trickplay, 1234, 64), cut.Find(".scene-editor-thumb-tile").GetAttribute("style"));
        Assert.Empty(cut.FindAll(".scene-editor-thumb .scene-preview-shimmer"));
        await sceneMedia.Received(1).EnsureQueuedAsync(movieId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SceneWithoutMediaOrTrickplay_ShimmersWhileItsScreenshotIsGenerated()
    {
        await sceneService.AddSceneAsync(movieId, 1234, null, null);
        sceneMedia.ServesVariant(SceneMediaService.VariantThumb).Returns(true);

        var cut = RenderEditor();

        Assert.Empty(cut.FindAll(".scene-editor-thumb img"));
        cut.Find(".scene-editor-thumb.scene-editor-thumb-generating .scene-preview-shimmer");
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task SceneWithoutMediaOrTrickplay_NoScreenshotComing_StaysAnEmptyFrame(bool servesThumb, bool hasLocalVideo)
    {
        await sceneService.AddSceneAsync(movieId, 1234, null, null);
        sceneMedia.ServesVariant(SceneMediaService.VariantThumb).Returns(servesThumb);

        var cut = RenderEditor(hasLocalVideo: hasLocalVideo);

        Assert.Empty(cut.Find(".scene-editor-thumb").Children);
        Assert.Empty(cut.FindAll(".scene-editor-thumb-generating"));
    }

    [Fact]
    public async Task GeneratedMediaForThisMovie_ReloadsTheList()
    {
        var sceneId = await SeedSceneWithMediaAsync();
        await using (var db = await factory.CreateDbContextAsync())
        {
            await db.CachedImages.ExecuteDeleteAsync();
        }
        var cut = RenderEditor();
        Assert.Empty(cut.FindAll(".scene-editor-thumb img"));

        // The media appears (as if the worker wrote it) and the queue reports it.
        await SeedMediaRowsAsync(sceneId);
        workerMedia.GenerateAsync(sceneId, Arg.Any<CancellationToken>()).Returns(movieId);
        await mediaQueue.StartAsync(CancellationToken.None);
        mediaQueue.Enqueue(sceneId, movieId, new SceneMediaWindow(600_000, 3_600_000));

        cut.WaitForAssertion(() => Assert.Single(cut.FindAll(".scene-editor-thumb img")), TimeSpan.FromSeconds(5));
        await mediaQueue.StopAsync(CancellationToken.None);
    }

    private async Task SeedMediaRowsAsync(int sceneId)
    {
        await using var db = await factory.CreateDbContextAsync();
        db.CachedImages.Add(new CachedImage { Code = "ABC-123", Role = "scene", Index = sceneId, Variant = "thumb", StorageId = Guid.NewGuid(), SceneStartMs = 600_000, SceneEndMs = 3_600_000 });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task FavoriteToggle_IsSavedFromTheList()
    {
        await sceneService.AddSceneAsync(movieId, 60, null, "Bath");
        var cut = RenderEditor();

        cut.Find(".scene-editor-fav-btn").Click();
        cut.WaitForAssertion(() => Assert.Equal("true", cut.Find(".scene-editor-fav-btn").GetAttribute("aria-pressed")));

        Assert.True(Assert.Single(await sceneService.GetScenesAsync(movieId)).IsFavorite);
    }

    [Fact]
    public async Task EditingAScene_HideFromOverview_SavesImmediately_AndMarksTheRow()
    {
        var sceneId = (await sceneService.AddSceneAsync(movieId, 0, 90, "Intro")).SceneId!.Value;
        var cut = RenderEditor();

        cut.Find(".scene-editor-edit-btn").Click();
        var checkbox = cut.Find(".scene-editor-hidden-input");
        Assert.False(checkbox.HasAttribute("checked"));
        checkbox.Change(true);

        Assert.True((await StoredScenesAsync()).Single().IsHiddenFromOverview);
        Assert.NotNull(cut.Find(".scene-editor-form"));

        cut.Find(".scene-editor-cancel-btn").Click();
        Assert.NotNull(cut.Find(".scene-editor-item.scene-editor-item-hidden"));
        Assert.Equal("Hidden from the Scenes overview", cut.Find(".scene-editor-hidden-badge").GetAttribute("title"));

        cut.Find(".scene-editor-edit-btn").Click();
        cut.Find(".scene-editor-hidden-input").Change(false);
        Assert.False((await StoredScenesAsync()).Single().IsHiddenFromOverview);
        Assert.Equal(sceneId, (await StoredScenesAsync()).Single().Id);
    }

    [Fact]
    public void NewSceneForm_OffersNoHideToggle()
    {
        var cut = RenderEditor();

        cut.Find(".scene-editor-add-btn").Click();

        Assert.Empty(cut.FindAll(".scene-editor-hidden-input"));
    }

    [Fact]
    public async Task Refresh_RereadsTheScenes_AndReportsThemToHost()
    {
        int sceneId;
        int tagId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var scene = new Scene { MovieId = movieId, StartSeconds = 0 };
            var tag = new Tag { Name = "Creampie" };
            db.AddRange(scene, tag);
            await db.SaveChangesAsync();
            (sceneId, tagId) = (scene.Id, tag.Id);
        }
        IReadOnlyList<SceneItem>? reported = null;
        var cut = RenderEditor(onChanged: s => reported = s);
        Assert.Empty(Assert.Single(reported!).Tags);

        await sceneService.AddSceneTagAsync(sceneId, tagId);
        await cut.InvokeAsync(() => cut.Instance.RefreshAsync());

        Assert.Equal("Creampie", Assert.Single(Assert.Single(reported!).Tags).Name);
    }
}
