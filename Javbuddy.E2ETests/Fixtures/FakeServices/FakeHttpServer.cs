using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;

namespace Javbuddy.E2ETests.Fixtures.FakeServices;

/// <summary>Base for the fake javinizer-go/Prowlarr/Jellyfin/qBittorrent test doubles the E2E
/// suite points the real app at instead of the real (CI-unavailable) services — each is a
/// minimal ASP.NET Core app on its own real Kestrel socket, the same UseKestrel(0)-on-a-free-port
/// approach <see cref="JavbuddyAppFactory"/> already uses, so the app's real HttpClient calls
/// reach an actual server rather than needing HttpMessageHandler mocking.</summary>
public abstract class FakeHttpServer : IAsyncDisposable
{
    private WebApplication? app;

    public string Address { get; private set; } = string.Empty;

    public async Task StartAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseKestrel().UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();

        app = builder.Build();
        MapEndpoints(app);

        await app.StartAsync();
        Address = app.Urls.First().TrimEnd('/');
    }

    protected abstract void MapEndpoints(WebApplication app);

    /// <summary>Clears arranged responses/captured requests back to defaults. xUnit shares one
    /// fake-server instance across an entire test collection (see E2EFixture), so a test class
    /// should call this (typically from its constructor) rather than assume a clean slate.</summary>
    public abstract void Reset();

    public async ValueTask DisposeAsync()
    {
        if (app is not null)
        {
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }
}
