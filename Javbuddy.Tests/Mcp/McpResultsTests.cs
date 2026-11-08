using Javbuddy.Mcp;
using Javbuddy.Services.Api;
using ModelContextProtocol.Protocol;

namespace Javbuddy.Tests.Mcp;

public class McpResultsTests
{
    private static string TextOf(CallToolResult result) => Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;

    [Fact]
    public void From_Ok_IsJsonTextWithCamelCaseAndStringEnums()
    {
        var result = McpResults.From(ApiResult<StatusHolder>.Ok(new StatusHolder(ApiStatus.NotFound, 3)), v => v);

        Assert.NotEqual(true, result.IsError);
        Assert.Equal("""{"status":"notFound","itemCount":3}""", TextOf(result));
    }

    [Fact]
    public void From_Ok_AppliesTheMap()
    {
        var result = McpResults.From(ApiResult<int>.Ok(4), n => new { doubled = n * 2 });

        Assert.Equal("""{"doubled":8}""", TextOf(result));
    }

    [Theory]
    [InlineData(ApiStatus.NotFound, "No movie with code 'X' in the library.")]
    [InlineData(ApiStatus.Invalid, "Invalid input: bad date")]
    [InlineData(ApiStatus.Conflict, "Conflict: already there")]
    [InlineData(ApiStatus.Upstream, "An integration failed: boom")]
    [InlineData(ApiStatus.NotConfigured, "Not available: not set up")]
    [InlineData(ApiStatus.Gone, "Gone: expired")]
    public void From_Failure_IsErrorResultWithClearMessage(ApiStatus status, string expected)
    {
        var failure = status switch
        {
            ApiStatus.NotFound => ApiResult<int>.NotFound("No movie with code 'X' in the library."),
            ApiStatus.Invalid => ApiResult<int>.Invalid("bad date"),
            ApiStatus.Conflict => ApiResult<int>.Conflict("already there"),
            ApiStatus.Upstream => ApiResult<int>.Upstream("boom"),
            ApiStatus.NotConfigured => ApiResult<int>.NotConfigured("not set up"),
            _ => ApiResult<int>.Gone("expired"),
        };

        var result = McpResults.From(failure, n => n);

        Assert.True(result.IsError);
        Assert.Equal(expected, TextOf(result));
    }

    [Fact]
    public void ErrorMessage_JoinsSeveralValidationErrors()
    {
        var message = McpResults.ErrorMessage(ApiResult<int>.Invalid("a is wrong.", "b is wrong."));

        Assert.Equal("Invalid input: a is wrong. b is wrong.", message);
    }

    [Fact]
    public void Error_IsAnErrorResult()
    {
        var result = McpResults.Error("nope");

        Assert.True(result.IsError);
        Assert.Equal("nope", TextOf(result));
    }

    private sealed record StatusHolder(ApiStatus Status, int ItemCount);
}
