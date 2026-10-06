using Javbuddy.Data;
using Javbuddy.E2ETests.Fixtures.FakeServices;
using Javbuddy.E2ETests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;

namespace Javbuddy.E2ETests.Fixtures;

/// <summary>
/// One app instance + one headless Chromium browser + one fake server per external integration,
/// shared across every test in the "E2E" collection (see <see cref="E2ECollection"/>) — all
/// expensive to start, and nothing about them needs to be per-test as long as each test gets its
/// own <see cref="IPage"/> and calls <see cref="ResetFakes"/> to clear prior arrangements.
/// </summary>
public sealed class E2EFixture : IAsyncLifetime
{
    private IPlaywright _playwright = null!;

    public JavbuddyAppFactory App { get; private set; } = null!;
    public IBrowser Browser { get; private set; } = null!;

    public FakeJavinizerServer FakeJavinizer { get; } = new();
    public FakeProwlarrServer FakeProwlarr { get; } = new();
    public FakeJellyfinServer FakeJellyfin { get; } = new();
    public FakeQBittorrentServer FakeQBittorrent { get; } = new();

    public async Task InitializeAsync()
    {
        await Task.WhenAll(
            FakeJavinizer.StartAsync(),
            FakeProwlarr.StartAsync(),
            FakeJellyfin.StartAsync(),
            FakeQBittorrent.StartAsync());

        App = new JavbuddyAppFactory
        {
            // Prowlarr is deliberately left unconfigured here (not env-configured) so its
            // Settings > Connections form stays DB-backed and editable — env config disables
            // that form entirely (see ProwlarrConnectionSection.razor's envConfigured flag),
            // which SettingsConnectionsFlowTests needs to actually drive. Tests that need Prowlarr
            // working seed ProwlarrSettings via DbSeeding.SeedProwlarrSettingsAsync instead.
            IntegrationSettings = new Dictionary<string, string>
            {
                ["Javinizer:BaseUrl"] = FakeJavinizer.Address,
                ["Javinizer:ApiToken"] = FakeJavinizerServer.ApiToken,
                ["Jellyfin:BaseUrl"] = FakeJellyfin.Address,
                ["Jellyfin:ApiKey"] = FakeJellyfinServer.ApiKey,
                // Matches FakeJellyfinServer's default Libraries entry — without a selected
                // library, every Jellyfin lookup (ownership check, MovieDetail's "Jellyfin"
                // refresh) short-circuits to an empty result without contacting the server at
                // all (see JellyfinClient.LookupInSelectedLibrariesAsync).
                ["Jellyfin:SelectedLibraryNames"] = "Movies",
                ["QBittorrent:BaseUrl"] = FakeQBittorrent.Address,
                ["QBittorrent:Username"] = FakeQBittorrentServer.Username,
                ["QBittorrent:Password"] = FakeQBittorrentServer.Password,
                // Never the developer's real object store.
                ["ObjectStore:Path"] = DbSeeding.ObjectStoreRoot,
            },
        };

        // WebApplicationFactory starts the host lazily; force it now so App.ServerAddress is
        // populated before any test navigates to it.
        App.StartServer();

        // A system Chromium opt-in supports distributions outside Playwright's dependency
        // installer (which falls back to apt-get on other distributions). CI leaves this unset and keeps using
        // Playwright's pinned browser build and Linux dependencies.
        var chromiumExecutablePath = Environment.GetEnvironmentVariable("PLAYWRIGHT_CHROMIUM_EXECUTABLE_PATH");
        if (string.IsNullOrWhiteSpace(chromiumExecutablePath))
        {
            if (File.Exists("/usr/sbin/chromium-browser"))
            {
                chromiumExecutablePath = "/usr/sbin/chromium-browser";
            }
            else if (File.Exists("/usr/bin/chromium-browser"))
            {
                chromiumExecutablePath = "/usr/bin/chromium-browser";
            }
            else if (File.Exists("/usr/bin/chromium"))
            {
                chromiumExecutablePath = "/usr/bin/chromium";
            }
            else
            {
                // Downloads the Chromium build Playwright drives on first run (cached under
                // PLAYWRIGHT_BROWSERS_PATH / the default user cache dir afterwards); --with-deps
                // installs the OS packages Chromium needs on Linux and is a harmless no-op elsewhere.
                var installExitCode = Microsoft.Playwright.Program.Main(["install", "--with-deps", "chromium"]);
                if (installExitCode != 0)
                {
                    throw new InvalidOperationException($"Playwright browser install failed with exit code {installExitCode}.");
                }
            }
        }

        _playwright = await Playwright.CreateAsync();
        Browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = true,
            ExecutablePath = chromiumExecutablePath,
        });
    }

    private readonly List<IPage> _openPages = [];

    public async Task<IPage> NewPageAsync()
    {
        await CloseOpenPagesAsync();

        var page = await Browser.NewPageAsync(new BrowserNewPageOptions { BaseURL = App.ServerAddress });
        _openPages.Add(page);
        return page;
    }

    public async Task CloseOpenPagesAsync()
    {
        foreach (var page in _openPages)
        {
            try
            {
                if (page.Context is { } context)
                {
                    await context.CloseAsync();
                }
                else if (!page.IsClosed)
                {
                    await page.CloseAsync();
                }
            }
            catch
            {
                // Ignore disposal errors if the browser or page is already closed
            }
        }

        _openPages.Clear();
    }

    /// <summary>The running app's own <see cref="IDbContextFactory{TContext}"/> — for
    /// <see cref="Support.DbSeeding"/> to seed rows directly, bypassing the UI, exactly as the
    /// app itself accesses the database (see CLAUDE.md's IDbContextFactory rule).</summary>
    public IDbContextFactory<AppDbContext> DbFactory => App.Services.GetRequiredService<IDbContextFactory<AppDbContext>>();

    /// <summary>Clears every fake server's arranged responses/captured requests back to
    /// defaults. Call from a test class's constructor (xUnit constructs the class fresh per test
    /// method even though this fixture instance is shared across the whole collection), so one
    /// test's arrangements can't leak into the next.</summary>
    public void ResetFakes()
    {
        FakeJavinizer.Reset();
        FakeProwlarr.Reset();
        FakeJellyfin.Reset();
        FakeQBittorrent.Reset();
    }

    public async Task DisposeAsync()
    {
        await CloseOpenPagesAsync();

        if (Browser is not null)
        {
            await Browser.DisposeAsync();
        }

        _playwright?.Dispose();

        if (App is not null)
        {
            await App.DisposeAsync();
        }

        await FakeJavinizer.DisposeAsync();
        await FakeProwlarr.DisposeAsync();
        await FakeJellyfin.DisposeAsync();
        await FakeQBittorrent.DisposeAsync();

        foreach (var root in new[] { DbSeeding.PlayableLibraryRoot, DbSeeding.ObjectStoreRoot })
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
