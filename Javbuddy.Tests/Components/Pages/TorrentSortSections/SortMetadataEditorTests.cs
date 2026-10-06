using Bunit;
using Javbuddy.Components.Pages.TorrentSortSections;
using Javbuddy.Services.Actors;
using Javbuddy.Services.Javinizer;
using Javbuddy.Services.Tags;
using Javbuddy.Services.Torrents;
using Javbuddy.Services.Torrents.SortWizard;
using Javbuddy.Tests.TestSupport;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Javbuddy.Tests.Components.Pages.TorrentSortSections;

/// <summary>TorrentSort's metadata editor drawer content.</summary>
public class SortMetadataEditorTests : BunitContext
{
    private readonly ISortEditorLookupService lookup = Substitute.For<ISortEditorLookupService>();
    private readonly ITorrentSortService sortService = Substitute.For<ITorrentSortService>();
    private readonly TestDbContextFactory factory = new();

    protected override void Dispose(bool disposing)
    {
        if (disposing) factory.Dispose();
        base.Dispose(disposing);
    }

    private MovieEditModalState Open(MovieViewDto movie, WizardSession? session = null)
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        lookup.MatchTrackedActorsAsync(Arg.Any<IReadOnlyList<ActressViewDto>>(), Arg.Any<CancellationToken>())
            .Returns(ci => ci.Arg<IReadOnlyList<ActressViewDto>>().Select(a => a.FirstName == "Noa" ? new TrackedActorMatch(12, true) : null).ToList());
        lookup.ResolveGenresAsync(Arg.Any<IEnumerable<string?>>(), Arg.Any<CancellationToken>())
            .Returns(ci => ci.Arg<IEnumerable<string?>>().OfType<string>().Select(n => n switch
            {
                "Solowork" => new GenreResolution(n, "Solo", true),
                "Sample" => new GenreResolution(n, null, true),
                "Fresh" => new GenreResolution(n, n, false),
                _ => new GenreResolution(n, n, true)
            }).ToList());
        lookup.GetTopTagsAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns([]);
        sortService.GetResultSourcesAsync(default, default!, default).ReturnsForAnyArgs(new JavinizerSourcesResult(true, [], null));
        sortService.GetScrapersAsync(default).ReturnsForAnyArgs(new JavinizerScrapersResult(true, [], null));
        Services.AddSingleton(lookup);
        Services.AddSingleton(Substitute.For<IActorService>());
        Services.AddSingleton(Substitute.For<ITagService>());

        var state = new MovieEditModalState(session ?? new WizardSession(), sortService, factory, lookup, TimeProvider.System, _ => Task.CompletedTask);
        state.OpenEditModal("res-1", movie);
        return state;
    }

    [Fact]
    public void RendersCastChipsWithLocalHeadshotThumbOrInitials_AndJapaneseOnlyName()
    {
        var state = Open(new MovieViewDto
        {
            Id = "ABC-123",
            Actresses =
            [
                new ActressViewDto { FirstName = "Noa", LastName = "Araki", JapaneseName = "新木希空", ThumbUrl = "remote.jpg" },
                new ActressViewDto { FirstName = "Yui", LastName = "Hatano", ThumbUrl = "remote2.jpg" },
                new ActressViewDto { JapaneseName = "榊原萌" }
            ]
        });

        var cut = Render<SortMetadataEditor>(p => p.Add(x => x.Editor, state));

        var chips = cut.FindAll(".sme-cast-chip");
        Assert.Equal(3, chips.Count);
        Assert.Equal("/actor-image/12/thumb", chips[0].QuerySelector("img")!.GetAttribute("src"));
        Assert.Contains("新木希空", chips[0].TextContent);
        Assert.Equal("remote2.jpg", chips[1].QuerySelector("img")!.GetAttribute("src"));
        Assert.Contains("sme-cast-untracked", chips[1].ClassList);
        Assert.Null(chips[2].QuerySelector("img"));
        Assert.Contains("榊原萌", chips[2].TextContent);
    }

    [Fact]
    public void RemovingCastChip_RemovesThatActressOnly()
    {
        var state = Open(new MovieViewDto { Id = "A", Actresses = [new() { FirstName = "Noa" }, new() { FirstName = "Yui" }] });
        var cut = Render<SortMetadataEditor>(p => p.Add(x => x.Editor, state));

        cut.FindAll(".sme-cast-remove")[0].Click();

        Assert.Equal("Yui", Assert.Single(state.EditingMovie!.Actresses!).FirstName);
        Assert.Single(cut.FindAll(".sme-cast-chip"));
    }

    [Fact]
    public void GenreChips_ShowNormalizationMarkers_AndCommaGenreStaysOneChip()
    {
        var state = Open(new MovieViewDto { Id = "A", Genres = [new() { Name = "Nasty, hardcore" }, new() { Name = "Solowork" }, new() { Name = "Sample" }, new() { Name = "Fresh" }] });
        var cut = Render<SortMetadataEditor>(p => p.Add(x => x.Editor, state));

        var chips = cut.FindAll(".sme-genre-chip");
        Assert.Equal(4, chips.Count);
        Assert.Contains("Nasty, hardcore", chips[0].TextContent);
        Assert.Contains("Solo", chips[1].QuerySelector(".sme-genre-renamed")!.TextContent);
        Assert.NotNull(chips[2].QuerySelector(".sme-genre-ignored"));
        Assert.NotNull(chips[3].QuerySelector(".sme-genre-new"));

        chips[1].QuerySelector(".sme-genre-remove")!.Click();
        Assert.DoesNotContain(state.EditingMovie!.Genres!, g => g.Name == "Solowork");
    }

    [Fact]
    public void UntrackedForm_AddsStructuredActress()
    {
        var state = Open(new MovieViewDto { Id = "A" });
        var cut = Render<SortMetadataEditor>(p => p.Add(x => x.Editor, state));

        cut.Find(".sme-untracked-toggle").Click();
        cut.FindAll(".sme-untracked-form input")[0].Change("Moe");
        cut.FindAll(".sme-untracked-form input")[1].Change("Sakakibara");
        cut.FindAll(".sme-untracked-form input")[2].Change("榊原萌");
        cut.Find(".sme-untracked-form").Submit();

        var a = Assert.Single(state.EditingMovie!.Actresses!);
        Assert.Equal(("Moe", "Sakakibara", "榊原萌"), (a.FirstName, a.LastName, a.JapaneseName));
        Assert.Empty(cut.FindAll(".sme-untracked-form"));
    }

    [Fact]
    public void UntrackedForm_BlankNames_AddsNothing()
    {
        var state = Open(new MovieViewDto { Id = "A" });
        var cut = Render<SortMetadataEditor>(p => p.Add(x => x.Editor, state));

        cut.Find(".sme-untracked-toggle").Click();
        cut.Find(".sme-untracked-form").Submit();

        Assert.Empty(state.EditingMovie!.Actresses!);
    }

    [Fact]
    public void TypingIntoTitle_UpdatesBufferAndMarksDirty()
    {
        var state = Open(new MovieViewDto { Id = "A", Title = "Old" });
        var cut = Render<SortMetadataEditor>(p => p.Add(x => x.Editor, state));

        cut.Find("input[data-field=title]").Input("New");

        Assert.Equal("New", state.EditingMovie!.Title);
        Assert.True(state.IsDirty);
    }

    [Fact]
    public void QualityWarnings_ShowNextToTheirField_AndClearLiveWhenFixed()
    {
        var state = Open(new MovieViewDto { Id = "A", Title = "A" });
        var cut = Render<SortMetadataEditor>(p => p.Add(x => x.Editor, state));

        Assert.NotNull(cut.Find(".sme-field:has(#sme-title) .sme-field-warning[data-issue=FallbackTitle]"));
        Assert.NotNull(cut.Find(".sme-field:has(#sme-maker) .sme-field-warning[data-issue=MissingMaker]"));
        Assert.NotNull(cut.Find(".sme-field:has(#sme-release_date) .sme-field-warning[data-issue=MissingReleaseDate]"));
        Assert.NotNull(cut.Find(".sme-section .sme-field-warning[data-issue=MissingActresses]"));

        cut.Find("input[data-field=maker]").Input("S1");

        Assert.Empty(cut.FindAll(".sme-field-warning[data-issue=MissingMaker]"));
        Assert.Equal(3, cut.FindAll(".sme-field-warning").Count);
    }

    [Fact]
    public void FieldLabels_PointAtTheirInput_NotTheSourceBadge()
    {
        var state = Open(new MovieViewDto { Id = "A" });
        var cut = Render<SortMetadataEditor>(p => p.Add(x => x.Editor, state));

        var inputs = cut.FindAll("input[data-field], textarea[data-field]");
        Assert.Equal(9, inputs.Count);
        foreach (var input in inputs)
        {
            Assert.Null(cut.Find($"label[for={input.Id}]").QuerySelector("button"));
        }
        Assert.Empty(cut.FindAll("label .sme-source-badge"));
    }

    [Fact]
    public void Palette_ShowsTopTagsNotApplied_AndClickAdds()
    {
        var state = Open(new MovieViewDto { Id = "A", Genres = [new() { Name = "vr" }] });
        lookup.GetTopTagsAsync(15, Arg.Any<CancellationToken>()).Returns(["VR", "Solo", "HD"]);
        var cut = Render<SortMetadataEditor>(p => p.Add(x => x.Editor, state));

        var chips = cut.FindAll(".sme-palette-chip");
        Assert.Equal(["Solo", "HD"], chips.Select(c => c.TextContent.Trim()));
        chips[0].Click();

        Assert.Contains(state.EditingMovie!.Genres!, g => g.Name == "Solo");
        Assert.DoesNotContain(cut.FindAll(".sme-palette-chip"), c => c.TextContent.Trim() == "Solo");
    }

    [Fact]
    public void Palette_HidesAGenreAppliedUnderAnotherNameThatResolvesToIt()
    {
        // "Solowork" is applied raw but resolves to "Solo" (the mocked normalization in Open).
        var state = Open(new MovieViewDto { Id = "A", Genres = [new() { Name = "Solowork" }] });
        lookup.GetTopTagsAsync(15, Arg.Any<CancellationToken>()).Returns(["Solo", "HD"]);
        var cut = Render<SortMetadataEditor>(p => p.Add(x => x.Editor, state));

        Assert.Equal(["HD"], cut.FindAll(".sme-palette-chip").Select(c => c.TextContent.Trim()));
    }

    [Fact]
    public void CastChips_ShowEachActressSource()
    {
        var state = Open(new MovieViewDto
        {
            Id = "A",
            Actresses = [new ActressViewDto { DmmId = 5, FirstName = "Noa" }, new ActressViewDto { JapaneseName = "榊原　萌" }, new ActressViewDto { FirstName = "Who" }]
        });
        var result = new BatchFileResultDto
        {
            ResultId = "res-1",
            ActressSources = System.Text.Json.JsonDocument.Parse("""{"dmmid:5":"dmm","name:榊原 萌":"r18dev"}""").RootElement
        };

        var cut = Render<SortMetadataEditor>(p => p.Add(x => x.Editor, state).Add(x => x.Result, result));

        var chips = cut.FindAll(".sme-cast-chip");
        Assert.Equal("dmm", chips[0].QuerySelector(".sme-cast-source")!.TextContent);
        Assert.Equal("r18dev", chips[1].QuerySelector(".sme-cast-source")!.TextContent);
        Assert.Null(chips[2].QuerySelector(".sme-cast-source"));
    }

    [Fact]
    public void SlowJavinizer_DoesNotHoldBackHeadshotsOrThePalette()
    {
        var state = Open(new MovieViewDto { Id = "A", Actresses = [new ActressViewDto { FirstName = "Noa" }] });
        sortService.GetResultSourcesAsync(default, default!, default).ReturnsForAnyArgs(new TaskCompletionSource<JavinizerSourcesResult>().Task);
        lookup.GetTopTagsAsync(15, Arg.Any<CancellationToken>()).Returns(["HD"]);

        var cut = Render<SortMetadataEditor>(p => p.Add(x => x.Editor, state));

        Assert.Equal("/actor-image/12/thumb", cut.Find(".sme-cast-chip img").GetAttribute("src"));
        Assert.Single(cut.FindAll(".sme-palette-chip"));
    }

    [Fact]
    public void Save_IsDisabledWhileAnOverrideIsInFlight()
    {
        var state = Open(new MovieViewDto { Id = "A", Maker = "Old" });
        sortService.GetResultSourcesAsync(default, default!, default).ReturnsForAnyArgs(new JavinizerSourcesResult(true, [new ScraperSourceResultDto { Source = "dmm", Maker = "S1" }], null));
        sortService.OverrideFieldAsync(default, default!, default!, default!, default).ReturnsForAnyArgs(new TaskCompletionSource<JavinizerFieldOverrideResult>().Task);
        var cut = Render<SortMetadataEditor>(p => p.Add(x => x.Editor, state));
        Assert.False(cut.Find(".sme-save").HasAttribute("disabled"));

        cut.Find(".sme-source-badge[data-field=maker]").Click();
        cut.Find(".sme-source-option").Click();

        Assert.True(cut.Find(".sme-save").HasAttribute("disabled"));
    }

    [Fact]
    public async Task MediaSection_ShowsAFailedMediaActionInsideTheDrawer()
    {
        var session = new WizardSession();
        var result = new BatchFileResultDto { ResultId = "res-1", Movie = new MovieViewDto { Id = "A", CoverUrl = "cover.jpg" } };
        session.JobData = new BatchJobResponseDto { Results = new() { ["/in/a.mp4"] = result } };
        var state = Open(new MovieViewDto { Id = "A" }, session);
        sortService.SetPosterFromUrlAsync(default, default!, default!, default).ReturnsForAnyArgs(new JavinizerPosterFromUrlResult(false, "javinizer-go returned 500: disk full"));
        var review = new ReviewStep(session, sortService);
        var cut = Render<SortMetadataEditor>(p => p.Add(x => x.Editor, state).Add(x => x.Result, result).Add(x => x.Review, review));
        Assert.Empty(cut.FindAll(".sme-media-error"));

        await cut.InvokeAsync(() => cut.Find(".sme-full-cover-btn").Click());
        cut.Render();

        Assert.Contains("disk full", cut.Find(".sme-media .sme-media-error").TextContent);
    }

    [Fact]
    public void Palette_AllApplied_RendersNoPalette()
    {
        var state = Open(new MovieViewDto { Id = "A", Genres = [new() { Name = "VR" }] });
        lookup.GetTopTagsAsync(15, Arg.Any<CancellationToken>()).Returns(["VR"]);
        var cut = Render<SortMetadataEditor>(p => p.Add(x => x.Editor, state));

        Assert.Empty(cut.FindAll(".sme-palette"));
    }

    [Fact]
    public void PreviewPanel_ShowsFolderFileAndOverlongWarning_OrError()
    {
        var state = Open(new MovieViewDto { Id = "A" });
        var cut = Render<SortMetadataEditor>(p => p.Add(x => x.Editor, state));

        state.SetLivePreview(new OrganizePreviewResponseDto { FolderName = new string('あ', 90), VideoFiles = ["/o/A.mp4"] }, null);
        cut.Render();
        Assert.Contains("A.mp4", cut.Find(".sme-preview-file").TextContent);
        Assert.NotNull(cut.Find(".sme-preview-warning"));

        state.SetLivePreview(null, "down");
        cut.Render();
        Assert.Contains("Preview unavailable: down", cut.Find(".sme-preview-error").TextContent);
    }

    [Fact]
    public void SourceBadge_ListsSourceValues_AndApplyingOverridesOnlyThatField()
    {
        var state = Open(new MovieViewDto { Id = "A", Maker = "Old", Title = "Mine" });
        sortService.GetResultSourcesAsync(default, default!, default).ReturnsForAnyArgs(new JavinizerSourcesResult(true,
            [new ScraperSourceResultDto { Source = "dmm", Maker = "S1" }, new ScraperSourceResultDto { Source = "r18dev", Maker = "S-One" }], null));
        sortService.OverrideFieldAsync(Arg.Any<int>(), "res-1", "maker", "r18dev", Arg.Any<CancellationToken>())
            .Returns(new JavinizerFieldOverrideResult(true, new FieldOverrideResponseDto { Movie = new MovieViewDto { Maker = "S-One" }, FieldSources = new() { ["maker"] = "r18dev" } }, null));
        var result = new BatchFileResultDto { ResultId = "res-1", FieldSources = System.Text.Json.JsonDocument.Parse("""{"maker":"dmm"}""").RootElement };

        var cut = Render<SortMetadataEditor>(p => p.Add(x => x.Editor, state).Add(x => x.Result, result));
        cut.WaitForAssertion(() => Assert.Equal("dmm", cut.Find(".sme-source-badge[data-field=maker]").TextContent.Trim()));

        cut.Find(".sme-source-badge[data-field=maker]").Click();
        Assert.NotNull(cut.Find(".sme-override-note"));
        var options = cut.FindAll(".sme-source-option");
        Assert.Equal(2, options.Count);
        options[1].Click();

        cut.WaitForAssertion(() => Assert.Equal("S-One", state.EditingMovie!.Maker));
        Assert.Equal("Mine", state.EditingMovie!.Title);
        Assert.Equal("r18dev", cut.Find(".sme-source-badge[data-field=maker]").TextContent.Trim());
        Assert.Empty(cut.FindAll(".sme-source-panel"));
    }

    [Fact]
    public void SourcePanel_NoSourceHasValue_SaysSo()
    {
        var state = Open(new MovieViewDto { Id = "A" });
        sortService.GetResultSourcesAsync(default, default!, default).ReturnsForAnyArgs(new JavinizerSourcesResult(true, [new ScraperSourceResultDto { Source = "dmm" }], null));
        var cut = Render<SortMetadataEditor>(p => p.Add(x => x.Editor, state));

        cut.Find(".sme-source-badge[data-field=series]").Click();

        Assert.Empty(cut.FindAll(".sme-source-option"));
        Assert.Contains("No source", cut.Find(".sme-source-panel").TextContent);
    }

    [Fact]
    public void Rescrape_DisabledUntilScraperChecked_AndAsksBeforeDiscardingEdits()
    {
        var state = Open(new MovieViewDto { Id = "A" });
        sortService.GetScrapersAsync(default).ReturnsForAnyArgs(new JavinizerScrapersResult(true, [new ScraperInfoDto { Name = "dmm", DisplayTitle = "DMM", Enabled = true }], null));
        sortService.RescrapeWithScrapersAsync(default, default!, default!, default!, default).ReturnsForAnyArgs(new JavinizerRescrapeResult(true, null));
        var cut = Render<SortMetadataEditor>(p => p.Add(x => x.Editor, state));

        cut.WaitForAssertion(() => Assert.True(cut.Find(".sme-rescrape-btn").HasAttribute("disabled")));
        cut.Find(".sme-rescrape-select input[type=checkbox]").Change(true);
        Assert.False(cut.Find(".sme-rescrape-btn").HasAttribute("disabled"));

        state.MarkDirty();
        cut.Find(".sme-rescrape-btn").Click();
        Assert.NotNull(cut.Find(".sme-rescrape-confirm"));
        sortService.DidNotReceiveWithAnyArgs().RescrapeWithScrapersAsync(default, default!, default!, default!, default);

        cut.Find(".sme-rescrape-confirm .btn-warning").Click();
        cut.WaitForAssertion(() => sortService.Received().RescrapeWithScrapersAsync(Arg.Any<int>(), "res-1", "A",
            Arg.Is<IReadOnlyList<string>>(l => l.SequenceEqual(new[] { "dmm" })), Arg.Any<CancellationToken>()));
    }

    [Fact]
    public void MediaSection_RendersGalleryActionsForTheResult()
    {
        var state = Open(new MovieViewDto { Id = "A" });
        var review = new ReviewStep(new WizardSession(), sortService);
        var result = new BatchFileResultDto { ResultId = "res-1", Movie = new MovieViewDto { Id = "A", ScreenshotUrls = ["s1.jpg", "s2.jpg"] } };

        var cut = Render<SortMetadataEditor>(p => p.Add(x => x.Editor, state).Add(x => x.Result, result).Add(x => x.Review, review));

        Assert.Equal(2, cut.FindAll(".sort-screenshot-action-poster").Count);
        Assert.NotNull(cut.Find(".sme-crop-btn"));
        Assert.Empty(cut.FindAll(".sme-full-cover-btn")); // no CoverUrl
    }

    [Fact]
    public async Task FullCoverButton_SetsCoverAsPoster()
    {
        var state = Open(new MovieViewDto { Id = "A" });
        sortService.SetPosterFromUrlAsync(default, default!, default!, default).ReturnsForAnyArgs(new JavinizerPosterFromUrlResult(true, null));
        sortService.PollJobAsync(default, default, default).ReturnsForAnyArgs(new JavinizerBatchJobResult(true, new BatchJobResponseDto(), null));
        var result = new BatchFileResultDto { ResultId = "res-1", Movie = new MovieViewDto { Id = "A", CoverUrl = "cover.jpg" } };
        var session = new WizardSession { JobData = new BatchJobResponseDto { Results = new() { ["/in/a.mp4"] = result } } };
        var review = new ReviewStep(session, sortService);
        var cut = Render<SortMetadataEditor>(p => p.Add(x => x.Editor, state).Add(x => x.Result, result).Add(x => x.Review, review));

        await cut.InvokeAsync(() => cut.Find(".sme-full-cover-btn").Click());

        await sortService.Received().SetPosterFromUrlAsync(Arg.Any<int>(), "res-1", "cover.jpg", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TextFields_BindOnInput_SoCtrlEnterKeepsTheLatestText()
    {
        var state = Open(new MovieViewDto { Id = "A", Description = "old" });
        var cut = Render<SortMetadataEditor>(p => p.Add(x => x.Editor, state));

        // Find + dispatch inside InvokeAsync so no render can land between them (bUnit guidance).
        await cut.InvokeAsync(() => cut.Find("textarea[data-field=description]").Input("typed but not blurred"));
        await cut.InvokeAsync(() => cut.Find("input[data-field=title]").Input("New title"));

        Assert.Equal("typed but not blurred", state.EditingMovie!.Description);
        Assert.Equal("New title", state.EditingMovie.Title);
    }

    [Fact]
    public void DiscardConfirm_ShownWhenConfirming_AndKeepEditingCancelsIt()
    {
        var state = Open(new MovieViewDto { Id = "A" });
        state.MarkDirty();
        var cut = Render<SortMetadataEditor>(p => p.Add(x => x.Editor, state));

        state.RequestClose();
        cut.Render();
        cut.Find(".sme-discard-confirm .sme-keep-editing").Click();

        Assert.False(state.ConfirmingDiscard);
        Assert.NotNull(state.EditingMovie);
    }
}
