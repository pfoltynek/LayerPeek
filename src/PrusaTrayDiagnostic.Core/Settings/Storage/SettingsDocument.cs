using System.Text.Json;
using System.Text.Json.Serialization;

namespace PrusaTrayDiagnostic.Core.Settings.Storage;

/// <summary>
/// Persisted shape of <see cref="AppSettings"/>. Every value is optional: missing values fall back to defaults and
/// invalid printer entries are skipped (see <see cref="SettingsMapper"/>) and quoted numbers are accepted.
/// Any other value of the wrong JSON type fails deserialization, i.e. the whole file is treated as corrupted.
/// </summary>
internal sealed class SettingsDocument
{
    public const int CurrentVersion = 1;

    public int? Version { get; set; }

    public double? PollingIntervalSeconds { get; set; }

    /// <summary>Language code (<see cref="AppLanguages.Supported"/>); not written when the app follows the system language.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Language { get; set; }

    public NotificationsDocument? Notifications { get; set; }

    public List<PrinterDocument?>? Printers { get; set; }
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
internal sealed partial class SettingsJsonContext : JsonSerializerContext;
