using Javbuddy.Services.Infrastructure;

namespace Javbuddy.Tests.Services.Infrastructure;

public class DiskSpaceGuardTests
{
    [Fact]
    public void CheckFreeSpace_ImpossibleRequirement_ReturnsMessage() =>
        Assert.Equal("full", DiskSpaceGuard.CheckFreeSpace(Path.GetTempPath(), long.MaxValue / 2, "full"));

    [Fact]
    public void CheckFreeSpace_ZeroBytes_ReturnsNull() =>
        Assert.Null(DiskSpaceGuard.CheckFreeSpace(Path.GetTempPath(), 0, "full"));

    [Fact]
    public void CheckFreeSpace_UnresolvableFolder_ReturnsNull() =>
        Assert.Null(DiskSpaceGuard.CheckFreeSpace("relative-dir", long.MaxValue / 2, "full"));
}
