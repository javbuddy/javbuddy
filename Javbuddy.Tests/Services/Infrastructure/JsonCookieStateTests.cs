using Javbuddy.Services.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.JSInterop;
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

    [Fact]
    public async Task SaveAsync_WritesTheSerializedStateForOneYear()
    {
        var js = Substitute.For<IJSRuntime>();

        await JsonCookieState.SaveAsync(js, "test-cookie", new TestState("saved", 3));

        var call = Assert.Single(js.ReceivedCalls());
        var args = call.GetArguments();
        Assert.Equal("lsJsonCookie.set", args[0]);
        Assert.Equal(["test-cookie", """{"Name":"saved","Count":3}""", 31536000], (object?[])args[1]!);
    }

    [Fact]
    public async Task SaveAsync_IgnoresADisconnectedCircuit()
    {
        await JsonCookieState.SaveAsync(new DisconnectedJs(), "test-cookie", new TestState());
    }

    private sealed class DisconnectedJs : IJSRuntime
    {
        public ValueTask<T> InvokeAsync<T>(string identifier, object?[]? args) =>
            ValueTask.FromException<T>(new JSDisconnectedException("gone"));

        public ValueTask<T> InvokeAsync<T>(string identifier, CancellationToken cancellationToken, object?[]? args) =>
            ValueTask.FromException<T>(new JSDisconnectedException("gone"));
    }
}
