using Javbuddy.Services.LocalLibrary;

namespace Javbuddy.Tests.Services.LocalLibrary;

public sealed class ActorImagePathContainmentTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "jb-containment-" + Guid.NewGuid().ToString("N"));

    public ActorImagePathContainmentTests()
    {
        Directory.CreateDirectory(Path.Combine(_root, "ABC-123"));
        Directory.CreateDirectory(Path.Combine(_root, "ABC-1234"));
        File.WriteAllText(Path.Combine(_root, "ABC-123", "a.jpg"), "x");
        File.WriteAllText(Path.Combine(_root, "ABC-123", "a.nfo"), "x");
        File.WriteAllText(Path.Combine(_root, "ABC-1234", "x.jpg"), "x");
    }

    public void Dispose() => Directory.Delete(_root, true);

    private string Folder => Path.Combine(_root, "ABC-123");

    [Fact]
    public void InsideImage_IsAccepted() =>
        Assert.True(LocalLibraryClient.IsImageFileInside(Folder, Path.Combine(Folder, "a.jpg")));

    [Fact]
    public void SiblingSharingNamePrefix_IsRejected() =>
        Assert.False(LocalLibraryClient.IsImageFileInside(Folder, Path.Combine(Folder, "..", "ABC-1234", "x.jpg")));

    [Fact]
    public void NonImageExtension_IsRejected() =>
        Assert.False(LocalLibraryClient.IsImageFileInside(Folder, Path.Combine(Folder, "a.nfo")));

    [Fact]
    public void MissingFile_IsRejected() =>
        Assert.False(LocalLibraryClient.IsImageFileInside(Folder, Path.Combine(Folder, "nope.jpg")));
}
