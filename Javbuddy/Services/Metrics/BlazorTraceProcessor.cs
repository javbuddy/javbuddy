using System.Diagnostics;
using OpenTelemetry;

namespace Javbuddy.Services.Metrics;

/// <summary>Trims and names the Blazor circuit's built-in spans. The SignalR hub spans stay, since interactive
/// renders run inside them (a page's queries after the circuit connects sit under
/// <c>ComponentHub/UpdateRootComponents</c>), except the acknowledgements the browser sends after every
/// render batch and JS interop call, which never run app code. A hub span that renders a page is renamed
/// after it ("Render /movies/{RouteCode}", "Navigate /actors"; see <see cref="NameRender"/>), since every
/// page's first interactive render is otherwise the same "UpdateRootComponents". The circuit span's name carries the circuit id, so it's
/// renamed to a fixed name (the id stays in its aspnetcore.components.circuit.id tag).</summary>
public sealed class BlazorTraceProcessor : BaseProcessor<Activity>
{
    public const string SignalRSourceName = "Microsoft.AspNetCore.SignalR.Server";
    public const string CircuitSourceName = "Microsoft.AspNetCore.Components.Server.Circuits";
    public const string RouteTag = "aspnetcore.components.route";

    private const string HubMethodPrefix = "Microsoft.AspNetCore.Components.Server.ComponentHub/";

    private static readonly string[] AcknowledgementMethods = ["/OnRenderCompleted", "/EndInvokeJSFromDotNet"];

    public override void OnStart(Activity data)
    {
        if (IsAcknowledgement(data.Source.Name, data.DisplayName))
        {
            // Unrecorded activities are skipped by the exporter.
            data.IsAllDataRequested = false;
            data.ActivityTraceFlags &= ~ActivityTraceFlags.Recorded;
        }
        else if (data.Source.Name == CircuitSourceName && data.DisplayName.StartsWith("Circuit ", StringComparison.Ordinal))
        {
            data.DisplayName = "Circuit";
        }
    }

    /// <summary>Names the SignalR hub span that <paramref name="current"/> runs under after the page it renders.
    /// Called by TracingComponentActivator as a page is created; Blazor's own Route span only exists for the
    /// prerender, so the interactive render has no other way to say which page it was. Outside a hub
    /// invocation (the prerender, or a component that isn't a page) this does nothing.</summary>
    public static void NameRender(Activity? current, string? routeTemplate)
    {
        if (string.IsNullOrEmpty(routeTemplate)) return;
        for (var activity = current; activity is not null; activity = activity.Parent)
        {
            if (activity.Source.Name != SignalRSourceName) continue;
            if (activity.IsStopped) return;
            if (HubSpanName(activity.DisplayName, routeTemplate) is { } name) activity.DisplayName = name;
            activity.SetTag(RouteTag, routeTemplate);
            return;
        }
    }

    public static bool IsAcknowledgement(string sourceName, string displayName) =>
        sourceName == SignalRSourceName
        && AcknowledgementMethods.Any(method => displayName.EndsWith(method, StringComparison.Ordinal));

    /// <summary>The name for a hub span that routed to <paramref name="route"/>, or null to keep its own.</summary>
    public static string? HubSpanName(string hubSpanName, string route) =>
        hubSpanName.StartsWith(HubMethodPrefix, StringComparison.Ordinal)
            ? hubSpanName[HubMethodPrefix.Length..] switch
            {
                "UpdateRootComponents" => $"Render {route}",
                "OnLocationChanged" => $"Navigate {route}",
                _ => null,
            }
            : null;
}
