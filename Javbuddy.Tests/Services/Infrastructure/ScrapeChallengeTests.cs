using System.Net;
using Javbuddy.Services.Infrastructure;

namespace Javbuddy.Tests.Services.Infrastructure;

public class ScrapeChallengeTests
{
    [Theory]
    [InlineData(HttpStatusCode.Forbidden, true, false, "<html>denied</html>", true)]
    [InlineData(HttpStatusCode.ServiceUnavailable, true, false, "", true)]
    [InlineData(HttpStatusCode.Forbidden, false, false, "<html>denied</html>", false)]
    [InlineData(HttpStatusCode.ServiceUnavailable, false, false, "maintenance", false)]
    [InlineData(HttpStatusCode.OK, false, true, "<html></html>", true)]
    [InlineData(HttpStatusCode.Forbidden, false, false, "<html><head><title>Just a moment...</title></head></html>", true)]
    [InlineData(HttpStatusCode.OK, false, false, "<html><title>Just a moment...</title></html>", true)]
    [InlineData(HttpStatusCode.OK, true, false, "<html><title>Performer</title></html>", false)]
    [InlineData(HttpStatusCode.NotFound, true, false, "<html>missing</html>", false)]
    [InlineData(HttpStatusCode.InternalServerError, true, false, null, false)]
    public void IsChallenge_DetectsCloudflareStyleResponses(HttpStatusCode status, bool viaCloudflare, bool cfMitigated, string? body, bool expected) =>
        Assert.Equal(expected, ScrapeChallenge.IsChallenge(status, viaCloudflare, cfMitigated, body));

    [Fact]
    public void IsChallenge_TreatsA403FromCloudflareHeadersAsAChallenge()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.Forbidden);
        response.Headers.Add("cf-ray", "abc-NRT");

        Assert.True(ScrapeChallenge.IsChallenge(response, "denied"));
    }

    [Fact]
    public void IsChallenge_LeavesAPlain403Alone()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.Forbidden);

        Assert.False(ScrapeChallenge.IsChallenge(response, "denied"));
    }

    [Fact]
    public void IsChallenge_ReadsTheCfMitigatedHeader()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.OK);
        response.Headers.Add("cf-mitigated", "challenge");

        Assert.True(ScrapeChallenge.IsChallenge(response, "<html></html>"));
    }
}
