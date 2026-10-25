using System.Globalization;

using LayerPeek.App.Localization;
using LayerPeek.App.Resources;
using LayerPeek.Core.Cameras;

namespace LayerPeek.App.Settings;

/// <summary>An item of the "Open as" selection of the camera window (T-035).</summary>
public sealed class CameraStartModeOption(CameraWindowStartMode value, Func<string> text) : LocalizedOption(text)
{
    public CameraWindowStartMode Value { get; } = value;
}

/// <summary>An item of the position selection of the camera window: a corner or the center (T-035).</summary>
public sealed class CameraCornerOption(CameraWindowCorner value, Func<string> text) : LocalizedOption(text)
{
    public CameraWindowCorner Value { get; } = value;
}

/// <summary>An item of the monitor selection of the camera window; <see langword="null"/> = the primary monitor (T-035).</summary>
public sealed class CameraMonitorOption : LocalizedOption
{
    internal CameraMonitorOption(CameraMonitorId? value, bool isConnected, Func<string> text)
        : base(text)
    {
        Value = value;
        IsConnected = isConnected;
    }

    internal CameraMonitorId? Value { get; }

    /// <summary>Shown as "not connected": the saved monitor is not connected now (<see cref="CameraWindowDefaultPlacement.IsConnected"/>).</summary>
    public bool IsConnected { get; }
}

/// <summary>
/// The text of a camera window size in the editable size box (T-035, T-033 Q5): "960 × 540" with non-breaking spaces
/// around "×", like every resolution in the app. Input accepts "×", "x" or "X" with any (or no) white space around.
/// </summary>
public static class CameraWindowSizeText
{
    private const string Separator = " × ";

    /// <summary>The presets of the size box, smallest first.</summary>
    public static IReadOnlyList<string> Presets { get; } = [.. CameraWindowSize.Presets.Select(Format)];

    public static string Format(CameraWindowSize size)
    {
        ArgumentNullException.ThrowIfNull(size);
        return string.Concat(size.Width.ToString(CultureInfo.InvariantCulture), Separator, size.Height.ToString(CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// Width and height typed by the user ("1280 × 720", "1280x720", " 1280 X 720 "); whole numbers without signs or
    /// separators. Says nothing about the range (see <see cref="CameraWindowSize.IsValid"/>).
    /// </summary>
    public static bool TryParse(string? text, out int width, out int height)
    {
        width = 0;
        height = 0;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        ReadOnlySpan<char> value = text.AsSpan().Trim();
        int separator = value.IndexOfAny('×', 'x', 'X');
        if (separator < 0 || value[(separator + 1)..].IndexOfAny('×', 'x', 'X') >= 0)
        {
            return false;
        }

        return TryParseNumber(value[..separator], out width) && TryParseNumber(value[(separator + 1)..], out height);
    }

    /// <summary>The size typed by the user when it is a usable window size; otherwise <see langword="null"/>.</summary>
    public static CameraWindowSize? Parse(string? text) =>
        TryParse(text, out int width, out int height) && CameraWindowSize.IsValid(width, height) ? new CameraWindowSize(width, height) : null;

    /// <summary>
    /// The size of the mini preview typed by the user when it is usable (at least <see cref="CameraWindowSize.MinMiniWidth"/>
    /// wide, T-089 Q4); otherwise <see langword="null"/>.
    /// </summary>
    public static CameraWindowSize? ParseMini(string? text) =>
        TryParse(text, out int width, out int height) && CameraWindowSize.IsValidMini(width, height) ? new CameraWindowSize(width, height) : null;

    private static bool TryParseNumber(ReadOnlySpan<char> text, out int value)
    {
        ReadOnlySpan<char> digits = text.Trim();
        // Only ASCII digits: NumberStyles.None still accepts other scripts' digits in some runtimes, and signs or
        // separators would make "1,280" or "+1280" look valid.
        value = 0;
        return digits.Length is > 0 and <= 9
            && !digits.ContainsAnyExceptInRange('0', '9')
            && int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out value);
    }
}
