using Javbuddy.Services.Common;

namespace Javbuddy.Tests.Services.Common;

public class SetExtensionsTests
{
    [Fact]
    public void CycleInclusion_StepsNeitherThenIncludedThenExcludedThenNeither()
    {
        var included = new HashSet<string>();
        var excluded = new HashSet<string>();

        included.CycleInclusion(excluded, "a");
        Assert.Equal(["a"], included);
        Assert.Empty(excluded);

        included.CycleInclusion(excluded, "a");
        Assert.Empty(included);
        Assert.Equal(["a"], excluded);

        included.CycleInclusion(excluded, "a");
        Assert.Empty(included);
        Assert.Empty(excluded);
    }
}
