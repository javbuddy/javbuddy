using Bunit;
using Javbuddy.Components.Shared;
using Javbuddy.Models;
using Javbuddy.Services.Scenes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Javbuddy.Tests.Components.Shared;

/// <summary>ClipEditor's apexes, ported from ApexEditorTests.</summary>
public class ClipEditorApexTests : ClipEditorTestBase
{
    private readonly int tagId;
    private readonly int cowgirlId;

    public ClipEditorApexTests()
    {
        tagId = SeedTag("Squirt");
        cowgirlId = SeedTag("Cowgirl");
    }

    private IRenderedComponent<ClipEditor> RenderEditor(Action<IReadOnlyList<ApexItem>>? onChanged = null, bool shortcuts = false) =>
        RenderClipEditor(shortcuts, onApexes: onChanged);

    [Fact]
    public void NoApexes_ShowsNoRowsAndNoCount()
    {
        var cut = RenderEditor();

        Assert.Empty(cut.FindAll(".apex-editor-item"));
        Assert.Empty(cut.FindAll(".clip-editor-counts"));
    }

    [Fact]
    public async Task ExistingApexes_ListTimeAndTags_AndReportToHost()
    {
        await apexService.AddApexAsync(movieId, 125, [tagId]);
        IReadOnlyList<ApexItem>? reported = null;

        var cut = RenderEditor(a => reported = a);

        Assert.Equal("0 highlights · 1 apex", cut.Find(".clip-editor-counts").GetAttribute("title"));
        Assert.Equal("2:05", cut.Find(".apex-editor-time").TextContent);
        Assert.Equal("Squirt", cut.Find(".apex-editor-tag").TextContent);
        Assert.Single(reported!);
    }

    [Fact]
    public async Task MarkApex_PrefillsThePlayersTime_AddsTagAndSaves()
    {
        module.Setup<double?>("currentTime", Selector).SetResult(61.5);
        IReadOnlyList<ApexItem>? reported = null;
        var cut = RenderEditor(a => reported = a);

        await cut.Find(".apex-editor-add-btn").ClickAsync();
        Assert.Equal("1:01.5", cut.Find(".apex-editor-time-input").GetAttribute("value"));
        await cut.InvokeAsync(() => cut.FindComponent<TagSearchAdd>().Instance.OnAdd.InvokeAsync(tagId));
        Assert.Contains("Squirt", cut.Find(".apex-editor-chip").TextContent);
        await cut.Find(".apex-editor-form").SubmitAsync();

        var apex = Assert.Single(await apexService.GetApexesAsync(movieId));
        Assert.Equal(61.5, apex.Seconds);
        Assert.Equal(["Squirt"], apex.Tags.Select(t => t.Name));
        Assert.Equal(apex.Id, Assert.Single(reported!).Id);
        Assert.Empty(cut.FindAll(".apex-editor-form"));
    }

    [Fact]
    public async Task ApexWithoutPreview_QueuesItsMedia_SoTheHostsCardsGetItWithoutARefresh()
    {
        await apexService.AddApexAsync(movieId, 125, [tagId]);

        RenderEditor();

        await apexMedia.Received(1).EnsureQueuedAsync(movieId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InvalidTime_ShowsAnError_AndSavesNothing()
    {
        var cut = RenderEditor();

        await cut.Find(".apex-editor-add-btn").ClickAsync();
        await cut.Find(".apex-editor-time-input").InputAsync("nope");
        await cut.Find(".apex-editor-form").SubmitAsync();

        Assert.Contains("m:ss", cut.Find(".scene-editor-error").TextContent);
        Assert.Empty(await apexService.GetApexesAsync(movieId));
    }

    [Fact]
    public async Task RowClick_SeeksFiveSecondsBeforeTheApex()
    {
        await apexService.AddApexAsync(movieId, 125, []);
        var seek = module.Setup<bool>("seek", Selector, 120d);
        var cut = RenderEditor();

        await cut.Find(".apex-editor-row").ClickAsync();

        Assert.Single(seek.Invocations);
    }

    [Fact]
    public async Task Delete_ConfirmsThenRemovesTheApex()
    {
        await apexService.AddApexAsync(movieId, 125, []);
        var cut = RenderEditor();

        await cut.Find(".apex-editor-delete-btn").ClickAsync();
        await cut.Find(".delete-confirm-confirm-btn").ClickAsync();

        Assert.Empty(await apexService.GetApexesAsync(movieId));
        Assert.Empty(cut.FindAll(".apex-editor-item"));
    }

    [Fact]
    public void Shortcuts_AreOnlyRegisteredWhenEnabled_BindingAllFiveKeysOnce()
    {
        RenderEditor(shortcuts: false);
        Assert.DoesNotContain(module.Invocations, i => i.Identifier == "initShortcuts");

        RenderEditor(shortcuts: true);
        var init = Assert.Single(module.Invocations, i => i.Identifier == "initShortcuts");
        Assert.Equal(Selector, init.Arguments[2]);
        var bindings = Assert.IsAssignableFrom<IReadOnlyDictionary<string, string>>(init.Arguments[3]);
        Assert.Equal(new Dictionary<string, string>
        {
            ["m"] = nameof(ClipEditor.AddSceneAtAsync),
            ["shift+m"] = nameof(ClipEditor.EndSceneAtAsync),
            ["h"] = nameof(ClipEditor.StartHighlightAtAsync),
            ["shift+h"] = nameof(ClipEditor.EndHighlightAtAsync),
            ["a"] = nameof(ClipEditor.StartApexAtAsync),
        }, bindings);
    }

    [Fact]
    public async Task ShortcutA_OpensTheFormAtTheCurrentTime_WithoutSaving()
    {
        var cut = RenderEditor(shortcuts: true);

        await cut.InvokeAsync(() => cut.Instance.StartApexAtAsync(125.5));

        Assert.Equal("2:05.5", cut.Find(".apex-editor-time-input").GetAttribute("value"));
        Assert.Empty(cut.FindAll(".apex-editor-chip"));
        Assert.Empty(await apexService.GetApexesAsync(movieId));

        await cut.Find(".apex-editor-form").SubmitAsync();

        var apex = Assert.Single(await apexService.GetApexesAsync(movieId));
        Assert.Equal(125.5, apex.Seconds);
        Assert.Empty(apex.Tags);
        Assert.Empty(cut.FindAll(".apex-editor-form"));
    }

    [Fact]
    public async Task ShortcutA_Again_MovesTheTime_KeepingTheForm()
    {
        var cut = RenderEditor(shortcuts: true);
        await cut.InvokeAsync(() => cut.Instance.StartApexAtAsync(10));
        await cut.InvokeAsync(() => cut.FindComponent<TagSearchAdd>().Instance.OnAdd.InvokeAsync(tagId));

        await cut.InvokeAsync(() => cut.Instance.StartApexAtAsync(20));

        Assert.Equal("0:20", cut.Find(".apex-editor-time-input").GetAttribute("value"));
        Assert.Contains("Squirt", cut.Find(".apex-editor-chip").TextContent);
        Assert.Single(cut.FindAll(".apex-editor-form"));
    }

    [Fact]
    public async Task ShortcutA_WhileEditingAnApex_ShowsAnError_AndKeepsTheEdit()
    {
        await apexService.AddApexAsync(movieId, 125, []);
        var cut = RenderEditor(shortcuts: true);
        await cut.Find(".apex-editor-edit-btn").ClickAsync();

        await cut.InvokeAsync(() => cut.Instance.StartApexAtAsync(10));

        Assert.Contains("being edited", cut.Find(".scene-editor-error").TextContent);
        Assert.Equal("2:05", cut.Find(".apex-editor-time-input").GetAttribute("value"));
        Assert.Single(await apexService.GetApexesAsync(movieId));
    }

    [Fact]
    public async Task EditApex_ShowsTheDefaultWindowAsPlaceholders_AndSavesItsOwnLeadInAndTail()
    {
        var apexId = (await apexService.AddApexAsync(movieId, 125, [], window: new ApexWindowOverride(null, 15))).ApexId;
        var cut = RenderEditor();
        await cut.Find(".apex-editor-edit-btn").ClickAsync();

        var leadIn = cut.Find(".apex-editor-lead-in-input");
        var tail = cut.Find(".apex-editor-tail-input");
        Assert.Equal(("5", "", "5", "15"), (leadIn.GetAttribute("placeholder"), leadIn.GetAttribute("value"), tail.GetAttribute("placeholder"), tail.GetAttribute("value")));

        leadIn.Input("2.5");
        cut.Find(".apex-editor-tail-input").Input("");
        await cut.Find(".apex-editor-form").SubmitAsync();

        var apex = Assert.Single(await apexService.GetApexesAsync(movieId));
        Assert.Equal((apexId, (double?)2.5, (double?)null), ((int?)apex.Id, apex.OwnLeadInSeconds, apex.OwnTailSeconds));
    }

    [Fact]
    public async Task WindowNowButtons_SetTheLeadInAndTail_FromThePlayheadsDistanceToTheApex()
    {
        await apexService.AddApexAsync(movieId, 125, []);
        var cut = RenderEditor();
        await cut.Find(".apex-editor-edit-btn").ClickAsync();

        module.Setup<double?>("currentTime", Selector).SetResult(136);
        await cut.Find(".apex-editor-tail-now-btn").ClickAsync();
        module.Setup<double?>("currentTime", Selector).SetResult(117.5);
        await cut.Find(".apex-editor-lead-in-now-btn").ClickAsync();

        Assert.Equal(("7.5", "11"), (cut.Find(".apex-editor-lead-in-input").GetAttribute("value"), cut.Find(".apex-editor-tail-input").GetAttribute("value")));
        await cut.Find(".apex-editor-form").SubmitAsync();
        Assert.Equal(new ApexWindow(7.5, 11), Assert.Single(await apexService.GetApexesAsync(movieId)).Window);
    }

    [Fact]
    public async Task WindowNowButtons_OnTheWrongSideOfTheApex_ShowAnError()
    {
        await apexService.AddApexAsync(movieId, 125, []);
        var cut = RenderEditor();
        await cut.Find(".apex-editor-edit-btn").ClickAsync();

        module.Setup<double?>("currentTime", Selector).SetResult(130);
        await cut.Find(".apex-editor-lead-in-now-btn").ClickAsync();

        Assert.Contains("before the apex", cut.Find(".scene-editor-error").TextContent);
        Assert.Equal("", cut.Find(".apex-editor-lead-in-input").GetAttribute("value"));
    }

    [Fact]
    public async Task MarkApex_ANonNumericLeadIn_ShowsAnErrorWithoutSaving()
    {
        var cut = RenderEditor();
        await cut.Find(".apex-editor-add-btn").ClickAsync();
        cut.Find(".apex-editor-time-input").Input("1:00");

        cut.Find(".apex-editor-lead-in-input").Input("soon");
        await cut.Find(".apex-editor-form").SubmitAsync();

        Assert.Contains("lead-in and tail as seconds", cut.Find(".scene-editor-error").TextContent);
        Assert.Empty(await apexService.GetApexesAsync(movieId));
    }

    [Fact]
    public async Task ShortcutA_ClampsToTheDuration()
    {
        var cut = RenderEditor(shortcuts: true);

        await cut.InvokeAsync(() => cut.Instance.StartApexAtAsync(3600.4));
        await cut.Find(".apex-editor-form").SubmitAsync();

        Assert.Equal(3600d, Assert.Single(await apexService.GetApexesAsync(movieId)).Seconds);
    }

    [Fact]
    public async Task MarkApex_OffersThePreviousApexsTags_AndAddsAClickedOne()
    {
        await apexService.AddApexAsync(movieId, 30, [cowgirlId]);
        await apexService.AddApexAsync(movieId, 50, [tagId, cowgirlId]);
        await apexService.AddApexAsync(movieId, 90, []);
        module.Setup<double?>("currentTime", Selector).SetResult(61.5);
        var cut = RenderEditor();

        await cut.Find(".apex-editor-add-btn").ClickAsync();
        Assert.Equal(["Cowgirl", "Squirt"], cut.FindAll(".apex-editor-suggestion").Select(b => b.TextContent));
        await cut.FindAll(".apex-editor-suggestion")[1].ClickAsync();

        Assert.Contains("Squirt", cut.Find(".apex-editor-chip").TextContent);
        Assert.Equal(["Cowgirl"], cut.FindAll(".apex-editor-suggestion").Select(b => b.TextContent));
        await cut.Find(".apex-editor-form").SubmitAsync();

        var added = (await apexService.GetApexesAsync(movieId)).Single(a => a.Seconds == 61.5);
        Assert.Equal(["Squirt"], added.Tags.Select(t => t.Name));
    }

    [Fact]
    public async Task CopyPreviousTags_AddsThemAll_AndHidesTheSuggestions()
    {
        await apexService.AddApexAsync(movieId, 50, [tagId, cowgirlId]);
        module.Setup<double?>("currentTime", Selector).SetResult(61.5);
        var cut = RenderEditor();

        await cut.Find(".apex-editor-add-btn").ClickAsync();
        await cut.Find(".apex-editor-copy-tags-btn").ClickAsync();

        Assert.Equal(2, cut.FindAll(".apex-editor-chip").Count);
        Assert.Empty(cut.FindAll(".apex-editor-suggestions"));
    }

    [Fact]
    public async Task EditingTheTime_ReEvaluatesThePreviousApex()
    {
        await apexService.AddApexAsync(movieId, 30, [cowgirlId]);
        await apexService.AddApexAsync(movieId, 50, [tagId]);
        module.Setup<double?>("currentTime", Selector).SetResult(61.5);
        var cut = RenderEditor();

        await cut.Find(".apex-editor-add-btn").ClickAsync();
        Assert.Equal("Squirt", cut.Find(".apex-editor-suggestion").TextContent);
        await cut.Find(".apex-editor-time-input").InputAsync("0:40");

        Assert.Equal("Cowgirl", cut.Find(".apex-editor-suggestion").TextContent);
    }

    [Fact]
    public async Task NoPreviousTags_OrEditingAnApex_ShowsNoSuggestions()
    {
        await apexService.AddApexAsync(movieId, 50, []);
        await apexService.AddApexAsync(movieId, 90, [tagId]);
        module.Setup<double?>("currentTime", Selector).SetResult(61.5);
        var cut = RenderEditor();

        await cut.Find(".apex-editor-add-btn").ClickAsync();
        Assert.Empty(cut.FindAll(".apex-editor-suggestions"));
        await cut.Find(".apex-editor-cancel-btn").ClickAsync();

        await cut.FindAll(".apex-editor-edit-btn")[1].ClickAsync();
        Assert.Empty(cut.FindAll(".apex-editor-suggestions"));
    }

    [Fact]
    public void NoApexes_MarkApex_ShowsNoSuggestions()
    {
        var cut = RenderEditor();

        cut.Find(".apex-editor-add-btn").Click();

        Assert.Empty(cut.FindAll(".apex-editor-suggestions"));
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
        cut.FindAll(".apex-editor-actor-chip[aria-pressed=true]").Select(c => c.TextContent);

    private static IEnumerable<string> InheritedActorChips(IRenderedComponent<ClipEditor> cut) =>
        cut.FindAll(".apex-editor-actor-chip.clip-actor-chip-inherited").Select(c => c.TextContent);

    [Fact]
    public async Task MarkApex_ShowsTheActorsItInherits_FollowingTheTime_AndSavesNoneOfItsOwn()
    {
        SeedCastAndScene();
        PlayerAt(50);
        var cut = RenderEditor();

        await cut.Find(".apex-editor-add-btn").ClickAsync();
        Assert.Equal(["Aika", "Bea"], cut.FindAll(".apex-editor-actor-chip").Select(c => c.TextContent));
        Assert.Equal(["Aika"], InheritedActorChips(cut));
        Assert.Equal("Inherited from scene 1", cut.Find(".clip-actor-chip-inherited").GetAttribute("title"));
        await cut.Find(".apex-editor-time-input").InputAsync("2:00");
        Assert.Equal(["Aika", "Bea"], InheritedActorChips(cut));
        await cut.Find(".apex-editor-time-input").InputAsync("0:50");
        await cut.Find(".apex-editor-form").SubmitAsync();

        // Nothing stored; the scene's actors are inherited, and the row shows them grayed.
        var inheriting = Assert.Single(await apexService.GetApexesAsync(movieId));
        Assert.Empty(inheriting.Actors);
        Assert.Equal(["Aika"], inheriting.EffectiveActors.Actors.Select(a => a.Name));
        var chip = cut.Find(".apex-editor-row .implicit-chip");
        Assert.Equal(("Aika", "From scene 1"), (chip.TextContent, chip.GetAttribute("title")));
    }

    [Fact]
    public async Task MarkApex_InsideAHighlightWithActors_InheritsTheHighlights()
    {
        var (_, bea) = SeedCastAndScene();
        await highlightService.AddHighlightAsync(movieId, 40, 60, "Climax", [], [bea]);
        PlayerAt(50);
        var cut = RenderEditor();

        await cut.Find(".apex-editor-add-btn").ClickAsync();

        Assert.Equal(["Bea"], InheritedActorChips(cut));
        Assert.Equal("Inherited from highlight 1", cut.Find(".clip-actor-chip-inherited").GetAttribute("title"));
    }

    [Fact]
    public async Task MarkApex_ClickingAChipPicksIt_AndCustomizeNarrowsTheInheritedSet()
    {
        SeedCastAndScene();
        await using (var db = await factory.CreateDbContextAsync())
        {
            var scene = await db.Scenes.Include(s => s.SceneActors).SingleAsync();
            scene.SceneActors.Add(new SceneActor { MovieId = movieId, ActorId = db.Actors.Single(a => a.FirstName == "Bea").Id });
            await db.SaveChangesAsync();
        }
        PlayerAt(30);
        var cut = RenderEditor();

        await cut.Find(".apex-editor-add-btn").ClickAsync();
        Assert.Equal(["Aika", "Bea"], InheritedActorChips(cut));
        // Picks Bea alone, rather than unpicking her from the inherited pair.
        await cut.FindAll(".apex-editor-actor-chip")[1].ClickAsync();
        Assert.Equal(["Bea"], SelectedActorChips(cut));
        Assert.Empty(InheritedActorChips(cut));
        Assert.Empty(cut.FindAll(".clip-actor-customize"));

        // Customize starts from both, to unpick from.
        await cut.Find(".clip-actor-use-inherited").ClickAsync();
        await cut.Find(".clip-actor-customize").ClickAsync();
        Assert.Equal(["Aika", "Bea"], SelectedActorChips(cut));
        Assert.Empty(InheritedActorChips(cut));
        await cut.FindAll(".apex-editor-actor-chip")[0].ClickAsync();
        Assert.Equal(["Bea"], SelectedActorChips(cut));
        await cut.Find(".apex-editor-form").SubmitAsync();

        var chosen = Assert.Single(await apexService.GetApexesAsync(movieId));
        Assert.Equal(["Bea"], chosen.Actors.Select(a => a.Name));
        Assert.Equal("Bea", cut.Find(".apex-editor-actors").TextContent);
    }

    [Fact]
    public async Task EditApex_ShowsItsActors_AndSavesToggles()
    {
        var (aika, _) = SeedCastAndScene();
        await apexService.AddApexAsync(movieId, 50, [], [aika]);
        var cut = RenderEditor();

        await cut.Find(".apex-editor-edit-btn").ClickAsync();
        Assert.Equal(["Aika"], SelectedActorChips(cut));
        await cut.FindAll(".apex-editor-actor-chip")[1].ClickAsync();
        await cut.Find(".apex-editor-form").SubmitAsync();

        Assert.Equal(["Aika", "Bea"], Assert.Single(await apexService.GetApexesAsync(movieId)).Actors.Select(a => a.Name));
        Assert.Equal("Aika, Bea", cut.Find(".apex-editor-actors").TextContent);
    }

    [Fact]
    public async Task MarkApex_WithoutCast_ShowsAHint()
    {
        var cut = RenderEditor();

        await cut.Find(".apex-editor-add-btn").ClickAsync();

        Assert.Empty(cut.FindAll(".apex-editor-actor-chip"));
        Assert.Contains("no matched cast", cut.Find(".apex-editor-form .clip-actor-hint").TextContent);
    }

    [Fact]
    public async Task ShortcutA_WithoutAPreviousApex_InheritsTheActorsAtTheTime()
    {
        SeedCastAndScene();
        var cut = RenderEditor(shortcuts: true);

        await cut.InvokeAsync(() => cut.Instance.StartApexAtAsync(50));
        await cut.Find(".apex-editor-form").SubmitAsync();

        Assert.Equal(["Aika"], Assert.Single(await apexService.GetApexesAsync(movieId)).EffectiveActors.Actors.Select(a => a.Name));
    }

    [Fact]
    public async Task ShortcutA_PrefillsThePreviousApexsTagsAndActors()
    {
        var (aika, bea) = SeedCastAndScene();
        await apexService.AddApexAsync(movieId, 30, [cowgirlId], [aika]);
        await apexService.AddApexAsync(movieId, 50, [tagId, cowgirlId], [bea]);
        await apexService.AddApexAsync(movieId, 90, [], [aika, bea]);
        var cut = RenderEditor(shortcuts: true);

        // Between 50 and 90, in a scene that would give Aika: 50's tags and actors instead.
        await cut.InvokeAsync(() => cut.Instance.StartApexAtAsync(60));
        await cut.Find(".apex-editor-form").SubmitAsync();

        var added = (await apexService.GetApexesAsync(movieId))[2];
        Assert.Equal(60, added.Seconds);
        Assert.Equal(["Cowgirl", "Squirt"], added.Tags.Select(t => t.Name));
        Assert.Equal(["Bea"], added.Actors.Select(a => a.Name));
    }

    [Fact]
    public async Task ShortcutA_AfterAnApexWithoutActors_InheritsTheActorsAtTheTime()
    {
        SeedCastAndScene();
        await apexService.AddApexAsync(movieId, 30, [cowgirlId], []);
        var cut = RenderEditor(shortcuts: true);

        await cut.InvokeAsync(() => cut.Instance.StartApexAtAsync(500));
        await cut.Find(".apex-editor-form").SubmitAsync();

        var added = (await apexService.GetApexesAsync(movieId))[1];
        Assert.Equal(["Cowgirl"], added.Tags.Select(t => t.Name));
        Assert.Empty(added.Actors);
        Assert.Equal(["Aika", "Bea"], added.EffectiveActors.Actors.Select(a => a.Name));
    }

    [Fact]
    public async Task HeartButton_TogglesTheFavorite_AndReportsToHost()
    {
        await apexService.AddApexAsync(movieId, 125, []);
        IReadOnlyList<ApexItem>? reported = null;
        var cut = RenderEditor(a => reported = a);
        var heart = cut.Find(".apex-editor-fav-btn");
        Assert.Equal("false", heart.GetAttribute("aria-pressed"));
        Assert.Equal("Add to favorite apexes", heart.GetAttribute("title"));

        await heart.ClickAsync();

        Assert.True(Assert.Single(await apexService.GetApexesAsync(movieId)).IsFavorite);
        Assert.True(Assert.Single(reported!).IsFavorite);
        heart = cut.Find(".apex-editor-fav-btn");
        Assert.Equal("true", heart.GetAttribute("aria-pressed"));
        Assert.Contains("apex-editor-fav-on", heart.ClassList);
        Assert.Equal("Remove from favorite apexes", heart.GetAttribute("title"));

        await heart.ClickAsync();

        Assert.False(Assert.Single(await apexService.GetApexesAsync(movieId)).IsFavorite);
    }

    [Fact]
    public async Task SavingAnApexThatChangesTheMovieTags_RaisesOnMovieTagsMayHaveChanged_OnlyThen()
    {
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Scenes.Add(new Scene { MovieId = movieId, StartSeconds = 0, EndSeconds = 600 });
            await db.SaveChangesAsync();
        }
        module.Setup<double?>("currentTime", Selector).SetResult(100);
        var propagated = 0;
        var cut = RenderClipEditor(onMovieTags: () => propagated++);

        await cut.Find(".apex-editor-add-btn").ClickAsync();
        await cut.Find(".apex-editor-form").SubmitAsync();
        Assert.Equal(0, propagated);

        await cut.Find(".apex-editor-add-btn").ClickAsync();
        await cut.InvokeAsync(() => cut.FindComponent<TagSearchAdd>().Instance.OnAdd.InvokeAsync(tagId));
        await cut.Find(".apex-editor-form").SubmitAsync();
        Assert.Equal(1, propagated);
    }
}
