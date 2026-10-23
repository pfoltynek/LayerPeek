using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using System.Runtime.Versioning;

namespace PrusaTrayDiagnostic.App.Platform.Windows;

// COM toast activator (T-068): Windows SDK 10.0.26100 unknwnbase.h (IClassFactory) and notificationactivationcallback.h
// (INotificationActivationCallback). Method order must match. Source-generated COM only (Native AOT).

/// <summary>IClassFactory.</summary>
[GeneratedComInterface]
[Guid("00000001-0000-0000-C000-000000000046")]
internal partial interface IClassFactory
{
    [PreserveSig]
    int CreateInstance(nint outer, in Guid interfaceId, out nint instance);

    [PreserveSig]
    int LockServer([MarshalAs(UnmanagedType.Bool)] bool lockServer);
}

/// <summary>
/// INotificationActivationCallback: Windows calls <see cref="Activate"/> for a click on a toast of an app whose Start menu
/// shortcut names this server (<c>System.AppUserModel.ToastActivatorCLSID</c>).
/// </summary>
[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
[Guid("53E31837-6600-4A81-9395-75CFFE746F94")]
internal partial interface INotificationActivationCallback
{
    /// <param name="appUserModelId">The app the toast belongs to.</param>
    /// <param name="invokedArgs">The <c>launch</c> argument of the toast (or of the clicked button).</param>
    /// <param name="data">NOTIFICATION_USER_INPUT_DATA of input fields; the app's toasts have none.</param>
    [PreserveSig]
    int Activate(string? appUserModelId, string? invokedArgs, nint data, uint count);
}

internal static class ToastActivatorIds
{
    public static readonly Guid ClassFactory = new("00000001-0000-0000-C000-000000000046");
    public static readonly Guid NotificationActivationCallback = new("53E31837-6600-4A81-9395-75CFFE746F94");
}

/// <summary>The activator Windows creates per click; reports the click and always answers S_OK.</summary>
[GeneratedComClass]
[SupportedOSPlatform("windows")]
internal sealed partial class ToastActivator(Action<string?, string?> activated) : INotificationActivationCallback
{
    public int Activate(string? appUserModelId, string? invokedArgs, nint data, uint count)
    {
        try
        {
            activated(appUserModelId, invokedArgs);
        }
        catch (Exception)
        {
            // Called by Windows: nothing may escape (the app's side logs its own failures).
        }

        return ComNative.SOk;
    }
}

/// <summary>The registered class object: creates <see cref="ToastActivator"/>s.</summary>
[GeneratedComClass]
[SupportedOSPlatform("windows")]
internal sealed partial class ToastActivatorFactory(Action<string?, string?> activated) : IClassFactory
{
    private const int ClassENoAggregation = unchecked((int)0x80040110);

    public int CreateInstance(nint outer, in Guid interfaceId, out nint instance)
    {
        instance = 0;
        if (outer != 0)
        {
            return ClassENoAggregation;
        }

        try
        {
            instance = ComObjects.GetInterfacePointer(new ToastActivator(activated), interfaceId);
            return ComNative.SOk;
        }
        catch (Exception ex)
        {
            // E_NOINTERFACE for an interface the activator does not have.
            return ex.HResult;
        }
    }

    /// <summary>The server lives as long as the app; nothing to count.</summary>
    public int LockServer(bool lockServer) => ComNative.SOk;
}

/// <summary>
/// The COM toast activator of the running app (T-068): its class object registered under the activator's CLSID
/// (<c>CoRegisterClassObject</c>, <c>CLSCTX_LOCAL_SERVER</c>, <c>REGCLS_MULTIPLEUSE</c>) until disposed. The registration
/// lives in the multithreaded apartment, held by a thread of its own that only waits, so Windows' calls arrive on COM
/// threads without depending on the UI thread's message loop (they work while the app is still starting). Clicks go to
/// the callback (app id, launch argument) on those threads.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class ToastActivatorServer : IDisposable
{
    private static readonly TimeSpan ThreadTimeout = TimeSpan.FromSeconds(10);

    private readonly Guid _clsid;
    private readonly Action<string?, string?> _activated;
    private readonly Thread _thread;
    private readonly ManualResetEventSlim _registered = new();
    private readonly ManualResetEventSlim _stop = new();
    private ExceptionDispatchInfo? _failure;
    private int _disposed;

    private ToastActivatorServer(Guid clsid, Action<string?, string?> activated)
    {
        _clsid = clsid;
        _activated = activated;
        _thread = new Thread(Run) { IsBackground = true, Name = "Toast activator" };
    }

    /// <summary>Registers the class object of <paramref name="clsid"/>; clicks go to <paramref name="activated"/>.</summary>
    /// <exception cref="Exception">The registration failed (COM error), or did not finish in time.</exception>
    public static ToastActivatorServer Start(Guid clsid, Action<string?, string?> activated)
    {
        if (clsid == Guid.Empty)
        {
            throw new ArgumentException("The CLSID must not be empty.", nameof(clsid));
        }

        ArgumentNullException.ThrowIfNull(activated);
        ToastActivatorServer server = new(clsid, activated);
        server._thread.Start();
        // Synchronous by design: called in Main before the UI exists, the wait is bounded.
        if (!server._registered.Wait(ThreadTimeout))
        {
            server.Dispose();
            throw new TimeoutException("Registering the toast activator did not finish in time.");
        }

        if (server._failure is { } failure)
        {
            server.Dispose();
            failure.Throw();
        }

        return server;
    }

    /// <summary>Revokes the registration (Windows then starts the app again for a click) and ends the thread. Safe to repeat.</summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _stop.Set();
        if (_thread.Join(ThreadTimeout))
        {
            _registered.Dispose();
            _stop.Dispose();
        }

        // Otherwise the thread still uses the events (a hung revoke): they are left to the GC.
    }

    private void Run()
    {
        try
        {
            using ComApartment apartment = ComApartment.EnterMultithreaded();
            ToastActivatorFactory factory = new(_activated);
            nint unknown = ComObjects.Wrappers.GetOrCreateComInterfaceForObject(factory, CreateComInterfaceFlags.None);
            uint cookie;
            try
            {
                // COM keeps its own reference to the factory for the registration.
                Marshal.ThrowExceptionForHR(ComNative.CoRegisterClassObject(_clsid, unknown, ComNative.ClsCtxLocalServer, ComNative.RegClsMultipleUse, out cookie));
            }
            finally
            {
                Marshal.Release(unknown);
            }

            _registered.Set();
            try
            {
                // The registration lives as long as this thread keeps the apartment; blocking is fine in the MTA.
                _stop.Wait();
            }
            finally
            {
                _ = ComNative.CoRevokeClassObject(cookie);
                GC.KeepAlive(factory);
            }
        }
        catch (Exception ex)
        {
            _failure = ExceptionDispatchInfo.Capture(ex);
            _registered.Set();
        }
    }
}
