using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace LayerPeek.App.Platform.MacOS;

/// <summary>
/// <c>NSWorkspaceDidWakeNotification</c> of the workspace notification center (T-151), observed with a global block on
/// the main queue. Only registered when AppKit is already loaded (the UI runs); never loads it. Never throws.
/// </summary>
[SupportedOSPlatform("macos")]
internal sealed unsafe class MacSystemResumeEvents : ISystemResumeEvents, IDisposable
{
    // The block is global (no captured state): it finds the instance here. One instance per process.
    private static MacSystemResumeEvents? s_current;
    private static readonly Lazy<nint> WakeBlock = new(static () => ObjCBlocks.Global((nint)(delegate* unmanaged<nint, nint, void>)&OnWake));

    private nint _center;
    private nint _token;
    private bool _started;

    public event EventHandler? Resumed;

    public void Start()
    {
        if (_started)
        {
            return;
        }

        _started = true;
        try
        {
            nint workspaceClass = ObjCRuntime.Class("NSWorkspace");
            if (workspaceClass == 0)
            {
                // No AppKit in this process (headless tests): there is no wake notification to observe.
                return;
            }

            s_current = this;
            _token = ObjCRuntime.InAutoreleasePool(() =>
            {
                nint workspace = ObjCRuntime.Send(workspaceClass, ObjCRuntime.Selector("sharedWorkspace"));
                _center = workspace == 0 ? 0 : ObjCRuntime.Send(workspace, ObjCRuntime.Selector("notificationCenter"));
                if (_center == 0)
                {
                    return 0;
                }

                nint mainQueue = ObjCRuntime.Send(ObjCRuntime.Class("NSOperationQueue"), ObjCRuntime.Selector("mainQueue"));
                nint token = ObjCRuntime.Send(
                    _center,
                    ObjCRuntime.Selector("addObserverForName:object:queue:usingBlock:"),
                    ObjCRuntime.NSString("NSWorkspaceDidWakeNotification"),
                    0,
                    mainQueue,
                    WakeBlock.Value);
                // Kept (retained) until Dispose removes the registration with it.
                return token == 0 ? 0 : ObjCRuntime.Send(token, ObjCRuntime.Selector("retain"));
            });
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            // Not an AppKit process.
        }
    }

    public void Dispose()
    {
        if (_token == 0)
        {
            return;
        }

        try
        {
            ObjCRuntime.Send(_center, ObjCRuntime.Selector("removeObserver:"), _token);
            ObjCRuntime.Release(_token);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
        }

        _token = 0;
        if (ReferenceEquals(s_current, this))
        {
            s_current = null;
        }
    }

    [UnmanagedCallersOnly]
    private static void OnWake(nint block, nint notification)
    {
        try
        {
            MacSystemResumeEvents? current = s_current;
            current?.Resumed?.Invoke(current, EventArgs.Empty);
        }
        catch (Exception)
        {
            // Never into AppKit: the handlers post to the UI thread and do not throw.
        }
    }
}
