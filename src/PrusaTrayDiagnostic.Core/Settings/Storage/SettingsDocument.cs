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
