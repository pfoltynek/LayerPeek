using System.Globalization;

using LayerPeek.App.Resources;
using LayerPeek.Core.Cameras.Timelapse;
using LayerPeek.Core.Notifications;

namespace LayerPeek.App.Notifications;

/// <summary>
/// The notification about a finished JPEG export of a timelapse (T-165), the same on every OS and short (user decision
/// 2026-11-04): the printer name as the title (the app name without one), "Timelapse exported to JPEG" with the number of
/// frames, or "Timelapse export to JPEG failed" with the reason by <see cref="CameraTimelapseExportProblem"/>, all in one
/// given language. A timelapse from a recording (T-164, <see cref="TimelapseExportKind.FromRecording"/>) says "Timelapse
/// created from recording" with the number of frames, or "Creating a timelapse from the recording failed" with the reason.
/// No launch argument (a click opens nothing of its own) and no tag.
/// </summary>
internal static class TimelapseExportNotificationText
{
    /// <summary>The title without a printer.</summary>
    internal const string AppName = "LayerPeek";

    /// <summary>The texts of <paramref name="notification"/> in <paramref name="textCulture"/>; the title is not cleaned here.</summary>
    public static NotificationLines Lines(TimelapseExportNotification notification, CultureInfo textCulture)
    {
        ArgumentNullException.ThrowIfNull(notification);
        ArgumentNullException.ThrowIfNull(textCulture);
        string title = !string.IsNullOrWhiteSpace(notification.PrinterName) ? notification.PrinterName
            : !string.IsNullOrWhiteSpace(notification.PrinterId) ? notification.PrinterId
            : AppName;
        if (notification.Kind == TimelapseExportKind.FromRecording)
        {
            return notification.Succeeded
                ? new NotificationLines(
                    title,
                    Text(nameof(Strings.NotificationTimelapseFromRecordingDone), textCulture),
                    string.Format(textCulture, Text(nameof(Strings.NotificationTimelapseFromRecordingFrames), textCulture), notification.Frames))
                : new NotificationLines(title, Text(nameof(Strings.NotificationTimelapseFromRecordingFailed), textCulture), FromRecordingReason(notification.Problem, textCulture));
        }

        return notification.Succeeded
            ? new NotificationLines(
                title,
                Text(nameof(Strings.NotificationTimelapseExportDone), textCulture),
                string.Format(textCulture, Text(nameof(Strings.NotificationTimelapseExportFrames), textCulture), notification.Frames))
            : new NotificationLines(title, Text(nameof(Strings.NotificationTimelapseExportFailed), textCulture), Reason(notification.Problem, textCulture));
    }

    /// <summary>The reason line of a failed timelapse from a recording (T-164); an unknown problem has the generic one.</summary>
    internal static string FromRecordingReason(CameraTimelapseExportProblem problem, CultureInfo culture) => problem switch
    {
        CameraTimelapseExportProblem.NotARecording => Text(nameof(Strings.NotificationTimelapseFromRecordingNotARecording), culture),
        CameraTimelapseExportProblem.Unreadable => Text(nameof(Strings.NotificationTimelapseFromRecordingUnreadable), culture),
        CameraTimelapseExportProblem.Unavailable => Text(nameof(Strings.NotificationTimelapseFromRecordingUnavailable), culture),
        CameraTimelapseExportProblem.WriteFailed => Text(nameof(Strings.NotificationTimelapseFromRecordingWriteFailed), culture),
        CameraTimelapseExportProblem.DiskFull => Text(nameof(Strings.NotificationRecordingDiskFull), culture),
        _ => Text(nameof(Strings.NotificationTimelapseExportError), culture),
    };

    /// <summary>The reason line of a failure; an unknown problem has the generic one.</summary>
    internal static string Reason(CameraTimelapseExportProblem problem, CultureInfo culture) => problem switch
    {
        CameraTimelapseExportProblem.NotATimelapse => Text(nameof(Strings.NotificationTimelapseExportNotATimelapse), culture),
        CameraTimelapseExportProblem.Unreadable => Text(nameof(Strings.NotificationTimelapseExportUnreadable), culture),
        CameraTimelapseExportProblem.Unavailable => Text(nameof(Strings.NotificationTimelapseExportUnavailable), culture),
        CameraTimelapseExportProblem.WriteFailed => Text(nameof(Strings.NotificationTimelapseExportWriteFailed), culture),
        CameraTimelapseExportProblem.DiskFull => Text(nameof(Strings.NotificationRecordingDiskFull), culture),
        _ => Text(nameof(Strings.NotificationTimelapseExportError), culture),
    };

    private static string Text(string name, CultureInfo culture) => Strings.ResourceManager.GetString(name, culture) ?? name;
}
