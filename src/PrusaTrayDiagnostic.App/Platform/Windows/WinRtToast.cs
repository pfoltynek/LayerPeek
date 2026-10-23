using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using System.Runtime.Versioning;

using PrusaTrayDiagnostic.App.Notifications;

namespace PrusaTrayDiagnostic.App.Platform.Windows;

/// <summary>
/// Creates WinRT toast objects. Holds the activation factories (looked up once) and the calling thread's COM
/// apartment: create, use and dispose it on one thread.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class WinRtToastFactory : IDisposable
{
    private readonly ComApartment _apartment;
    private readonly IToastNotificationFactory _toastFactory;
    private readonly IActivationFactory _xmlDocumentFactory;
    private bool _disposed;

    private WinRtToastFactory(ComApartment apartment, IToastNotificationFactory toastFactory, IActivationFactory xmlDocumentFactory)
    {
        _apartment = apartment;
        _toastFactory = toastFactory;
        _xmlDocumentFactory = xmlDocumentFactory;
    }

    /// <exception cref="Exception">WinRT toasts are not available (e.g. Windows Server Core).</exception>
    public static WinRtToastFactory Create()
    {
        ComApartment apartment = ComApartment.EnterMultithreaded();
        IToastNotificationFactory? toastFactory = null;
        try
        {
            toastFactory = GetActivationFactory<IToastNotificationFactory>(WinRtClassNames.ToastNotification, WinRtInterfaceIds.ToastNotificationFactory);
            IActivationFactory xmlDocumentFactory = GetActivationFactory<IActivationFactory>(WinRtClassNames.XmlDocument, WinRtInterfaceIds.ActivationFactory);
            return new WinRtToastFactory(apartment, toastFactory, xmlDocumentFactory);
        }
        catch
        {
            ComObjects.Release(toastFactory);
            apartment.Dispose();
            throw;
        }
    }

    /// <summary>A notifier that shows toasts as the app with <paramref name="appUserModelId"/>.</summary>
    public WinRtToastNotifier CreateNotifier(string appUserModelId)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(appUserModelId);
        IToastNotificationManagerStatics manager = GetActivationFactory<IToastNotificationManagerStatics>(WinRtClassNames.ToastNotificationManager, WinRtInterfaceIds.ToastNotificationManagerStatics);
        try
        {
            using HString id = HString.Create(appUserModelId);
            manager.CreateToastNotifierWithId(id.Handle, out nint notifier);
            return new WinRtToastNotifier(ComObjects.Wrap<IToastNotifier>(notifier));
        }
        finally
        {
            ComObjects.Release(manager);
        }
    }

    /// <summary>Parses <paramref name="xml"/> (toast schema) into a toast that is not shown yet.</summary>
    /// <exception cref="Exception">The XML is not well-formed.</exception>
    public WinRtToast CreateToast(string xml)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(xml);
        _xmlDocumentFactory.ActivateInstance(out nint instance);
        IXmlDocumentIO document = ComObjects.Wrap<IXmlDocumentIO>(instance);
        try
        {
            using (HString content = HString.Create(xml))
            {
                document.LoadXml(content.Handle);
            }

            _toastFactory.CreateToastNotification((IXmlDocument)document, out nint toast);
            return new WinRtToast(ComObjects.Wrap<IToastNotification>(toast));
        }
        finally
        {
            ComObjects.Release(document);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        // Nested finally: a failing release never keeps the thread in the apartment or leaks the other factory.
        try
        {
            ComObjects.Release(_xmlDocumentFactory);
        }
        finally
        {
            try
            {
                ComObjects.Release(_toastFactory);
            }
            finally
            {
                _apartment.Dispose();
            }
        }
    }

    private static T GetActivationFactory<T>(string className, in Guid interfaceId)
        where T : class
    {
        using HString name = HString.Create(className);
        Marshal.ThrowExceptionForHR(ComNative.RoGetActivationFactory(name.Handle, in interfaceId, out nint factory));
        return ComObjects.Wrap<T>(factory);
    }
}

/// <summary>Shows toasts as one app (Windows.UI.Notifications.ToastNotifier).</summary>
[SupportedOSPlatform("windows")]
internal sealed class WinRtToastNotifier : IDisposable
{
    private readonly IToastNotifier _notifier;
    private bool _disposed;

    internal WinRtToastNotifier(IToastNotifier notifier) => _notifier = notifier;

    public void Show(WinRtToast toast)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(toast);
        _notifier.Show(toast.Notification);
    }

    /// <summary>
    /// Whether Windows shows the toasts of the app (switched off in Settings, by policy...). Toasts of a disabled app are
    /// dropped silently, so this is the only hint.
    /// </summary>
    public ToastNotifierSetting Setting
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _notifier.GetSetting(out int value);
            return (ToastNotifierSetting)value;
        }
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            ComObjects.Release(_notifier);
        }
    }
}

/// <summary>One toast (Windows.UI.Notifications.ToastNotification) and its click and failure subscriptions.</summary>
[SupportedOSPlatform("windows")]
internal sealed class WinRtToast : IDisposable
{
    private long? _activatedToken;
    private long? _failedToken;
    private bool _disposed;

    internal WinRtToast(IToastNotification notification) => Notification = notification;

    internal IToastNotification Notification { get; }

    /// <summary>Calls <paramref name="callback"/> when the user clicks this toast (while the app is running).</summary>
    public void SubscribeActivated(ToastActivationCallback callback)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(callback);
        if (_activatedToken is not null)
        {
            throw new InvalidOperationException("The toast is already subscribed.");
        }

        Notification.AddActivated(callback.Pointer, out long token);
        _activatedToken = token;
    }

    /// <summary>Calls <paramref name="callback"/> when Windows reports that it did not show this toast.</summary>
    public void SubscribeFailed(ToastFailedCallback callback)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(callback);
        if (_failedToken is not null)
        {
            throw new InvalidOperationException("The toast is already subscribed.");
        }

        Notification.AddFailed(callback.Pointer, out long token);
        _failedToken = token;
    }

    /// <summary>Unsubscribes and releases the toast object; a toast already on screen stays there.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        try
        {
            Unsubscribe(_activatedToken, Notification.RemoveActivated);
            Unsubscribe(_failedToken, Notification.RemoveFailed);
        }
        finally
        {
            ComObjects.Release(Notification);
        }
    }

    private static void Unsubscribe(long? token, Action<long> remove)
    {
        if (token is not long value)
        {
            return;
        }

        try
        {
            remove(value);
        }
        catch (Exception)
        {
            // The toast object is gone or closed on the OS side (COMException, RO_E_CLOSED as ObjectDisposedException...);
            // releasing our reference is all that is left.
        }
    }
}

/// <summary>
/// The native click handler (TypedEventHandler&lt;ToastNotification, Object&gt;) for <see cref="WinRtToast"/>:
/// a source-generated COM object forwarding the toast's launch argument to a managed callback. One instance serves
/// every toast; it stays alive while a toast references it.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class ToastActivationCallback : IDisposable
{
    private nint _pointer;

    /// <param name="activated">Called on an OS thread with the launch argument, <see langword="null"/> when unreadable.</param>
    public ToastActivationCallback(Action<string?> activated)
    {
        ArgumentNullException.ThrowIfNull(activated);
        _pointer = ComObjects.GetInterfacePointer(new ToastActivatedHandler(activated), WinRtInterfaceIds.ToastActivatedHandler);
    }

    internal nint Pointer
    {
        get
        {
            nint pointer = _pointer;
            ObjectDisposedException.ThrowIf(pointer == 0, this);
            return pointer;
        }
    }

    public void Dispose()
    {
        nint pointer = Interlocked.Exchange(ref _pointer, 0);
        if (pointer != 0)
        {
            Marshal.Release(pointer);
        }
    }
}

[SupportedOSPlatform("windows")]
[GeneratedComClass]
internal sealed partial class ToastActivatedHandler(Action<string?> activated) : IToastActivatedHandler
{
    public void Invoke(nint sender, nint args)
    {
        // Called by the OS: nothing may escape (the service's callback handles its own failures).
        string? argument;
        try
        {
            argument = ReadArguments(args);
        }
        catch (Exception)
        {
            argument = null;
        }

        activated(argument);
    }

    /// <summary>The launch argument of a body click; <see langword="null"/> for anything else.</summary>
    private static string? ReadArguments(nint args)
    {
        if (args == 0)
        {
            return null;
        }

        IToastActivatedEventArgs eventArgs = ComObjects.Borrow<IToastActivatedEventArgs>(args);
        try
        {
            eventArgs.GetArguments(out nint value);
            return HString.MoveToManaged(value);
        }
        finally
        {
            ComObjects.Release(eventArgs);
        }
    }
}

/// <summary>
/// The native failure handler (TypedEventHandler&lt;ToastNotification, ToastFailedEventArgs&gt;) for
/// <see cref="WinRtToast"/>: forwards the HRESULT why Windows did not show a toast to a managed callback. One instance
/// serves every toast.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class ToastFailedCallback : IDisposable
{
    private nint _pointer;

    /// <param name="failed">Called on an OS thread with the error code; must not throw.</param>
    public ToastFailedCallback(Action<int> failed)
    {
        ArgumentNullException.ThrowIfNull(failed);
        _pointer = ComObjects.GetInterfacePointer(new ToastFailedHandler(failed), WinRtInterfaceIds.ToastFailedHandler);
    }

    internal nint Pointer
    {
        get
        {
            nint pointer = _pointer;
            ObjectDisposedException.ThrowIf(pointer == 0, this);
            return pointer;
        }
    }

    public void Dispose()
    {
        nint pointer = Interlocked.Exchange(ref _pointer, 0);
        if (pointer != 0)
        {
            Marshal.Release(pointer);
        }
    }
}

[SupportedOSPlatform("windows")]
[GeneratedComClass]
internal sealed partial class ToastFailedHandler(Action<int> failed) : IToastFailedHandler
{
    /// <summary>Error code reported when the event arguments cannot be read (E_FAIL).</summary>
    internal const int UnknownError = unchecked((int)0x80004005);

    public void Invoke(nint sender, nint args)
    {
        // Called by the OS: nothing may escape.
        int errorCode;
        try
        {
            errorCode = ReadErrorCode(args);
        }
        catch (Exception)
        {
            errorCode = UnknownError;
        }

        try
        {
            failed(errorCode);
        }
        catch (Exception)
        {
            // The callback logs; a failing logger (quitting) loses only this report.
        }
    }

    private static int ReadErrorCode(nint args)
    {
        if (args == 0)
        {
            return UnknownError;
        }

        IToastFailedEventArgs eventArgs = ComObjects.Borrow<IToastFailedEventArgs>(args);
        try
        {
            eventArgs.GetErrorCode(out int value);
            return value;
        }
        finally
        {
            ComObjects.Release(eventArgs);
        }
    }
}

/// <summary>
/// The notification center history of one app (Windows.UI.Notifications.ToastNotificationHistory). Usable from any
/// thread except an STA that never initialized COM for it: every call joins the multithreaded apartment for its duration.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class WinRtToastHistory : IToastHistory
{
    /// <exception cref="Exception">WinRT toasts are not available, or Windows refused to clear the history.</exception>
    public void Clear(string appUserModelId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(appUserModelId);
        using ComApartment apartment = ComApartment.EnterMultithreaded();
        using HString className = HString.Create(WinRtClassNames.ToastNotificationManager);
        Marshal.ThrowExceptionForHR(ComNative.RoGetActivationFactory(className.Handle, in WinRtInterfaceIds.ToastNotificationManagerStatics2, out nint statics));
        IToastNotificationManagerStatics2 manager = ComObjects.Wrap<IToastNotificationManagerStatics2>(statics);
        try
        {
            manager.GetHistory(out nint history);
            IToastNotificationHistory toasts = ComObjects.Wrap<IToastNotificationHistory>(history);
            try
            {
                using HString id = HString.Create(appUserModelId);
                toasts.ClearWithId(id.Handle);
            }
            finally
            {
                ComObjects.Release(toasts);
            }
        }
        finally
        {
            ComObjects.Release(manager);
        }
    }
}
