using System.Globalization;

using LayerPeek.App.Resources;
using LayerPeek.App.Tray;
using LayerPeek.Core.Printers;

namespace LayerPeek.App.Diagnostics;

/// <summary>
/// Display texts of the diagnostics window: numbers in the current culture, units and words in the current UI culture.
/// Unknown values (null, NaN, ±∞) are shown as <see cref="Strings.DiagnosticsMissingValue"/>, never as "NaN" or 0.
/// </summary>
internal static class DiagnosticsFormatter
{
    /// <summary>Decimals of an actual temperature (the firmware reports tenths).</summary>
    public const int TemperatureDecimals = 1;

    /// <summary>Decimals of a target temperature (set in whole degrees).</summary>
    public const int TargetDecimals = 0;

    /// <summary>Decimals of the Z position (the firmware moves in hundredths of a millimetre).</summary>
    public const int AxisZDecimals = 2;

    /// <summary>Decimals of the nozzle diameter (0.25, 0.40, 0.60...).</summary>
    public const int NozzleDiameterDecimals = 2;

    public static string Missing => Strings.DiagnosticsMissingValue;

    /// <summary>Actual temperature, e.g. "215.3 °C".</summary>
    public static string Temperature(double? celsius) =>
        IsKnown(celsius, out double value) ? Unit(Strings.DiagnosticsTemperatureValue, Number(value, TemperatureDecimals)) : Missing;

    /// <summary>Value axis label of the chart, e.g. "200 °C".</summary>
    public static string AxisTemperature(double celsius) =>
        Unit(Strings.DiagnosticsTemperatureValue, celsius.ToString("N0", CultureInfo.CurrentCulture));

    /// <summary>Target temperature, e.g. "215 °C"; 0 means the heater is off.</summary>
    public static string TargetTemperature(double? celsius)
    {
        if (!IsKnown(celsius, out double value))
        {
            return Missing;
        }

        return Math.Round(value, TargetDecimals) == 0 ? Strings.DiagnosticsHeaterOff : Unit(Strings.DiagnosticsTemperatureValue, Number(value, TargetDecimals));
    }

    /// <summary>Fan speed, e.g. "7,200 RPM".</summary>
    public static string Rpm(int? rpm) =>
        rpm is { } value ? Unit(Strings.DiagnosticsRpmValue, value.ToString("N0", CultureInfo.CurrentCulture)) : Missing;

    /// <summary>Multiplier in percent, e.g. "100%" (English) or "100 %" (Czech).</summary>
    public static string Percent(int? percent) =>
        percent is { } value ? Unit(Strings.DiagnosticsPercentValue, value.ToString("N0", CultureInfo.CurrentCulture)) : Missing;

    /// <summary>Length, e.g. "14.00 mm".</summary>
    public static string Millimetres(double? millimetres, int decimals) =>
        IsKnown(millimetres, out double value) ? Unit(Strings.DiagnosticsMillimetreValue, Number(value, decimals)) : Missing;

    /// <summary>
    /// Local time of <paramref name="timestamp"/> in <paramref name="zone"/> with seconds; the date is added when it is
    /// not the same local day as <paramref name="now"/>.
    /// </summary>
    public static string Timestamp(DateTimeOffset? timestamp, DateTimeOffset now, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(zone);
        if (timestamp is not { } value)
        {
            return Missing;
        }

        DateTimeOffset local = TimeZoneInfo.ConvertTime(value, zone);
        DateTimeOffset localNow = TimeZoneInfo.ConvertTime(now, zone);
        // "T" = long time (with seconds), "G" = short date + long time.
        return local.ToString(local.Date == localNow.Date ? "T" : "G", CultureInfo.CurrentCulture);
    }

    public static string YesNo(bool value) => value ? Strings.DiagnosticsYes : Strings.DiagnosticsNo;

    /// <summary>
    /// The translated reason why a printer gives no data (T-044), or <see langword="null"/> for
    /// <see cref="PrinterProblem.None"/> (the window then shows the technical status message).
    /// </summary>
    public static string? Problem(PrinterProblem problem) => problem switch
    {
        PrinterProblem.InvalidAddress => Strings.DiagnosticsProblemInvalidAddress,
        // T-097: the window shows LocalNetworkAccessMayBeOff instead where ILocalNetworkPrivacy says so.
        PrinterProblem.NotReachable or PrinterProblem.HostUnreachable => Strings.DiagnosticsProblemNotReachable,
        PrinterProblem.Timeout => Strings.SettingsTestTimeout,
        PrinterProblem.SecureConnectionFailed => Strings.DiagnosticsProblemSecureConnection,
        PrinterProblem.PasswordRejected => Strings.SettingsTestPasswordRejected,
        PrinterProblem.AccessDenied => Strings.SettingsTestAccessDenied,
        PrinterProblem.PasswordMissing => Strings.DiagnosticsProblemPasswordMissing,
        PrinterProblem.PasswordUnreadable => Strings.SettingsTestPasswordUnavailable,
        PrinterProblem.UnsupportedAuthentication => Strings.SettingsTestUnsupportedAuthentication,
        PrinterProblem.UnexpectedResponse => Strings.SettingsTestUnexpectedResponse,
        _ => null,
    };

    /// <summary>Why printer information could not be read, e.g. "Could not be read: Offline".</summary>
    public static string InfoUnavailable(PrinterState state) => Unit(Strings.DiagnosticsInfoUnavailable, TrayText.StateName(state));

    /// <summary>Text reported by the printer as is; missing or blank text is <see cref="Missing"/>.</summary>
    public static string Text(string? value) => string.IsNullOrWhiteSpace(value) ? Missing : value;

    /// <summary>Rounds like the display does, so that comparing rounded values tells whether the text changes.</summary>
    public static double? Round(double? value, int decimals) =>
        IsKnown(value, out double known) ? Math.Round(known, decimals, MidpointRounding.AwayFromZero) : null;

    private static bool IsKnown(double? value, out double known)
    {
        known = value ?? double.NaN;
        return double.IsFinite(known);
    }

    private static string Number(double value, int decimals) =>
        Math.Round(value, decimals, MidpointRounding.AwayFromZero).ToString(decimals switch
        {
            0 => "F0",
            1 => "F1",
            2 => "F2",
            _ => "F" + decimals.ToString(CultureInfo.InvariantCulture),
        }, CultureInfo.CurrentCulture);

    private static string Unit(string format, string value) => string.Format(CultureInfo.CurrentCulture, format, value);
}
