namespace Javbuddy.Tests.TestSupport;

/// <summary>An <see cref="HttpMessageHandler"/> whose response (or exception) is fully
/// controlled by the test, so client code can be exercised without a real network call.</summary>
public sealed class FakeHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond = respond;

    public HttpRequestMessage? LastRequest { get; private set; }

    public static FakeHttpMessageHandler ReturningStatus(System.Net.HttpStatusCode statusCode, string? content = null) =>
        new((_, _) => Task.FromResult(new HttpResponseMessage(statusCode)
        {
            Content = content is null ? null : new StringContent(content)
        }));

    public static FakeHttpMessageHandler Throwing(Exception exception) =>
        new((_, _) => throw exception);

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        LastRequest = request;
        return await respond(request, cancellationToken);
    }
}
