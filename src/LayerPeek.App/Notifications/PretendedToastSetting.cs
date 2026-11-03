#if DEBUG
using Microsoft.Extensions.Logging;

using LayerPeek.App.Platform.Windows;

namespace LayerPeek.App.Notifications;

/// <summary>
/// Debug builds only, effective only in the isolated test mode with toasts (T-133, user decision 2026-11-03): what the
/// <see cref="ToastNotificationService"/> reads instead of Windows' own <c>ToastNotifier.Setting</c> while the end-to-end
/// test of the test notification pretends that the notifications of the test app are switched off in Windows. Switching
/// them off for real cannot be undone by a program (Windows keeps the "off" of an app id for good), so the warning of the
/// settings window and its button to Settings are tested with this stand-in; the real reading of Windows' setting stays
/// covered by <see cref="ToastNotificationService.StateOf"/> and a manual check. Set through the test command
/// <c>notifications:off</c> / <c>notifications:on</c> of the instance channel (<see cref="SingleInstance.InstanceProtocol"/>).
/// Thread-safe: written on the UI thread, read on the toast thread.
/// </summary>
internal sealed partial class PretendedToastSetting(ILogger<PretendedToastSetting> logger)
{
    private const int None = -1;

    private readonly ILogger<PretendedToastSetting> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    private int _value = None;

    /// <summary>The pretended setting, or <see langword="null"/>: Windows' own setting is read.</summary>
    public ToastNotifierSetting? Value => Volatile.Read(ref _value) is var value and not None ? (ToastNotifierSetting)value : null;

    /// <summary>Pretends <paramref name="setting"/> from now on; <see langword="null"/> reads Windows' own setting again.</summary>
    public void Pretend(ToastNotifierSetting? setting)
    {
        Volatile.Write(ref _value, setting is { } pretended ? (int)pretended : None);
        if (setting is { } value)
        {
            LogPretended(value);
        }
        else
        {
            LogCleared();
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Test mode: Windows' notification setting of the app is pretended to be {Setting}.")]
    private partial void LogPretended(ToastNotifierSetting setting);

    [LoggerMessage(Level = LogLevel.Information, Message = "Test mode: Windows' own notification setting of the app is read again.")]
    private partial void LogCleared();
}
#endif
