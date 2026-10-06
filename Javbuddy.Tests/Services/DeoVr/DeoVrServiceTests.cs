using System.Text.Json;
using Javbuddy.Models;
using Javbuddy.Services.DeoVr;
using Javbuddy.Services.LocalLibrary;
using Javbuddy.Services.Movies;
using Javbuddy.Services.Trickplay;
using Javbuddy.Tests.TestSupport;
using NSubstitute;

namespace Javbuddy.Tests.Services.DeoVr;

public class DeoVrServiceTests : IDisposable
{
    private const string Base = "https://jb.example.com";

    private readonly TestDbContextFactory dbFactory = new();
    private readonly IDeoVrSettingsService settings = Substitute.For<IDeoVrSettingsService>();
    private readonly ILocalLibraryClient localLibrary = Substitute.For<ILocalLibraryClient>();
    private readonly InMemoryObjectStoreProvider stores = new();
    private int midePrimaryFileId;
    private int mideRifeFileId;

    public void Dispose() => dbFactory.Dispose();

    private DeoVrService Service() =>
        new(dbFactory, settings, new DeoVrGroupService(dbFactory), new MovieGridQueryService(dbFactory), localLibrary, new TrickplayStore(dbFactory, stores));

    private async Task<(Movie Mide, Movie Sivr, Movie Missing)> SeedAsync()
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var mide = new Movie { Code = "MIDE-400", MetaTitle = "Title A", Status = MovieStatus.Got, FileAddedAt = new DateTime(2026, 1, 1), MediaDurationSeconds = 3500 };
        var sivr = new Movie { Code = "SIVR-001", Status = MovieStatus.Got, FileAddedAt = new DateTime(2026, 6, 1), MetaStudio = "S1", MediaDurationSeconds = 1800 };
        var missing = new Movie { Code = "ABC-123", Status = MovieStatus.Missing, MetaCoverUrl = "https://img.example.com/abc.jpg" };
        db.Movies.AddRange(mide, sivr, missing);
        await db.SaveChangesAsync();

        var rife = new MovieFile { MovieId = mide.Id, FileName = "MIDE-400-RIFE.mkv", VersionTag = "RIFE", Width = 1920, Height = 1080, DurationSeconds = 3600.4, FrameRate = 60 };
        var primary = new MovieFile { MovieId = mide.Id, FileName = "MIDE-400.mkv", VersionTag = "Original", IsPrimary = true, Width = 1920, Height = 1080, DurationSeconds = 3600.9 };
        db.MovieFiles.AddRange(rife, primary, new MovieFile { MovieId = sivr.Id, FileName = "SIVR-001.mp4", IsPrimary = true, Width = 3840, Height = 1920, VrType = VrFormat.Vr360Tb });
        db.Scenes.AddRange(
            new Scene { MovieId = mide.Id, StartSeconds = 125.7, Title = "Kiss" },
            new Scene { MovieId = mide.Id, StartSeconds = 0 });
        await db.SaveChangesAsync();
        (midePrimaryFileId, mideRifeFileId) = (primary.Id, rife.Id);
        return (mide, sivr, missing);
    }

    [Fact]
    public async Task GetScenesAsync_ListsEachGroupInOrder_OnlyMoviesWithAFile()
    {
        var (mide, sivr, _) = await SeedAsync();
        await new DeoVrGroupService(dbFactory).SaveAsync(0, "S1 only", new MovieGridFilter(Studios: ["S1"]), "title", false);
        await new DeoVrGroupService(dbFactory).SaveAsync(0, "By title", new MovieGridFilter(), "title", false);

        var response = await Service().GetScenesAsync(Base);

        Assert.Equal(["All movies", "S1 only", "By title"], response.Scenes.Select(s => s.Name));
        Assert.Equal(
            [
                new DeoVrListItem("SIVR-001", 1800, $"{Base}/deovr/movies/{sivr.Id}", $"{Base}/deovr/thumb/{sivr.Id}"),
                new DeoVrListItem("MIDE-400 Title A", 3500, $"{Base}/deovr/movies/{mide.Id}", $"{Base}/deovr/thumb/{mide.Id}"),
            ],
            response.Scenes[0].List);
        Assert.Equal(["SIVR-001"], response.Scenes[1].List.Select(i => i.Title));
        Assert.Equal(["MIDE-400 Title A", "SIVR-001"], response.Scenes[2].List.Select(i => i.Title));
    }

    [Fact]
    public async Task ATitleThatAlreadyStartsWithTheCode_IsNotPrefixedAgain()
    {
        var (mide, _, _) = await SeedAsync();
        await using (var db = await dbFactory.CreateDbContextAsync())
        {
            var movie = await db.Movies.FindAsync(mide.Id);
            movie!.MetaTitle = "MIDE-400 Title A";
            await db.SaveChangesAsync();
        }

        Assert.Equal("MIDE-400 Title A", (await Service().GetVideoAsync(mide.Id, Base))!.Title);
    }

    [Fact]
    public async Task GetScenesAsync_WithNoMovies_HasTheSeededAllMoviesListEmpty()
    {
        var response = await Service().GetScenesAsync(Base);

        var scene = Assert.Single(response.Scenes);
        Assert.Equal(("All movies", 0), (scene.Name, scene.List.Count));
    }

    [Fact]
    public async Task GetScenesAsync_FollowsTheGroups_IncludingAllMoviesBeingMovedOrDeleted()
    {
        await SeedAsync();
        var groups = new DeoVrGroupService(dbFactory);
        await groups.SaveAsync(0, "S1 only", new MovieGridFilter(Studios: ["S1"]), "title", false);
        await groups.MoveAsync(DeoVrGroup.AllMovies.Id, 1);
        Assert.Equal(["S1 only", "All movies"], (await Service().GetScenesAsync(Base)).Scenes.Select(s => s.Name));

        await groups.DeleteAsync(DeoVrGroup.AllMovies.Id);
        Assert.Equal(["S1 only"], (await Service().GetScenesAsync(Base)).Scenes.Select(s => s.Name));

        await groups.DeleteAsync((await groups.ListAsync())[0].Id);
        Assert.Empty((await Service().GetScenesAsync(Base)).Scenes);
    }

    [Fact]
    public async Task GetVideoAsync_HasEachVersionAsAnEncoding_PrimaryFirst_AndScenesAsChapters()
    {
        var (mide, _, _) = await SeedAsync();

        var video = await Service().GetVideoAsync(mide.Id, Base);

        Assert.NotNull(video);
        Assert.Equal((mide.Id, "MIDE-400 Title A", 3600), (video.Id, video.Title, video.VideoLength));
        Assert.Equal(("flat", "off", true), (video.ScreenType, video.StereoMode, video.Is3D));
        Assert.Equal($"{Base}/deovr/thumb/{mide.Id}", video.ThumbnailUrl);
        Assert.Equal(2, video.Encodings.Count);
        Assert.StartsWith("Original", video.Encodings[0].Name, StringComparison.Ordinal);
        Assert.StartsWith("RIFE", video.Encodings[1].Name, StringComparison.Ordinal);
        Assert.Equal(new DeoVrVideoSource(1080, $"{Base}/deovr/stream/{mide.Id}/{midePrimaryFileId}/MIDE-400.mkv"), Assert.Single(video.Encodings[0].VideoSources));
        Assert.Equal(new DeoVrVideoSource(1080, $"{Base}/deovr/stream/{mide.Id}/{mideRifeFileId}/MIDE-400-RIFE.mkv"), Assert.Single(video.Encodings[1].VideoSources));
        Assert.Equal([new DeoVrTimestamp(0, "Scene 1"), new DeoVrTimestamp(125, "Kiss")], video.TimeStamps);
    }

    [Fact]
    public async Task GetVideoAsync_ProjectsByThePrimaryVersionsVrFormat()
    {
        var (_, sivr, _) = await SeedAsync();

        var video = await Service().GetVideoAsync(sivr.Id, Base);

        Assert.NotNull(video);
        // From the stored format, not the 2:1 frame (which alone would read as 180° side-by-side).
        Assert.Equal(("sphere", "tb", true), (video.ScreenType, video.StereoMode, video.Is3D));
        Assert.Equal(1800, video.VideoLength);
        Assert.Empty(video.TimeStamps);
    }

    [Fact]
    public async Task Is3d_IsAlwaysTrueInTheJson_EvenForFlatVideo()
    {
        var (mide, _, _) = await SeedAsync();

        var json = JsonSerializer.Serialize(await Service().GetVideoAsync(mide.Id, Base));

        Assert.Contains("\"is3d\":true", json, StringComparison.Ordinal);
        Assert.Contains("\"screenType\":\"flat\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetVideoAsync_IsNullForAMovieWithoutFilesOrUnknown()
    {
        var (_, _, missing) = await SeedAsync();

        Assert.Null(await Service().GetVideoAsync(missing.Id, Base));
        Assert.Null(await Service().GetVideoAsync(9999, Base));
    }

    [Fact]
    public async Task TheDocuments_UseDeoVrsPropertyNames()
    {
        var (mide, _, _) = await SeedAsync();
        var service = Service();

        var list = JsonSerializer.Serialize(await service.GetScenesAsync(Base));
        var video = JsonSerializer.Serialize(await service.GetVideoAsync(mide.Id, Base));

        foreach (var name in new[] { "\"scenes\"", "\"name\"", "\"list\"", "\"title\"", "\"videoLength\"", "\"video_url\"", "\"thumbnailUrl\"" })
        {
            Assert.Contains(name, list, StringComparison.Ordinal);
        }
        foreach (var name in new[] { "\"is3d\"", "\"screenType\"", "\"stereoMode\"", "\"encodings\"", "\"videoSources\"", "\"resolution\"", "\"url\"", "\"timeStamps\"", "\"ts\"" })
        {
            Assert.Contains(name, video, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task GetVideoAsync_OffersTheTimeline_OnlyWhenThePrimaryFileHasATrickplaySet()
    {
        var (mide, _, _) = await SeedAsync();
        Assert.DoesNotContain("timelinePreview", JsonSerializer.Serialize(await Service().GetVideoAsync(mide.Id, Base)), StringComparison.Ordinal);

        // A set for another version doesn't count: DeoVR's one timeline is the primary file's.
        var rifeIdentity = TrickplayIdentity.For("MIDE-400-RIFE.mkv", 3600.4, 1920, 1080)!;
        await new TrickplayStore(dbFactory, stores).SaveAsync("MIDE-400", new TrickplaySet { Identity = rifeIdentity, FileName = "MIDE-400-RIFE.mkv" }, []);
        Assert.Null((await Service().GetVideoAsync(mide.Id, Base))!.TimelinePreview);

        var identity = TrickplayIdentity.For("MIDE-400.mkv", 3600.9, 1920, 1080)!;
        await new TrickplayStore(dbFactory, stores).SaveAsync("MIDE-400", new TrickplaySet { Identity = identity, FileName = "MIDE-400.mkv" }, []);
        var video = await Service().GetVideoAsync(mide.Id, Base);

        Assert.Equal($"{Base}/deovr/timeline/{mide.Id}/{identity}/4096_timelinePreview341x195.jpg", video!.TimelinePreview);
        Assert.Contains("\"timelinePreview\"", JsonSerializer.Serialize(video), StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetThumbnailAsync_PrefersLocalFanart_ThenTheRemoteCover()
    {
        var (mide, _, missing) = await SeedAsync();
        localLibrary.ResolveFirstExistingLocalFilePathAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns((string?)null);
        localLibrary.ResolveFirstExistingLocalFilePathAsync("MIDE-400", Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns("/lib/MIDE-400/fanart.jpg");

        Assert.Equal(new DeoVrThumbnail("/lib/MIDE-400/fanart.jpg", null), await Service().GetThumbnailAsync(mide.Id));
        Assert.Equal(new DeoVrThumbnail(null, "https://img.example.com/abc.jpg"), await Service().GetThumbnailAsync(missing.Id));
        Assert.Null(await Service().GetThumbnailAsync(9999));
    }

    [Fact]
    public async Task GetThumbnailAsync_NeverOffersWebPFanart()
    {
        var (mide, _, _) = await SeedAsync();

        await Service().GetThumbnailAsync(mide.Id);

        await localLibrary.Received().ResolveFirstExistingLocalFilePathAsync("MIDE-400",
            Arg.Is<IReadOnlyList<string>>(names => !names.Any(n => n.EndsWith(".webp", StringComparison.OrdinalIgnoreCase))),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetEnabledSettingsAsync_IsNullWhileOff()
    {
        settings.GetEffectiveAsync(Arg.Any<CancellationToken>()).Returns(new DeoVrSettings { Enabled = false }, new DeoVrSettings { Enabled = true });

        Assert.Null(await Service().GetEnabledSettingsAsync());
        Assert.NotNull(await Service().GetEnabledSettingsAsync());
    }
}
