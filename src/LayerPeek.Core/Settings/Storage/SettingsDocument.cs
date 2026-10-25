using System.Text.Json;
using System.Text.Json.Serialization;

namespace LayerPeek.Core.Settings.Storage;

/// <summary>
/// Persisted shape of <see cref="AppSettings"/>. Every value is optional: missing values fall back to defaults and
/// invalid printer entries are skipped (see <see cref="SettingsMapper"/>) and quoted numbers are accepted.
/// Any other value of the wrong JSON type fails deserialization, i.e. the whole file is treated as corrupted; the values
/// inside <see cref="CameraOverlayDocument"/> are the exception (read from raw JSON with warnings).
/// </summary>
internal sealed class SettingsDocument
{
    /// <summary>
    /// 2 since the overlay widgets (T-077): <c>printers[*].camera.overlay</c> holds <c>widgets</c> instead of the T-036
    /// <c>corner</c> / <c>normal</c> / <c>mini</c>, which are converted when read and never written again.
    /// </summary>
    public const int CurrentVersion = 2;

    public int? Version { get; set; }

    public double? PollingIntervalSeconds { get; set; }

    /// <summary>Language code (<see cref="AppLanguages.Supported"/>); not written when the app follows the system language.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Language { get; set; }

    /// <summary>Detailed logging (T-047); missing in files of older versions, which load with <see langword="false"/>.</summary>
    public bool? DebugLogging { get; set; }

    /// <summary>
    /// "Offer pre-release versions" (T-017); written only once the user chose, so that until then the default follows the
    /// running version.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? OfferPrereleaseUpdates { get; set; }

    public NotificationsDocument? Notifications { get; set; }

    /// <summary>Global settings of the camera overlay (T-077); missing in older files and not written while all are the defaults.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public CameraOverlaySettingsDocument? CameraOverlay { get; set; }

    public List<PrinterDocument?>? Printers { get; set; }
}

internal sealed class CameraOverlaySettingsDocument
{
    /// <summary>
    /// Card background opacity in whole percent, 30–95; read as a number so that fractions and out-of-range values fall
    /// back to the default with a warning.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? BackgroundOpacityPercent { get; set; }
}

internal sealed class NotificationsDocument
{
    public bool? Enabled { get; set; }

    public bool? PrintStarted { get; set; }

    public bool? PrintFinished { get; set; }

    public bool? PrintPaused { get; set; }

    public bool? PrintResumed { get; set; }

    public bool? PrintStopped { get; set; }

    public bool? Error { get; set; }

    public bool? Attention { get; set; }

    public bool? Offline { get; set; }

    /// <summary>"Printer cooled down" (T-080); written only when on (the default is off), missing in older files.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? CooledDown { get; set; }

    /// <summary>Written as a whole number; read leniently (fractions are rounded, out-of-range values clamped).</summary>
    public double? RemainingTimeMinutes { get; set; }

    /// <summary><see cref="NotificationSound"/> name (<c>Off</c>, <c>ImportantOnly</c>, <c>All</c>); unknown names fall back to the default.</summary>
    public string? Sound { get; set; }
}

internal sealed class PrinterDocument
{
    public string? Id { get; set; }

    public string? Name { get; set; }

    public string? ConnectionType { get; set; }

    public string? Address { get; set; }

    /// <summary>PrusaLink user name; missing in files written before it was configurable (then <see cref="Printers.PrinterUserName.Default"/>).</summary>
    public string? UserName { get; set; }

    /// <summary>Missing in files written before cameras existed: the camera is disabled.</summary>
    public CameraDocument? Camera { get; set; }

    /// <summary>
    /// Model code last reported by the printer, "type.version.subversion" (T-039); not written while unknown. An invalid
    /// value is dropped with a warning.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? DetectedModel { get; set; }

    /// <summary>Image choice (T-039); not written while automatic (the default).</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public PrinterImageDocument? Image { get; set; }

    /// <summary>
    /// Cool-down threshold of the nozzle in whole °C (T-079); missing in older files and not written while it is the
    /// default. Read as a number so that fractions and out-of-range values fall back to the default with a warning.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? CooldownThresholdCelsius { get; set; }
}

/// <summary>Missing or invalid values mean the automatic image (invalid ones with a warning).</summary>
internal sealed class PrinterImageDocument
{
    /// <summary><see cref="Printers.PrinterImageMode"/> name (<c>Automatic</c>, <c>Model</c>, <c>Custom</c>).</summary>
    public string? Mode { get; set; }

    /// <summary><see cref="Printers.PrinterModels.Format"/> name of the chosen model; only for <c>Model</c>.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Model { get; set; }
}

internal sealed class CameraDocument
{
    public bool? Enabled { get; set; }

    /// <summary>Absolute <c>rtsp://</c> URL without credentials; invalid values disable the camera.</summary>
    public string? StreamUrl { get; set; }

    /// <summary>
    /// Camera window defaults (T-035); missing in older files and not written while all values are the defaults. Never
    /// touched by <see cref="CameraCredentialScrubber"/>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public CameraWindowDocument? Window { get; set; }

    /// <summary>
    /// Widget layout of the camera overlay (T-077, T-036 before format 2); missing in older files and not written while it
    /// is the default layout. Never touched by <see cref="CameraCredentialScrubber"/>; written alone by
    /// <see cref="CameraOverlaySectionWriter"/>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public CameraOverlayDocument? Overlay { get; set; }
}

/// <summary>
/// The overlay section. Values are kept as raw JSON and read leniently by <see cref="SettingsMapper"/>: a value of the
/// wrong type is a warning, not a corrupted file, and the old T-036 values are only inspected for the conversion.
/// </summary>
internal sealed class CameraOverlayDocument
{
    /// <summary>
    /// Array of <see cref="OverlayWidgetDocument"/> objects; when present (not <see langword="null"/>), the old keys are
    /// ignored.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonElement? Widgets { get; set; }

    /// <summary>T-036 overlay corner name (format 1); read for the conversion only, never written.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonElement? Corner { get; set; }

    /// <summary>T-036 item names of the normal window (format 1); dropped by the conversion, never written.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonElement? Normal { get; set; }

    /// <summary>T-036 item names of the mini preview (format 1); dropped by the conversion, never written.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonElement? Mini { get; set; }
}

/// <summary>One entry of <see cref="CameraOverlayDocument.Widgets"/> as written; reading goes through the raw JSON.</summary>
internal sealed class OverlayWidgetDocument
{
    /// <summary><see cref="Cameras.Overlay.OverlayWidgetIds"/> value, or an id of a newer version.</summary>
    public string? Id { get; set; }

    /// <summary><see cref="Cameras.Overlay.OverlayAnchor"/> name.</summary>
    public string? Anchor { get; set; }

    /// <summary><see cref="Cameras.Overlay.OverlayWidgetSize"/> name.</summary>
    public string? Size { get; set; }

    public bool? Emphasized { get; set; }

    public bool? Visible { get; set; }
}

/// <summary>Every value is optional; missing or invalid values fall back to the defaults (invalid ones with a warning).</summary>
internal sealed class CameraWindowDocument
{
    public bool? RememberLastPlacement { get; set; }

    /// <summary><see cref="Cameras.CameraWindowStartMode"/> name (<c>Last</c>, <c>Normal</c>, <c>Mini</c>, <c>FullScreen</c>).</summary>
    public string? StartMode { get; set; }

    /// <summary>Missing = the primary monitor.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public CameraMonitorDocument? Monitor { get; set; }

    public CameraWindowLayoutDocument? Normal { get; set; }

    public CameraWindowLayoutDocument? Mini { get; set; }

    /// <summary>Normal window always on top (T-037); missing in older files = <see langword="false"/>.</summary>
    public bool? TopmostNormal { get; set; }

    /// <summary>Mini preview always on top (T-037); missing in older files = <see langword="true"/>.</summary>
    public bool? TopmostMini { get; set; }
}

internal sealed class CameraMonitorDocument
{
    public string? DisplayName { get; set; }

    /// <summary>Resolution in pixels; whole numbers (read as numbers so that fractions are reported, not fatal).</summary>
    public double? Width { get; set; }

    public double? Height { get; set; }

    /// <summary>Path of the physical monitor (Windows, T-035 Q8); missing in older files and on other platforms.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? DevicePath { get; set; }

    /// <summary>
    /// Device paths of the other monitors with the same name when it was chosen (T-071 Q5); missing in older files and when
    /// there are none.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string?>? KnownTwinPaths { get; set; }
}

internal sealed class CameraWindowLayoutDocument
{
    /// <summary>Client size in whole device-independent units.</summary>
    public double? Width { get; set; }

    public double? Height { get; set; }

    /// <summary><see cref="Cameras.CameraWindowCorner"/> name.</summary>
    public string? Corner { get; set; }
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true,
    ReadCommentHandling = JsonCommentHandling.Skip,
    AllowTrailingCommas = true,
    NumberHandling = JsonNumberHandling.AllowReadingFromString,
    WriteIndented = true)]
[JsonSerializable(typeof(SettingsDocument))]
[JsonSerializable(typeof(CameraOverlayDocument))]
[JsonSerializable(typeof(List<OverlayWidgetDocument>))]
internal sealed partial class SettingsJsonContext : JsonSerializerContext;
