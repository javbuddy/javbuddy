using Bunit;
using Javbuddy.Components.Shared;
using Javbuddy.Models;
using Javbuddy.Services.Scenes;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Javbuddy.Tests.Components.Shared;

/// <summary>ClipEditor's highlights, ported from HighlightEditorTests.</summary>
public class ClipEditorHighlightTests : ClipEditorTestBase
{
    private readonly int tagId;

    public ClipEditorHighlightTests() => tagId = SeedTag("Squirt");

    private IRenderedComponent<ClipEditor> RenderEditor(Action<IReadOnlyList<HighlightItem>>? onChanged = null, bool shortcuts = false) =>
        RenderClipEditor(shortcuts, onHighlights: onChanged);

    private async Task<List<MovieHighlight>> StoredAsync()
    {
        await using var db = await factory.CreateDbContextAsync();
        return await db.MovieHighlights.OrderBy(h => h.StartSeconds).ToListAsync();
    }

    [Fact]
    public void NoHighlights_ShowsNoRowsAndNoCount()
    {
        var cut = RenderEditor();

        Assert.Empty(cut.FindAll(".highlight-editor-item"));
        Assert.Empty(cut.FindAll(".clip-editor-counts"));
    }

    [Fact]
    public async Task ExistingHighlights_ListRangeAndDuration_AndReportToHost()
    {
        await highlightService.AddHighlightAsync(movieId, 600, 640, "Climax");
        await highlightService.AddHighlightAsync(movieId, 610, 620, null);
        IReadOnlyList<HighlightItem>? reported = null;

        var cut = RenderEditor(h => reported = h);

        var rows = cut.FindAll(".highlight-editor-row");
        Assert.Equal(2, rows.Count);
        Assert.Contains("Climax", rows[0].TextContent);
        Assert.Equal("10:00–10:40", rows[0].QuerySelector(".highlight-editor-range")!.TextContent);
        Assert.Equal("(0:40)", rows[0].QuerySelector(".highlight-editor-duration")!.TextContent);
        Assert.Equal("Highlight 2", rows[1].QuerySelector(".highlight-editor-name")!.TextContent);
        Assert.Equal("2 highlights · 0 apexes", cut.Find(".clip-editor-counts").GetAttribute("title"));
        Assert.Equal("2", cut.Find(".clip-editor-counts").TextContent.Trim());
        Assert.Empty(cut.FindAll(".clip-editor-apex-glyph"));
        Assert.Equal([0, 1], reported!.Select(h => h.Lane));
    }

    [Fact]
    public async Task AddHighlight_MarksInAndOutFromPlayer_AndSaves()
    {
        PlayerAt(125.5);
        IReadOnlyList<HighlightItem>? reported = null;
        var cut = RenderEditor(h => reported = h);

        cut.Find(".highlight-editor-add-btn").Click();
        Assert.Equal("2:05.5", cut.Find(".highlight-editor-start-input").GetAttribute("value"));
        PlayerAt(150);
        cut.Find(".highlight-editor-end-now-btn").Click();
        Assert.Equal("2:30", cut.Find(".highlight-editor-end-input").GetAttribute("value"));
        cut.Find(".highlight-editor-title-input").Input("Tease");
        cut.Find(".highlight-editor-form").Submit();

        var stored = Assert.Single(await StoredAsync());
        Assert.Equal((125.5, 150d, "Tease"), (stored.StartSeconds, stored.EndSeconds, stored.Title));
        Assert.Empty(cut.FindAll(".highlight-editor-form"));
        Assert.Single(reported!);
    }

    [Fact]
    public async Task AddedHighlight_QueuesItsMedia_SoTheHostsCardsGetItWithoutARefresh()
    {
        PlayerAt(100);
        var cut = RenderEditor();
        await highlightMedia.DidNotReceiveWithAnyArgs().EnsureQueuedAsync(default, default);

        cut.Find(".highlight-editor-add-btn").Click();
        cut.Find(".highlight-editor-end-input").Input("2:00");
        cut.Find(".highlight-editor-form").Submit();

        Assert.Single(await StoredAsync());
        await highlightMedia.Received(1).EnsureQueuedAsync(movieId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AddHighlight_RequiresEnd_AndShowsServiceErrors()
    {
        PlayerAt(100);
        var cut = RenderEditor();

        cut.Find(".highlight-editor-add-btn").Click();
        cut.Find(".highlight-editor-form").Submit();
        Assert.Equal("Enter the end as m:ss or h:mm:ss.", cut.Find(".scene-editor-error").TextContent);

        cut.Find(".highlight-editor-end-input").Input("1:00");
        cut.Find(".highlight-editor-form").Submit();
        Assert.Equal("End must be after start.", cut.Find(".scene-editor-error").TextContent);
        Assert.Empty(await StoredAsync());
    }

    [Fact]
    public async Task EditHighlight_SavesTimesAndTitle()
    {
        var id = (await highlightService.AddHighlightAsync(movieId, 10, 20, "Old")).HighlightId!.Value;
        var cut = RenderEditor();

        cut.Find(".highlight-editor-edit-btn").Click();
        Assert.Equal("Old", cut.Find(".highlight-editor-title-input").GetAttribute("value"));
        Assert.Equal("0:20", cut.Find(".highlight-editor-end-input").GetAttribute("value"));

        cut.Find(".highlight-editor-end-input").Input("0:45");
        cut.Find(".highlight-editor-title-input").Input("New");
        cut.Find(".highlight-editor-form").Submit();

        var stored = Assert.Single(await StoredAsync());
        Assert.Equal((id, 45d, "New"), (stored.Id, stored.EndSeconds, stored.Title));
    }

    [Fact]
    public async Task FavoriteAndDelete_Apply()
    {
        await highlightService.AddHighlightAsync(movieId, 10, 20, null);
        var cut = RenderEditor();

        cut.Find(".highlight-editor-fav-btn").Click();
        Assert.True((await StoredAsync()).Single().IsFavorite);
        Assert.Equal("true", cut.Find(".highlight-editor-fav-btn").GetAttribute("aria-pressed"));

        cut.Find(".highlight-editor-delete-btn").Click();
        Assert.Single(await StoredAsync());
        Assert.Equal("Delete \"Highlight 1\"?", cut.Find(".delete-confirm-dialog h2").TextContent);

        cut.Find(".delete-confirm-confirm-btn").Click();
        Assert.Empty(await StoredAsync());
        Assert.Empty(cut.FindAll(".delete-confirm-dialog"));
        Assert.Empty(cut.FindAll(".highlight-editor-item"));
    }

    [Fact]
    public async Task Delete_CancelledByButtonEscapeOrBackdrop_KeepsIt()
    {
        await highlightService.AddHighlightAsync(movieId, 10, 20, "Climax");
        var cut = RenderEditor();

        cut.Find(".highlight-editor-delete-btn").Click();
        cut.Find(".delete-confirm-cancel-btn").Click();
        Assert.Empty(cut.FindAll(".delete-confirm-dialog"));

        cut.Find(".highlight-editor-delete-btn").Click();
        cut.Find(".delete-confirm-backdrop").KeyDown(new KeyboardEventArgs { Key = "Escape" });
        Assert.Empty(cut.FindAll(".delete-confirm-dialog"));

        cut.Find(".highlight-editor-delete-btn").Click();
        cut.Find(".delete-confirm-backdrop").Click();
        Assert.Empty(cut.FindAll(".delete-confirm-dialog"));

        Assert.Single(await StoredAsync());
    }

    [Fact]
    public async Task RowSeeksToStart_AndPlayButtonPlaysJustTheClip()
    {
        await highlightService.AddHighlightAsync(movieId, 30, 42.5, null);
        var cut = RenderEditor();

        cut.Find(".highlight-editor-row").Click();
        cut.Find(".highlight-editor-play-btn").Click();

        Assert.Single(module.Invocations["seek"], i => i.Arguments.SequenceEqual([Selector, 30d]));
        Assert.Single(module.Invocations["playClip"], i => i.Arguments.SequenceEqual([Selector, 30d, 42.5]));
    }

    [Fact]
    public void Shortcuts_AreOnlyRegisteredWhenEnabled_BindingHAndShiftH()
    {
        RenderEditor(shortcuts: false);
        Assert.DoesNotContain(module.Invocations, i => i.Identifier == "initShortcuts");

        RenderEditor(shortcuts: true);
        var init = Assert.Single(module.Invocations, i => i.Identifier == "initShortcuts");
        Assert.Equal(Selector, init.Arguments[2]);
        var bindings = Assert.IsAssignableFrom<IReadOnlyDictionary<string, string>>(init.Arguments[3]);
        Assert.Equal(nameof(ClipEditor.StartHighlightAtAsync), bindings["h"]);
        Assert.Equal(nameof(ClipEditor.EndHighlightAtAsync), bindings["shift+h"]);
    }

    [Fact]
    public async Task ShortcutH_StartsAHighlight_AndShiftH_EndsAndSavesIt()
    {
        IReadOnlyList<HighlightItem>? reported = null;
        var cut = RenderEditor(h => reported = h, shortcuts: true);

        await cut.InvokeAsync(() => cut.Instance.StartHighlightAtAsync(125.5));
        cut.WaitForAssertion(() => Assert.Equal("2:05.5", cut.Find(".highlight-editor-start-input").GetAttribute("value")));
        Assert.Empty(await StoredAsync());

        await cut.InvokeAsync(() => cut.Instance.EndHighlightAtAsync(150));

        var stored = Assert.Single(await StoredAsync());
        Assert.Equal((125.5, 150d, (string?)null), (stored.StartSeconds, stored.EndSeconds, stored.Title));
        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll(".highlight-editor-form")));
        Assert.Single(cut.FindAll(".highlight-editor-row"));
        Assert.Single(reported!);
    }

    [Fact]
    public async Task ShortcutH_Again_MovesTheStart_KeepingTheTitle()
    {
        var cut = RenderEditor(shortcuts: true);

        await cut.InvokeAsync(() => cut.Instance.StartHighlightAtAsync(100));
        cut.Find(".highlight-editor-title-input").Input("Tease");
        await cut.InvokeAsync(() => cut.Instance.StartHighlightAtAsync(110));
        await cut.InvokeAsync(() => cut.Instance.EndHighlightAtAsync(130));

        var stored = Assert.Single(await StoredAsync());
        Assert.Equal((110d, 130d, "Tease"), (stored.StartSeconds, stored.EndSeconds, stored.Title));
    }

    [Fact]
    public async Task ShortcutShiftH_ClampsToTheDuration()
    {
        var cut = RenderEditor(shortcuts: true);

        await cut.InvokeAsync(() => cut.Instance.StartHighlightAtAsync(3590));
        await cut.InvokeAsync(() => cut.Instance.EndHighlightAtAsync(3600.4));

        var stored = Assert.Single(await StoredAsync());
        Assert.Equal(3600d, stored.EndSeconds);
    }

    [Fact]
    public async Task ShortcutShiftH_WithoutAStartedHighlight_ShowsError()
    {
        var cut = RenderEditor(shortcuts: true);

        await cut.InvokeAsync(() => cut.Instance.EndHighlightAtAsync(150));

        cut.WaitForAssertion(() => Assert.Equal("Press H to start a highlight first.", cut.Find(".scene-editor-error").TextContent));
        Assert.Empty(await StoredAsync());
    }

    [Fact]
    public async Task ShortcutShiftH_BeforeTheStart_ShowsError_AndKeepsTheHighlightInProgress()
    {
        var cut = RenderEditor(shortcuts: true);

        await cut.InvokeAsync(() => cut.Instance.StartHighlightAtAsync(100));
        await cut.InvokeAsync(() => cut.Instance.EndHighlightAtAsync(60));

        cut.WaitForAssertion(() => Assert.Equal("End must be after start.", cut.Find(".scene-editor-error").TextContent));
        Assert.Equal("1:40", cut.Find(".highlight-editor-start-input").GetAttribute("value"));
        Assert.Empty(await StoredAsync());
    }

    [Fact]
    public async Task ShortcutH_WhileEditingAHighlight_ShowsError_AndKeepsTheEdit()
    {
        await highlightService.AddHighlightAsync(movieId, 10, 20, "Existing");
        var cut = RenderEditor(shortcuts: true);
        cut.Find(".highlight-editor-edit-btn").Click();
        cut.Find(".highlight-editor-title-input").Input("Renamed");

        await cut.InvokeAsync(() => cut.Instance.StartHighlightAtAsync(100));
        cut.WaitForAssertion(() => Assert.Equal("Save or cancel the highlight being edited first.", cut.Find(".scene-editor-error").TextContent));
        await cut.InvokeAsync(() => cut.Instance.EndHighlightAtAsync(130));

        cut.WaitForAssertion(() => Assert.Equal("Press H to start a highlight first.", cut.Find(".scene-editor-error").TextContent));
        Assert.Equal("Renamed", cut.Find(".highlight-editor-title-input").GetAttribute("value"));
        var stored = Assert.Single(await StoredAsync());
        Assert.Equal((10d, 20d, "Existing"), (stored.StartSeconds, stored.EndSeconds, stored.Title));
    }

    /// <summary>Cast Aika and Bea, with a scene [0, 100) of Aika only; later times are outside every scene.</summary>
    private (int Aika, int Bea) SeedCastAndScene()
    {
        using var db = factory.CreateDbContext();
        var aika = new Actor { FirstName = "Aika" };
        var bea = new Actor { FirstName = "Bea" };
        db.Actors.AddRange(aika, bea);
        db.SaveChanges();
        db.MovieActors.AddRange(new MovieActor { MovieId = movieId, ActorId = aika.Id }, new MovieActor { MovieId = movieId, ActorId = bea.Id });
        var scene = new Scene { MovieId = movieId, StartSeconds = 0, EndSeconds = 100 };
        scene.SceneActors.Add(new SceneActor { MovieId = movieId, ActorId = aika.Id });
        db.Scenes.Add(scene);
        db.SaveChanges();
        return (aika.Id, bea.Id);
    }

    private static IEnumerable<string> SelectedActorChips(IRenderedComponent<ClipEditor> cut) =>
        cut.FindAll(".highlight-editor-actor-chip[aria-pressed=true]").Select(c => c.TextContent);

    private static IEnumerable<string> InheritedActorChips(IRenderedComponent<ClipEditor> cut) =>
        cut.FindAll(".highlight-editor-actor-chip.clip-actor-chip-inherited").Select(c => c.TextContent);

    private static IEnumerable<string> OwnActorChips(IRenderedComponent<ClipEditor> cut) =>
        cut.FindAll(".highlight-editor-actor-chip-on").Select(c => c.TextContent);

    [Fact]
    public async Task AddHighlight_ShowsTheActorsItInherits_FollowingTheStart_AndSavesNoneOfItsOwn()
    {
        SeedCastAndScene();
        PlayerAt(50);
        var cut = RenderEditor();

        await cut.Find(".highlight-editor-add-btn").ClickAsync();
        Assert.Equal(["Aika", "Bea"], cut.FindAll(".highlight-editor-actor-chip").Select(c => c.TextContent));
        Assert.Equal(["Aika"], InheritedActorChips(cut));
        Assert.Equal(["Aika"], SelectedActorChips(cut));
        Assert.Equal("Inherited from scene 1", cut.Find(".clip-actor-chip-inherited").GetAttribute("title"));
        Assert.Equal("Aika", cut.Find(".highlight-editor-title-input").GetAttribute("placeholder"));
        Assert.Empty(cut.FindAll(".clip-actor-use-inherited"));

        // Past the scene's end it inherits the whole cast.
        await cut.Find(".highlight-editor-start-input").InputAsync("2:00");
        Assert.Equal(["Aika", "Bea"], InheritedActorChips(cut));
        Assert.Equal("Inherited from the cast", cut.Find(".clip-actor-chip-inherited").GetAttribute("title"));

        await cut.Find(".highlight-editor-start-input").InputAsync("0:30");
        await cut.Find(".highlight-editor-end-input").InputAsync("0:40");
        await cut.Find(".highlight-editor-form").SubmitAsync();

        // Nothing stored; the scene's actors are inherited.
        var highlight = Assert.Single(await highlightService.GetHighlightsAsync(movieId));
        Assert.Empty(highlight.Actors);
        Assert.Equal(["Aika"], highlight.EffectiveActors.Actors.Select(a => a.Name));
        Assert.Equal("Aika", cut.Find(".highlight-editor-name").TextContent);
        Assert.Equal(["Aika"], cut.FindAll(".highlight-editor-row .implicit-chip").Select(c => c.TextContent));
    }

    [Fact]
    public async Task AddHighlight_ClickingAChip_PicksThatActorAlone_AndUseInheritedGoesBack()
    {
        SeedCastAndScene();
        PlayerAt(30);
        var cut = RenderEditor();
        await cut.Find(".highlight-editor-add-btn").ClickAsync();
        // One inherited actor: nothing to customize.
        Assert.Empty(cut.FindAll(".clip-actor-customize"));

        await cut.FindAll(".highlight-editor-actor-chip")[0].ClickAsync();
        Assert.Equal(["Aika"], OwnActorChips(cut));
        Assert.Empty(InheritedActorChips(cut));

        await cut.Find(".clip-actor-use-inherited").ClickAsync();
        Assert.Empty(OwnActorChips(cut));
        Assert.Equal(["Aika"], InheritedActorChips(cut));

        // Picked, not unpicked: Bea alone.
        await cut.FindAll(".highlight-editor-actor-chip")[1].ClickAsync();
        Assert.Equal(["Bea"], OwnActorChips(cut));
        await cut.Find(".highlight-editor-end-input").InputAsync("0:40");
        await cut.Find(".highlight-editor-form").SubmitAsync();

        var highlight = Assert.Single(await highlightService.GetHighlightsAsync(movieId));
        Assert.Equal(["Bea"], highlight.Actors.Select(a => a.Name));
        Assert.Equal("Bea", cut.Find(".highlight-editor-name").TextContent);
        Assert.Empty(cut.FindAll(".highlight-editor-row .implicit-chip"));
    }

    [Fact]
    public async Task EditHighlight_UseInherited_ClearsItsOwnActors()
    {
        var (_, bea) = SeedCastAndScene();
        await highlightService.AddHighlightAsync(movieId, 10, 20, null, [], [bea]);
        var cut = RenderEditor();

        await cut.Find(".highlight-editor-edit-btn").ClickAsync();
        await cut.Find(".clip-actor-use-inherited").ClickAsync();
        await cut.Find(".highlight-editor-form").SubmitAsync();

        var highlight = Assert.Single(await highlightService.GetHighlightsAsync(movieId));
        Assert.Empty(highlight.Actors);
        Assert.Equal(["Aika"], highlight.EffectiveActors.Actors.Select(a => a.Name));
    }

    [Fact]
    public async Task EditHighlight_OwnActorsEqualToInherited_AreMarkedSameAsInherited()
    {
        var (aika, _) = SeedCastAndScene();
        await highlightService.AddHighlightAsync(movieId, 10, 20, "Same", [], [aika]);
        var cut = RenderEditor();

        await cut.Find(".highlight-editor-edit-btn").ClickAsync();

        var mark = cut.Find(".clip-actor-chips .redundant-mark");
        Assert.Equal("same as inherited", mark.TextContent);
        Assert.Contains("inherit from scene 1", mark.GetAttribute("title"));

        // Narrowed or widened, it's its own choice again.
        await cut.FindAll(".highlight-editor-actor-chip")[1].ClickAsync();
        Assert.Equal(["Aika", "Bea"], OwnActorChips(cut));
        Assert.Empty(cut.FindAll(".clip-actor-chips .redundant-mark"));
    }

    [Fact]
    public async Task EditHighlight_AnOwnTagItsApexCarries_IsMarkedRedundant_AndRemovingItLeavesItGrayed()
    {
        SeedCastAndScene();
        var squirt = tagId;
        var kiss = SeedTag("Kiss");
        await highlightService.AddHighlightAsync(movieId, 10, 20, "Tagged", [squirt, kiss], []);
        await apexService.AddApexAsync(movieId, 15, [squirt]);
        var cut = RenderEditor();

        await cut.Find(".highlight-editor-edit-btn").ClickAsync();

        var chips = cut.FindAll(".highlight-editor-form-wrap .highlight-editor-chip");
        Assert.Null(chips.Single(c => c.TextContent.Contains("Kiss")).QuerySelector(".redundant-mark"));
        var mark = chips.Single(c => c.TextContent.Contains("Squirt")).QuerySelector(".redundant-mark")!;
        Assert.Equal("Also from apex 1. Remove it and it stays, grayed, as implied.", mark.GetAttribute("title"));
        Assert.Empty(cut.FindAll(".highlight-editor-form-wrap .implicit-chip"));

        await cut.Find(".highlight-editor-form-wrap .highlight-editor-chip-remove[aria-label='Remove Squirt']").ClickAsync();

        Assert.Equal("Squirt", cut.Find(".highlight-editor-form-wrap .implicit-chip").TextContent);
        Assert.Empty(cut.FindAll(".highlight-editor-form-wrap .redundant-mark"));
    }

    [Fact]
    public async Task EditHighlight_ShowsItsActorsAndTags_AndSavesChanges()
    {
        var (aika, _) = SeedCastAndScene();
        await highlightService.AddHighlightAsync(movieId, 10, 20, null, [], [aika]);
        var cut = RenderEditor();

        await cut.Find(".highlight-editor-edit-btn").ClickAsync();
        Assert.Equal(["Aika"], SelectedActorChips(cut));
        await cut.FindAll(".highlight-editor-actor-chip")[1].ClickAsync();
        await cut.InvokeAsync(() => cut.FindComponent<TagSearchAdd>().Instance.OnAdd.InvokeAsync(tagId));
        Assert.Equal("Aika, Bea — Squirt", cut.Find(".highlight-editor-title-input").GetAttribute("placeholder"));
        await cut.Find(".highlight-editor-form").SubmitAsync();

        var highlight = Assert.Single(await highlightService.GetHighlightsAsync(movieId));
        Assert.Equal(["Aika", "Bea"], highlight.Actors.Select(a => a.Name));
        Assert.Equal(["Squirt"], highlight.Tags.Select(t => t.Name));
        Assert.Equal("Aika, Bea — Squirt", cut.Find(".highlight-editor-name").TextContent);
    }

    [Fact]
    public async Task AddHighlight_WithoutCast_ShowsAHint()
    {
        var cut = RenderEditor();

        await cut.Find(".highlight-editor-add-btn").ClickAsync();

        Assert.Empty(cut.FindAll(".highlight-editor-actor-chip"));
        Assert.Contains(cut.FindAll(".highlight-editor-form .clip-actor-hint"), h => h.TextContent.Contains("no matched cast"));
    }

    [Fact]
    public async Task ShortcutH_ThenShiftH_InheritsTheActorsAtTheStart()
    {
        SeedCastAndScene();
        var cut = RenderEditor(shortcuts: true);

        await cut.InvokeAsync(() => cut.Instance.StartHighlightAtAsync(50));
        await cut.InvokeAsync(() => cut.Instance.EndHighlightAtAsync(60));

        // Nothing stored; the scene's actors are inherited.
        var highlight = Assert.Single(await highlightService.GetHighlightsAsync(movieId));
        Assert.Empty(highlight.Actors);
        Assert.Equal(["Aika"], highlight.EffectiveActors.Actors.Select(a => a.Name));
    }

    [Fact]
    public async Task SavingAHighlightThatChangesTheMovieTags_RaisesOnMovieTagsMayHaveChanged_OnlyThen()
    {
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Scenes.Add(new Scene { MovieId = movieId, StartSeconds = 0, EndSeconds = 600 });
            await db.SaveChangesAsync();
        }
        PlayerAt(100);
        var propagated = 0;
        var cut = RenderClipEditor(onMovieTags: () => propagated++);

        await cut.Find(".highlight-editor-add-btn").ClickAsync();
        await cut.Find(".highlight-editor-end-input").InputAsync("2:00");
        await cut.Find(".highlight-editor-form").SubmitAsync();
        Assert.Equal(0, propagated);

        await cut.Find(".highlight-editor-add-btn").ClickAsync();
        await cut.Find(".highlight-editor-end-input").InputAsync("2:00");
        await cut.InvokeAsync(() => cut.FindComponent<TagSearchAdd>().Instance.OnAdd.InvokeAsync(tagId));
        await cut.Find(".highlight-editor-form").SubmitAsync();
        Assert.Equal(1, propagated);
    }
}
