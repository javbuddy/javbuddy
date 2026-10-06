using Microsoft.JSInterop;

namespace Javbuddy.Components.Shared;

/// <summary>An editor's playhead keyboard shortcuts, registered with
/// ClipEditor.razor.js's <c>initShortcuts</c> while the host enables them. <paramref name="bindings"/>
/// maps a key ("m", or "shift+m" with Shift) to the owner's [JSInvokable] method taking the player's
/// current time. Used by ClipEditor.</summary>
internal sealed class PlayheadShortcuts<T>(T owner, IReadOnlyDictionary<string, string> bindings) where T : class
{
    private readonly string key = Guid.NewGuid().ToString("N");
    private DotNetObjectReference<T>? selfRef;
    private bool disposed;

    public bool Active { get; private set; }

    /// <summary>Registers or removes the listener when <paramref name="enable"/> differs from what's
    /// registered.</summary>
    public async Task SyncAsync(Func<Task<IJSObjectReference?>> getModule, bool enable, string videoSelector)
    {
        if (enable == Active || disposed) return;

        try
        {
            var module = await getModule();
            if (module is null || disposed) return;
            if (enable)
            {
                selfRef ??= DotNetObjectReference.Create(owner);
                await module.InvokeVoidAsync("initShortcuts", key, selfRef, videoSelector, bindings);
            }
            else
            {
                await module.InvokeVoidAsync("disposeShortcuts", key);
            }
            Active = enable;
        }
        catch (JSDisconnectedException)
        {
        }
    }

    /// <summary>Removes the listener (call before disposing the module) and releases the .NET reference.
    /// Also when a registration may still be in flight: interop calls run in order, so this removal
    /// lands after it instead of leaving a stale listener that would claim the keys.</summary>
    public async Task DisposeAsync(IJSObjectReference? module)
    {
        disposed = true;
        try
        {
            if (selfRef is not null && module is not null)
            {
                await module.InvokeVoidAsync("disposeShortcuts", key);
            }
        }
        catch (JSDisconnectedException)
        {
        }
        Active = false;
        selfRef?.Dispose();
    }
}
