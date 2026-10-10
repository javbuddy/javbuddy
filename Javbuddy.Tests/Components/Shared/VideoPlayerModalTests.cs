using Bunit;
using Javbuddy.Components.Shared;
using Javbuddy.Models;
using Javbuddy.Services.Jellyfin;
using Javbuddy.Services.Movies;
using Javbuddy.Services.Scenes;
using Javbuddy.Services.Tags;
using Javbuddy.Services.Trickplay;
using Javbuddy.Tests.TestSupport;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Javbuddy.Tests.Components.Shared;

public class VideoPlayerModalTests : BunitContext
{
    private readonly IJellyfinClient jellyfinClient = Substitute.For<IJellyfinClient>();
    private readonly IMovieStreamService streamService = Substitute.For<IMovieStreamService>();
    private readonly ITrickplayService trickplayService = Substitute.For<ITrickplayService>();
    private readonly TestDbContextFactory factory = new();

    public VideoPlayerModalTests()
    {
        Services.AddSingleton(jellyfinClient);
        Services.AddSingleton(streamService);
        Services.AddSingleton(trickplayService);
        Services.AddSingleton(new TrickplayGenerationTracker());
        streamService.GetMainFilePathAsync(Arg.Any<int>()).Returns("/library/ABC-123/ABC-123.mp4");
        Services.AddSingleton<IMovieSceneService>(new MovieSceneService(factory));
        Services.AddSingleton<IMovieDetailQueryService>(new MovieDetailQueryService(factory));
        Services.AddSingleton<IMovieService>(new MovieService(factory));
        Services.AddSingleton<IMovieHighlightService>(new MovieHighlightService(factory));
        Services.AddSingleton<IMovieApexService>(new MovieApexService(factory));
        Services.AddSingleton<IActorTagService>(new ActorTagService(factory));
        Services.AddSingleton(Substitute.For<Javbuddy.Services.Tags.ITagService>());
        Services.AddSingleton(Substitute.For<ISceneChapterImportService>());
        Services.AddSingleton(Substitute.For<ISceneChapterWriteService>());
        Services.AddSingleton(Substitute.For<ISceneDetectionService>());
        Services.AddSingleton(Substitute.For<Javbuddy.Services.SceneMedia.ISceneMediaService>());
        Services.AddSingleton(Substitute.For<Javbuddy.Services.SceneMedia.IHighlightMediaService>());
        Services.AddSingleton(Substitute.For<Javbuddy.Services.SceneMedia.IApexMediaService>());
        Services.AddSingleton(new Javbuddy.Services.SceneMedia.SceneMediaQueue(Substitute.For<IServiceScopeFactory>(), Microsoft.Extensions.Logging.Abstractions.NullLogger<Javbuddy.Services.SceneMedia.SceneMediaQueue>.Instance));
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing) factory.Dispose();
    }

    [Fact]
    public void WithoutMovieId_HasNoSceneSidePanel()
    {

        var cut = Render<VideoPlayerModal>(p => p
            .Add(x => x.Show, true));

        Assert.Empty(cut.FindAll(".video-player-side-panel"));
        Assert.DoesNotContain("M", cut.FindAll(".shortcuts-info-keys kbd").Select(k => k.TextContent));
    }

    [Fact]
    public async Task WithMovieId_EnablesAndListsTheSceneShortcuts()
    {
        int movieId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var movie = new Movie { Code = "ABC-123", MediaDurationSeconds = 180 };
            db.Movies.Add(movie);
            await db.SaveChangesAsync();
            movieId = movie.Id;
        }

        var cut = Render<VideoPlayerModal>(p => p
            .Add(x => x.Show, true)
            .Add(x => x.MovieId, movieId)
            .Add(x => x.ShowEditor, true));

        // M / Shift+M in Movie Detail's player too, not just on the Review card, and H / Shift+H
        // and A for highlights and apexes — all on the one ClipEditor.
        Assert.True(cut.FindComponent<ClipEditor>().Instance.EnableShortcuts);
        var keys = cut.FindAll(".shortcuts-info-keys").Select(k => k.QuerySelector("kbd")!.TextContent);
        Assert.Equal(["M", "Shift+M", "H", "Shift+H", "A", "←", ",", "Esc"], keys);
    }

    [Fact]
    public async Task WithMovieId_ShowsSceneEditorAndStartsAtRequestedTime()
    {
        int movieId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var movie = new Movie { Code = "ABC-123", MediaDurationSeconds = 180 };
            db.Movies.Add(movie);
            await db.SaveChangesAsync();
            movieId = movie.Id;
        }
        await new MovieSceneService(factory).AddSceneAsync(movieId, 90, null, "Bath");
        await new MovieHighlightService(factory).AddHighlightAsync(movieId, 100, 120, "Climax");
        IReadOnlyList<SceneItem>? reported = null;

        var cut = Render<VideoPlayerModal>(p => p
            .Add(x => x.Show, true)
            .Add(x => x.MovieId, movieId)
            .Add(x => x.ShowEditor, true)
            .Add(x => x.DurationSeconds, 180d)
            .Add(x => x.StartAtSeconds, 90d)
            .Add(x => x.OnScenesChanged, (IReadOnlyList<SceneItem> s) => reported = s));

        Assert.Equal($"/api/movies/{movieId}/stream#t=90", cut.Find("video.video-player-video").GetAttribute("src"));
        Assert.Contains("Bath", cut.Find(".video-player-side-panel .scene-editor-row").TextContent);
        // No trickplay here, so the scenes are drawn on the scrub bar without thumbnails.
        Assert.Single(cut.FindAll(".video-player-column .scrub-bar-track .scene-segment"));
        // Highlights are listed below the scenes and drawn as a track above them.
        Assert.Contains("Climax", cut.Find(".video-player-side-panel .highlight-editor-row").TextContent);
        Assert.Single(cut.FindAll(".video-player-column .scrub-bar-track-wrap .highlight-segment"));
        Assert.Equal("Bath", Assert.Single(reported!).Title);
    }

    [Fact]
    public void WithoutShowEditor_PlaysWithoutTheEditor_ButDrawsTheGivenMarkers()
    {
        var cut = Render<VideoPlayerModal>(p => p
            .Add(x => x.Show, true)
            .Add(x => x.MovieId, 1)
            .Add(x => x.DurationSeconds, 180d)
            .Add(x => x.Scenes, [new SceneItem(1, 1, "Bath", "Bath", 90, null, 180)])
            .Add(x => x.Highlights, [new HighlightItem(4, 1, "Climax", "Climax", 100, 120, false, 0)]));

        // Play opens just the player; the editor is behind Movie Detail's Edit actions.
        Assert.Empty(cut.FindAll(".video-player-side-panel"));
        Assert.Empty(cut.FindComponents<ClipEditor>());
        Assert.DoesNotContain("M", cut.FindAll(".shortcuts-info-keys kbd").Select(k => k.TextContent));
        Assert.Single(cut.FindAll(".video-player-column .scrub-bar-track .scene-segment"));
        Assert.Single(cut.FindAll(".video-player-column .scrub-bar-track-wrap .highlight-segment"));
        // H jumps between the highlights while the editor's H isn't there to start one.
        Assert.Equal([100d], cut.FindComponent<VideoSeekKeys>().Instance.HighlightTargets);
    }

    [Fact]
    public void WithTheEditor_HAndAMarkInsteadOfJumping()
    {
        var cut = Render<VideoPlayerModal>(p => p
            .Add(x => x.Show, true)
            .Add(x => x.MovieId, 1)
            .Add(x => x.ShowEditor, true)
            .Add(x => x.Highlights, [new HighlightItem(4, 1, "Climax", "Climax", 100, 120, false, 0)])
            .Add(x => x.Apexes, [new ApexItem(2, 1, 300, [])]));

        var keys = cut.FindComponent<VideoSeekKeys>().Instance;
        Assert.Empty(keys.HighlightTargets);
        Assert.Empty(keys.ApexTargets);
        var actions = cut.FindAll(".shortcuts-info-action").Select(a => a.TextContent).ToList();
        Assert.Contains(KeyboardShortcut.MarkApex.Action, actions);
        Assert.DoesNotContain(KeyboardShortcut.JumpApex.Action, actions);
    }

    [Theory]
    [InlineData(90d, 100.5, "#t=90,100.5")]
    [InlineData(0d, 5d, "#t=0,5")]
    public void WithAnEnd_TheFragmentCarriesIt_SoTheBrowserPausesThere(double start, double end, string fragment)
    {

        var cut = Render<VideoPlayerModal>(p => p
            .Add(x => x.Show, true)
            .Add(x => x.MovieId, 1)
            .Add(x => x.StartAtSeconds, start)
            .Add(x => x.EndAtSeconds, end));

        Assert.Equal("/api/movies/1/stream" + fragment, cut.Find("video.video-player-video").GetAttribute("src"));
    }

    [Fact]
    public void WhenShowFalse_RendersNothing()
    {
        var cut = Render<VideoPlayerModal>(p => p
            .Add(x => x.Show, false)
            .Add(x => x.Title, "Sample Movie")
            .Add(x => x.Code, "ABC-123")
            .Add(x => x.MovieId, 1));

        Assert.Empty(cut.Markup.Trim());
    }

    [Fact]
    public void WhenShowTrue_StreamsTheLocalFile_RendersVideoElementAndHeader()
    {

        var cut = Render<VideoPlayerModal>(p => p
            .Add(x => x.Show, true)
            .Add(x => x.Title, "Sample Movie")
            .Add(x => x.Code, "ABC-123")
            .Add(x => x.MovieId, 1)
            .Add(x => x.ExternalWebUrl, "http://jellyfin:8096/web/index.html#!/details?id=item-123"));

        Assert.Contains("ABC-123", cut.Find(".video-player-modal-code").TextContent);
        Assert.Contains("Sample Movie", cut.Find(".video-player-modal-name").TextContent);

        var video = cut.Find("video.video-player-video");
        Assert.Equal("/api/movies/1/stream", video.GetAttribute("src"));

        var externalLink = cut.Find("a.video-player-external-btn");
        Assert.Equal("http://jellyfin:8096/web/index.html#!/details?id=item-123", externalLink.GetAttribute("href"));
    }

    [Fact]
    public void WhenShowTrue_TrickplayAvailable_RendersScrubBar()
    {
        var trickplay = new TrickplayLayout(320, 180, 5, 5, 100, 10000, 1000, "http://jellyfin/tile_{index}.jpg");
        trickplayService.GetAsync(1)
            .Returns(trickplay);

        var cut = Render<VideoPlayerModal>(p => p
            .Add(x => x.Show, true)
            .Add(x => x.Title, "Sample Movie")
            .Add(x => x.Code, "ABC-123")
            .Add(x => x.MovieId, 1));

        cut.WaitForAssertion(() =>
        {
            var scrubBar = cut.FindComponent<ScrubBar>();
            Assert.NotNull(scrubBar);
            Assert.Equal(trickplay, scrubBar.Instance.Trickplay);
        });
    }

    [Fact]
    public void WhenVrToggleClicked_ActivatesVrModeAndRendersVrViewer()
    {

        var cut = Render<VideoPlayerModal>(p => p
            .Add(x => x.Show, true)
            .Add(x => x.Title, "VR Sample")
            .Add(x => x.Code, "VR-001")
            .Add(x => x.MovieId, 1));

        Assert.Empty(cut.FindComponents<VrViewer>());

        var vrBtn = cut.Find(".video-player-vr-toggle");
        vrBtn.Click();

        var vrViewer = Assert.Single(cut.FindComponents<VrViewer>());
        Assert.Equal("video.video-player-video", vrViewer.Instance.VideoSelector);
        Assert.Contains("video-player-vr-toggle-on", cut.Find(".video-player-vr-toggle").ClassList);
    }

    [Fact]
    public void WhenCloseButtonClicked_FiresOnClose()
    {

        var closed = false;
        var cut = Render<VideoPlayerModal>(p => p
            .Add(x => x.Show, true)
            .Add(x => x.Title, "Sample Movie")
            .Add(x => x.Code, "ABC-123")
            .Add(x => x.MovieId, 1)
            .Add(x => x.OnClose, () => closed = true));

        var closeBtn = cut.Find(".video-player-modal-close-btn");
        closeBtn.Click();

        Assert.True(closed);
    }

    [Fact]
    public void WhenEscapeKeyPressed_FiresOnClose()
    {

        var closed = false;
        var cut = Render<VideoPlayerModal>(p => p
            .Add(x => x.Show, true)
            .Add(x => x.Title, "Sample Movie")
            .Add(x => x.Code, "ABC-123")
            .Add(x => x.MovieId, 1)
            .Add(x => x.OnClose, () => closed = true));

        var backdrop = cut.Find(".video-player-modal-backdrop");
        backdrop.KeyDown(new KeyboardEventArgs { Key = "Escape" });

        Assert.True(closed);
    }

    [Fact]
    public void WhenNoLocalFile_RendersErrorFallback()
    {
        streamService.GetMainFilePathAsync(1).Returns((string?)null);

        var cut = Render<VideoPlayerModal>(p => p
            .Add(x => x.Show, true)
            .Add(x => x.Title, "Sample Movie")
            .Add(x => x.Code, "ABC-123")
            .Add(x => x.MovieId, 1)
            .Add(x => x.ExternalWebUrl, "http://jellyfin:8096/web/index.html#!/details?id=item-123"));

        Assert.Empty(cut.FindAll("video"));
        var error = cut.Find(".video-player-error");
        Assert.Contains("not available locally", error.TextContent);
        var fallbackLink = cut.Find(".video-player-error a");
        Assert.Equal("http://jellyfin:8096/web/index.html#!/details?id=item-123", fallbackLink.GetAttribute("href"));
    }
}
