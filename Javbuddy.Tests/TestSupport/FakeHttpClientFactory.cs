namespace Javbuddy.Tests.TestSupport;

/// <summary>Always returns an <see cref="HttpClient"/> wrapping the given handler, regardless of
/// the requested client name — good enough for tests that only care about one client's traffic.</summary>
public sealed class FakeHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
{
    private readonly HttpMessageHandler handler = handler;

    public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
}
