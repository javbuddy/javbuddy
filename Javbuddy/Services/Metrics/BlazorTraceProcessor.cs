using System.Diagnostics;
using OpenTelemetry;

namespace Javbuddy.Services.Metrics;

/// <summary>Trims the Blazor circuit's built-in spans down to the useful ones. The SignalR hub spans stay,
/// since interactive renders run inside them (a page's queries after the circuit connects sit under
/// <c>ComponentHub/UpdateRootComponents</c>), except the acknowledgements the browser sends after every
/// render batch and JS interop call, which never run app code. The circuit span's name carries the
/// circuit id, so it's renamed to a fixed name (the id stays in its aspnetcore.components.circuit.id tag).</summary>
public sealed class BlazorTraceProcessor : BaseProcessor<Activity>
{
    private const string SignalRSourceName = "Microsoft.AspNetCore.SignalR.Server";
    private const string CircuitSourceName = "Microsoft.AspNetCore.Components.Server.Circuits";

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

    public static bool IsAcknowledgement(string sourceName, string displayName) =>
        sourceName == SignalRSourceName
        && AcknowledgementMethods.Any(method => displayName.EndsWith(method, StringComparison.Ordinal));
}
