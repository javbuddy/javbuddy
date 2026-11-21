using System.Diagnostics;
using System.Globalization;
using OpenTelemetry.Exporter;

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

    /// <summary>The OTEL_EXPORTER_OTLP_TRACES_* overrides, which AddOtlpExporter doesn't read itself (only
    /// UseOtlpExporter does, and that would also export logs and metrics). Each one set wins over its
    /// OTEL_EXPORTER_OTLP_* counterpart, as the specification has it. Throws on a value that can't be used, so a
    /// typo fails startup instead of silently exporting nowhere.</summary>
    public sealed record TracesExporterOverrides(Uri? Endpoint, OtlpExportProtocol? Protocol, string? Headers, int? TimeoutMilliseconds, OtlpExportCompression? Compression)
    {
        public void ApplyTo(OtlpExporterOptions options)
        {
            // An endpoint set in code is used exactly as given: nothing like /v1/traces is appended.
            if (Endpoint is not null) options.Endpoint = Endpoint;
            if (Protocol is { } protocol) options.Protocol = protocol;
            if (Headers is not null) options.Headers = Headers;
            if (TimeoutMilliseconds is { } timeout) options.TimeoutMilliseconds = timeout;
            if (Compression is { } compression) options.Compression = compression;
        }
    }

    public static TracesExporterOverrides ReadTracesExporterOverrides(IConfiguration configuration)
    {
        string? Value(string suffix) => configuration[$"OTEL_EXPORTER_OTLP_TRACES_{suffix}"] is { Length: > 0 } value ? value.Trim() : null;
        InvalidOperationException Invalid(string suffix, string value, string expected) =>
            new($"OTEL_EXPORTER_OTLP_TRACES_{suffix} is '{value}', which isn't {expected}.");

        var endpoint = Value("ENDPOINT") is { } rawEndpoint
            ? Uri.TryCreate(rawEndpoint, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
                ? uri
                : throw Invalid("ENDPOINT", rawEndpoint, "an absolute http(s) URL")
            : null;
        OtlpExportProtocol? protocol = Value("PROTOCOL") switch
        {
            null => null,
            "grpc" => OtlpExportProtocol.Grpc,
            "http/protobuf" => OtlpExportProtocol.HttpProtobuf,
            var other => throw Invalid("PROTOCOL", other, "'grpc' or 'http/protobuf'"),
        };
        int? timeout = Value("TIMEOUT") is { } rawTimeout
            ? int.TryParse(rawTimeout, NumberStyles.None, CultureInfo.InvariantCulture, out var ms) && ms > 0 ? ms : throw Invalid("TIMEOUT", rawTimeout, "a positive number of milliseconds")
            : null;
        OtlpExportCompression? compression = Value("COMPRESSION") switch
        {
            null => null,
            "gzip" => OtlpExportCompression.GZip,
            "none" => OtlpExportCompression.None,
            var other => throw Invalid("COMPRESSION", other, "'gzip' or 'none'"),
        };
        return new TracesExporterOverrides(endpoint, protocol, Value("HEADERS"), timeout, compression);
    }

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
        // Tags go in at start so a sampler sees them; a link to a span that isn't exported would only dangle.
        var activity = Source.StartActivity(
            name, ActivityKind.Internal, default(ActivityContext), tags: tags.Length == 0 ? null : tags.ToArray(),
            links: previous is { Recorded: true } ? [new ActivityLink(previous.Context)] : null);
        if (activity is null) Activity.Current = previous;
        return activity;
    }

    /// <summary>Records <paramref name="outcome"/> on <paramref name="activity"/>. Anything but "success", "skipped" or
    /// "cancelled" is a failure: the span gets Error status, the standard low-cardinality error.type
    /// (<paramref name="errorType"/>, else the exception's type, else the outcome) and, given one, the exception
    /// as an event (its message and stack trace, which can name files).</summary>
    public static void SetOutcome(this Activity? activity, string outcome, Exception? exception = null, string? errorType = null)
    {
        if (activity is null) return;
        activity.SetTag("javbuddy.outcome", outcome);
        if (outcome is "success" or "skipped" or "cancelled") return;
        activity.SetStatus(ActivityStatusCode.Error, outcome);
        activity.SetTag("error.type", errorType ?? exception?.GetType().FullName ?? outcome);
        if (exception is not null) activity.AddException(exception);
    }

    /// <summary>Starts a span for one image decode-and-encode (reading the source included), so a cache miss's
    /// conversion shows apart from serving a file that's already cached.</summary>
    public static Activity? StartImageConversion(string operation)
    {
        var activity = Source.StartActivity("image convert");
        activity?.SetTag("javbuddy.image.operation", operation);
        return activity;
    }

    /// <summary>Starts a span for one lookup in the r18.dev dump database, which EF Core's instrumentation can't
    /// see because it's read with plain SqliteConnection commands. The span covers the whole lookup (opening the
    /// file and every statement it runs), so it's an internal span of the app rather than a database client
    /// span, which by convention stands for a single call; <paramref name="operation"/> must be a fixed name,
    /// never a code or other per-call value.</summary>
    public static Activity? StartR18DevQuery(string operation)
    {
        var activity = Source.StartActivity($"r18dev {operation}", ActivityKind.Internal);
        activity?.SetTag("javbuddy.r18dev.operation", operation);
        return activity;
    }
}
