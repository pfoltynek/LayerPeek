using LayerPeek.App.Platform.Windows;

namespace LayerPeek.App.Notifications;

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
    /// <param name="tag">
    /// The tag of the toast; a toast with the same tag replaces the earlier one in the notification center (the test
    /// notification, T-101). <see langword="null"/>: none (every toast stays).
    /// </param>
    void Show(string toastXml, string? tag);

    /// <summary>Whether Windows shows the toasts of the app (switched off in Settings, by policy...; T-010, T-101).</summary>
    /// <exception cref="Exception">Windows did not say.</exception>
    ToastNotifierSetting Setting { get; }
}

/// <summary>The toasts an app left in the notification center (WinRT on Windows), replaceable by a fake in tests.</summary>
internal interface IToastHistory
{
    /// <summary>Removes every toast of <paramref name="appUserModelId"/> from the notification center.</summary>
    /// <exception cref="Exception">The history is not available or Windows refused.</exception>
    void Clear(string appUserModelId);
}
