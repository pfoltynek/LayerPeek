using System.Collections.Immutable;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

using LayerPeek.Core.Display;

namespace LayerPeek.App.Platform.MacOS;

/// <summary>
/// Whether the screen can be seen on macOS (T-187): the lock and unlock of the screen (<c>com.apple.screenIsLocked</c> /
/// <c>com.apple.screenIsUnlocked</c> of <c>NSDistributedNotificationCenter</c>) and the sleep and wake of the displays
/// (<c>NSWorkspaceScreensDidSleepNotification</c> / <c>NSWorkspaceScreensDidWakeNotification</c> of the workspace
/// notification center).
/// </summary>
/// <remarks>
/// The observers are global blocks on the main queue (<see cref="ObjCBlocks"/>), so <see cref="ScreenVisibilityChanged"/>
/// is raised on the main (UI) thread; no thread, timer or polling of its own. The blocks find the started instances in a
/// static list (several instances in tests never miss a notification). The current state is read once when listening
/// starts, after registering: a camera window may open while the screen is locked (T-151 reopens it when the printer comes
/// back). Lock: <c>CGSSessionScreenIsLocked</c> of <c>CGSessionCopyCurrentDictionary</c> (the key the lock notifications
/// follow; missing while unlocked); display: <c>CGDisplayIsAsleep</c> of the main display. The workspace center is only
/// used when AppKit is already loaded (the UI runs); nothing loads it. Never throws.
/// </remarks>
[SupportedOSPlatform("macos")]
internal sealed unsafe partial class MacDisplayActivityService : IDisplayActivityService, IDisposable
{
    internal const string ScreenLocked = "com.apple.screenIsLocked";
    internal const string ScreenUnlocked = "com.apple.screenIsUnlocked";
    internal const string ScreensDidSleep = "NSWorkspaceScreensDidSleepNotification";
    internal const string ScreensDidWake = "NSWorkspaceScreensDidWakeNotification";

    private const string CoreGraphics = "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";
    private const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
    private const string ScreenIsLockedKey = "CGSSessionScreenIsLocked";
    private const uint CFStringEncodingUtf8 = 0x08000100;

    private static readonly Lazy<nint> LockedBlock = new(static () => ObjCBlocks.Global((nint)(delegate* unmanaged<nint, nint, void>)&OnLocked));
    private static readonly Lazy<nint> UnlockedBlock = new(static () => ObjCBlocks.Global((nint)(delegate* unmanaged<nint, nint, void>)&OnUnlocked));
    private static readonly Lazy<nint> SleepBlock = new(static () => ObjCBlocks.Global((nint)(delegate* unmanaged<nint, nint, void>)&OnSleep));
    private static readonly Lazy<nint> WakeBlock = new(static () => ObjCBlocks.Global((nint)(delegate* unmanaged<nint, nint, void>)&OnWake));

    // The started, not yet disposed instances (the blocks are global and find them here).
    private static ImmutableArray<MacDisplayActivityService> s_listening = [];

    private readonly DisplayActivityTracker _tracker = new();
    private readonly Lock _gate = new();

    // Guarded by _gate: (center, retained observer token) of every registration.
    private readonly List<(nint Center, nint Token)> _observers = [];
    private bool _started;
    private bool _disposed;

    /// <summary>A notification of the OS, as the blocks report it.</summary>
    internal enum Notice
    {
        Locked,
        Unlocked,
        DisplaysAsleep,
        DisplaysAwake,
    }

    public bool IsScreenVisible => _tracker.IsScreenVisible;

    public event EventHandler? ScreenVisibilityChanged
    {
        add => _tracker.ScreenVisibilityChanged += value;
        remove => _tracker.ScreenVisibilityChanged -= value;
    }

    /// <summary>How many notifications are observed (tests: 2 with Foundation only, 4 with AppKit, 0 after <see cref="Dispose"/>).</summary>
    internal int ObserverCount
    {
        get
        {
            lock (_gate)
            {
                return _observers.Count;
            }
        }
    }

    /// <summary>
    /// Starts listening (any thread; the app: the UI thread with the first camera window) and reads the current state.
    /// Later calls and calls after <see cref="Dispose"/> do nothing.
    /// </summary>
    public void Start()
    {
        lock (_gate)
        {
            if (_started || _disposed)
            {
                return;
            }

            _started = true;
            ImmutableInterlocked.Update(ref s_listening, static (list, item) => list.Add(item), this);
            try
            {
                ObjCRuntime.InAutoreleasePool(() =>
                {
                    nint distributed = ObjCRuntime.Class("NSDistributedNotificationCenter") is var type and not 0
                        ? ObjCRuntime.Send(type, ObjCRuntime.Selector("defaultCenter"))
                        : 0;
                    Observe(distributed, ScreenLocked, LockedBlock.Value);
                    Observe(distributed, ScreenUnlocked, UnlockedBlock.Value);

                    // Never loads AppKit: without it (headless tests) no display notification exists to observe.
                    nint workspaceType = ObjCRuntime.Class("NSWorkspace");
                    nint workspace = workspaceType == 0 ? 0 : ObjCRuntime.Send(workspaceType, ObjCRuntime.Selector("sharedWorkspace"));
                    nint workspaceCenter = workspace == 0 ? 0 : ObjCRuntime.Send(workspace, ObjCRuntime.Selector("notificationCenter"));
                    Observe(workspaceCenter, ScreensDidSleep, SleepBlock.Value);
                    Observe(workspaceCenter, ScreensDidWake, WakeBlock.Value);
                    return 0;
                });
            }
            catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
            {
                // No Objective-C runtime or Foundation: the screen counts as visible.
            }
        }

        // After registering: a change in between arrives as a notification later and wins.
        if (QueryScreenLocked() is { } locked)
        {
            _tracker.SetSessionLocked(locked);
        }

        if (QueryMainDisplayAsleep() is { } asleep)
        {
            _tracker.SetDisplayAsleep(asleep);
        }
    }

    /// <summary>Stops listening (any thread); no notification reaches the instance afterwards.</summary>
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            ImmutableInterlocked.Update(ref s_listening, static (list, item) => list.Remove(item), this);
            try
            {
                foreach ((nint center, nint token) in _observers)
                {
                    _ = ObjCRuntime.Send(center, ObjCRuntime.Selector("removeObserver:"), token);
                    ObjCRuntime.Release(token);
                }
            }
            catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
            {
            }

            _observers.Clear();
        }
    }

    /// <summary>
    /// Whether the screen is locked now (<c>CGSSessionScreenIsLocked</c>); <see langword="null"/> without a window server
    /// session (e.g. a process started over SSH outside the GUI session) or CoreGraphics.
    /// </summary>
    internal static bool? QueryScreenLocked()
    {
        try
        {
            nint session = CGSessionCopyCurrentDictionary();
            if (session == 0)
            {
                return null;
            }

            nint key = CFStringCreateWithCString(0, ScreenIsLockedKey, CFStringEncodingUtf8);
            try
            {
                // Missing while unlocked; a CFBoolean while locked.
                nint value = key == 0 ? 0 : CFDictionaryGetValue(session, key);
                return value != 0 && CFGetTypeID(value) == CFBooleanGetTypeID() && CFBooleanGetValue(value) != 0;
            }
            finally
            {
                CFRelease(session);
                if (key != 0)
                {
                    CFRelease(key);
                }
            }
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return null;
        }
    }

    /// <summary>Whether the main display sleeps now; <see langword="null"/> without CoreGraphics.</summary>
    internal static bool? QueryMainDisplayAsleep()
    {
        try
        {
            return CGDisplayIsAsleep(CGMainDisplayID()) != 0;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return null;
        }
    }

    /// <summary>What a block does with a notification: every listening instance follows it (main thread; tests call it directly).</summary>
    internal static void Report(Notice notice)
    {
        foreach (MacDisplayActivityService service in s_listening)
        {
            service.Apply(notice);
        }
    }

    private void Apply(Notice notice)
    {
        switch (notice)
        {
            case Notice.Locked:
                _tracker.SetSessionLocked(true);
                break;
            case Notice.Unlocked:
                _tracker.SetSessionLocked(false);
                break;
            case Notice.DisplaysAsleep:
                _tracker.SetDisplayAsleep(true);
                break;
            case Notice.DisplaysAwake:
                _tracker.SetDisplayAsleep(false);
                break;
        }
    }

    // Inside the autorelease pool of Start, under _gate.
    private void Observe(nint center, string name, nint block)
    {
        if (center == 0)
        {
            return;
        }

        nint mainQueue = ObjCRuntime.Send(ObjCRuntime.Class("NSOperationQueue"), ObjCRuntime.Selector("mainQueue"));
        nint token = ObjCRuntime.Send(
            center,
            ObjCRuntime.Selector("addObserverForName:object:queue:usingBlock:"),
            ObjCRuntime.NSString(name),
            0,
            mainQueue,
            block);
        if (token != 0)
        {
            // Kept (retained) until Dispose removes the registration with it.
            _observers.Add((center, ObjCRuntime.Send(token, ObjCRuntime.Selector("retain"))));
        }
    }

    private static void Raise(Notice notice)
    {
        try
        {
            Report(notice);
        }
        catch (Exception)
        {
            // Never into Foundation: the handlers post to the UI thread and do not throw.
        }
    }

    [UnmanagedCallersOnly]
    private static void OnLocked(nint block, nint notification) => Raise(Notice.Locked);

    [UnmanagedCallersOnly]
    private static void OnUnlocked(nint block, nint notification) => Raise(Notice.Unlocked);

    [UnmanagedCallersOnly]
    private static void OnSleep(nint block, nint notification) => Raise(Notice.DisplaysAsleep);

    [UnmanagedCallersOnly]
    private static void OnWake(nint block, nint notification) => Raise(Notice.DisplaysAwake);

    // CFDictionaryRef CGSessionCopyCurrentDictionary(void); owned by the caller, NULL outside a window server session.
    [LibraryImport(CoreGraphics, EntryPoint = "CGSessionCopyCurrentDictionary")]
    private static partial nint CGSessionCopyCurrentDictionary();

    [LibraryImport(CoreFoundation, EntryPoint = "CFStringCreateWithCString", StringMarshalling = StringMarshalling.Utf8)]
    private static partial nint CFStringCreateWithCString(nint allocator, string text, uint encoding);

    [LibraryImport(CoreFoundation, EntryPoint = "CFDictionaryGetValue")]
    private static partial nint CFDictionaryGetValue(nint dictionary, nint key);

    [LibraryImport(CoreFoundation, EntryPoint = "CFGetTypeID")]
    private static partial nuint CFGetTypeID(nint value);

    [LibraryImport(CoreFoundation, EntryPoint = "CFBooleanGetTypeID")]
    private static partial nuint CFBooleanGetTypeID();

    // Boolean (unsigned char) CFBooleanGetValue(CFBooleanRef boolean);
    [LibraryImport(CoreFoundation, EntryPoint = "CFBooleanGetValue")]
    private static partial byte CFBooleanGetValue(nint boolean);

    [LibraryImport(CoreFoundation, EntryPoint = "CFRelease")]
    private static partial void CFRelease(nint value);

    [LibraryImport(CoreGraphics, EntryPoint = "CGMainDisplayID")]
    private static partial uint CGMainDisplayID();

    // boolean_t CGDisplayIsAsleep(CGDirectDisplayID display);
    [LibraryImport(CoreGraphics, EntryPoint = "CGDisplayIsAsleep")]
    private static partial int CGDisplayIsAsleep(uint display);
}
