using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;

namespace Javbuddy.E2ETests.Fixtures;

/// <summary>
/// Hosts the real Javbuddy app for a Playwright-driven browser to navigate to. A browser is a
/// separate OS process, so it can't reach WebApplicationFactory's default in-memory TestServer
/// transport — <see cref="WebApplicationFactory{TEntryPoint}.UseKestrel(int)"/> below makes it
/// bind a real Kestrel socket on a free loopback port instead.
/// Backed by an isolated, throwaway SQLite file (never a developer's real database) that
/// Program.cs's startup <c>db.Database.Migrate()</c> creates on first use.
/// </summary>
public sealed class JavbuddyAppFactory : WebApplicationFactory<Program>
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"javbuddy-e2e-{Guid.NewGuid():N}.db");

    public JavbuddyAppFactory() => UseKestrel(0);

    public string ServerAddress => ClientOptions.BaseAddress.ToString();

    /// <summary>Config keys (e.g. "Javinizer:BaseUrl") to point the app's integration clients at
    /// the fake test-double servers instead of leaving them unconfigured — set this (via object
    /// initializer, before <see cref="WebApplicationFactory{TEntryPoint}.StartServer"/> runs) to
    /// the same env-var-shaped keys each client reads via IConfiguration (see
    /// Services/&lt;Integration&gt;/*Client.cs's *EnvConfig classes).</summary>
    public IReadOnlyDictionary<string, string> IntegrationSettings { get; init; } = new Dictionary<string, string>();

    // WebApplicationFactory defaults to (and static-web-asset serving of blazor.web.js etc. only
    // works under) the "Development" environment — see the long comment on BlankIntegrationSettings
    // below for why that's a hazard here and how it's neutralized without changing environment.
    private static readonly IReadOnlyDictionary<string, string> BlankIntegrationSettings = new Dictionary<string, string>
    {
        ["Javinizer:BaseUrl"] = "",
        ["Javinizer:ExternalUrl"] = "",
        ["Javinizer:ApiToken"] = "",
        ["Prowlarr:BaseUrl"] = "",
        ["Prowlarr:ExternalUrl"] = "",
        ["Prowlarr:ApiKey"] = "",
        ["Jellyfin:BaseUrl"] = "",
        ["Jellyfin:ExternalUrl"] = "",
        ["Jellyfin:ApiKey"] = "",
        ["Jellyfin:SelectedLibraryNames"] = "",
        ["QBittorrent:BaseUrl"] = "",
        ["QBittorrent:ExternalUrl"] = "",
        ["QBittorrent:Username"] = "",
        ["QBittorrent:Password"] = "",
        ["QBittorrent:Category"] = "",
        ["LocalLibrary:RootPaths"] = "",
        ["R18Dev:DumpSourceOverride"] = "",
    };

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:Default", $"Data Source={_dbPath}");

        // The "Development" environment (required above for static assets) loads a developer's own
        // gitignored appsettings.Development.json, which can hold real integration credentials and paths.
        // Blank every one of those keys first so the suite never inherits or acts on them; IntegrationSettings
        // below then overrides the ones this run points at a fake.
        //
        // Array settings (Jellyfin:SelectedLibraryNames, LocalLibrary:RootPaths) bind as child keys
        // ("...:0"), and UseSetting on the parent key can't remove children a lower-priority provider
        // defined, so those child keys are blanked too.
        builder.ConfigureAppConfiguration((context, config) =>
        {
            var arrayShapedKeys = new[] { "Jellyfin:SelectedLibraryNames", "LocalLibrary:RootPaths" };
            var childOverrides = arrayShapedKeys
                .SelectMany(key => context.Configuration.GetSection(key).GetChildren())
                .ToDictionary(child => child.Path, _ => (string?)"");
            if (childOverrides.Count > 0)
            {
                config.AddInMemoryCollection(childOverrides);
            }
        });

        foreach (var (key, value) in BlankIntegrationSettings)
        {
            builder.UseSetting(key, value);
        }
        foreach (var (key, value) in IntegrationSettings)
        {
            builder.UseSetting(key, value);
        }
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        // Microsoft.Data.Sqlite pools the native connection behind each "Data Source=..." string
        // rather than closing it when a DbContext is disposed, so the app shutdown above doesn't
        // release the file handle on its own — deleting it straight after would otherwise fail
        // with "The process cannot access the file" on Windows.
        SqliteConnection.ClearAllPools();
        File.Delete(_dbPath);
        File.Delete(_dbPath + "-wal");
        File.Delete(_dbPath + "-shm");
    }
}
