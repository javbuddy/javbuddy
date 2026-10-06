using Javbuddy.E2ETests.Fixtures;

namespace Javbuddy.E2ETests.Flows;

[Collection(E2ECollection.Name)]
public class MetricsEndpointTests(E2EFixture fixture)
{
    [Fact]
    public async Task Metrics_endpoint_reports_javbuddy_as_the_service_name()
    {
        using var client = new HttpClient { BaseAddress = new Uri(fixture.App.ServerAddress) };
        var metrics = await client.GetStringAsync("/metrics");

        Assert.Contains("target_info{", metrics);
        Assert.Contains("service_name=\"Javbuddy\"", metrics);
    }
}
