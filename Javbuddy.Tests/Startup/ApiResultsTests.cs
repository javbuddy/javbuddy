using Javbuddy.Services.Api;
using Javbuddy.Startup;

namespace Javbuddy.Tests.Startup;

public class ApiResultsTests
{
    [Theory]
    [InlineData(ApiStatus.Ok, 200)]
    [InlineData(ApiStatus.NotFound, 404)]
    [InlineData(ApiStatus.Conflict, 409)]
    [InlineData(ApiStatus.Invalid, 400)]
    [InlineData(ApiStatus.Upstream, 502)]
    [InlineData(ApiStatus.NotConfigured, 503)]
    [InlineData(ApiStatus.Gone, 410)]
    public void StatusCodeOf_MapsEveryStatus(ApiStatus status, int expected) =>
        Assert.Equal(expected, ApiResults.StatusCodeOf(status));

    [Fact]
    public void Json_UsesCamelCaseAndEnumStrings()
    {
        var json = System.Text.Json.JsonSerializer.Serialize(new { SomeName = ApiStatus.NotConfigured }, ApiResults.Json);

        Assert.Equal("""{"someName":"notConfigured"}""", json);
    }
}
