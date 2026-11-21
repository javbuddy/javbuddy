using System.Diagnostics;
using Javbuddy.Services.Metrics;
using Javbuddy.Tests.TestSupport;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Exporter;

namespace Javbuddy.Tests.Services.Metrics;

public class JavbuddyTracingTests
{
    private static IConfiguration Config(params (string Key, string? Value)[] values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values.ToDictionary(v => v.Key, v => v.Value))
            .Build();

    [Theory]
    [InlineData("OTEL_EXPORTER_OTLP_ENDPOINT", "http://collector:4317", true)]
    [InlineData("OTEL_EXPORTER_OTLP_TRACES_ENDPOINT", "http://vt:10428/insert/opentelemetry/v1/traces", true)]
    [InlineData("OTEL_EXPORTER_OTLP_ENDPOINT", " ", false)]
    [InlineData("OTEL_SERVICE_NAME", "javbuddy", false)]
    public void IsOtlpEndpointConfigured_OnlyForAnEndpointVariable(string key, string value, bool expected)
    {
        Assert.Equal(expected, JavbuddyTracing.IsOtlpEndpointConfigured(Config((key, value))));
    }

    [Fact]
    public void TracesExporterOverrides_ApplyEveryTracesVariable_AndKeepTheEndpointAsGiven()
    {
        var config = Config(
            ("OTEL_EXPORTER_OTLP_TRACES_ENDPOINT", "http://vt:10428/insert/opentelemetry/v1/traces"),
            ("OTEL_EXPORTER_OTLP_TRACES_PROTOCOL", "http/protobuf"),
            ("OTEL_EXPORTER_OTLP_TRACES_HEADERS", "Authorization=Bearer abc"),
            ("OTEL_EXPORTER_OTLP_TRACES_TIMEOUT", "2500"),
            ("OTEL_EXPORTER_OTLP_TRACES_COMPRESSION", "gzip"));
        var options = new OtlpExporterOptions();

        JavbuddyTracing.ReadTracesExporterOverrides(config).ApplyTo(options);

        Assert.Equal(new Uri("http://vt:10428/insert/opentelemetry/v1/traces"), options.Endpoint);
        Assert.Equal(OtlpExportProtocol.HttpProtobuf, options.Protocol);
        Assert.Equal("Authorization=Bearer abc", options.Headers);
        Assert.Equal(2500, options.TimeoutMilliseconds);
        Assert.Equal(OtlpExportCompression.GZip, options.Compression);
    }

    [Fact]
    public void TracesExporterOverrides_NoneSet_LeaveTheOptionsAlone()
    {
        var options = new OtlpExporterOptions { Protocol = OtlpExportProtocol.Grpc, TimeoutMilliseconds = 1234 };
        var endpoint = options.Endpoint;

        JavbuddyTracing.ReadTracesExporterOverrides(Config()).ApplyTo(options);

        Assert.Equal(endpoint, options.Endpoint);
        Assert.Equal(OtlpExportProtocol.Grpc, options.Protocol);
        Assert.Equal(1234, options.TimeoutMilliseconds);
    }

    [Theory]
    [InlineData("ENDPOINT", "not a url")]
    [InlineData("ENDPOINT", "ftp://collector/traces")]
    [InlineData("PROTOCOL", "http/json")]
    [InlineData("TIMEOUT", "-5")]
    [InlineData("COMPRESSION", "zstd")]
    public void TracesExporterOverrides_UnusableValue_FailsWithTheVariableName(string suffix, string value)
    {
        var error = Assert.Throws<InvalidOperationException>(() =>
            JavbuddyTracing.ReadTracesExporterOverrides(Config(($"OTEL_EXPORTER_OTLP_TRACES_{suffix}", value))));

        Assert.Contains($"OTEL_EXPORTER_OTLP_TRACES_{suffix}", error.Message);
    }

    [Theory]
    [InlineData("success", null, null)]
    [InlineData("skipped", null, null)]
    [InlineData("cancelled", null, null)]
    [InlineData("failure", null, "failure")]
    [InlineData("timeout", "timeout", "timeout")]
    public void SetOutcome_OnlyFailuresGetErrorStatusAndErrorType(string outcome, string? errorType, string? expectedErrorType)
    {
        using var capture = new TraceCapture(JavbuddyTracing.SourceName);
        using var span = JavbuddyTracing.Source.StartActivity("work")!;

        span.SetOutcome(outcome, errorType: errorType);

        Assert.Equal(outcome, span.GetTagItem("javbuddy.outcome"));
        Assert.Equal(expectedErrorType, span.GetTagItem("error.type"));
        Assert.Equal(expectedErrorType is null ? ActivityStatusCode.Unset : ActivityStatusCode.Error, span.Status);
    }

    [Fact]
    public void SetOutcome_WithAnException_RecordsItsTypeAndAnExceptionEvent()
    {
        using var capture = new TraceCapture(JavbuddyTracing.SourceName);
        using var span = JavbuddyTracing.Source.StartActivity("work")!;

        span.SetOutcome("failure", new InvalidOperationException("boom"));

        Assert.Equal("System.InvalidOperationException", span.GetTagItem("error.type"));
        Assert.Equal("failure", span.StatusDescription);
        Assert.Contains(span.Events, e => e.Name == "exception");
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/movies/MIDE-400")]
    [InlineData("/image-cache/MIDE-400/poster/thumb")]
    [InlineData("/trickplay/12/abc/3.webp")]
    [InlineData("/api/movies/12/subtitles/0")]
    [InlineData("/scene-image/4/preview")]
    [InlineData("/deovr")]
    public void ShouldTraceRequest_PagesAndDynamicEndpoints_AreTraced(string path)
    {
        Assert.True(JavbuddyTracing.ShouldTraceRequest(path));
    }

    [Theory]
    [InlineData("/_blazor")]
    [InlineData("/_blazor/negotiate")]
    [InlineData("/_framework/blazor.web.js")]
    [InlineData("/healthz/live")]
    [InlineData("/healthz/ready")]
    [InlineData("/metrics")]
    [InlineData("/lib/bootstrap/dist/css/bootstrap.min.css")]
    [InlineData("/studio-logos/s1.webp")]
    [InlineData("/app.abc123.css")]
    [InlineData("/Javbuddy.abc123.styles.css")]
    [InlineData("/Components/Shared/ModalShell.pxbkdbp836.razor.js")]
    [InlineData("/favicon.webp")]
    [InlineData("/manifest.json")]
    [InlineData("/api/movies/12/stream")]
    [InlineData("/deovr/stream/12/3/movie.mp4")]
    [InlineData("/api/backups/javbuddy-2026.zip/download")]
    public void ShouldTraceRequest_ProbesScrapesStaticFilesAndFileTransfers_AreNotTraced(string path)
    {
        Assert.False(JavbuddyTracing.ShouldTraceRequest(path));
    }

    [Theory]
    [InlineData("Microsoft.AspNetCore.SignalR.Server", "Microsoft.AspNetCore.Components.Server.ComponentHub/OnRenderCompleted", true)]
    [InlineData("Microsoft.AspNetCore.SignalR.Server", "Microsoft.AspNetCore.Components.Server.ComponentHub/EndInvokeJSFromDotNet", true)]
    [InlineData("Microsoft.AspNetCore.SignalR.Server", "Microsoft.AspNetCore.Components.Server.ComponentHub/UpdateRootComponents", false)]
    [InlineData("Microsoft.AspNetCore.SignalR.Server", "Microsoft.AspNetCore.Components.Server.ComponentHub/BeginInvokeDotNetFromJS", false)]
    [InlineData("Other.Source", "Something/OnRenderCompleted", false)]
    public void BlazorTraceProcessor_DropsOnlyTheHubAcknowledgements(string source, string name, bool expected)
    {
        Assert.Equal(expected, BlazorTraceProcessor.IsAcknowledgement(source, name));
    }

    [Fact]
    public void BlazorTraceProcessor_RenamesTheCircuitSpanToAFixedName()
    {
        using var source = new ActivitySource("Microsoft.AspNetCore.Components.Server.Circuits");
        using var capture = new TraceCapture(source.Name);
        using var circuit = source.StartActivity("Circuit KI4wNCSLaD9EaruUJCZ")!;

        new BlazorTraceProcessor().OnStart(circuit);

        Assert.Equal("Circuit", circuit.DisplayName);
    }

    [Fact]
    public async Task OutgoingHttpSpan_RedactsQueryValues()
    {
        // Jellyfin takes its API key in the query string, so this guards it never reaching a trace.
        using var capture = new TraceCapture("System.Net.Http");
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };

        // Nothing listens on port 1, so the request fails without leaving the machine; the span still records the URL.
        await Assert.ThrowsAnyAsync<Exception>(() => client.GetAsync("http://127.0.0.1:1/System/Info?apikey=secret-key"));

        var span = Assert.Single(capture.Spans);
        var url = Assert.IsType<string>(span.GetTagItem("url.full"));
        Assert.DoesNotContain("secret-key", url);
        Assert.StartsWith("http://127.0.0.1:1/System/Info", url);
    }

    [Fact]
    public async Task StartRoot_IsARootWithALinkAndParentsWhatRunsUnderIt()
    {
        var stopped = new List<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name is JavbuddyTracing.SourceName or "Javbuddy.Tests.StartRoot",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = a => { lock (stopped) stopped.Add(a); },
        };
        ActivitySource.AddActivityListener(listener);
        using var testSource = new ActivitySource("Javbuddy.Tests.StartRoot");
        using var click = testSource.StartActivity("click")!;

        await RunTaskAsync();

        Activity root, child;
        lock (stopped)
        {
            root = stopped.Single(a => a.DisplayName == "task StartRootTest");
            child = stopped.Single(a => a.DisplayName == "child of StartRootTest");
        }
        Assert.Null(root.ParentId);
        Assert.NotEqual(click.TraceId, root.TraceId);
        Assert.Equal(click.SpanId, Assert.Single(root.Links).Context.SpanId);
        Assert.Equal(root.SpanId, child.ParentSpanId);
        Assert.Same(click, Activity.Current);

        static async Task RunTaskAsync()
        {
            using var root = JavbuddyTracing.StartRoot("task StartRootTest");
            await Task.Yield();
            using var child = JavbuddyTracing.Source.StartActivity("child of StartRootTest");
        }
    }

    [Theory]
    [InlineData("Microsoft.AspNetCore.Components.Server.ComponentHub/UpdateRootComponents", "/movies/{RouteCode}", "Render /movies/{RouteCode}")]
    [InlineData("Microsoft.AspNetCore.Components.Server.ComponentHub/OnLocationChanged", "/actors", "Navigate /actors")]
    [InlineData("Microsoft.AspNetCore.Components.Server.ComponentHub/BeginInvokeDotNetFromJS", "/actors", null)]
    [InlineData("GET /actors", "/actors", null)]
    public void HubSpanName_NamesRendersAndNavigationsAfterTheRoute(string hubName, string route, string? expected)
    {
        Assert.Equal(expected, BlazorTraceProcessor.HubSpanName(hubName, route));
    }

    [Fact]
    public void NameRender_RenamesTheEnclosingHubSpanAndTagsTheRoute()
    {
        using var hubSource = new ActivitySource(BlazorTraceProcessor.SignalRSourceName);
        using var capture = new TraceCapture(hubSource.Name);
        using var hub = hubSource.StartActivity("Microsoft.AspNetCore.Components.Server.ComponentHub/UpdateRootComponents")!;

        BlazorTraceProcessor.NameRender(Activity.Current, "/movies/{RouteCode}");

        Assert.Equal("Render /movies/{RouteCode}", hub.DisplayName);
        Assert.Equal("/movies/{RouteCode}", hub.GetTagItem(BlazorTraceProcessor.RouteTag));
    }

    [Fact]
    public void NameRender_OutsideAHubSpan_ChangesNothing()
    {
        using var capture = new TraceCapture();
        var requestName = Activity.Current!.DisplayName;

        BlazorTraceProcessor.NameRender(Activity.Current, "/movies/{RouteCode}");

        Assert.Equal(requestName, Activity.Current!.DisplayName);
    }

    [Microsoft.AspNetCore.Components.Route("/traced-page/{Id}")]
    private sealed class TracedPage(TimeProvider time) : Microsoft.AspNetCore.Components.ComponentBase
    {
        public TimeProvider Time { get; } = time;
    }

    [Microsoft.AspNetCore.Components.Route("/two-routes/first")]
    [Microsoft.AspNetCore.Components.Route("/two-routes/second")]
    private sealed class TwoRoutePage : Microsoft.AspNetCore.Components.ComponentBase;

    [Fact]
    public void TracingComponentActivator_PageWithTwoRoutes_IsCreatedAndNamedAfterOneOfThem()
    {
        using var hubSource = new ActivitySource(BlazorTraceProcessor.SignalRSourceName);
        using var capture = new TraceCapture(hubSource.Name);
        using var hub = hubSource.StartActivity("Microsoft.AspNetCore.Components.Server.ComponentHub/UpdateRootComponents")!;
        using var services = new ServiceCollection().BuildServiceProvider();

        var page = new TracingComponentActivator(services).CreateInstance(typeof(TwoRoutePage));

        Assert.IsType<TwoRoutePage>(page);
        Assert.Contains(hub.DisplayName, new[] { "Render /two-routes/first", "Render /two-routes/second" });
    }

    [Fact]
    public void TracingComponentActivator_CreatesWithConstructorInjection_AndNamesTheRenderAfterThePage()
    {
        using var hubSource = new ActivitySource(BlazorTraceProcessor.SignalRSourceName);
        using var capture = new TraceCapture(hubSource.Name);
        using var hub = hubSource.StartActivity("Microsoft.AspNetCore.Components.Server.ComponentHub/UpdateRootComponents")!;
        var time = new Microsoft.Extensions.Time.Testing.FakeTimeProvider();
        using var services = new ServiceCollection().AddSingleton<TimeProvider>(time).BuildServiceProvider();

        var page = new TracingComponentActivator(services).CreateInstance(typeof(TracedPage));

        Assert.Same(time, Assert.IsType<TracedPage>(page).Time);
        Assert.Equal("Render /traced-page/{Id}", hub.DisplayName);
    }
}
