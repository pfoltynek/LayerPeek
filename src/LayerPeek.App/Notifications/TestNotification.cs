using System.Globalization;

using LayerPeek.App.Resources;

namespace LayerPeek.App.Notifications;

/// <summary>
/// The test notification of the settings window (T-101), the same on every OS: a title and one line of text in the language
/// of the app, the fixed <see cref="Identifier"/> (macOS: the identifier of the request; Windows: the tag of the toast), so
/// that a new test notification replaces the previous one instead of piling up in the notification center, and the launch
/// argument <see cref="ToastLaunchArgument.Settings"/>: a click brings the settings window to the front on its General tab.
/// </summary>
internal static class TestNotification
{
    /// <summary>
    /// Identifier of the test notification. Windows allows at most 64 characters for a tag; the notifications of printer
    /// events use GUIDs (macOS) or no tag (Windows), so it never replaces one of them.
    /// </summary>
    public const string Identifier = "layerpeek-test-notification";

    /// <summary>The title in <paramref name="textCulture"/>.</summary>
    public static string Title(CultureInfo textCulture) => Text(nameof(Strings.NotificationTestTitle), textCulture);

    /// <summary>The text in <paramref name="textCulture"/>.</summary>
    public static string Body(CultureInfo textCulture) => Text(nameof(Strings.NotificationTestBody), textCulture);

    private static string Text(string name, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);
        return Strings.ResourceManager.GetString(name, culture) ?? name;
    }
}
