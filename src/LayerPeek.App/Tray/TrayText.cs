using LayerPeek.App.Resources;
using LayerPeek.Core.Printers;

namespace LayerPeek.App.Tray;

/// <summary>Text helpers shared by the tray tooltip and the tray menu.</summary>
internal static class TrayText
{
    public const string Ellipsis = "…";

    /// <summary>Localized state name (current UI culture); <see langword="null"/> means "Connecting…" (not polled yet).</summary>
    public static string StateName(PrinterState? state) => state switch
    {
        null => Strings.StateConnecting,
        PrinterState.Idle => Strings.StateIdle,
        PrinterState.Busy => Strings.StateBusy,
        PrinterState.Printing => Strings.StatePrinting,
        PrinterState.Paused => Strings.StatePaused,
        PrinterState.Finished => Strings.StateFinished,
        PrinterState.Stopped => Strings.StateStopped,
        PrinterState.Error => Strings.StateError,
        PrinterState.Attention => Strings.StateAttention,
        PrinterState.Offline => Strings.StateOffline,
        PrinterState.Unauthorized => Strings.StateUnauthorized,
        _ => Strings.StateUnknown,
    };

    /// <summary>
    /// Shortens <paramref name="text"/> to at most <paramref name="maxLength"/> UTF-16 units, ending with "…".
    /// Never leaves half of a surrogate pair (e.g. an emoji) in front of the ellipsis.
    /// </summary>
    public static string Ellipsize(string text, int maxLength)
    {
        if (text.Length <= maxLength)
        {
            return text;
        }

        int cut = maxLength - Ellipsis.Length;
        if (cut <= 0)
        {
            return Ellipsis[..Math.Max(maxLength, 0)];
        }

        if (char.IsHighSurrogate(text[cut - 1]))
        {
            cut--;
        }

        return string.Concat(text.AsSpan(0, cut), Ellipsis);
    }
}
