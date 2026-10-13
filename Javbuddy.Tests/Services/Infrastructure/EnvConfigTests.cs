using Javbuddy.Services.Infrastructure;
using Microsoft.Extensions.Configuration;

namespace Javbuddy.Tests.Services.Infrastructure;

public class EnvConfigTests
{
    private static IConfiguration Config(params (string Key, string? Value)[] values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values.Select(v => new KeyValuePair<string, string?>(v.Key, v.Value))).Build();

    [Theory]
    [InlineData("true", true)]
    [InlineData("False", false)]
    [InlineData("yes", null)]
    [InlineData("", null)]
    public void GetBool_ParsesOrFallsBackToNull(string raw, bool? expected)
    {
        Assert.Equal(expected, EnvConfig.GetBool(Config(("X:Flag", raw)), "X:Flag"));
    }

    [Fact]
    public void GetBool_IsNullWhenUnset()
    {
        Assert.Null(EnvConfig.GetBool(Config(), "X:Flag"));
    }

    [Fact]
    public void AllSet_NeedsEveryValueNonBlank()
    {
        Assert.True(EnvConfig.AllSet("a", "b"));
        Assert.False(EnvConfig.AllSet("a", " "));
        Assert.False(EnvConfig.AllSet("a", null));
    }

    [Fact]
    public void GetList_PrefersArrayEntries_TrimmedAndNonBlank()
    {
        var config = Config(("X:Names:0", " One "), ("X:Names:1", ""), ("X:Names:2", "Two"));

        Assert.Equal(["One", "Two"], EnvConfig.GetList(config, "X:Names", _ => ["unused"]));
    }

    [Fact]
    public void GetList_FallsBackToTheScalarParser()
    {
        var config = Config(("X:Names", "a,b"));

        Assert.Equal(["a", "b"], EnvConfig.GetList(config, "X:Names", raw => [.. raw.Split(',')]));
    }

    [Fact]
    public void GetList_IsEmptyWhenUnset()
    {
        Assert.Empty(EnvConfig.GetList(Config(), "X:Names", _ => ["unused"]));
    }
}
