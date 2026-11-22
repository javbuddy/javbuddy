using Javbuddy.Services.Infrastructure;

namespace Javbuddy.Tests.Services.Infrastructure;

public class PathContainmentTests
{
    [Theory]
    [InlineData("/lib", "/lib/a/b.mp4", true)]
    [InlineData("/lib/", "/lib/a/b.mp4", true)]
    [InlineData("/lib", "/lib/a/../a/b.mp4", true)]
    [InlineData("/lib", "/lib/../secret/b.mp4", false)]
    [InlineData("/lib", "/lib/a/../../b.mp4", false)]
    [InlineData("/lib", "/lib2/a.mp4", false)]
    [InlineData("/lib/ABC-123", "/lib/ABC-1234/x.jpg", false)]
    [InlineData("/lib", "/lib", false)]
    [InlineData("/lib", "/lib/", true)]
    [InlineData("/lib", "/other/lib/a.mp4", false)]
    [InlineData("/lib", "/LIB/a.mp4", false)]
    public void IsInside_NormalizesBothSidesAndRequiresAWholeSegment(string folder, string path, bool expected)
    {
        if (OperatingSystem.IsWindows()) return;

        Assert.Equal(expected, PathContainment.IsInside(folder, path));
    }

    [Fact]
    public void IsInside_RelativeInputsResolveAgainstTheCurrentDirectory()
    {
        var folder = Path.Combine(Path.GetTempPath(), "jb-pc-" + Guid.NewGuid().ToString("N"));

        Assert.True(PathContainment.IsInside(folder, Path.Combine(folder, "x", "..", "y.jpg")));
        Assert.False(PathContainment.IsInside(folder, folder + "2" + Path.DirectorySeparatorChar + "y.jpg"));
    }

    [Theory]
    [InlineData("/media/jav/ABC-123/ABC-123.mp4", true)]
    [InlineData("/media/jav/ABC-123/../ABC-123/ABC-123.mp4", true)]
    [InlineData("/media/jav/../secret/file.mp4", false)]
    [InlineData("/media/jav2/ABC-123/ABC-123.mp4", false)]
    [InlineData("/media/jav", false)]
    public void IsInsideAny_SkipsBlankRootsAndNormalizesBothSides(string path, bool expected)
    {
        if (OperatingSystem.IsWindows()) return;

        Assert.Equal(expected, PathContainment.IsInsideAny(path, ["", "  ", "/media/jav/"]));
    }

    [Fact]
    public void IsInsideAny_MatchesWhicheverRootContainsThePath()
    {
        if (OperatingSystem.IsWindows()) return;

        Assert.True(PathContainment.IsInsideAny("/b/x.mp4", ["/a", "/b"]));
        Assert.False(PathContainment.IsInsideAny("/c/x.mp4", ["/a", "/b"]));
        Assert.False(PathContainment.IsInsideAny("/a/x.mp4", []));
    }

    [Fact]
    public void IsInside_DoesNotResolveSymlinks()
    {
        if (OperatingSystem.IsWindows()) return;

        var root = Path.Combine(Path.GetTempPath(), "jb-pc-" + Guid.NewGuid().ToString("N"));
        var outside = Path.Combine(root, "outside");
        var lib = Path.Combine(root, "lib");
        Directory.CreateDirectory(outside);
        Directory.CreateDirectory(lib);
        try
        {
            Directory.CreateSymbolicLink(Path.Combine(lib, "link"), outside);

            // Lexical check by design: a link inside the folder counts as inside.
            Assert.True(PathContainment.IsInside(lib, Path.Combine(lib, "link", "a.jpg")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
