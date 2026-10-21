using System.Runtime.Versioning;

using Microsoft.Extensions.Logging;

using PrusaTrayDiagnostic.App.Notifications;

namespace PrusaTrayDiagnostic.App.Platform.Windows;

/// <summary>The AppUserModelID under which the app shows toasts and groups its windows on the taskbar.</summary>
[SupportedOSPlatform("windows")]
internal static class WindowsAppIdentity
{
    /// <summary>
    /// Must stay stable: Windows keeps the user's notification settings (on/off, banners, sound) per id, and the
    /// installer of T-017 has to create its shortcut with the same id.
    /// </summary>
    public const string AppUserModelId = "PrusaTrayDiagnostic";

    /// <summary>
    /// Sets the id of this process. Call at startup, before any window is created; failures are ignored (only the
    /// taskbar grouping of auxiliary windows would differ, toasts use the id explicitly).
    /// </summary>
    /// <param name="appUserModelId">The id of the process: <see cref="AppUserModelId"/>, or the test id (T-044).</param>
    public static void ApplyToCurrentProcess(string appUserModelId = AppUserModelId) => _ = ComNative.SetCurrentProcessExplicitAppUserModelID(appUserModelId);
}

/// <summary>
/// Windows toasts through WinRT (<c>ToastNotificationManager</c>), for an unpackaged app: on <see cref="Open"/> the
/// per-user Start menu shortcut with the AppUserModelID is created or updated (<see cref="AppShortcutRegistration"/>),
/// then a notifier for that id is created.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed partial class WindowsToastPlatform : IToastPlatform
{
    private readonly AppShortcutRegistration _registration;
    private readonly Func<string?> _shortcutPath;
    private readonly string? _executablePath;
    private readonly ILogger<WindowsToastPlatform> _logger;

    /// <param name="shortcutPath">Where the shortcut goes; evaluated on <see cref="Open"/> (thread of the service).</param>
    /// <param name="executablePath">The shortcut's target, normally <see cref="Environment.ProcessPath"/>.</param>
    public WindowsToastPlatform(
        AppShortcutRegistration registration,
        Func<string?> shortcutPath,
        string? executablePath,
        ILogger<WindowsToastPlatform> logger)
    {
        ArgumentNullException.ThrowIfNull(registration);
        ArgumentNullException.ThrowIfNull(shortcutPath);
        ArgumentNullException.ThrowIfNull(logger);
        _registration = registration;
        _shortcutPath = shortcutPath;
        _executablePath = executablePath;
        _logger = logger;
    }

    public IToastSession Open(Action<string?> activated)
    {
        ArgumentNullException.ThrowIfNull(activated);
        RegisterShortcut();
        return WindowsToastSession.Open(WindowsAppIdentity.AppUserModelId, activated, LogReleaseFailed);
    }

    private void RegisterShortcut()
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

            _registration.Ensure(shortcutPath, new AppShortcut(_executablePath, WindowsAppIdentity.AppUserModelId));
        }
        catch (Exception ex)
        {
            LogShortcutFailed(ex);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "The executable path or the Start menu folder is unknown; the notification shortcut was not created.")]
    private partial void LogNoShortcut();

    [LoggerMessage(Level = LogLevel.Warning, Message = "Creating the Start menu shortcut for notifications failed; Windows may not show them.")]
    private partial void LogShortcutFailed(Exception exception);

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
    private readonly Action<Exception> _releaseFailed;
    private readonly Queue<WinRtToast> _recent = new(KeptToasts + 1);
    private bool _disposed;

    private WindowsToastSession(WinRtToastFactory factory, WinRtToastNotifier notifier, ToastActivationCallback callback, Action<Exception> releaseFailed)
    {
        _factory = factory;
        _notifier = notifier;
        _callback = callback;
        _releaseFailed = releaseFailed;
    }

    /// <param name="releaseFailed">Reports a failure to release a WinRT object; releasing never throws.</param>
    public static WindowsToastSession Open(string appUserModelId, Action<string?> activated, Action<Exception> releaseFailed)
    {
        WinRtToastFactory factory = WinRtToastFactory.Create();
        WinRtToastNotifier? notifier = null;
        try
        {
            notifier = factory.CreateNotifier(appUserModelId);
            ArgumentNullException.ThrowIfNull(releaseFailed);
            return new WindowsToastSession(factory, notifier, new ToastActivationCallback(activated), releaseFailed);
        }
        catch
        {
            notifier?.Dispose();
            factory.Dispose();
            throw;
        }
    }

    public void Show(string toastXml)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        WinRtToast toast = _factory.CreateToast(toastXml);
        try
        {
            toast.SubscribeActivated(_callback);
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
        List<IDisposable> resources = [.. _recent, _notifier, _callback, _factory];
        _recent.Clear();
        Disposables.DisposeAll(resources, _releaseFailed);
    }
}
