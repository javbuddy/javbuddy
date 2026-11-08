using Javbuddy.Services.LocalLibrary;

namespace Javbuddy.Tests.Services.LocalLibrary;

public class LocalLibraryNamesTests
{
    [Theory]
    [InlineData("/media/jav", "jav")]
    [InlineData("/media/jav/", "jav")]
    [InlineData(@"D:\media\vr", "vr")]
    public void Label_UsesLastFolderName(string root, string expected) =>
        Assert.Equal(expected, LocalLibraryNames.Label(root, [root, "/media/other"]));

    [Fact]
    public void Label_PrefixesParentFolderWhenLastNamesClash()
    {
        string[] roots = ["/mnt/disk1/jav", "/mnt/disk2/jav", "/media/vr"];
        Assert.Equal("disk1/jav", LocalLibraryNames.Label(roots[0], roots));
        Assert.Equal("disk2/jav", LocalLibraryNames.Label(roots[1], roots));
        Assert.Equal("vr", LocalLibraryNames.Label(roots[2], roots));
    }

    [Fact]
    public void Label_FallsBackToFullPathWhenStillAmbiguous()
    {
        string[] roots = ["/a/disk/jav", "/b/disk/jav"];
        Assert.Equal("/a/disk/jav", LocalLibraryNames.Label(roots[0], roots));
    }
}
