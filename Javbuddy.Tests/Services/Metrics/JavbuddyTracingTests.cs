using System.Diagnostics;
using Javbuddy.Services.Metrics;
using Javbuddy.Tests.TestSupport;
using Microsoft.Extensions.Configuration;

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
    public void TracesEndpoint_IsTheFullUrlAsGiven()
    {
        var config = Config(("OTEL_EXPORTER_OTLP_TRACES_ENDPOINT", "http://vt:10428/insert/opentelemetry/v1/traces"));

        Assert.Equal(new Uri("http://vt:10428/insert/opentelemetry/v1/traces"), JavbuddyTracing.TracesEndpoint(config));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("not a url")]
    public void TracesEndpoint_UnsetOrInvalid_IsNull(string? value)
    {
        Assert.Null(JavbuddyTracing.TracesEndpoint(Config(("OTEL_EXPORTER_OTLP_TRACES_ENDPOINT", value))));
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/movies/MIDE-400")]
    [InlineData("/image-cache/MIDE-400/poster/thumb")]
    [InlineData("/trickplay/12/abc/3.webp")]
    [InlineData("/api/movies/12/stream")]
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
    public void ShouldTraceRequest_ProbesScrapesAndStaticFiles_AreNotTraced(string path)
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
}
