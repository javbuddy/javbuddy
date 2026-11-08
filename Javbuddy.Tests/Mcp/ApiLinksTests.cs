using Javbuddy.Services.Api;
using Javbuddy.Services.Api.Contracts;
using Microsoft.AspNetCore.Http;

namespace Javbuddy.Tests.Mcp;

public class ApiLinksTests
{
    private static MovieSummary Summary() =>
        new(1, "ABP-123", "T", "missing", false, "censored", null, null, 0, "none", "/movies/ABP-123");

    [Fact]
    public void AbsoluteUrl_UsesSchemeHostAndPathBase()
    {
        var context = new DefaultHttpContext();
        context.Request.Scheme = "https";
        context.Request.Host = new HostString("jav.local", 8080);
        context.Request.PathBase = "/base";

        Assert.Equal("https://jav.local:8080/base/movies/ABP-123", ApiLinks.AbsoluteUrl(context.Request, "/movies/ABP-123"));
    }

    [Fact]
    public void AbsoluteUrl_WithoutRequest_FallsBackToThePath()
    {
        Assert.Equal("/movies/ABP-123", ApiLinks.WithUrl(null, Summary()).WebUrl);
    }
}
