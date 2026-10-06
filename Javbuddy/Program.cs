using Javbuddy.Components;
using Javbuddy.Data;
using Javbuddy.Services.Scenes;
using Javbuddy.Services.Tasks;
using Javbuddy.Startup;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;

// The local-library scan (LibraryImport.razor, and the image-cache tasks) does thousands of
// small blocking filesystem calls in a row — over a network share these each cost a real
// round-trip, and the CLR's ThreadPool only grows by about one thread per ~1s once starved, so a
// long scan can leave every *other* concurrent request (including another browser tab's SignalR
// keep-alive) queued long enough to look unresponsive or time out. Raising the minimum worker
// thread count means the pool doesn't need to slow-ramp under that kind of sudden, sustained
// blocking-I/O load in the first place.
ThreadPool.SetMinThreads(Math.Max(Environment.ProcessorCount * 4, 32), Environment.ProcessorCount);

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
//
// CircuitOptions' defaults (retain up to 100 disconnected circuits for 3 minutes each) assume many
// concurrent users. A rendering exception is fatal to a circuit, and until its retention window
// elapses a disconnected circuit keeps pinning its component tree and query results in memory. This
// is a single-/few-user app, so bounding both numbers keeps stale tabs or a client-side bug from
// accumulating dead circuits' memory, without hurting a real reconnect (a brief blip resolves in
// well under 30s).
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents(options =>
    {
        options.DisconnectedCircuitMaxRetained = 5;
        options.DisconnectedCircuitRetentionPeriod = TimeSpan.FromSeconds(30);
    });

// Lets Movies.razor read its poster-display-options cookie during OnInitializedAsync (both the
// prerendered request and the interactive circuit's own reconnect request carry it) instead of
// waiting for a JS-interop round trip after the circuit connects.
builder.Services.AddHttpContextAccessor();

// Blazor Server's default circuit timeout (30s ClientTimeoutInterval / 15s KeepAliveInterval) is
// tuned for a responsive server: a thread-pool hiccup during a long local-library scan (see
// ThreadPool.SetMinThreads above) can delay a keep-alive pong past it and silently dispose the
// circuit, so it just stops receiving live updates. More tolerant timeouts give transient load room
// to recover.
//
// MaximumReceiveMessageSize's default (32KB) also needs raising: a component that persists
// prerendered state via PersistentComponentState hands it back as one protected, base64-encoded
// message when the circuit starts, which inflates it well past its raw JSON (a page of 60 movies was
// ~19KB raw, ~34KB encoded). SignalR rejects the oversized message before any component code runs,
// so the circuit died on every reconnect with no server-side exception ("Connection disconnected ...
// Server returned an error on close."). 128KB is headroom for real-world titles without being
// unbounded.
builder.Services.Configure<Microsoft.AspNetCore.SignalR.HubOptions>(options =>
{
    options.ClientTimeoutInterval = TimeSpan.FromSeconds(60);
    options.KeepAliveInterval = TimeSpan.FromSeconds(20);
    options.MaximumReceiveMessageSize = 128 * 1024;
});

// Compresses the prerendered HTML document and other dynamic text responses —
// static assets are already served pre-compressed by MapStaticAssets, which the middleware leaves
// alone because they carry their own Content-Encoding. The default MIME list covers HTML/JSON/
// text but not video, images or text/event-stream, so media streaming and range requests are
// untouched; SignalR's WebSocket transport bypasses HTTP response bodies entirely. EnableForHttps
// is a deliberate opt-in: BREACH needs an attacker able to inject chosen text into a response
// alongside a secret and observe its size many times over, which is not a realistic threat for
// this single-user self-hosted app.
builder.Services.AddResponseCompression(options => options.EnableForHttps = true);

// DataProtection keys default to an ephemeral path inside the container — every restart would
// generate a fresh key ring and invalidate antiforgery tokens on any already-rendered page.
// DataProtection:KeysPath (env "DataProtection__KeysPath") lets the container image point this
// at the same persisted volume as the DB/image cache; unset (the local dev default) leaves the
// framework's own default behavior untouched.
var dataProtectionKeysPath = builder.Configuration["DataProtection:KeysPath"];
if (!string.IsNullOrWhiteSpace(dataProtectionKeysPath))
{
    builder.Services.AddDataProtection()
        .PersistKeysToFileSystem(new DirectoryInfo(dataProtectionKeysPath));
}

// Database. Provider is configurable so we can move from SQLite (dev) to
// Postgres later without touching call sites. Default is SQLite.
var provider = builder.Configuration.GetValue("Database:Provider", "Sqlite");
var connectionString = builder.Configuration.GetConnectionString("Default")
    ?? "Data Source=Javbuddy.db";

builder.Services.AddDbContextFactory<AppDbContext>((sp, options) =>
{
    switch (provider)
    {
        case "Sqlite":
            options.UseSqlite(connectionString, sqliteOptions =>
                sqliteOptions.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery));
            break;
        // To enable Postgres later: add the Npgsql.EntityFrameworkCore.PostgreSQL
        // package and uncomment the line below.
        // case "Postgres":
        //     options.UseNpgsql(connectionString);
        //     break;
        default:
            throw new InvalidOperationException(
                $"Unsupported Database:Provider '{provider}'.");
    }
    // Keeps the stored effective actors' stale flag in step with every tracked write.
    options.AddInterceptors(sp.GetRequiredService<ClipActorStaleInterceptor>());
});

builder.Services
    .AddJavbuddyHttpClients(builder.Configuration)
    .AddIntegrationAndMovieServices(builder.Configuration)
    .AddImageServices(builder.Configuration)
    .AddMetadataServices(builder.Configuration)
    .AddBackgroundServices(builder.Configuration)
    .AddJavbuddyObservability(builder.Configuration);

var app = builder.Build();

// Apply pending migrations and clean up any orphaned task runs from prior shutdowns/crashes at startup.
using (var scope = app.Services.CreateScope())
{
    var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
    using var db = factory.CreateDbContext();
    db.Database.Migrate();

    var runner = scope.ServiceProvider.GetRequiredService<ScheduledTaskRunner>();
    await runner.CleanUpOrphanedRunsAsync();
}

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
app.UseResponseCompression();
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);

app.UseAntiforgery();

// Unauthenticated like the image-cache endpoints below — single-user, self-hosted app with no
// existing auth layer, scraped by the user's own Prometheus/Grafana infra.
app.MapPrometheusScrapingEndpoint();

// K8s probes. Liveness never runs a check (Predicate: false) so a briefly slow/
// locked SQLite file can't trigger a restart loop; readiness runs only checks tagged "ready"
// (currently just DB connectivity) and correctly takes the pod out of rotation instead.
app.MapHealthChecks("/healthz/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/healthz/ready", new HealthCheckOptions { Predicate = c => c.Tags.Contains("ready") });

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.MapMediaEndpoints();
app.MapDeoVrEndpoints();

app.Run();
