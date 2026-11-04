using System.Globalization;

using LayerPeek.App.Detection;
using LayerPeek.App.Resources;
using LayerPeek.Core.Detection;

namespace LayerPeek.App.Notifications;

/// <summary>
/// The notification of a suspected print failure (T-154, user decisions 2026-11-04), the same on every OS: the printer name as
/// the title, "Possible print failure – &lt;kind&gt;" and the job file name, all in one given language; the snapshot of the
/// alarm as its image; a click opens the camera window of the printer (<see cref="ToastLaunchArgument.TryForCamera"/>) and
/// the button "False alarm" records the alarm locally (<see cref="ToastLaunchArgument.ForFalseAlarm"/>). The sound is the one
/// of a printer error. No tag: two alarms are two notifications.
/// </summary>
internal static class PrintFailureNotificationText
{
    /// <summary>The texts of <paramref name="notification"/> in <paramref name="textCulture"/>; not cleaned or escaped here.</summary>
    public static NotificationLines Lines(PrintFailureNotification notification, CultureInfo textCulture)
    {
        ArgumentNullException.ThrowIfNull(notification);
        ArgumentNullException.ThrowIfNull(textCulture);
        PrintFailureAlarm alarm = notification.Alarm;
        string title = string.IsNullOrWhiteSpace(alarm.PrinterName) ? alarm.PrinterId : alarm.PrinterName;
        string body = string.Format(CultureInfo.CurrentCulture, Text(nameof(Strings.NotificationPrintFailure), textCulture), DetectorTexts.Name(alarm.DetectorId, textCulture));
        return new NotificationLines(title, body, string.IsNullOrWhiteSpace(alarm.FileName) ? null : alarm.FileName);
    }

    /// <summary>The label of the "False alarm" button.</summary>
    public static string FalseAlarmAction(CultureInfo textCulture) => Text(nameof(Strings.NotificationFalseAlarm), textCulture);

    private static string Text(string name, CultureInfo culture) => Strings.ResourceManager.GetString(name, culture) ?? name;
}
