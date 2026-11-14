using Javbuddy.Startup;
using Microsoft.AspNetCore.Http;

namespace Javbuddy.Tests.Startup;

public class MediaEndpointsCacheControlTests
{
    private static HttpContext Request(string query)
    {
        var http = new DefaultHttpContext();
        http.Request.QueryString = new QueryString(query);
        return http;
    }

    [Fact]
    public void VersionedPoster_IsServedFromCacheWhileRevalidatingInTheBackground()
    {
        Assert.Equal("private, max-age=300, stale-while-revalidate=86400", MediaEndpoints.PosterCacheControl(Request("?v=eee5c9ba")));
    }

    [Fact]
    public void UnversionedPoster_KeepsTheShortMaxAge()
    {
        Assert.Equal("private, max-age=300", MediaEndpoints.PosterCacheControl(Request("")));
    }

    [Fact]
    public void ImmutableCacheControl_IsALongLivedImmutableEntry()
    {
        Assert.Equal("private, max-age=31536000, immutable", MediaEndpoints.ImmutableCacheControl);
    }
}
