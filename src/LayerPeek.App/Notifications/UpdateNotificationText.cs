using System.Globalization;

using LayerPeek.App.Resources;

namespace LayerPeek.App.Notifications;

/// <summary>
/// The notification about a new version (T-058), the same on every OS: a title and one line in the language of the app, the
/// fixed <see cref="Identifier"/> (macOS: the identifier of the request; Windows: the tag of the toast), so that the
/// notification of a newer version replaces the previous one, and the launch argument <see cref="ToastLaunchArgument.Update"/>:
/// a click starts the update action (the release page, or the update of a Velopack installation).
/// <para>
/// T-181: the notification about a refused update (its feed failed the signature check) has its own fixed
/// <see cref="RejectedIdentifier"/> (a later refusal replaces it, never the offer of a good update) and the launch argument
/// <see cref="ToastLaunchArgument.UpdateSettings"/>: a click opens the Startup and updates tab, where the error stays.
/// </para>
/// </summary>
internal static class UpdateNotificationText
{
    /// <summary>Identifier of the update notification (at most 64 characters for a Windows tag).</summary>
    public const string Identifier = "layerpeek-update-available";

    /// <summary>Identifier of the notification about a refused update (T-181).</summary>
    public const string RejectedIdentifier = "layerpeek-update-rejected";

    /// <summary>The title in <paramref name="textCulture"/>.</summary>
    public static string Title(CultureInfo textCulture) => Text(nameof(Strings.NotificationUpdateTitle), textCulture);

    /// <summary>The text in <paramref name="textCulture"/>, with <paramref name="version"/>.</summary>
    public static string Body(string version, CultureInfo textCulture)
    {
        ArgumentNullException.ThrowIfNull(version);
        return string.Format(textCulture, Text(nameof(Strings.NotificationUpdateBody), textCulture), version);
    }

    /// <summary>The title of the notification about a refused update (T-181) in <paramref name="textCulture"/>.</summary>
    public static string RejectedTitle(CultureInfo textCulture) => Text(nameof(Strings.NotificationUpdateRejectedTitle), textCulture);

    /// <summary>
    /// The text of the notification about a refused update in <paramref name="textCulture"/>, with the version the feed claims
    /// when it names one.
    /// </summary>
    public static string RejectedBody(string? version, CultureInfo textCulture) => version is null
        ? Text(nameof(Strings.NotificationUpdateRejectedBodyNoVersion), textCulture)
        : string.Format(textCulture, Text(nameof(Strings.NotificationUpdateRejectedBody), textCulture), version);

    private static string Text(string name, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);
        return Strings.ResourceManager.GetString(name, culture) ?? name;
    }
}
