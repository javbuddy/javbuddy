using Javbuddy.Services.Infrastructure;
using Microsoft.AspNetCore.Http;
using NSubstitute;

namespace Javbuddy.Tests.Services.Infrastructure;

file sealed record TestState(string Name = "default", int Count = 0);

public class JsonCookieStateTests
{
    [Fact]
    public void TryLoad_ReturnsNull_WhenHttpContextAccessorIsNull()
    {
        Assert.Null(JsonCookieState.TryLoad<TestState>(null, "test-cookie"));
    }

    [Fact]
    public void TryLoad_ReturnsNull_WhenHttpContextIsNull()
    {
        var accessor = Substitute.For<IHttpContextAccessor>();
        accessor.HttpContext.Returns((HttpContext?)null);

        Assert.Null(JsonCookieState.TryLoad<TestState>(accessor, "test-cookie"));
    }

    [Fact]
    public void TryLoad_ReturnsNull_WhenCookieIsMissing()
    {
        var accessor = Substitute.For<IHttpContextAccessor>();
        accessor.HttpContext.Returns(new DefaultHttpContext());

        Assert.Null(JsonCookieState.TryLoad<TestState>(accessor, "test-cookie"));
    }

    [Fact]
    public void TryLoad_ReturnsNull_WhenCookieIsMalformedJson()
    {
        var accessor = Substitute.For<IHttpContextAccessor>();
        var context = new DefaultHttpContext();
        context.Request.Headers.Cookie = "test-cookie=not-valid-json{{{";
        accessor.HttpContext.Returns(context);

        Assert.Null(JsonCookieState.TryLoad<TestState>(accessor, "test-cookie"));
    }

    [Fact]
    public void TryLoad_RoundTripsValidJson()
    {
        var accessor = Substitute.For<IHttpContextAccessor>();
        var context = new DefaultHttpContext();
        context.Request.Headers.Cookie = "test-cookie=" + Uri.EscapeDataString("""{"Name":"restored","Count":5}""");
        accessor.HttpContext.Returns(context);

        var result = JsonCookieState.TryLoad<TestState>(accessor, "test-cookie");

        Assert.Equal(new TestState("restored", 5), result);
    }

    [Fact]
    public void TryLoad_FallsBackToPropertyDefault_WhenJsonIsMissingAProperty()
    {
        var accessor = Substitute.For<IHttpContextAccessor>();
        var context = new DefaultHttpContext();
        context.Request.Headers.Cookie = "test-cookie=" + Uri.EscapeDataString("""{"Name":"restored"}""");
        accessor.HttpContext.Returns(context);

        var result = JsonCookieState.TryLoad<TestState>(accessor, "test-cookie");

        Assert.Equal(new TestState("restored", 0), result);
    }
}
