using System.Diagnostics;

namespace Javbuddy.Services.Metrics;

/// <summary>Javbuddy's own trace spans plus the rules for which traces are collected at all. Tracing
/// is only switched on when an OTLP endpoint is configured (see AddJavbuddyObservability), so with no
/// collector configured, starting a span from <see cref="Source"/> costs no more than a null check.</summary>
public static class JavbuddyTracing
{
    public const string SourceName = "Javbuddy";

    public static readonly ActivitySource Source = new(SourceName);

    /// <summary>The configuration key OpenTelemetry's instrumentations read their semantic-convention opt-in from.</summary>
    public const string SemconvOptInKey = "OTEL_SEMCONV_STABILITY_OPT_IN";

    /// <summary>The opt-in Javbuddy always runs with: the stable database conventions only.</summary>
    public const string SemconvOptIn = "database";

    // Requests that would only add noise to traces: the Blazor WebSocket (one HTTP request held open
    // for the whole circuit; the interactive work inside it gets its own Blazor component spans),
    // probes, the Prometheus scrape and framework/static files.
    private static readonly string[] ExcludedPathPrefixes =
    [
        "/_blazor",
        "/_framework",
        "/_content",
        "/healthz",
        "/metrics",
        "/lib/",
        "/js/",
        "/studio-logos/",
    ];

    // Scripts and stylesheets are always static files, wherever they sit (e.g. a component's collocated
    // /Components/Shared/ModalShell.{fingerprint}.razor.js).
    private static readonly string[] StaticFileExtensions = [".js", ".css", ".map"];

    // Other static files served straight from the wwwroot root (icons, logo, manifest.json). Only
    // single-segment paths count, so a dynamic image like /trickplay/{movieId}/{identity}/{index}.webp
    // is still traced.
    private static readonly string[] RootStaticFileExtensions = [".webp", ".png", ".ico", ".json"];

    /// <summary>Whether an OTLP endpoint for traces is configured through the standard
    /// OTEL_EXPORTER_OTLP_ENDPOINT or OTEL_EXPORTER_OTLP_TRACES_ENDPOINT variable.</summary>
    public static bool IsOtlpEndpointConfigured(IConfiguration configuration) =>
        !string.IsNullOrWhiteSpace(configuration["OTEL_EXPORTER_OTLP_TRACES_ENDPOINT"])
        || !string.IsNullOrWhiteSpace(configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]);

    /// <summary>OTEL_EXPORTER_OTLP_TRACES_ENDPOINT as a URL used exactly as given (no /v1/traces is
    /// appended), or null when it isn't set or isn't an absolute URL.</summary>
    public static Uri? TracesEndpoint(IConfiguration configuration) =>
        Uri.TryCreate(configuration["OTEL_EXPORTER_OTLP_TRACES_ENDPOINT"], UriKind.Absolute, out var uri) ? uri : null;

    /// <summary>Whether an incoming HTTP request to <paramref name="path"/> gets a trace.</summary>
    public static bool ShouldTraceRequest(string? path)
    {
        if (string.IsNullOrEmpty(path)) return true;

        if (ExcludedPathPrefixes.Any(prefix => path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
            return false;

        if (IsFileTransfer(path)) return false;

        if (StaticFileExtensions.Any(extension => path.EndsWith(extension, StringComparison.OrdinalIgnoreCase)))
            return false;

        return path.LastIndexOf('/') != 0
            || !RootStaticFileExtensions.Any(extension => path.EndsWith(extension, StringComparison.OrdinalIgnoreCase));
    }

    // Video streams and backup downloads: their duration is the time spent sending the file (often whole
    // seconds as a player buffers), not work on the server, so they'd crowd out real slow requests when
    // traces are sorted by duration. The cost is that a stream that's slow to start isn't traced either.
    private static bool IsFileTransfer(string path) =>
        (path.StartsWith("/api/movies/", StringComparison.OrdinalIgnoreCase) && path.EndsWith("/stream", StringComparison.OrdinalIgnoreCase))
        || path.StartsWith("/deovr/stream/", StringComparison.OrdinalIgnoreCase)
        || (path.StartsWith("/api/backups/", StringComparison.OrdinalIgnoreCase) && path.EndsWith("/download", StringComparison.OrdinalIgnoreCase));

    /// <summary>Starts <paramref name="name"/> as the root of its own trace, linked to whatever span was current
    /// (e.g. the click that queued a task). Background work outlives what started it, so as a child it would
    /// hang off a span that ended long before. Only call this from an async method: it clears
    /// Activity.Current, and an async method's changes to it don't leak back to its caller.</summary>
    public static Activity? StartRoot(string name, params ReadOnlySpan<KeyValuePair<string, object?>> tags)
    {
        if (!Source.HasListeners()) return null;

        var previous = Activity.Current;
        Activity.Current = null;
        var activity = Source.StartActivity(
            name, ActivityKind.Internal, default(ActivityContext), tags: null,
            links: previous is null ? null : [new ActivityLink(previous.Context)]);
        if (activity is null)
        {
            Activity.Current = previous;
            return null;
        }
        foreach (var tag in tags) activity.SetTag(tag.Key, tag.Value);
        return activity;
    }

    /// <summary>Records <paramref name="outcome"/> on <paramref name="activity"/> and marks it failed unless it's
    /// "success" or "cancelled".</summary>
    public static void SetOutcome(this Activity? activity, string outcome, string? error = null)
    {
        if (activity is null) return;
        activity.SetTag("javbuddy.outcome", outcome);
        if (outcome is not ("success" or "cancelled" or "skipped")) activity.SetStatus(ActivityStatusCode.Error, error ?? outcome);
    }

    /// <summary>Starts a span for one image decode-and-encode (reading the source included), so a cache miss's
    /// conversion shows apart from serving a file that's already cached.</summary>
    public static Activity? StartImageConversion(string operation)
    {
        var activity = Source.StartActivity("image convert");
        activity?.SetTag("javbuddy.image.operation", operation);
        return activity;
    }

    /// <summary>Starts a span for one query operation against the r18.dev dump database, which EF
    /// Core's instrumentation can't see because it's read with plain SqliteConnection commands. The
    /// span covers the whole operation (opening the file and every statement it runs), and
    /// <paramref name="operation"/> must be a fixed name, never a code or other per-call value.</summary>
    public static Activity? StartR18DevQuery(string operation)
    {
        var activity = Source.StartActivity($"r18dev {operation}", ActivityKind.Client);
        activity?.SetTag("db.system.name", "sqlite");
        activity?.SetTag("db.namespace", "r18dev");
        activity?.SetTag("db.operation.name", operation);
        return activity;
    }
}
