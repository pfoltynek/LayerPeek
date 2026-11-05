using System.Globalization;
using System.Text.Json;

using LayerPeek.Core.Detection;

namespace LayerPeek.Core.Settings.Storage;

/// <summary>
/// The print failure detection sections of <c>settings.json</c> (T-153): the global <c>detection</c> section and
/// <c>printers[*].detection</c>. Written only while something differs from the defaults; read leniently from raw JSON like
/// the recording section: a missing value is its default, an invalid one its default with a warning (never a corrupted
/// file); property names are case-insensitive, the last duplicate wins, unknown names (a newer version) are ignored.
/// </summary>
internal static class DetectionSettingsMapper
{
    private const string OncePerPrint = nameof(PrintFailureAlarmRepeat.OncePerPrint);
    private const string WhilePersisting = nameof(PrintFailureAlarmRepeat.WhilePersisting);

    public static JsonElement? ToDocument(PrintFailureDetectionSettings settings)
    {
        if (settings == PrintFailureDetectionSettings.Default)
        {
            return null;
        }

        DetectionDocument document = new()
        {
            Enabled = settings.Enabled ? true : null,
            AlarmRepeat = settings.AlarmRepeat == PrintFailureAlarmRepeat.OncePerPrint ? null : WhilePersisting,
            SaveSnapshots = settings.SaveSnapshots ? null : false,
            SampleIntervalSeconds = settings.SampleInterval == PrintFailureDetectionSettings.DefaultSampleInterval ? null : settings.SampleInterval.TotalSeconds,
        };
        return JsonSerializer.SerializeToElement(document, SettingsJsonContext.Default.DetectionDocument);
    }

    public static JsonElement? ToDocument(PrinterDetectionSettings settings)
    {
        if (settings == PrinterDetectionSettings.Default)
        {
            return null;
        }

        Dictionary<string, DetectorDocument>? detectors = null;
        foreach ((string id, DetectorSettings detector) in settings.Detectors)
        {
            // Every entry is a choice of the user (T-167: what differs from the detector's own default, which only the app
            // knows): one equal to DetectorSettings.Default (a detector that is off by default, switched on) keeps
            // "enabled": true, so that it is not read back as missing.
            bool sensitivityDefault = detector.Sensitivity == DetectorSettings.DefaultSensitivity;
            (detectors ??= new Dictionary<string, DetectorDocument>(StringComparer.Ordinal))[id] = new DetectorDocument
            {
                Enabled = !detector.Enabled ? false : sensitivityDefault ? true : null,
                Sensitivity = sensitivityDefault ? null : detector.Sensitivity,
            };
        }

        PrinterDetectionDocument document = new()
        {
            Enabled = settings.Enabled ? true : null,
            Region = settings.Region is { } region
                ? new DetectionRegionDocument { X = region.X, Y = region.Y, Width = region.Width, Height = region.Height }
                : null,
            Detectors = detectors,
            DarkFilament = settings.DarkFilament ? true : null,
        };
        return JsonSerializer.SerializeToElement(document, SettingsJsonContext.Default.PrinterDetectionDocument);
    }

    public static PrintFailureDetectionSettings ToSettings(JsonElement? section, List<string> warnings)
    {
        if (!IsPresent(section))
        {
            return PrintFailureDetectionSettings.Default;
        }

        if (section!.Value.ValueKind != JsonValueKind.Object)
        {
            warnings.Add("Print failure detection settings are invalid; the defaults are used (detection off).");
            return PrintFailureDetectionSettings.Default;
        }

        PrintFailureDetectionSettings settings = PrintFailureDetectionSettings.Default;
        foreach (JsonProperty property in section.Value.EnumerateObject())
        {
            JsonElement value = property.Value;
            if (Is(property, "enabled"))
            {
                settings = settings with { Enabled = ToBool(value, false, "Print failure detection: the master switch", warnings) };
            }
            else if (Is(property, "alarmRepeat"))
            {
                settings = settings with { AlarmRepeat = ToRepeat(value, warnings) };
            }
            else if (Is(property, "saveSnapshots"))
            {
                settings = settings with { SaveSnapshots = ToBool(value, true, "Print failure detection: saving alarm snapshots", warnings) };
            }
            else if (Is(property, "sampleIntervalSeconds"))
            {
                settings = settings with { SampleInterval = ToSampleInterval(value, warnings) };
            }
        }

        return settings == PrintFailureDetectionSettings.Default ? PrintFailureDetectionSettings.Default : settings;
    }

    public static PrinterDetectionSettings ToPrinterSettings(JsonElement? section, string printerId, List<string> warnings)
    {
        if (!IsPresent(section))
        {
            return PrinterDetectionSettings.Default;
        }

        if (section!.Value.ValueKind != JsonValueKind.Object)
        {
            warnings.Add($"Printer '{printerId}': print failure detection settings are invalid; detection is off.");
            return PrinterDetectionSettings.Default;
        }

        bool enabled = false;
        bool darkFilament = false;
        DetectionRegion? region = null;
        Dictionary<string, DetectorSettings> detectors = new(StringComparer.Ordinal);
        foreach (JsonProperty property in section.Value.EnumerateObject())
        {
            JsonElement value = property.Value;
            if (Is(property, "enabled"))
            {
                enabled = ToBool(value, false, $"Printer '{printerId}': print failure detection", warnings);
            }
            else if (Is(property, "region"))
            {
                region = ToRegion(value, printerId, warnings);
            }
            else if (Is(property, "detectors"))
            {
                detectors = ToDetectors(value, printerId, warnings);
            }
            else if (Is(property, "darkFilament"))
            {
                darkFilament = ToBool(value, false, $"Printer '{printerId}': the dark filament mode of print failure detection", warnings);
            }
        }

        PrinterDetectionSettings settings = new() { Enabled = enabled, Region = region, Detectors = detectors, DarkFilament = darkFilament };
        return settings == PrinterDetectionSettings.Default ? PrinterDetectionSettings.Default : settings;
    }

    /// <summary>All four numbers in 0–1 (a fraction of the frame); otherwise the default region with a warning.</summary>
    private static DetectionRegion? ToRegion(JsonElement value, string printerId, List<string> warnings)
    {
        if (value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        double? x = null;
        double? y = null;
        double? width = null;
        double? height = null;
        if (value.ValueKind == JsonValueKind.Object)
        {
            foreach (JsonProperty property in value.EnumerateObject())
            {
                double? number = ToNumber(property.Value);
                if (Is(property, "x"))
                {
                    x = number;
                }
                else if (Is(property, "y"))
                {
                    y = number;
                }
                else if (Is(property, "width"))
                {
                    width = number;
                }
                else if (Is(property, "height"))
                {
                    height = number;
                }
            }
        }

        if (x is not (>= 0 and <= 1) || y is not (>= 0 and <= 1) || width is not (> 0 and <= 1) || height is not (> 0 and <= 1))
        {
            warnings.Add($"Printer '{printerId}': the bed region of print failure detection is invalid (x, y, width, height within 0–1); the default region is used.");
            return null;
        }

        // Clipped into the frame (e.g. x + width slightly above 1 after rounding).
        return new DetectionRegion(x.Value, y.Value, width.Value, height.Value);
    }

    private static Dictionary<string, DetectorSettings> ToDetectors(JsonElement value, string printerId, List<string> warnings)
    {
        Dictionary<string, DetectorSettings> detectors = new(StringComparer.Ordinal);
        if (value.ValueKind == JsonValueKind.Null)
        {
            return detectors;
        }

        if (value.ValueKind != JsonValueKind.Object)
        {
            warnings.Add($"Printer '{printerId}': the detector settings of print failure detection are invalid; the defaults are used.");
            return detectors;
        }

        foreach (JsonProperty detector in value.EnumerateObject())
        {
            if (!DetectorSettings.IsValidId(detector.Name) || detector.Value.ValueKind != JsonValueKind.Object)
            {
                warnings.Add($"Printer '{printerId}': an entry of the detector settings is invalid and skipped.");
                continue;
            }

            DetectorSettings settings = DetectorSettings.Default;
            bool switchedOn = false;
            foreach (JsonProperty property in detector.Value.EnumerateObject())
            {
                if (Is(property, "enabled"))
                {
                    switchedOn = property.Value.ValueKind == JsonValueKind.True;
                    settings = settings with { Enabled = ToBool(property.Value, true, $"Printer '{printerId}': detector '{detector.Name}'", warnings) };
                }
                else if (Is(property, "sensitivity"))
                {
                    double? number = ToNumber(property.Value);
                    if (number is { } sensitivity && double.IsFinite(sensitivity) && sensitivity == Math.Floor(sensitivity)
                        && sensitivity is >= DetectorSettings.MinSensitivity and <= DetectorSettings.MaxSensitivity)
                    {
                        settings = settings with { Sensitivity = (int)sensitivity };
                    }
                    else
                    {
                        warnings.Add(
                            $"Printer '{printerId}': the sensitivity of detector '{detector.Name}' is invalid (whole number {DetectorSettings.MinSensitivity}–{DetectorSettings.MaxSensitivity}); {DetectorSettings.DefaultSensitivity} is used.");
                    }
                }
            }

            // Kept also when equal to DetectorSettings.Default if it says "enabled": true (T-167: a detector that is off by
            // default, switched on); an entry left at the defaults by invalid values is no choice.
            if (settings != DetectorSettings.Default || switchedOn)
            {
                detectors[detector.Name] = settings;
            }
        }

        return detectors;
    }

    private static PrintFailureAlarmRepeat ToRepeat(JsonElement value, List<string> warnings)
    {
        string? text = value.ValueKind == JsonValueKind.String ? TryGetString(value) : null;
        if (string.Equals(text, OncePerPrint, StringComparison.OrdinalIgnoreCase))
        {
            return PrintFailureAlarmRepeat.OncePerPrint;
        }

        if (string.Equals(text, WhilePersisting, StringComparison.OrdinalIgnoreCase))
        {
            return PrintFailureAlarmRepeat.WhilePersisting;
        }

        if (value.ValueKind != JsonValueKind.Null)
        {
            warnings.Add($"Print failure detection: the alarm repeat mode is invalid ({OncePerPrint}, {WhilePersisting}); {OncePerPrint} is used.");
        }

        return PrintFailureAlarmRepeat.OncePerPrint;
    }

    /// <summary>A number of seconds, clamped into 2–5 s (user decision 2026-11-04); not a number: the default with a warning.</summary>
    private static TimeSpan ToSampleInterval(JsonElement value, List<string> warnings)
    {
        if (ToNumber(value) is { } seconds && double.IsFinite(seconds))
        {
            return PrintFailureDetectionSettings.ClampSampleInterval(TimeSpan.FromSeconds(Math.Clamp(seconds, 0, 3600)));
        }

        if (value.ValueKind != JsonValueKind.Null)
        {
            warnings.Add(string.Create(
                CultureInfo.InvariantCulture,
                $"Print failure detection: the sampling interval is invalid (seconds); {PrintFailureDetectionSettings.DefaultSampleInterval.TotalSeconds:0} s is used."));
        }

        return PrintFailureDetectionSettings.DefaultSampleInterval;
    }

    private static bool ToBool(JsonElement value, bool fallback, string what, List<string> warnings)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.True:
                return true;
            case JsonValueKind.False:
                return false;
            case JsonValueKind.Null:
                return fallback;
            default:
                warnings.Add($"{what} is invalid (true / false); {(fallback ? "on" : "off")} is used.");
                return fallback;
        }
    }

    private static double? ToNumber(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Number when value.TryGetDouble(out double number) => number,
        JsonValueKind.String when double.TryParse(TryGetString(value), NumberStyles.Float, CultureInfo.InvariantCulture, out double number) => number,
        _ => null,
    };

    private static string? TryGetString(JsonElement value)
    {
        try
        {
            return value.GetString();
        }
        catch (InvalidOperationException)
        {
            // Not valid text (e.g. a lone escaped surrogate).
            return null;
        }
    }

    private static bool Is(JsonProperty property, string name) => string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase);

    private static bool IsPresent(JsonElement? value) => value is { ValueKind: not (JsonValueKind.Null or JsonValueKind.Undefined) };
}
