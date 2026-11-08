using Javbuddy.Startup;

namespace Javbuddy.Tests.Startup;

public class OriginGuardFilterTests
{
    private const string Own = "http://localhost:8080";

    [Theory]
    [InlineData("http://localhost:8080")]
    [InlineData("HTTP://LOCALHOST:8080")]
    [InlineData("http://localhost:8080/")]
    public void OwnOrigin_IsAllowed(string origin) =>
        Assert.True(OriginGuardFilter.IsAllowed(origin, Own, []));

    [Theory]
    [InlineData("https://evil.example")]
    [InlineData("http://localhost:9999")]
    [InlineData("https://localhost:8080")]
    [InlineData("null")]
    [InlineData("")]
    public void ForeignOrigin_IsRejected(string origin) =>
        Assert.False(OriginGuardFilter.IsAllowed(origin, Own, []));

    [Fact]
    public void ListedOrigin_IsAllowed() =>
        Assert.True(OriginGuardFilter.IsAllowed("chrome-extension://abc", Own, ["https://other.example", "chrome-extension://abc"]));

    [Fact]
    public void ParseAllowed_SplitsOnCommasAndTrims()
    {
        Assert.Equal(["a://x", "b://y"], OriginGuardFilter.ParseAllowed(" a://x , b://y ,, "));
        Assert.Empty(OriginGuardFilter.ParseAllowed(null));
        Assert.Empty(OriginGuardFilter.ParseAllowed(""));
    }
}
