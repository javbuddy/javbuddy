using Javbuddy.Services.Infrastructure;

namespace Javbuddy.Tests.Services.Infrastructure;

public sealed class BestEffortFileTests : IDisposable
{
    private readonly string root = Directory.CreateTempSubdirectory("best-effort-file-").FullName;

    public void Dispose() => Directory.Delete(root, recursive: true);

    [Fact]
    public void TryDeleteFile_ExistingFile_DeletesAndReturnsTrue()
    {
        var path = Path.Combine(root, "a.tmp");
        File.WriteAllText(path, "x");

        Assert.True(BestEffortFile.TryDeleteFile(path));
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void TryDeleteFile_MissingFile_ReturnsFalse() =>
        Assert.False(BestEffortFile.TryDeleteFile(Path.Combine(root, "missing.tmp")));

    [Fact]
    public void TryDeleteFile_Directory_SwallowsFailureAndReturnsFalse() =>
        Assert.False(BestEffortFile.TryDeleteFile(root));

    [Fact]
    public void TryDeleteDirectory_ExistingTree_DeletesRecursively()
    {
        var dir = Path.Combine(root, "d", "nested");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "f"), "x");

        Assert.True(BestEffortFile.TryDeleteDirectory(Path.Combine(root, "d")));
        Assert.False(Directory.Exists(Path.Combine(root, "d")));
    }

    [Fact]
    public void TryDeleteDirectory_Missing_ReturnsFalse() =>
        Assert.False(BestEffortFile.TryDeleteDirectory(Path.Combine(root, "nope")));
}
