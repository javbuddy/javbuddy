using Javbuddy.Models;
using Javbuddy.Services.LocalLibrary;
using Javbuddy.Services.Movies;
using Javbuddy.Tests.TestSupport;
using NSubstitute;

namespace Javbuddy.Tests.Services.Movies;

public class MovieStreamServiceTests : IDisposable
{
    private readonly string tempFolder = Directory.CreateTempSubdirectory("javbuddy-stream-test-").FullName;
    private readonly TestDbContextFactory dbFactory = new();
    private readonly ILocalLibraryClient localLibraryClient = Substitute.For<ILocalLibraryClient>();

    public void Dispose()
    {
        dbFactory.Dispose();
        try
        {
            Directory.Delete(tempFolder, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private string Root => Path.Combine(tempFolder, "library");

    private async Task<int> AddMovieWithFileAsync(string code, string fileName)
    {
        var folder = Directory.CreateDirectory(Path.Combine(Root, code)).FullName;
        await File.WriteAllTextAsync(Path.Combine(folder, fileName), "video");
        localLibraryClient.ResolveMovieFolderPathAsync(code, Arg.Any<CancellationToken>()).Returns(folder);

        await using var db = await dbFactory.CreateDbContextAsync();
        var movie = new Movie { Code = code, Status = MovieStatus.Got };
        movie.MovieFiles.Add(new MovieFile { FileName = fileName, IsPrimary = true });
        db.Movies.Add(movie);
        await db.SaveChangesAsync();
        return movie.Id;
    }

    [Fact]
    public async Task GetVrTypeAsync_ReturnsTheRequestedVersionsFormat_OrThePrimarys()
    {
        var (movieId, primaryId, otherId) = await AddMovieWithTwoVersionsAsync();
        await using (var db = await dbFactory.CreateDbContextAsync())
        {
            db.MovieFiles.Single(f => f.Id == primaryId).VrType = VrFormat.Vr180Sbs;
            db.MovieFiles.Single(f => f.Id == otherId).VrType = VrFormat.FisheyeSbs;
            await db.SaveChangesAsync();
        }
        var service = new MovieStreamService(dbFactory, localLibraryClient);

        Assert.Equal(VrFormat.FisheyeSbs, await service.GetVrTypeAsync(movieId, otherId));
        Assert.Equal(VrFormat.Vr180Sbs, await service.GetVrTypeAsync(movieId, primaryId));
        Assert.Equal(VrFormat.Vr180Sbs, await service.GetVrTypeAsync(movieId));
        Assert.Null(await service.GetVrTypeAsync(movieId + 1));
    }

    [Fact]
    public async Task GetMainFilePathAsync_ReturnsTheMainFile_InsideALibraryRoot()
    {
        var movieId = await AddMovieWithFileAsync("ABC-123", "ABC-123.mp4");
        localLibraryClient.GetRootPathsAsync(Arg.Any<CancellationToken>()).Returns([Root]);

        var path = await new MovieStreamService(dbFactory, localLibraryClient).GetMainFilePathAsync(movieId);

        Assert.Equal(Path.Combine(Root, "ABC-123", "ABC-123.mp4"), path);
    }

    [Fact]
    public async Task GetMainFilePathAsync_IsNull_WhenTheFileResolvesOutsideEveryRoot()
    {
        var movieId = await AddMovieWithFileAsync("ABC-123", "ABC-123.mp4");
        localLibraryClient.GetRootPathsAsync(Arg.Any<CancellationToken>()).Returns([Path.Combine(tempFolder, "other")]);

        Assert.Null(await new MovieStreamService(dbFactory, localLibraryClient).GetMainFilePathAsync(movieId));
    }

    [Fact]
    public async Task GetMainFilePathAsync_IsNull_ForAnUnknownMovieOrAMissingFolder()
    {
        localLibraryClient.GetRootPathsAsync(Arg.Any<CancellationToken>()).Returns([Root]);
        await using (var db = await dbFactory.CreateDbContextAsync())
        {
            db.Movies.Add(new Movie { Id = 5, Code = "GONE-1", Status = MovieStatus.Got });
            await db.SaveChangesAsync();
        }
        var service = new MovieStreamService(dbFactory, localLibraryClient);

        Assert.Null(await service.GetMainFilePathAsync(404));
        Assert.Null(await service.GetMainFilePathAsync(5));
    }

    // A movie with a primary "ABC-123.mp4" and a second version "ABC-123-4K.mkv".
    private async Task<(int MovieId, int PrimaryId, int OtherId)> AddMovieWithTwoVersionsAsync()
    {
        var movieId = await AddMovieWithFileAsync("ABC-123", "ABC-123.mp4");
        await File.WriteAllTextAsync(Path.Combine(Root, "ABC-123", "ABC-123-4K.mkv"), "video");
        await using var db = await dbFactory.CreateDbContextAsync();
        var other = new MovieFile { MovieId = movieId, FileName = "ABC-123-4K.mkv", VersionTag = "4K", Width = 3840, Height = 2160 };
        db.MovieFiles.Add(other);
        await db.SaveChangesAsync();
        return (movieId, db.MovieFiles.Single(f => f.FileName == "ABC-123.mp4").Id, other.Id);
    }

    [Fact]
    public async Task GetFilePathAsync_ReturnsTheRequestedVersion()
    {
        var (movieId, _, otherId) = await AddMovieWithTwoVersionsAsync();
        localLibraryClient.GetRootPathsAsync(Arg.Any<CancellationToken>()).Returns([Root]);

        var path = await new MovieStreamService(dbFactory, localLibraryClient).GetFilePathAsync(movieId, otherId);

        Assert.Equal(Path.Combine(Root, "ABC-123", "ABC-123-4K.mkv"), path);
    }

    [Fact]
    public async Task GetFilePathAsync_IsNull_ForAnotherMoviesFile_OrOneOutsideEveryRoot()
    {
        var (movieId, _, otherId) = await AddMovieWithTwoVersionsAsync();
        var elsewhereId = await AddMovieWithFileAsync("XYZ-999", "XYZ-999.mp4");
        localLibraryClient.GetRootPathsAsync(Arg.Any<CancellationToken>()).Returns([Root]);
        var service = new MovieStreamService(dbFactory, localLibraryClient);

        Assert.Null(await service.GetFilePathAsync(elsewhereId, otherId));
        Assert.Null(await service.GetFilePathAsync(movieId, 404));

        localLibraryClient.GetRootPathsAsync(Arg.Any<CancellationToken>()).Returns([Path.Combine(tempFolder, "other")]);
        Assert.Null(await service.GetFilePathAsync(movieId, otherId));
    }

    [Fact]
    public async Task GetFilePathAsync_IsNull_WhenTheVersionIsGoneFromDisk()
    {
        var (movieId, _, otherId) = await AddMovieWithTwoVersionsAsync();
        File.Delete(Path.Combine(Root, "ABC-123", "ABC-123-4K.mkv"));
        localLibraryClient.GetRootPathsAsync(Arg.Any<CancellationToken>()).Returns([Root]);

        Assert.Null(await new MovieStreamService(dbFactory, localLibraryClient).GetFilePathAsync(movieId, otherId));
    }

    [Fact]
    public async Task GetVersionsAsync_ListsThePrimaryFirst()
    {
        var (movieId, primaryId, otherId) = await AddMovieWithTwoVersionsAsync();

        var versions = await new MovieStreamService(dbFactory, localLibraryClient).GetVersionsAsync(movieId);

        Assert.Equal([primaryId, otherId], versions.Select(v => v.FileId));
        Assert.True(versions[0].IsPrimary);
        Assert.StartsWith("4K · ", versions[1].Label);
    }

    [Fact]
    public void StreamUrl_NamesTheVersion_WhenOneIsGiven()
    {
        Assert.Equal("/api/movies/7/stream", MovieStreamService.StreamUrl(7));
        Assert.Equal("/api/movies/7/stream?fileId=3", MovieStreamService.StreamUrl(7, 3));
    }

    [Theory]
    [InlineData("/media/jav/ABC-123/ABC-123.mp4", true)]
    [InlineData("/media/jav/ABC-123/../ABC-123/ABC-123.mp4", true)]
    [InlineData("/media/jav/../secret/file.mp4", false)]
    [InlineData("/media/jav2/ABC-123/ABC-123.mp4", false)]
    [InlineData("/media/jav", false)]
    public void IsUnderAnyRoot_NormalizesBothSides(string path, bool expected)
    {
        if (OperatingSystem.IsWindows()) return;

        Assert.Equal(expected, MovieStreamService.IsUnderAnyRoot(path, ["", "/media/jav/"]));
    }

    [Theory]
    [InlineData("a.MP4", "video/mp4")]
    [InlineData("a.m4v", "video/mp4")]
    [InlineData("a.mkv", "video/x-matroska")]
    [InlineData("a.webm", "video/webm")]
    [InlineData("a.ts", "video/mp2t")]
    [InlineData("a.xyz", "application/octet-stream")]
    public void ContentType_ByExtension(string path, string expected) =>
        Assert.Equal(expected, MovieStreamService.ContentType(path));
}
