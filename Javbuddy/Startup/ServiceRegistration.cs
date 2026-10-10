using Javbuddy.Services.ActorEnrichment;
using Javbuddy.Services.ActorEnrichment.Sources;
using Javbuddy.Services.Actors;
using Javbuddy.Services.DeoVr;
using Javbuddy.Services.Ffmpeg;
using Javbuddy.Services.HealthChecks;
using Javbuddy.Services.Images;
using Javbuddy.Services.Infrastructure;
using Javbuddy.Services.Javinizer;
using Javbuddy.Services.Jellyfin;
using Javbuddy.Services.LocalLibrary;
using Javbuddy.Services.MediaInfo;
using Javbuddy.Services.MediaServer;
using Javbuddy.Services.Metrics;
using Javbuddy.Services.MinnanoAv;
using Javbuddy.Services.Monitoring;
using Javbuddy.Services.MovieDiscovery;
using Javbuddy.Services.MovieDiscovery.Sources;
using Javbuddy.Services.Movies;
using Javbuddy.Services.Nfo;
using Javbuddy.Services.Prowlarr;
using Javbuddy.Services.QBittorrent;
using Javbuddy.Services.R18Dev;
using Javbuddy.Services.SceneMedia;
using Javbuddy.Services.Scenes;
using Javbuddy.Services.Settings;
using Javbuddy.Services.Statistics;
using Javbuddy.Services.Tags;
using Javbuddy.Services.Tasks;
using Javbuddy.Services.Torrents;
using Javbuddy.Services.Torrents.SortWizard;
using Javbuddy.Services.Trickplay;
using Javbuddy.Services.VideoRepair;
using Javbuddy.Services.VrMerge;
using Javbuddy.Services.Warashi;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;

namespace Javbuddy.Startup;

/// <summary>Program.cs's service registrations, grouped by area and applied in the same order as before.</summary>
public static class ServiceRegistration
{
    public static IServiceCollection AddJavbuddyHttpClients(this IServiceCollection services, IConfiguration configuration)
    {
        // Sent by every outgoing HttpClient registered below unless a specific call overrides it (see
        // R18DevDumpImporter, which needs a browser-like User-Agent to get past r18.dev's Cloudflare —
        // a request that already sets its own User-Agent header wins; ConfigureHttpClient below only
        // fills in the default for requests that didn't set one). Identifying the app is both good
        // practice (most of the APIs we talk to log/rate-limit by User-Agent) and needed outright by
        // APIs that reject the bare, blank default HttpClient sends when nothing is set.
        const string DefaultUserAgent = "Javbuddy/1.0 (+https://github.com/javbuddy/javbuddy)";

        // AddHttpClient(string.Empty), not the parameterless AddHttpClient() — the parameterless overload
        // only registers the factory infrastructure and returns IServiceCollection, not a builder; "" is
        // the actual name IHttpClientFactory uses for CreateClient() calls made with no name.
        services.AddHttpClient(string.Empty)
            .ConfigureHttpClient(client => client.DefaultRequestHeaders.UserAgent.ParseAdd(DefaultUserAgent));
        // .NET's default HttpClient tries to auto-follow redirects to non-http(s) targets and throws
        // deep inside SocketsHttpHandler when it can't parse one as a host (e.g. a magnet: URI) rather
        // than just returning the 3xx response — used by TorrentGrabService to resolve a release's
        // download URL itself, manually following hops so it can stop at a magnet: redirect.
        services.AddHttpClient("NoRedirect")
            .ConfigureHttpClient(client => client.DefaultRequestHeaders.UserAgent.ParseAdd(DefaultUserAgent))
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
        // QBittorrentClient manages its own session cookie explicitly (see AddAuthHeaders) rather than
        // relying on HttpClient's automatic cookie jar — which matters because IHttpClientFactory pools
        // and reuses handler instances (including their internal CookieContainer) across separate
        // CreateClient() calls for a couple of minutes. With cookies left on, a second login within that
        // window silently resends the first login's SID cookie; qBittorrent then treats the client as
        // already authenticated and doesn't send back a new Set-Cookie header at all, which QBittorrentClient
        // was reading as a rejected login. UseCookies = false stops the framework from doing this.
        services.AddHttpClient("QBittorrent")
            .ConfigureHttpClient(client => client.DefaultRequestHeaders.UserAgent.ParseAdd(DefaultUserAgent))
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { UseCookies = false });
        // "Import image from URL" (actor portrait, movie cover, movie extrafanart) fetches a URL the user
        // pasted; its handler refuses local and private addresses so the field can't be used to reach
        // the server itself or its network (see PublicAddressGuard).
        services.AddHttpClient(RemoteImageDownloader.ImportClientName)
            .ConfigureHttpClient(client => client.DefaultRequestHeaders.UserAgent.ParseAdd(DefaultUserAgent))
            .ConfigurePrimaryHttpMessageHandler(PublicAddressGuard.CreateHandler);
        // Backs LocalLibraryClient's short-lived cache of each library root's top-level folder listing —
        // see GetRootFolderIndex for why a poster request must not enumerate a network share.
        services.AddMemoryCache();

        return services;
    }

    public static IServiceCollection AddIntegrationAndMovieServices(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<IJavinizerClient, JavinizerClient>();
        services.AddScoped<IProwlarrClient, ProwlarrClient>();
        services.AddScoped<IJellyfinClient, JellyfinClient>();
        services.AddScoped<IMediaServerClient>(sp => sp.GetRequiredService<IJellyfinClient>());
        services.AddScoped<ILocalLibraryClient, LocalLibraryClient>();
        services.AddSingleton<MediaProbeGate>();
        services.AddScoped<IMediaInfoProber, MediaInfoProber>();
        services.AddScoped<IQBittorrentClient, QBittorrentClient>();
        services.AddScoped<ITorrentGrabService, TorrentGrabService>();
        services.AddScoped<ITorrentQueueService, TorrentQueueService>();
        services.AddScoped<ITorrentHistoryService, TorrentHistoryService>();
        services.AddScoped<ITorrentSortService, TorrentSortService>();
        services.AddScoped<ISortEditorLookupService, SortEditorLookupService>();
        services.AddTransient<TorrentSortWizardState>();
        services.AddScoped<IPathMappingService, PathMappingService>();
        services.AddSingleton<IFfmpegBinaryResolver, FfmpegBinaryResolver>();
        services.AddScoped<IFfmpegClient, FfmpegClient>();
        services.AddScoped<IVrMergeService, VrMergeService>();
        services.AddSingleton<IVrMergeJobTracker, VrMergeJobTracker>();
        services.AddScoped<IVideoRepairService, VideoRepairService>();
        services.AddSingleton<IVideoRepairJobTracker, VideoRepairJobTracker>();
        services.AddScoped<IMovieAddService, MovieAddService>();
        services.AddScoped<IMovieService, MovieService>();
        services.AddScoped<IDeletedMovieService, DeletedMovieService>();
        services.AddScoped<IMovieSceneService, MovieSceneService>();
        services.AddScoped<IMovieHighlightService, MovieHighlightService>();
        services.AddScoped<IApexPlaybackSettingsService, ApexPlaybackSettingsService>();
        services.AddScoped<IMovieApexService, MovieApexService>();
        services.AddScoped<IClipTagSyncService, ClipTagSyncService>();
        services.AddScoped<IActorTagService, ActorTagService>();
        services.AddSingleton<ClipActorRefreshSignal>();
        services.AddSingleton<ClipActorStaleInterceptor>();
        services.AddHostedService<ClipActorRefreshWorker>();
        services.AddSingleton<NfoDriftCheckQueue>();
        services.AddSingleton<INfoDriftCheckQueue>(sp => sp.GetRequiredService<NfoDriftCheckQueue>());
        services.AddHostedService(sp => sp.GetRequiredService<NfoDriftCheckQueue>());
        services.AddScoped<INfoHistoryService, NfoHistoryService>();
        services.AddScoped<ISceneChapterImportService, SceneChapterImportService>();
        services.AddScoped<ISceneMediaService, SceneMediaService>();
        services.AddScoped<IHighlightMediaService, HighlightMediaService>();
        services.AddScoped<IApexMediaService, ApexMediaService>();
        services.AddScoped<ISceneChapterWriteService, SceneChapterWriteService>();
        services.AddScoped<ISceneDetectionService, SceneDetectionService>();
        services.AddScoped<ISceneWallQueryService, SceneWallQueryService>();
        services.AddSingleton<SceneMediaQueue>();
        services.AddHostedService(sp => sp.GetRequiredService<SceneMediaQueue>());
        services.AddScoped<IMovieGridQueryService, MovieGridQueryService>();
        services.AddScoped<IMovieRescanService, MovieRescanService>();
        services.AddScoped<IMovieStreamService, MovieStreamService>();
        services.AddScoped<IDeoVrSettingsService, DeoVrSettingsService>();
        services.AddScoped<IDeoVrGroupService, DeoVrGroupService>();
        services.AddScoped<IDeoVrService, DeoVrService>();
        services.AddScoped<IDeoVrTimelineService, DeoVrTimelineService>();
        // Durable data (actor images, trickplay) lives in one object store under ObjectStore:Path, one
        // key prefix per area.
        services.AddSingleton<IObjectStoreProvider>(sp =>
        {
            var root = FileSystemObjectStore.ResolveRoot(sp.GetRequiredService<IConfiguration>(), sp.GetRequiredService<IWebHostEnvironment>().ContentRootPath);
            return new ObjectStoreProvider(new FileSystemObjectStore(root), root);
        });
        services.AddSingleton<ITrickplayStore, TrickplayStore>();
        services.AddScoped<ITrickplaySettingsService, TrickplaySettingsService>();
        services.AddScoped<ITrickplayService, TrickplayService>();
        services.AddScoped<IHighlightTrickplayService, HighlightTrickplayService>();
        services.AddScoped<ITrickplayGenerator, TrickplayGenerator>();
        services.AddScoped<ITrickplayTrigger, TrickplayTrigger>();
        services.AddSingleton<TrickplayQueue>();
        services.AddSingleton<TrickplayGenerationTracker>();
        services.AddHostedService(sp => sp.GetRequiredService<TrickplayQueue>());
        services.AddSingleton<IMovieCleanupSessionTracker, MovieCleanupSessionTracker>();
        services.AddScoped<IMovieCleanupService, MovieCleanupService>();
        services.AddScoped<IJellyfinMovieScanService, JellyfinMovieScanService>();
        services.AddScoped<IConnectionSettingsSaveService, ConnectionSettingsSaveService>();
        services.AddScoped<IConnectionStatusService, ConnectionStatusService>();
        services.AddScoped<ISystemStatusService, SystemStatusService>();
        services.AddSingleton<LibraryStatisticsCache>();
        services.AddScoped<ILibraryStatisticsService, LibraryStatisticsService>();
        services.AddScoped<IMovieDetailQueryService, MovieDetailQueryService>();
        services.AddScoped<IActorDetailQueryService, ActorDetailQueryService>();
        services.AddScoped<ISearchService, SearchService>();

        return services;
    }

    public static IServiceCollection AddImageServices(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<ILocalImageCache, LocalImageCache>();
        services.AddSingleton<InFlightImageConversions>();
        services.AddSingleton<ImageCacheSizeTracker>();
        services.AddSingleton(ImageUploadSettings.FromConfiguration(configuration));
        services.AddSingleton<IActorImageDataStore, ActorImageDataStore>();
        services.AddScoped<ILocalImageCacheService, LocalImageCacheService>();
        services.AddScoped<IActorImageCacheService, ActorImageCacheService>();
        services.AddScoped<IActorPhotoService, ActorPhotoService>();

        return services;
    }

    public static IServiceCollection AddMetadataServices(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<IWarashiClient, WarashiClient>();
        services.AddScoped<IWarashiSettingsService, WarashiSettingsService>();
        services.AddScoped<IR18DevSettingsService, R18DevSettingsService>();
        services.AddScoped<IMediaInfoSettingsService, MediaInfoSettingsService>();
        services.AddScoped<IMinnanoAvClient, MinnanoAvClient>();
        services.AddScoped<IMinnanoAvSettingsService, MinnanoAvSettingsService>();
        services.AddScoped<IActorMetadataSource, LocalActorMetadataSource>();
        services.AddScoped<IActorMetadataSource, WarashiActorMetadataSource>();
        services.AddScoped<IActorMetadataSource, MinnanoAvActorMetadataSource>();
        services.AddScoped<IActorEnrichmentService, ActorEnrichmentService>();
        // Each logo's size is the bundled PNG's own pixel size, so Discover can reserve its box.
        AddUpTimelyStudio("https://s1s1s1.com", "S1", new StudioLogo("/studio-logos/s1.png", 205, 161), "S1 NO.1 STYLE");
        AddUpTimelyStudio("https://moodyz.com", "Moodyz", new StudioLogo("/studio-logos/moodyz.png", 830, 660), "MOODYZ");
        AddUpTimelyStudio("https://kawaiikawaii.jp", "Kawaii", new StudioLogo("/studio-logos/kawaii.png", 200, 46), "kawaii");
        AddUpTimelyStudio("https://ideapocket.com", "IdeaPocket", new StudioLogo("/studio-logos/ideapocket.png", 209, 38), "Idea Pocket");

        void AddUpTimelyStudio(string baseUrl, string sourceName, StudioLogo logo, string studio) =>
            services.AddScoped<IStudioDiscoverySource>(sp => new UpTimelyDiscoverySource(
                baseUrl,
                sourceName,
                logo,
                studio,
                sp.GetRequiredService<IHttpClientFactory>(),
                sp.GetService<ILogger<UpTimelyDiscoverySource>>()));
        services.AddScoped<IMovieDiscoveryService, MovieDiscoveryService>();
        services.AddScoped<IDiscoverySettingsService, DiscoverySettingsService>();
        services.AddScoped<IActorDiscoveryService, ActorDiscoveryService>();
        services.AddScoped<IActorService, ActorService>();
        services.AddScoped<INfoSyncService, NfoSyncService>();
        services.AddScoped<ITagService, TagService>();
        services.AddScoped<ITagRuleService, TagRuleService>();
        services.AddScoped<IRemotePosterCropService, RemotePosterCropService>();
        services.AddScoped<IMovieCoverCropService, MovieCoverCropService>();
        services.AddScoped<IMovieExtraFanartService, MovieExtraFanartService>();
        services.AddScoped<IImageCacheMaintenanceService, ImageCacheMaintenanceService>();
        services.AddScoped<IDiskSpaceService, DiskSpaceService>();
        services.AddScoped<IR18DevDumpImporter, R18DevDumpImporter>();
        services.AddScoped<IR18DevDumpStore, R18DevDumpStore>();
        services.AddScoped<IR18DevReleaseBrowseService, R18DevReleaseBrowseService>();
        services.AddScoped<IActorFilmographyService, ActorFilmographyService>();
        services.AddScoped<IImageServingService, ImageServingService>();

        return services;
    }

    public static IServiceCollection AddBackgroundServices(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<MovieChangeNotifier>();
        services.AddSingleton<MovieFilterNavigationState>();
        services.AddSingleton<TorrentChangeNotifier>();
        services.AddSingleton<ScheduledTaskChangeNotifier>();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<TaskActivityTracker>();
        // Application-owned execution for user-triggered jobs that must outlive their page;
        // hosted so shutdown cancels in-flight jobs and waits for them.
        services.AddSingleton<BackgroundJobRunner>();
        services.AddHostedService(sp => sp.GetRequiredService<BackgroundJobRunner>());
        services.AddSingleton<ScheduledTaskLauncher>();
        services.AddSingleton<ActorBatchEnrichmentLauncher>();
        services.AddSingleton<NfoDriftPushLauncher>();
        services.AddSingleton<JavbuddyMetrics>();
        services.AddHostedService<MetricsGaugeRefreshService>();

        services.AddScoped<IScheduledTask, JellyfinLinkSyncTask>();
        services.AddScoped<IScheduledTask, ImageCacheTask>();
        services.AddScoped<IScheduledTask, QBittorrentSyncTask>();
        services.AddScoped<IScheduledTask, R18DevImportTask>();
        services.AddScoped<IScheduledTask, LibraryRescanTask>();
        services.AddScoped<IScheduledTask, MovieDiscoveryScanTask>();
        services.AddScoped<IScheduledTask, TrickplayBackfillTask>();
        services.AddScoped<IScheduledTask, LibraryMaintenanceTask>();
        services.AddSingleton<ScheduledTaskCancellationRegistry>();
        services.AddScoped<ScheduledTaskRunner>();
        services.AddScoped<IScheduledTaskOverviewService, ScheduledTaskOverviewService>();
        services.AddHostedService<ScheduledTaskHostedService>();

        return services;
    }

    public static IServiceCollection AddJavbuddyObservability(this IServiceCollection services, IConfiguration configuration)
    {
        // Prometheus-format /metrics endpoint: built-in ASP.NET Core/HttpClient/runtime
        // instrumentation plus JavbuddyMetrics' own app-specific instruments (scheduled tasks, image
        // cache, library size, torrent grabs, VR merge jobs).
        services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService("Javbuddy"))
            .WithMetrics(metrics => metrics
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddRuntimeInstrumentation()
                .AddProcessInstrumentation()
                .AddMeter(JavbuddyMetrics.MeterName)
                // Built into EF Core itself (9.0+) via System.Diagnostics.Metrics — no separate NuGet
                // package needed, unlike OpenTelemetry.Instrumentation.EntityFrameworkCore (which only
                // does tracing, not metrics). Reports active DbContexts, queries, SaveChanges, compiled
                // query cache hit/miss, execution-strategy and optimistic-concurrency failures.
                .AddMeter("Microsoft.EntityFrameworkCore")
                .AddPrometheusExporter());

        // K8s liveness/readiness probes.
        services.AddHealthChecks()
            .AddCheck<DbHealthCheck>("db", tags: ["ready"]);

        return services;
    }
}
