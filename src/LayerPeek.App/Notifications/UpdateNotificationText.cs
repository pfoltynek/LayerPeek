using System.Globalization;

using LayerPeek.App.Resources;

namespace LayerPeek.App.Notifications;

/// <summary>
/// The notification about a new version (T-058), the same on every OS: a title and one line in the language of the app, the
/// fixed <see cref="Identifier"/> (macOS: the identifier of the request; Windows: the tag of the toast), so that the
/// notification of a newer version replaces the previous one, and the launch argument <see cref="ToastLaunchArgument.Update"/>:
/// a click starts the update action (the release page, or the update of a Velopack installation).
/// </summary>
internal static class UpdateNotificationText
{
    /// <summary>Identifier of the update notification (at most 64 characters for a Windows tag).</summary>
    public const string Identifier = "layerpeek-update-available";

    /// <summary>The title in <paramref name="textCulture"/>.</summary>
    public static string Title(CultureInfo textCulture) => Text(nameof(Strings.NotificationUpdateTitle), textCulture);

    /// <summary>The text in <paramref name="textCulture"/>, with <paramref name="version"/>.</summary>
    public static string Body(string version, CultureInfo textCulture)
    {
        ArgumentNullException.ThrowIfNull(version);
        return string.Format(textCulture, Text(nameof(Strings.NotificationUpdateBody), textCulture), version);
    }

    private static string Text(string name, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);
        return Strings.ResourceManager.GetString(name, culture) ?? name;
    }
}
