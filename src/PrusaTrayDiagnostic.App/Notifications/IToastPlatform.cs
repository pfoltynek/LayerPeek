namespace PrusaTrayDiagnostic.App.Notifications;

/// <summary>
/// The OS side of <see cref="ToastNotificationService"/> (WinRT on Windows), replaceable by a fake in tests.
/// Used only from the service's notification thread.
/// </summary>
internal interface IToastPlatform
{
    /// <summary>
    /// Prepares showing toasts on the calling thread (app identity, notifier). Called once, before the first toast.
    /// </summary>
    /// <param name="activated">
    /// Called with the toast's launch argument when the user clicks a toast, on an OS thread; <see langword="null"/>
    /// when the argument cannot be read.
    /// </param>
    /// <exception cref="Exception">Toasts are not available; the service then discards notifications.</exception>
    IToastSession Open(Action<string?> activated);
}

/// <summary>Shows toasts. Used and disposed only on the thread that opened it.</summary>
internal interface IToastSession : IDisposable
{
    /// <summary>Shows a toast described by <paramref name="toastXml"/> (built by <see cref="ToastContentBuilder"/>).</summary>
    void Show(string toastXml);
}
