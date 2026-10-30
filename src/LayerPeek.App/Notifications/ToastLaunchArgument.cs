using System.Diagnostics.CodeAnalysis;

namespace LayerPeek.App.Notifications;

/// <summary>
/// The <c>launch</c> argument of a toast: Windows hands it back on click. Carries only the printer id (percent-encoded,
/// so it contains no characters that need XML escaping); the address is looked up at click time, so a click on an old
/// toast opens the current address and a removed printer opens nothing. The test notification of the settings window
/// (T-101) carries the fixed <see cref="Settings"/> instead: its click brings the settings window to the front on the
/// General tab; the notification about a new version (T-058) carries the fixed <see cref="Update"/>. Nothing else is ever
/// accepted (<see cref="IsWellFormed"/>).
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
    /// The argument of the notification about a new version (T-058): start the update action (the release page, or the update
    /// of a Velopack installation). Exactly this text; no printer id can produce it.
    /// </summary>
    public const string Update = "update=available";

    private const string PrinterPrefix = "printer=";

    /// <summary>Whether <paramref name="argument"/> is exactly <see cref="Update"/> (the update notification, T-058).</summary>
    public static bool IsUpdate([NotNullWhen(true)] string? argument) => string.Equals(argument, Update, StringComparison.Ordinal);

    /// <summary>Whether <paramref name="argument"/> is exactly <see cref="Settings"/> (the test notification, T-101).</summary>
    public static bool IsSettings([NotNullWhen(true)] string? argument) => string.Equals(argument, Settings, StringComparison.Ordinal);

    /// <exception cref="ArgumentException">The id is empty or too long (see <see cref="TryForPrinter"/>).</exception>
    public static string ForPrinter(string printerId) =>
        TryForPrinter(printerId, out string? argument)
            ? argument
            : throw new ArgumentException($"The encoded printer id is longer than {MaxLength} characters.", nameof(printerId));

    /// <returns><see langword="false"/> when the encoded argument would exceed <see cref="MaxLength"/>.</returns>
    public static bool TryForPrinter(string printerId, [NotNullWhen(true)] out string? argument)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(printerId);
        argument = null;
        // Encoding never makes the id shorter: absurdly long ids are rejected without encoding them.
        if (printerId.Length > MaxLength)
        {
            return false;
        }

        string encoded = PrinterPrefix + Uri.EscapeDataString(printerId);
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
    /// naming a printer; or is exactly <see cref="Settings"/> (T-101). Arguments that came from outside the app (another
    /// start, T-068) must pass this check.
    /// </summary>
    public static bool IsWellFormed([NotNullWhen(true)] string? argument)
    {
        if (IsSettings(argument) || IsUpdate(argument))
        {
            return true;
        }

        if (argument is null || argument.Length > MaxLength || !argument.StartsWith(PrinterPrefix, StringComparison.Ordinal))
        {
            return false;
        }

        foreach (char c in argument.AsSpan(PrinterPrefix.Length))
        {
            if (!char.IsAsciiLetterOrDigit(c) && c is not '-' and not '.' and not '_' and not '~' and not '%')
            {
                return false;
            }
        }

        return TryGetPrinterId(argument, out _);
    }

    /// <returns><see langword="false"/> for anything not created by <see cref="ForPrinter"/>.</returns>
    public static bool TryGetPrinterId([NotNullWhen(true)] string? argument, [NotNullWhen(true)] out string? printerId)
    {
        printerId = null;
        if (argument is null || !argument.StartsWith(PrinterPrefix, StringComparison.Ordinal))
        {
            return false;
        }

        string id = Uri.UnescapeDataString(argument[PrinterPrefix.Length..]);
        if (string.IsNullOrWhiteSpace(id))
        {
            return false;
        }

        printerId = id;
        return true;
    }
}
