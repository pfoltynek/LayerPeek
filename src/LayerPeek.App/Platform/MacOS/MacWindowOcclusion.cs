using System.Runtime.InteropServices;
using System.Runtime.Versioning;

using Avalonia.Controls;
using Avalonia.Threading;

namespace LayerPeek.App.Platform.MacOS;

/// <summary>
/// Occluded windows on macOS (T-187): <c>NSWindow.occlusionState</c> when a watch starts, then
/// <c>NSWindowDidChangeOcclusionStateNotification</c> of that window (default notification center, a global block on the
/// main queue, no polling). A window counts as occluded when no part of it can be seen
/// (<c>NSWindowOcclusionStateVisible</c> is not set): fully covered by other windows, on another Space, behind an app in
/// full screen, while the screen is locked or the displays sleep. Reported through <see cref="OcclusionGrace"/>: only after
/// it lasted <see cref="OcclusionGrace.Default"/>, visible again at once. One observer per watched window; the block finds
/// the watch by the window of the notification. UI (main) thread only. Never throws.
/// </summary>
[SupportedOSPlatform("macos")]
internal sealed unsafe class MacWindowOcclusion : IWindowCloaking
{
    /// <summary><c>NSWindowOcclusionStateVisible</c>.</summary>
    internal const nuint OcclusionStateVisible = 1 << 1;

    private const string DidChangeOcclusionState = "NSWindowDidChangeOcclusionStateNotification";

    /// <summary>One per process (the block is global).</summary>
    public static readonly MacWindowOcclusion Shared = new();

    private static readonly Lazy<nint> ChangedBlock = new(static () => ObjCBlocks.Global((nint)(delegate* unmanaged<nint, nint, void>)&OnOcclusionChanged));

    private readonly Dictionary<nint, Subscription> _watches = [];

    private MacWindowOcclusion()
    {
    }

    /// <summary>Whether an <c>occlusionState</c> means no part of the window can be seen.</summary>
    internal static bool IsOccluded(nuint occlusionState) => (occlusionState & OcclusionStateVisible) == 0;

    IDisposable IWindowCloaking.Watch(TopLevel window, Action<bool> cloakedChanged)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(cloakedChanged);
        OcclusionGrace grace = new(cloakedChanged, static (action, delay) => DispatcherTimer.RunOnce(action, delay), OcclusionGrace.Default);
        nint native = NativeWindowCollectionBehavior.WindowOf(window.TryGetPlatformHandle());
        if (native == 0 || _watches.ContainsKey(native))
        {
            // Headless (no native window), or watched already: never reported occluded.
            return new Subscription(this, 0, 0, grace);
        }

        nint token = Observe(native);
        Subscription watch = new(this, native, token, grace);
        if (token != 0)
        {
            // Without the observer the window is never reported occluded (no notification would resume it).
            _watches[native] = watch;
            grace.Update(ReadOccluded(native));
        }

        return watch;
    }

    private static bool ReadOccluded(nint window)
    {
        try
        {
            return IsOccluded((nuint)ObjCRuntime.Send(window, ObjCRuntime.Selector("occlusionState")));
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return false;
        }
    }

    private static nint Observe(nint window)
    {
        try
        {
            return ObjCRuntime.InAutoreleasePool(() =>
            {
                nint center = ObjCRuntime.Send(ObjCRuntime.Class("NSNotificationCenter"), ObjCRuntime.Selector("defaultCenter"));
                nint mainQueue = ObjCRuntime.Send(ObjCRuntime.Class("NSOperationQueue"), ObjCRuntime.Selector("mainQueue"));
                nint token = center == 0 ? 0 : ObjCRuntime.Send(
                    center,
                    ObjCRuntime.Selector("addObserverForName:object:queue:usingBlock:"),
                    ObjCRuntime.NSString(DidChangeOcclusionState),
                    window,
                    mainQueue,
                    ChangedBlock.Value);
                // Kept (retained) until the watch removes the registration with it.
                return token == 0 ? 0 : ObjCRuntime.Send(token, ObjCRuntime.Selector("retain"));
            });
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return 0;
        }
    }

    private void Remove(Subscription watch)
    {
        watch.Grace.Dispose();
        if (watch.Token == 0)
        {
            return;
        }

        if (_watches.TryGetValue(watch.Window, out Subscription? current) && ReferenceEquals(current, watch))
        {
            _ = _watches.Remove(watch.Window);
        }

        try
        {
            nint center = ObjCRuntime.Send(ObjCRuntime.Class("NSNotificationCenter"), ObjCRuntime.Selector("defaultCenter"));
            _ = ObjCRuntime.Send(center, ObjCRuntime.Selector("removeObserver:"), watch.Token);
            ObjCRuntime.Release(watch.Token);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
        }
    }

    [UnmanagedCallersOnly]
    private static void OnOcclusionChanged(nint block, nint notification)
    {
        try
        {
            nint window = notification == 0 ? 0 : ObjCRuntime.Send(notification, ObjCRuntime.Selector("object"));
            if (window != 0 && Shared._watches.TryGetValue(window, out Subscription? watch))
            {
                // The state now, not a toggle: notifications of a quick switch may overtake each other.
                watch.Grace.Update(ReadOccluded(window));
            }
        }
        catch (Exception)
        {
            // Never into AppKit: the view model only switches its stream.
        }
    }

    private sealed class Subscription(MacWindowOcclusion owner, nint window, nint token, OcclusionGrace grace) : IDisposable
    {
        private bool _disposed;

        public nint Window { get; } = window;

        public nint Token { get; } = token;

        public OcclusionGrace Grace { get; } = grace;

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
                owner.Remove(this);
            }
        }
    }
}
