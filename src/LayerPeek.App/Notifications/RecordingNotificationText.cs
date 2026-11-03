using System.Globalization;

using LayerPeek.App.Resources;
using LayerPeek.Core.Cameras.Recording;
using LayerPeek.Core.Notifications;

namespace LayerPeek.App.Notifications;

/// <summary>
/// The notification about a failed camera recording (T-146), the same on every OS: the printer name as the title, "Camera
/// recording failed" and the reason by <see cref="CameraRecordingProblem"/>, all in one given language. A click opens the
/// settings window like the test notification (<see cref="ToastLaunchArgument.Settings"/>: the folder and the limit are
/// there); no tag, so two failures of different printers are two notifications.
/// </summary>
internal static class RecordingNotificationText
{
    /// <summary>The texts of <paramref name="notification"/> in <paramref name="textCulture"/>; the title is not cleaned here.</summary>
    public static NotificationLines Lines(RecordingNotification notification, CultureInfo textCulture)
    {
        ArgumentNullException.ThrowIfNull(notification);
        ArgumentNullException.ThrowIfNull(textCulture);
        string title = string.IsNullOrWhiteSpace(notification.PrinterName) ? notification.PrinterId : notification.PrinterName;
        return new NotificationLines(title, Text(nameof(Strings.NotificationRecordingFailed), textCulture), Reason(notification, textCulture));
    }

    /// <summary>The reason line; an unknown problem has the generic one.</summary>
    internal static string Reason(RecordingNotification notification, CultureInfo culture) => notification.Problem switch
    {
        CameraRecordingProblem.Unavailable => Text(nameof(Strings.NotificationRecordingUnavailable), culture),
        CameraRecordingProblem.CameraUnavailable => Text(nameof(Strings.NotificationRecordingCameraUnavailable), culture),
        CameraRecordingProblem.FolderUnavailable => Text(nameof(Strings.NotificationRecordingFolderUnavailable), culture),
        CameraRecordingProblem.DiskSpaceLow => Format(culture, Text(nameof(Strings.NotificationRecordingDiskSpaceLow), culture), notification.MinFreeGigabytes),
        CameraRecordingProblem.DiskFull => Text(nameof(Strings.NotificationRecordingDiskFull), culture),
        CameraRecordingProblem.WriteFailed => Text(nameof(Strings.NotificationRecordingWriteFailed), culture),
        CameraRecordingProblem.TooSlow => Text(nameof(Strings.NotificationRecordingTooSlow), culture),
        CameraRecordingProblem.StreamFailed => Text(nameof(Strings.NotificationRecordingStreamFailed), culture),
        CameraRecordingProblem.SizeLimitReached => Format(culture, Text(nameof(Strings.NotificationRecordingSizeLimitReached), culture), notification.SizeLimitGigabytes),
        _ => Text(nameof(Strings.NotificationRecordingStreamFailed), culture),
    };

    private static string Format(CultureInfo culture, string format, int value) => string.Format(culture, format, value);

    private static string Text(string name, CultureInfo culture) => Strings.ResourceManager.GetString(name, culture) ?? name;
}
