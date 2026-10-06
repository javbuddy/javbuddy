using Bunit;
using Javbuddy.Components.Shared;
using Javbuddy.Models;
using Javbuddy.Services.Nfo;
using Javbuddy.Services.SceneMedia;
using Javbuddy.Services.Scenes;
using Javbuddy.Services.Tags;
using Javbuddy.Services.Trickplay;
using Javbuddy.Tests.TestSupport;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Javbuddy.Tests.Components.Shared;

/// <summary>What every ClipEditor test needs: real scene, highlight and apex services (with clip
/// tag roll-up) and a real TagService over an in-memory database holding one movie (ABC-123, 1 h), the player
/// module in loose mode, and fakes for chapter import, detection, chapter writing and scene, highlight and apex media.</summary>
public abstract class ClipEditorTestBase : BunitContext
{
    protected const string ModulePath = "./Components/Shared/ClipEditor.razor.js";
    protected const string Selector = "video.cleanup-video";

    protected readonly TestDbContextFactory factory = new();
    protected readonly MovieSceneService sceneService;
    protected readonly MovieHighlightService highlightService;
    protected readonly MovieApexService apexService;
    protected readonly BunitJSModuleInterop module;
    protected readonly ISceneChapterImportService chapterImport = Substitute.For<ISceneChapterImportService>();
    protected readonly ISceneMediaService sceneMedia = Substitute.For<ISceneMediaService>();
    protected readonly IHighlightMediaService highlightMedia = Substitute.For<IHighlightMediaService>();
    protected readonly IApexMediaService apexMedia = Substitute.For<IApexMediaService>();
    protected readonly ISceneMediaService workerMedia = Substitute.For<ISceneMediaService>();
    protected readonly SceneMediaQueue mediaQueue;
    protected readonly int movieId;

    protected ClipEditorTestBase()
    {
        var clipTags = new ClipTagSyncService(factory, Substitute.For<INfoSyncService>());
        sceneService = new MovieSceneService(factory, clipTags: clipTags);
        highlightService = new MovieHighlightService(factory, clipTags: clipTags);
        apexService = new MovieApexService(factory, clipTags: clipTags);
        Services.AddSingleton<IMovieSceneService>(sceneService);
        Services.AddSingleton<IMovieHighlightService>(highlightService);
        Services.AddSingleton<IMovieApexService>(apexService);
        Services.AddSingleton<ITagService>(new TagService(factory, Substitute.For<INfoSyncService>()));
        Services.AddSingleton(chapterImport);
        Services.AddSingleton(Substitute.For<ISceneChapterWriteService>());
        Services.AddSingleton(Substitute.For<ISceneDetectionService>());
        Services.AddSingleton(sceneMedia);
        Services.AddSingleton(highlightMedia);
        Services.AddSingleton(apexMedia);
        // A real queue whose worker resolves workerMedia, so a test can drive a "generated" event.
        var workerServices = new ServiceCollection().AddSingleton(workerMedia).BuildServiceProvider();
        mediaQueue = new SceneMediaQueue(workerServices.GetRequiredService<IServiceScopeFactory>(), NullLogger<SceneMediaQueue>.Instance);
        Services.AddSingleton(mediaQueue);
        module = JSInterop.SetupModule(ModulePath);
        module.Mode = JSRuntimeMode.Loose;

        using var db = factory.CreateDbContext();
        var movie = new Movie { Code = "ABC-123", MediaDurationSeconds = 3600 };
        db.Movies.Add(movie);
        db.SaveChanges();
        movieId = movie.Id;
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            mediaQueue.Dispose();
            factory.Dispose();
        }
    }

    protected IRenderedComponent<ClipEditor> RenderClipEditor(
        bool shortcuts = false,
        Action<IReadOnlyList<SceneItem>>? onScenes = null,
        Action<IReadOnlyList<HighlightItem>>? onHighlights = null,
        Action<IReadOnlyList<ApexItem>>? onApexes = null,
        Action? onMovieTags = null,
        TrickplayLayout? trickplay = null,
        bool hasLocalVideo = true) =>
        Render<ClipEditor>(p => p
            .Add(x => x.Trickplay, trickplay)
            .Add(x => x.HasLocalVideo, hasLocalVideo)
            .Add(x => x.MovieId, movieId)
            .Add(x => x.VideoSelector, Selector)
            .Add(x => x.DurationSeconds, 3600d)
            .Add(x => x.EnableShortcuts, shortcuts)
            .Add(x => x.OnScenesChanged, onScenes ?? (_ => { }))
            .Add(x => x.OnHighlightsChanged, onHighlights ?? (_ => { }))
            .Add(x => x.OnApexesChanged, onApexes ?? (_ => { }))
            .Add(x => x.OnMovieTagsMayHaveChanged, onMovieTags ?? (() => { })));

    protected void PlayerAt(double? seconds) =>
        module.Setup<double?>("currentTime", Selector).SetResult(seconds);

    protected int SeedTag(string name)
    {
        using var db = factory.CreateDbContext();
        var tag = new Tag { Name = name };
        db.Tags.Add(tag);
        db.SaveChanges();
        return tag.Id;
    }
}
