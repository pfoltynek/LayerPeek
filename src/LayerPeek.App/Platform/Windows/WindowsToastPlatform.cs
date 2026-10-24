using System.Runtime.Versioning;

using Microsoft.Extensions.Logging;

using LayerPeek.App.Notifications;

namespace LayerPeek.App.Platform.Windows;

/// <summary>The AppUserModelID under which the app shows toasts and groups its windows on the taskbar.</summary>
[SupportedOSPlatform("windows")]
internal static class WindowsAppIdentity
{
    /// <summary>
    /// Must stay stable: Windows keeps the user's notification settings (on/off, banners, sound) per id, and the
    /// installer of T-017 has to create its shortcut with the same id.
    /// </summary>
    public const string AppUserModelId = "LayerPeek";

    /// <summary>
    /// Sets the id of this process. Call at startup, before any window is created; failures are ignored (only the
    /// taskbar grouping of auxiliary windows would differ, toasts use the id explicitly).
    /// </summary>
    /// <param name="appUserModelId">The id of the process: <see cref="AppUserModelId"/>, or the test id (T-044).</param>
    public static void ApplyToCurrentProcess(string appUserModelId = AppUserModelId) => _ = ComNative.SetCurrentProcessExplicitAppUserModelID(appUserModelId);
}

/// <summary>
/// Windows toasts through WinRT (<c>ToastNotificationManager</c>), for an unpackaged app: on <see cref="Open"/> the COM toast
/// activator is registered (T-068, <see cref="ToastActivatorRegistration"/>) and the per-user Start menu shortcut with the
/// AppUserModelID and the CLSID of the activator is created or updated (<see cref="AppShortcutRegistration"/>), then a notifier
/// for that id is created.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed partial class WindowsToastPlatform : IToastPlatform, Installer.IToastRegistration
{
    private readonly AppShortcutRegistration _registration;
    private readonly Func<string?> _shortcutPath;
    private readonly string? _executablePath;
    private readonly ILogger<WindowsToastPlatform> _logger;
    private readonly ToastActivatorRegistration? _activatorRegistration;

    /// <param name="shortcutPath">Where the shortcut goes; evaluated on <see cref="Open"/> (thread of the service).</param>
    /// <param name="executablePath">The shortcut's target, normally <see cref="Environment.ProcessPath"/>.</param>
    /// <param name="appUserModelId">
    /// The app the toasts belong to: <see cref="WindowsAppIdentity.AppUserModelId"/>, or the test app of the test mode (T-067).
    /// </param>
    /// <param name="activatorRegistration">Registers <paramref name="activator"/>; <see langword="null"/> without one.</param>
    /// <param name="activator">
    /// The COM toast activator the shortcut names (T-068); <see langword="null"/>: none (clicks in the notification center
    /// then reach no app).
    /// </param>
    public WindowsToastPlatform(
        AppShortcutRegistration registration,
        Func<string?> shortcutPath,
        string? executablePath,
        ILogger<WindowsToastPlatform> logger,
        string appUserModelId = WindowsAppIdentity.AppUserModelId,
        ToastActivatorRegistration? activatorRegistration = null,
        ToastActivatorSettings? activator = null)
    {
        ArgumentNullException.ThrowIfNull(registration);
        ArgumentNullException.ThrowIfNull(shortcutPath);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentException.ThrowIfNullOrWhiteSpace(appUserModelId);
        _registration = registration;
        _shortcutPath = shortcutPath;
        _executablePath = executablePath;
        _logger = logger;
        AppUserModelId = appUserModelId;
        _activatorRegistration = activatorRegistration;
        Activator = activatorRegistration is null ? null : activator;
    }

    /// <summary>The app the toasts and the shortcut belong to.</summary>
    internal string AppUserModelId { get; }

    /// <summary>Where the shortcut goes (composition tests).</summary>
    internal Func<string?> ShortcutPath => _shortcutPath;

    /// <summary>The COM toast activator registered at startup (composition tests); <see langword="null"/> without one.</summary>
    internal ToastActivatorSettings? Activator { get; }

    public IToastSession Open(Action<string?> activated)
    {
        ArgumentNullException.ThrowIfNull(activated);
        Register();
        WindowsToastSession session = WindowsToastSession.Open(AppUserModelId, activated, LogToastFailed, LogReleaseFailed);
        LogSetting(session);
        return session;
    }

    /// <summary>
    /// Toasts of an app that Windows does not show (switched off in Settings, by policy) are dropped without an error;
    /// the setting is the only hint, so it goes to the log.
    /// </summary>
    private void LogSetting(WindowsToastSession session)
    {
        try
        {
            ToastNotifierSetting setting = session.Setting;
            if (setting == ToastNotifierSetting.Enabled)
            {
                LogEnabled(AppUserModelId);
            }
            else
            {
                LogDisabled(AppUserModelId, setting);
            }
        }
        catch (Exception ex)
        {
            LogSettingUnreadable(ex, AppUserModelId);
        }
    }

    private void LogToastFailed(int errorCode) => LogFailed(AppUserModelId, errorCode);

    /// <summary>
    /// The registrations toasts need: the COM toast activator first, then the Start menu shortcut, which names the activator
    /// only when it is registered (a CLSID nobody serves would swallow clicks in the notification center). Never throws.
    /// </summary>
    internal void Register()
    {
        // Without the shortcut Windows may drop the toasts silently; the notifier is still created, an
        // installer (T-017) or an earlier run may have registered the id already.
        try
        {
            string? shortcutPath = _shortcutPath();
            if (string.IsNullOrEmpty(_executablePath) || string.IsNullOrEmpty(shortcutPath))
            {
                LogNoShortcut();
                return;
            }

            _registration.Ensure(shortcutPath, new AppShortcut(_executablePath, AppUserModelId, RegisterActivator()));
        }
        catch (Exception ex)
        {
            LogShortcutFailed(ex);
        }
    }

    /// <returns>The CLSID for the shortcut, or <see langword="null"/> when there is no activator or it could not be registered.</returns>
    private Guid? RegisterActivator()
    {
        if (_activatorRegistration is null || Activator is null)
        {
            return null;
        }

        try
        {
            _activatorRegistration.Ensure(Activator.Clsid, Activator.CommandLine);
            return Activator.Clsid;
        }
        catch (Exception ex)
        {
            LogActivatorFailed(ex);
            return null;
        }
    }

    /// <summary>The same registrations as a start, made by the install hook of Velopack (T-017).</summary>
    void Installer.IToastRegistration.Register() => Register();

    [LoggerMessage(Level = LogLevel.Warning, Message = "Registering the toast activator failed; clicks on notifications in the notification center will not reach the app.")]
    private partial void LogActivatorFailed(Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The executable path or the Start menu folder is unknown; the notification shortcut was not created.")]
    private partial void LogNoShortcut();

    [LoggerMessage(Level = LogLevel.Warning, Message = "Creating the Start menu shortcut for notifications failed; Windows may not show them.")]
    private partial void LogShortcutFailed(Exception exception);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Windows shows the notifications of {AppUserModelId}.")]
    private partial void LogEnabled(string appUserModelId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Windows does not show the notifications of {AppUserModelId}: {Setting}. They can be switched on in Settings > System > Notifications.")]
    private partial void LogDisabled(string appUserModelId, ToastNotifierSetting setting);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Whether Windows shows the notifications of {AppUserModelId} could not be read.")]
    private partial void LogSettingUnreadable(Exception exception, string appUserModelId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Windows did not show a notification of {AppUserModelId} (error 0x{ErrorCode:X8}).")]
    private partial void LogFailed(string appUserModelId, int errorCode);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Releasing a toast or the toast notifier failed; the toast itself was shown.")]
    private partial void LogReleaseFailed(Exception exception);
}

/// <summary>
/// Shows toasts on the thread that opened it. Keeps the last <see cref="KeptToasts"/> toast objects (and their click
/// subscriptions) alive: a toast whose object was released no longer reports clicks to the app.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class WindowsToastSession : IToastSession
{
    /// <summary>A few screens of the notification center; older toasts are rarely clicked.</summary>
    internal const int KeptToasts = 20;

    private readonly WinRtToastFactory _factory;
    private readonly WinRtToastNotifier _notifier;
    private readonly ToastActivationCallback _callback;
    private readonly ToastFailedCallback _failedCallback;
    private readonly Action<Exception> _releaseFailed;
    private readonly Queue<WinRtToast> _recent = new(KeptToasts + 1);
    private bool _disposed;

    private WindowsToastSession(WinRtToastFactory factory, WinRtToastNotifier notifier, ToastActivationCallback callback, ToastFailedCallback failedCallback, Action<Exception> releaseFailed)
    {
        _factory = factory;
        _notifier = notifier;
        _callback = callback;
        _failedCallback = failedCallback;
        _releaseFailed = releaseFailed;
    }

    /// <param name="failed">Receives the error code when Windows does not show a toast (on an OS thread).</param>
    /// <param name="releaseFailed">Reports a failure to release a WinRT object; releasing never throws.</param>
    public static WindowsToastSession Open(string appUserModelId, Action<string?> activated, Action<int> failed, Action<Exception> releaseFailed)
    {
        ArgumentNullException.ThrowIfNull(activated);
        ArgumentNullException.ThrowIfNull(failed);
        ArgumentNullException.ThrowIfNull(releaseFailed);
        WinRtToastFactory factory = WinRtToastFactory.Create();
        WinRtToastNotifier? notifier = null;
        ToastActivationCallback? callback = null;
        try
        {
            notifier = factory.CreateNotifier(appUserModelId);
            callback = new ToastActivationCallback(activated);
            return new WindowsToastSession(factory, notifier, callback, new ToastFailedCallback(failed), releaseFailed);
        }
        catch
        {
            callback?.Dispose();
            notifier?.Dispose();
            factory.Dispose();
            throw;
        }
    }

    /// <summary>Whether Windows shows the toasts of the app (<see cref="WinRtToastNotifier.Setting"/>).</summary>
    public ToastNotifierSetting Setting
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _notifier.Setting;
        }
    }

    public void Show(string toastXml)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        WinRtToast toast = _factory.CreateToast(toastXml);
        try
        {
            toast.SubscribeActivated(_callback);
            toast.SubscribeFailed(_failedCallback);
            _notifier.Show(toast);
        }
        catch
        {
            toast.Dispose();
            throw;
        }

        // The toast is on screen: releasing the oldest one must not report this one as failed.
        _recent.Enqueue(toast);
        if (_recent.Count > KeptToasts)
        {
            Disposables.DisposeAll([_recent.Dequeue()], _releaseFailed);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        // Each independently: one failure must not leak the rest. The factory goes last, it leaves the COM apartment.
        List<IDisposable> resources = [.. _recent, _notifier, _callback, _failedCallback, _factory];
        _recent.Clear();
        Disposables.DisposeAll(resources, _releaseFailed);
    }
}
