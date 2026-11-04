using System.Diagnostics.CodeAnalysis;

using LayerPeek.App.Settings;

namespace LayerPeek.App.Notifications;

/// <summary>
/// The <c>launch</c> argument of a toast: Windows hands it back on click. Carries only the printer id (percent-encoded,
/// so it contains no characters that need XML escaping); the address is looked up at click time, so a click on an old
/// toast opens the current address and a removed printer opens nothing. The test notification of the settings window
/// (T-101) carries the fixed <see cref="Settings"/> instead: its click brings the settings window to the front on the
/// General tab; the notification about a failed camera recording (T-146) the fixed <see cref="RecordingSettings"/> (the
/// Recording tab); the notification about a new version (T-058) the fixed <see cref="Update"/>. The notification of a
/// suspected print failure (T-154) carries <c>camera=</c> and the printer id (its click opens the printer's camera window),
/// its "False alarm" button <c>false-alarm=</c> and the alarm id (<see cref="ForFalseAlarm"/>). Nothing else is ever
/// accepted (<see cref="IsWellFormed"/>): every argument comes back from the OS and is untrusted.
/// </summary>
internal static class ToastLaunchArgument
{
    /// <summary>
    /// Longest argument. Keeps the toast payload (limited by Windows to about 5 kB) within bounds whatever the id;
    /// ids of normal length are far below it.
    /// </summary>
    public const int MaxLength = 512;

    /// <summary>
    /// The argument of the test notification (T-101): open the settings window on the General tab. Exactly this text; no
    /// printer id can produce it (those always start with <c>printer=</c>).
    /// </summary>
    public const string Settings = "settings=general";

    /// <summary>
    /// The argument of the notification about a failed camera recording (T-146): open the settings window on the Recording
    /// tab, where its folder and limit are. Exactly this text; no printer id can produce it.
    /// </summary>
    public const string RecordingSettings = "settings=recording";

    /// <summary>
    /// The argument of the notification about a new version (T-058): start the update action (the release page, or the update
    /// of a Velopack installation). Exactly this text; no printer id can produce it.
    /// </summary>
    public const string Update = "update=available";

    /// <summary>Longest alarm id of <see cref="ForFalseAlarm"/> (the detection service uses 32 hex digits).</summary>
    public const int MaxAlarmIdLength = 64;

    private const string PrinterPrefix = "printer=";

    private const string CameraPrefix = "camera=";

    private const string FalseAlarmPrefix = "false-alarm=";

    /// <summary>Whether <paramref name="argument"/> is exactly <see cref="Update"/> (the update notification, T-058).</summary>
    public static bool IsUpdate([NotNullWhen(true)] string? argument) => string.Equals(argument, Update, StringComparison.Ordinal);

    /// <summary>
    /// Whether <paramref name="argument"/> is exactly <see cref="Settings"/> (the test notification, T-101) or
    /// <see cref="RecordingSettings"/> (a failed recording, T-146): a click that opens the settings window.
    /// </summary>
    public static bool IsSettings([NotNullWhen(true)] string? argument) => TryGetSettingsPage(argument, out _);

    /// <summary>The tab a settings argument (<see cref="IsSettings"/>) opens; <see langword="false"/> for any other argument.</summary>
    public static bool TryGetSettingsPage([NotNullWhen(true)] string? argument, out SettingsPage page)
    {
        if (string.Equals(argument, Settings, StringComparison.Ordinal))
        {
            page = SettingsPage.General;
            return true;
        }

        page = SettingsPage.Recording;
        return string.Equals(argument, RecordingSettings, StringComparison.Ordinal);
    }

    /// <exception cref="ArgumentException">The id is empty or too long (see <see cref="TryForPrinter"/>).</exception>
    public static string ForPrinter(string printerId) =>
        TryForPrinter(printerId, out string? argument)
            ? argument
            : throw new ArgumentException($"The encoded printer id is longer than {MaxLength} characters.", nameof(printerId));

    /// <returns><see langword="false"/> when the encoded argument would exceed <see cref="MaxLength"/>.</returns>
    public static bool TryForPrinter(string printerId, [NotNullWhen(true)] out string? argument) => TryEncode(PrinterPrefix, printerId, out argument);

    /// <summary>
    /// The argument of a print failure notification (T-154): its click opens the camera window of <paramref name="printerId"/>.
    /// </summary>
    /// <returns><see langword="false"/> when the encoded argument would exceed <see cref="MaxLength"/>.</returns>
    public static bool TryForCamera(string printerId, [NotNullWhen(true)] out string? argument) => TryEncode(CameraPrefix, printerId, out argument);

    /// <summary>
    /// The argument of the "False alarm" button of a print failure notification (T-154): <c>false-alarm=</c> and
    /// <paramref name="alarmId"/>.
    /// </summary>
    /// <exception cref="ArgumentException">The id is not 1–<see cref="MaxAlarmIdLength"/> characters <c>a-z</c>, <c>0-9</c> and <c>-</c>.</exception>
    public static string ForFalseAlarm(string alarmId) =>
        IsValidAlarmId(alarmId) ? FalseAlarmPrefix + alarmId : throw new ArgumentException("An alarm id is 1–64 lower-case ASCII letters, digits and dashes.", nameof(alarmId));

    /// <summary>Whether <paramref name="alarmId"/> can travel in <see cref="ForFalseAlarm"/>.</summary>
    public static bool IsValidAlarmId([NotNullWhen(true)] string? alarmId)
    {
        if (string.IsNullOrEmpty(alarmId) || alarmId.Length > MaxAlarmIdLength)
        {
            return false;
        }

        foreach (char c in alarmId)
        {
            if (!(char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c == '-'))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>The alarm id of a "False alarm" argument; <see langword="false"/> for anything not created by <see cref="ForFalseAlarm"/>.</summary>
    public static bool TryGetFalseAlarmId([NotNullWhen(true)] string? argument, [NotNullWhen(true)] out string? alarmId)
    {
        alarmId = null;
        if (argument is null || argument.Length > FalseAlarmPrefix.Length + MaxAlarmIdLength || !argument.StartsWith(FalseAlarmPrefix, StringComparison.Ordinal))
        {
            return false;
        }

        string id = argument[FalseAlarmPrefix.Length..];
        if (!IsValidAlarmId(id))
        {
            return false;
        }

        alarmId = id;
        return true;
    }

    /// <summary>The printer of a camera argument (<see cref="TryForCamera"/>); <see langword="false"/> for anything else.</summary>
    public static bool TryGetCameraPrinterId([NotNullWhen(true)] string? argument, [NotNullWhen(true)] out string? printerId) =>
        TryDecode(CameraPrefix, argument, out printerId);

    private static bool TryEncode(string prefix, string printerId, [NotNullWhen(true)] out string? argument)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(printerId);
        argument = null;
        // Encoding never makes the id shorter: absurdly long ids are rejected without encoding them.
        if (printerId.Length > MaxLength)
        {
            return false;
        }

        string encoded = prefix + Uri.EscapeDataString(printerId);
        if (encoded.Length > MaxLength)
        {
            return false;
        }

        argument = encoded;
        return true;
    }

    /// <summary>
    /// Whether <paramref name="argument"/> has exactly the form <see cref="ForPrinter"/> creates: the prefix, then only
    /// unreserved characters and percent escapes (ASCII, nothing to quote or escape), at most <see cref="MaxLength"/>,
    /// naming a printer; or is exactly one of the fixed arguments (<see cref="IsSettings"/>, <see cref="Update"/>). Arguments that came from outside the app (another
    /// start, T-068) must pass this check.
    /// </summary>
    public static bool IsWellFormed([NotNullWhen(true)] string? argument)
    {
        if (IsSettings(argument) || IsUpdate(argument) || TryGetFalseAlarmId(argument, out _))
        {
            return true;
        }

        return IsEncodedId(PrinterPrefix, argument) || IsEncodedId(CameraPrefix, argument);
    }

    /// <returns><see langword="false"/> for anything not created by <see cref="ForPrinter"/>.</returns>
    public static bool TryGetPrinterId([NotNullWhen(true)] string? argument, [NotNullWhen(true)] out string? printerId) =>
        TryDecode(PrinterPrefix, argument, out printerId);

    /// <summary>The prefix, then only unreserved characters and percent escapes, at most <see cref="MaxLength"/>, naming an id.</summary>
    private static bool IsEncodedId(string prefix, [NotNullWhen(true)] string? argument) => TryDecode(prefix, argument, out _);

    /// <summary>
    /// The id of an argument of exactly the form <see cref="TryEncode"/> creates (the prefix, then only unreserved characters
    /// and percent escapes, at most <see cref="MaxLength"/>); anything else is rejected before it is decoded, also on the
    /// click path that does not ask <see cref="IsWellFormed"/> first.
    /// </summary>
    private static bool TryDecode(string prefix, [NotNullWhen(true)] string? argument, [NotNullWhen(true)] out string? id)
    {
        id = null;
        if (argument is null || argument.Length > MaxLength || !argument.StartsWith(prefix, StringComparison.Ordinal))
        {
            return false;
        }

        foreach (char c in argument.AsSpan(prefix.Length))
        {
            if (!char.IsAsciiLetterOrDigit(c) && c is not '-' and not '.' and not '_' and not '~' and not '%')
            {
                return false;
            }
        }

        string decoded = Uri.UnescapeDataString(argument[prefix.Length..]);
        if (string.IsNullOrWhiteSpace(decoded))
        {
            return false;
        }

        id = decoded;
        return true;
    }
}
