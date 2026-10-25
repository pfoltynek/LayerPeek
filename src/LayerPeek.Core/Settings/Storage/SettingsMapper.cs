using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

using LayerPeek.Core.Cameras;
using LayerPeek.Core.Cameras.Overlay;
using LayerPeek.Core.Cooldown;
using LayerPeek.Core.Printers;

namespace LayerPeek.Core.Settings.Storage;

/// <summary>
/// Converts between <see cref="SettingsDocument"/> and <see cref="AppSettings"/>. Loading is lenient:
/// invalid values are replaced by defaults or skipped, and each such fix is reported as a warning.
/// </summary>
internal static class SettingsMapper
{
    private const int MaxEchoedValueLength = 32;

    public static SettingsDocument ToDocument(AppSettings settings)
    {
        NotificationSettings notifications = settings.Notifications;
        return new SettingsDocument
        {
            Version = SettingsDocument.CurrentVersion,
            PollingIntervalSeconds = settings.PollingInterval.TotalSeconds,
            Language = settings.Language,
            DebugLogging = settings.DebugLogging,
            OfferPrereleaseUpdates = settings.OfferPrereleaseUpdates,
            Notifications = new NotificationsDocument
            {
                Enabled = notifications.Enabled,
                PrintStarted = notifications.PrintStarted,
                PrintFinished = notifications.PrintFinished,
                PrintPaused = notifications.PrintPaused,
                PrintResumed = notifications.PrintResumed,
                PrintStopped = notifications.PrintStopped,
                Error = notifications.Error,
                Attention = notifications.Attention,
                Offline = notifications.Offline,
                CooledDown = notifications.CooledDown ? true : null,
                RemainingTimeMinutes = notifications.RemainingTimeMinutes,
                Sound = FormatSound(notifications.Sound),
            },
            CameraOverlay = settings.CameraOverlayOpacityPercent == CameraOverlayOpacity.DefaultPercent
                ? null
                : new CameraOverlaySettingsDocument { BackgroundOpacityPercent = settings.CameraOverlayOpacityPercent },
            Printers = [.. settings.Printers.Select(static printer => new PrinterDocument
            {
                Id = printer.Id,
                Name = printer.Name,
                ConnectionType = FormatConnectionType(printer.ConnectionType),
                Address = printer.Address,
                UserName = printer.UserName,
                Camera = new CameraDocument
                {
                    Enabled = printer.Camera.Enabled,
                    StreamUrl = printer.Camera.StreamUri?.OriginalString ?? printer.Camera.InvalidStreamUrl,
                    Window = ToDocument(printer.Camera.Window),
                    Overlay = ToDocument(printer.Camera.Overlay),
                },
                DetectedModel = printer.DetectedModel?.ToString(),
                Image = ToDocument(printer.Image),
                CooldownThresholdCelsius = printer.CooldownThresholdCelsius == CooldownThreshold.DefaultCelsius ? null : printer.CooldownThresholdCelsius,
            })],
        };
    }

    public static AppSettings ToSettings(SettingsDocument document, List<string> warnings) => ToSettings(document, warnings, notices: []);

    /// <param name="notices">Receives informational messages for the log (e.g. the T-036 overlay conversion), without values.</param>
    public static AppSettings ToSettings(SettingsDocument document, List<string> warnings, List<string> notices)
    {
        if (document.Version > SettingsDocument.CurrentVersion)
        {
            warnings.Add($"Settings were written by a newer version (format {document.Version}); unknown values will be lost on save.");
        }

        int convertedOverlays = 0;
        AppSettings settings = new()
        {
            PollingInterval = ToPollingInterval(document.PollingIntervalSeconds, warnings),
            Language = ToLanguage(document.Language, warnings),
            DebugLogging = document.DebugLogging ?? false,
            OfferPrereleaseUpdates = document.OfferPrereleaseUpdates,
            CameraOverlayOpacityPercent = ToOverlayOpacity(document.CameraOverlay?.BackgroundOpacityPercent, warnings),
            Notifications = ToNotifications(document.Notifications, warnings),
            Printers = ToPrinters(document.Printers, warnings, ref convertedOverlays),
        };

        if (convertedOverlays > 0)
        {
            // One message for the whole file, without values (T-077); the new shape is written by the next save.
            notices.Add(convertedOverlays == 1
                ? "The camera overlay settings of 1 printer were converted to the widget layout (settings format 2)."
                : $"The camera overlay settings of {convertedOverlays} printers were converted to the widget layout (settings format 2).");
        }

        return settings;
    }

    /// <summary>Missing = the default; a fraction or a value out of range falls back to the default with a warning.</summary>
    private static int ToOverlayOpacity(double? value, List<string> warnings)
    {
        if (value is null)
        {
            return CameraOverlayOpacity.DefaultPercent;
        }

        if (TryGetWholeNumber(value, out int percent) && CameraOverlayOpacity.IsValid(percent))
        {
            return percent;
        }

        warnings.Add(
            $"Camera overlay card opacity is invalid (whole number {CameraOverlayOpacity.MinPercent}–{CameraOverlayOpacity.MaxPercent} %); {CameraOverlayOpacity.DefaultPercent} % is used.");
        return CameraOverlayOpacity.DefaultPercent;
    }

    private static TimeSpan ToPollingInterval(double? seconds, List<string> warnings)
    {
        if (seconds is not { } requested)
        {
            return AppSettings.DefaultPollingInterval;
        }

        if (!double.IsFinite(requested))
        {
            warnings.Add("Polling interval is not a number; the default is used.");
            return AppSettings.DefaultPollingInterval;
        }

        // Clamp in seconds before converting so that huge values cannot overflow TimeSpan.
        double clamped = Math.Clamp(requested, AppSettings.MinPollingInterval.TotalSeconds, AppSettings.MaxPollingInterval.TotalSeconds);
        if (clamped != requested)
        {
            warnings.Add($"Polling interval {requested} s is out of range; {clamped} s is used.");
        }

        // Rounded to whole ticks so that TotalSeconds written by ToDocument reads back as exactly the same interval.
        return TimeSpan.FromTicks((long)Math.Round(clamped * TimeSpan.TicksPerSecond));
    }

    /// <summary>Missing = follow the system; an unknown code also follows the system, with a warning.</summary>
    private static string? ToLanguage(string? value, List<string> warnings)
    {
        if (value is null)
        {
            return null;
        }

        if (AppLanguages.TryNormalize(value, out string? code))
        {
            return code;
        }

        warnings.Add($"Unknown language '{Shorten(value)}'; the system language is used.");
        return null;
    }

    private static NotificationSettings ToNotifications(NotificationsDocument? document, List<string> warnings)
    {
        NotificationSettings defaults = AppSettings.Default.Notifications;
        if (document is null)
        {
            return defaults;
        }

        return new NotificationSettings
        {
            Enabled = document.Enabled ?? defaults.Enabled,
            PrintStarted = document.PrintStarted ?? defaults.PrintStarted,
            PrintFinished = document.PrintFinished ?? defaults.PrintFinished,
            PrintPaused = document.PrintPaused ?? defaults.PrintPaused,
            PrintResumed = document.PrintResumed ?? defaults.PrintResumed,
            PrintStopped = document.PrintStopped ?? defaults.PrintStopped,
            Error = document.Error ?? defaults.Error,
            Attention = document.Attention ?? defaults.Attention,
            Offline = document.Offline ?? defaults.Offline,
            CooledDown = document.CooledDown ?? defaults.CooledDown,
            RemainingTimeMinutes = ToRemainingTimeMinutes(document.RemainingTimeMinutes, warnings),
            Sound = ToSound(document.Sound, defaults.Sound, warnings),
        };
    }

    private static NotificationSound ToSound(string? value, NotificationSound fallback, List<string> warnings)
    {
        if (value is null)
        {
            return fallback;
        }

        // Explicit names instead of Enum.Parse: numeric strings must not be accepted and the format must stay stable.
        ReadOnlySpan<char> name = value.AsSpan().Trim();
        foreach (NotificationSound sound in (ReadOnlySpan<NotificationSound>)[NotificationSound.Off, NotificationSound.ImportantOnly, NotificationSound.All])
        {
            if (name.Equals(FormatSound(sound), StringComparison.OrdinalIgnoreCase))
            {
                return sound;
            }
        }

        warnings.Add($"Unknown notification sound setting '{Shorten(value)}'; the default is used.");
        return fallback;
    }

    private static string FormatSound(NotificationSound sound) => sound switch
    {
        NotificationSound.Off => nameof(NotificationSound.Off),
        NotificationSound.ImportantOnly => nameof(NotificationSound.ImportantOnly),
        NotificationSound.All => nameof(NotificationSound.All),
        _ => throw new ArgumentOutOfRangeException(nameof(sound), sound, "Unknown notification sound setting."),
    };

    private static int ToRemainingTimeMinutes(double? minutes, List<string> warnings)
    {
        if (minutes is not { } requested)
        {
            return NotificationSettings.DefaultRemainingTimeMinutes;
        }

        if (!double.IsFinite(requested))
        {
            warnings.Add("Remaining time alert is not a number; the default is used.");
            return NotificationSettings.DefaultRemainingTimeMinutes;
        }

        // Clamp before converting so that huge values cannot overflow int.
        int corrected = (int)Math.Round(Math.Clamp(requested, 0, NotificationSettings.MaxRemainingTimeMinutes), MidpointRounding.AwayFromZero);
        if (corrected != requested)
        {
            warnings.Add($"Remaining time alert {requested} min is not a whole number within 0–{NotificationSettings.MaxRemainingTimeMinutes}; {corrected} min is used.");
        }

        return corrected;
    }

    private static List<PrinterDefinition> ToPrinters(List<PrinterDocument?>? documents, List<string> warnings, ref int convertedOverlays)
    {
        if (documents is null)
        {
            return [];
        }

        List<PrinterDefinition> printers = new(documents.Count);
        HashSet<string> ids = new(documents.Count, StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < documents.Count; i++)
        {
            string? problem = TryCreatePrinter(documents[i], i, warnings, out PrinterDefinition? printer);
            if (printer is not null && !ids.Add(printer.Id))
            {
                problem = $"duplicate id '{Shorten(printer.Id)}'";
            }

            if (problem is null)
            {
                // Only for accepted entries, so that a skipped printer does not also produce a camera warning.
                CameraSettings camera = ToCamera(documents[i]!.Camera, printer!.Id, warnings);
                CameraWindowDefaults window = ToCameraWindow(documents[i]!.Camera?.Window, printer.Id, warnings);
                if (!ReferenceEquals(window, CameraWindowDefaults.Default))
                {
                    camera = camera with { Window = window };
                }

                CameraOverlayLayout overlay = ToCameraOverlay(documents[i]!.Camera?.Overlay, printer.Id, warnings, ref convertedOverlays);
                if (!ReferenceEquals(overlay, CameraOverlayLayout.Default))
                {
                    camera = camera with { Overlay = overlay };
                }

                // Reference check: equality ignores CredentialsRemoved, which a credentials-only address sets on a Disabled copy.
                if (!ReferenceEquals(camera, CameraSettings.Disabled))
                {
                    printer = printer with { Camera = camera };
                }

                PrinterModelCode? detectedModel = ToDetectedModel(documents[i]!.DetectedModel, printer.Id, warnings);
                PrinterImageChoice image = ToImage(documents[i]!.Image, printer.Id, warnings);
                if (detectedModel is not null || !ReferenceEquals(image, PrinterImageChoice.Automatic))
                {
                    printer = printer with { DetectedModel = detectedModel, Image = image };
                }

                int cooldownThreshold = ToCooldownThreshold(documents[i]!.CooldownThresholdCelsius, printer.Id, warnings);
                if (cooldownThreshold != CooldownThreshold.DefaultCelsius)
                {
                    printer = printer with { CooldownThresholdCelsius = cooldownThreshold };
                }

                printers.Add(printer);
            }
            else
            {
                warnings.Add($"Printer entry {i + 1} skipped: {problem}.");
            }
        }

        return printers;
    }

    /// <returns><see langword="null"/> on success, otherwise the reason why the entry is invalid.</returns>
    private static string? TryCreatePrinter(PrinterDocument? document, int index, List<string> warnings, out PrinterDefinition? printer)
    {
        printer = null;
        if (document is null)
        {
            return "empty entry";
        }

        if (string.IsNullOrWhiteSpace(document.Id))
        {
            return "missing id";
        }

        if (string.IsNullOrWhiteSpace(document.Address))
        {
            return "missing address";
        }

        if (!TryParseConnectionType(document.ConnectionType, out ConnectionType connectionType))
        {
            return $"unknown connection type '{Shorten(document.ConnectionType)}'";
        }

        printer = new PrinterDefinition
        {
            Id = document.Id,
            Name = string.IsNullOrWhiteSpace(document.Name) ? document.Address : document.Name,
            ConnectionType = connectionType,
            Address = document.Address,
            UserName = ToUserName(document.UserName, index, warnings),
        };
        return null;
    }

    private static string ToUserName(string? value, int index, List<string> warnings)
    {
        if (value is null)
        {
            // Files written before the user name was configurable.
            return PrinterUserName.Default;
        }

        PrinterUserNameError error = PrinterUserName.Validate(value);
        if (error == PrinterUserNameError.None)
        {
            return value;
        }

        // The value is not echoed: it may contain control characters.
        warnings.Add($"Printer entry {index + 1}: invalid user name ({error}); '{PrinterUserName.Default}' is used.");
        return PrinterUserName.Default;
    }

    /// <summary>
    /// A missing section means no camera. An invalid address disables the camera but is kept unchanged (by the user's
    /// decision, for correction in the settings window); it is never echoed in the warning because it may contain credentials.
    /// </summary>
    private static CameraSettings ToCamera(CameraDocument? document, string printerId, List<string> warnings)
    {
        if (document is null)
        {
            return CameraSettings.Disabled;
        }

        bool enabled = document.Enabled ?? false;
        if (string.IsNullOrWhiteSpace(document.StreamUrl))
        {
            if (enabled)
            {
                warnings.Add($"Camera of printer '{Shorten(printerId)}' disabled: {CameraSettings.Describe(CameraUrlProblem.Missing)}.");
            }

            return CameraSettings.Disabled;
        }

        CameraSettings camera = CameraSettings.FromStoredStreamUrl(enabled, document.StreamUrl);
        if (camera.CredentialsRemoved)
        {
            warnings.Add($"Credentials were removed from the camera address of printer '{Shorten(printerId)}'; the camera is disabled until the address is reviewed.");
        }
        else if (camera.InvalidStreamUrl is not null)
        {
            warnings.Add($"Camera of printer '{Shorten(printerId)}' disabled until its address is corrected: {CameraSettings.Describe(camera.StreamUrlProblem)}.");
        }

        return camera;
    }

    /// <summary>Missing = never detected; an invalid code is dropped with a warning that does not echo it.</summary>
    private static PrinterModelCode? ToDetectedModel(string? value, string printerId, List<string> warnings)
    {
        if (value is null)
        {
            return null;
        }

        if (PrinterModelCode.TryParse(value, out PrinterModelCode code))
        {
            return code;
        }

        warnings.Add($"Printer '{Shorten(printerId)}': invalid detected model code; it is ignored until the model is detected again.");
        return null;
    }

    /// <summary>Missing = the default; a fraction or a value out of range falls back to the default with a warning.</summary>
    private static int ToCooldownThreshold(double? value, string printerId, List<string> warnings)
    {
        if (value is null)
        {
            return CooldownThreshold.DefaultCelsius;
        }

        if (TryGetWholeNumber(value, out int celsius) && CooldownThreshold.IsValid(celsius))
        {
            return celsius;
        }

        warnings.Add(
            $"Printer '{Shorten(printerId)}': cool-down threshold is invalid (whole number {CooldownThreshold.MinCelsius}–{CooldownThreshold.MaxCelsius} °C); {CooldownThreshold.DefaultCelsius} °C is used.");
        return CooldownThreshold.DefaultCelsius;
    }

    /// <summary>Not written while automatic (the default), so that files stay as they were.</summary>
    private static PrinterImageDocument? ToDocument(PrinterImageChoice image) => image.Mode switch
    {
        PrinterImageMode.Automatic => null,
        PrinterImageMode.Model => new PrinterImageDocument { Mode = FormatImageMode(image.Mode), Model = PrinterModels.Format(image.Model) },
        _ => new PrinterImageDocument { Mode = FormatImageMode(image.Mode) },
    };

    /// <summary>
    /// A missing section or mode means automatic. An unknown mode, and the model mode without a known model, fall back to
    /// automatic with one warning that does not echo the value.
    /// </summary>
    private static PrinterImageChoice ToImage(PrinterImageDocument? document, string printerId, List<string> warnings)
    {
        if (document?.Mode is not { } modeName)
        {
            return PrinterImageChoice.Automatic;
        }

        string subject = $"Image of printer '{Shorten(printerId)}'";
        if (!TryParseImageMode(modeName, out PrinterImageMode mode))
        {
            warnings.Add($"{subject}: unknown mode; the automatic image is used.");
            return PrinterImageChoice.Automatic;
        }

        switch (mode)
        {
            case PrinterImageMode.Custom:
                return PrinterImageChoice.Custom;
            case PrinterImageMode.Model when PrinterModels.TryParse(document.Model, out PrinterModel model) && model != PrinterModel.Unknown:
                return PrinterImageChoice.ForModel(model);
            case PrinterImageMode.Model:
                warnings.Add($"{subject}: unknown model; the automatic image is used.");
                return PrinterImageChoice.Automatic;
            default:
                return PrinterImageChoice.Automatic;
        }
    }

    private static string FormatImageMode(PrinterImageMode mode) => mode switch
    {
        PrinterImageMode.Automatic => nameof(PrinterImageMode.Automatic),
        PrinterImageMode.Model => nameof(PrinterImageMode.Model),
        PrinterImageMode.Custom => nameof(PrinterImageMode.Custom),
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown printer image mode."),
    };

    private static bool TryParseImageMode(string value, out PrinterImageMode mode)
    {
        // Explicit names instead of Enum.Parse: numeric strings must not be accepted and the format must stay stable.
        ReadOnlySpan<char> name = value.AsSpan().Trim();
        foreach (PrinterImageMode candidate in (ReadOnlySpan<PrinterImageMode>)[PrinterImageMode.Automatic, PrinterImageMode.Model, PrinterImageMode.Custom])
        {
            if (name.Equals(FormatImageMode(candidate), StringComparison.OrdinalIgnoreCase))
            {
                mode = candidate;
                return true;
            }
        }

        mode = default;
        return false;
    }

    /// <summary>Not written while everything is the default, so that files stay as they were and follow future defaults.</summary>
    private static CameraWindowDocument? ToDocument(CameraWindowDefaults window)
    {
        if (window == CameraWindowDefaults.Default)
        {
            return null;
        }

        return new CameraWindowDocument
        {
            RememberLastPlacement = window.RememberLastPlacement,
            StartMode = FormatStartMode(window.StartMode),
            Monitor = window.Monitor is { } monitor
                ? new CameraMonitorDocument
                {
                    DisplayName = monitor.DisplayName,
                    Width = monitor.Width,
                    Height = monitor.Height,
                    DevicePath = monitor.DevicePath,
                    KnownTwinPaths = monitor.KnownTwinPaths.Count > 0 ? [.. monitor.KnownTwinPaths] : null,
                }
                : null,
            Normal = ToDocument(window.Normal),
            Mini = ToDocument(window.Mini),
            TopmostNormal = window.TopmostNormal,
            TopmostMini = window.TopmostMini,
        };
    }

    private static CameraWindowLayoutDocument ToDocument(CameraWindowLayout layout) => new()
    {
        Width = layout.Size.Width,
        Height = layout.Size.Height,
        Corner = FormatCorner(layout.Corner),
    };

    /// <summary>
    /// A missing section or value means the default. Each invalid value (or monitor / size as a whole) falls back to its
    /// default with one warning; the values are not echoed (garbage from the file must not reach the UI).
    /// </summary>
    private static CameraWindowDefaults ToCameraWindow(CameraWindowDocument? document, string printerId, List<string> warnings)
    {
        if (document is null)
        {
            return CameraWindowDefaults.Default;
        }

        CameraWindowDefaults defaults = CameraWindowDefaults.Default;
        string subject = $"Camera window of printer '{Shorten(printerId)}'";
        CameraWindowStartMode startMode = defaults.StartMode;
        if (document.StartMode is not null && !TryParseStartMode(document.StartMode, out startMode))
        {
            warnings.Add($"{subject}: unknown start mode; '{FormatStartMode(defaults.StartMode)}' is used.");
            startMode = defaults.StartMode;
        }

        CameraWindowDefaults window = new()
        {
            RememberLastPlacement = document.RememberLastPlacement ?? defaults.RememberLastPlacement,
            StartMode = startMode,
            Monitor = ToMonitor(document.Monitor, subject, warnings),
            Normal = ToLayout(document.Normal, defaults.Normal, $"{subject}: normal window", warnings),
            Mini = ToMiniLayout(document.Mini, defaults.Mini, $"{subject}: mini preview", warnings),
            TopmostNormal = document.TopmostNormal ?? defaults.TopmostNormal,
            TopmostMini = document.TopmostMini ?? defaults.TopmostMini,
        };

        return window == defaults ? defaults : window;
    }

    /// <summary>Not written while it is the default layout, so that files stay as they were and follow future defaults.</summary>
    internal static CameraOverlayDocument? ToDocument(CameraOverlayLayout layout)
    {
        if (layout == CameraOverlayLayout.Default)
        {
            return null;
        }

        List<OverlayWidgetDocument> widgets = new(layout.Widgets.Count);
        foreach (OverlayWidgetPlacement widget in layout.Widgets)
        {
            widgets.Add(new OverlayWidgetDocument
            {
                Id = widget.WidgetId,
                Anchor = FormatAnchor(widget.Anchor),
                Size = FormatWidgetSize(widget.Size),
                Emphasized = widget.Emphasized,
                Visible = widget.Visible,
            });
        }

        return new CameraOverlayDocument { Widgets = JsonSerializer.SerializeToElement(widgets, SettingsJsonContext.Default.ListOverlayWidgetDocument) };
    }

    /// <summary>
    /// A missing section means the default layout. The widget shape (format 2) wins over the T-036 shape; a section with
    /// only the T-036 keys is converted (<see cref="ToLegacyLayout"/>) and counted in <paramref name="converted"/>.
    /// </summary>
    private static CameraOverlayLayout ToCameraOverlay(CameraOverlayDocument? document, string printerId, List<string> warnings, ref int converted)
    {
        if (document is null)
        {
            return CameraOverlayLayout.Default;
        }

        string subject = $"Camera overlay of printer '{Shorten(printerId)}'";
        if (document.Widgets is { } widgets && IsPresent(widgets))
        {
            return ToLayout(widgets, subject, warnings);
        }

        if (!IsPresent(document.Corner) && !IsPresent(document.Normal) && !IsPresent(document.Mini))
        {
            return CameraOverlayLayout.Default;
        }

        converted++;
        return ToLegacyLayout(document.Corner, subject, warnings);
    }

    private static bool IsPresent(JsonElement? value) => value is { ValueKind: not (JsonValueKind.Null or JsonValueKind.Undefined) };

    /// <summary>
    /// The text of a string value; <see langword="false"/> for any other value and for a string that is no valid text
    /// (e.g. a lone escaped surrogate), which <see cref="JsonElement.GetString"/> throws for. The raw overlay section is
    /// read leniently, so such a value must be a warning, not an exception that makes the whole file unusable.
    /// </summary>
    private static bool TryGetText(JsonElement value, [NotNullWhen(true)] out string? text)
    {
        text = null;
        if (value.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        try
        {
            text = value.GetString();
        }
        catch (InvalidOperationException)
        {
            return false;
        }

        return text is not null;
    }

    /// <summary>The property name, or <see langword="null"/> when it is no valid text (treated like an unknown property).</summary>
    private static string? NameOf(JsonProperty property)
    {
        try
        {
            return property.Name;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>
    /// The T-036 conversion (T-077 Q4): the item choices (<c>normal</c>, <c>mini</c>) are dropped, the result is the default
    /// layout mirrored into the old corner (<see cref="CameraOverlayLayout.FromLegacyCorner"/>). An invalid corner gives the
    /// default layout with a warning that does not echo it.
    /// </summary>
    private static CameraOverlayLayout ToLegacyLayout(JsonElement? corner, string subject, List<string> warnings)
    {
        if (corner is not { } value || !IsPresent(value))
        {
            return CameraOverlayLayout.Default;
        }

        if (TryGetText(value, out string? name) && TryParseAnchor(name, out OverlayAnchor anchor) && OverlayAnchors.IsCorner(anchor))
        {
            return CameraOverlayLayout.FromLegacyCorner(anchor);
        }

        warnings.Add($"{subject}: unknown corner of the old overlay; the default layout is used.");
        return CameraOverlayLayout.Default;
    }

    /// <summary>
    /// The widget list read leniently: entries that are no object or have no valid id are skipped, duplicate ids keep the
    /// first entry, invalid values fall back to the default placement of the widget (for unknown ids: the main anchor,
    /// medium, not emphasized, visible) and known widgets missing from the list are added at their default placement. Each
    /// kind of problem gives one warning without the values. Unknown ids are kept, as many as fit into
    /// <see cref="CameraOverlayLayout.MaxWidgets"/> next to the known widgets.
    /// </summary>
    private static CameraOverlayLayout ToLayout(JsonElement widgets, string subject, List<string> warnings)
    {
        if (widgets.ValueKind != JsonValueKind.Array)
        {
            warnings.Add($"{subject}: the widget list is invalid; the default layout is used.");
            return CameraOverlayLayout.Default;
        }

        int maxUnknown = CameraOverlayLayout.MaxWidgets - OverlayWidgetIds.Known.Count;
        List<OverlayWidgetPlacement> placements = [];
        HashSet<string> ids = new(StringComparer.OrdinalIgnoreCase);
        int unknown = 0;
        int invalidEntries = 0;
        int duplicates = 0;
        int invalidValues = 0;
        int leftOut = 0;
        foreach (JsonElement entry in widgets.EnumerateArray())
        {
            if (!TryReadWidget(entry, out WidgetValues values))
            {
                invalidEntries++;
                continue;
            }

            string id = OverlayWidgetIds.Normalize(values.Id);
            bool known = OverlayWidgetIds.IsKnown(id);
            if (!ids.Add(id))
            {
                duplicates++;
                continue;
            }

            if (!known && unknown == maxUnknown)
            {
                leftOut++;
                continue;
            }

            unknown += known ? 0 : 1;
            placements.Add(ToPlacement(id, values, ref invalidValues));
        }

        foreach (OverlayWidgetPlacement widget in CameraOverlayLayout.Default.Widgets)
        {
            if (!ids.Contains(widget.WidgetId))
            {
                placements.Add(widget);
            }
        }

        AddCountWarning(warnings, invalidEntries, $"{subject}: 1 invalid widget entry was skipped.", $"{subject}: {invalidEntries} invalid widget entries were skipped.");
        AddCountWarning(warnings, duplicates, $"{subject}: 1 duplicate widget entry was ignored; the first one is used.", $"{subject}: {duplicates} duplicate widget entries were ignored; the first ones are used.");
        AddCountWarning(warnings, invalidValues, $"{subject}: 1 invalid widget value was replaced by its default.", $"{subject}: {invalidValues} invalid widget values were replaced by their defaults.");
        AddCountWarning(warnings, leftOut, $"{subject}: too many widgets; 1 was left out.", $"{subject}: too many widgets; {leftOut} were left out.");

        CameraOverlayLayout layout = new(placements);
        return layout == CameraOverlayLayout.Default ? CameraOverlayLayout.Default : layout;
    }

    private static void AddCountWarning(List<string> warnings, int count, string one, string many)
    {
        if (count > 0)
        {
            warnings.Add(count == 1 ? one : many);
        }
    }

    /// <summary>
    /// The raw values of one widget entry; property names are matched case-insensitively and the last of duplicates wins,
    /// like <see cref="SettingsJsonContext"/> reads the rest of the file.
    /// </summary>
    /// <returns>Whether the entry is an object with a valid id (trimmed).</returns>
    private static bool TryReadWidget(JsonElement entry, out WidgetValues values)
    {
        values = default;
        if (entry.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        JsonElement? id = null;
        JsonElement? anchor = null;
        JsonElement? size = null;
        JsonElement? emphasized = null;
        JsonElement? visible = null;
        foreach (JsonProperty property in entry.EnumerateObject())
        {
            switch (NameOf(property)?.ToUpperInvariant())
            {
                case "ID":
                    id = property.Value;
                    break;
                case "ANCHOR":
                    anchor = property.Value;
                    break;
                case "SIZE":
                    size = property.Value;
                    break;
                case "EMPHASIZED":
                    emphasized = property.Value;
                    break;
                case "VISIBLE":
                    visible = property.Value;
                    break;
            }
        }

        string? text = id is { } value && TryGetText(value, out string? raw) ? raw.Trim() : null;
        if (!OverlayWidgetIds.IsValid(text))
        {
            return false;
        }

        values = new WidgetValues(text!, anchor, size, emphasized, visible);
        return true;
    }

    private static OverlayWidgetPlacement ToPlacement(string id, WidgetValues values, ref int invalidValues)
    {
        OverlayWidgetPlacement fallback = CameraOverlayLayout.DefaultPlacement(id) ?? new OverlayWidgetPlacement(id, CameraOverlayLayout.MainAnchor);
        OverlayAnchor anchor = fallback.Anchor;
        if (values.Anchor is { } anchorValue && IsPresent(anchorValue)
            && !(TryGetText(anchorValue, out string? anchorName) && TryParseAnchor(anchorName, out anchor)))
        {
            anchor = fallback.Anchor;
            invalidValues++;
        }

        OverlayWidgetSize size = fallback.Size;
        if (values.Size is { } sizeValue && IsPresent(sizeValue)
            && !(TryGetText(sizeValue, out string? sizeName) && TryParseWidgetSize(sizeName, out size)))
        {
            size = fallback.Size;
            invalidValues++;
        }

        return new OverlayWidgetPlacement(
            id,
            anchor,
            size,
            ToFlag(values.Emphasized, fallback.Emphasized, ref invalidValues),
            ToFlag(values.Visible, fallback.Visible, ref invalidValues));
    }

    private static bool ToFlag(JsonElement? value, bool fallback, ref int invalidValues)
    {
        switch (value?.ValueKind)
        {
            case null or JsonValueKind.Null or JsonValueKind.Undefined:
                return fallback;
            case JsonValueKind.True:
                return true;
            case JsonValueKind.False:
                return false;
            default:
                invalidValues++;
                return fallback;
        }
    }

    // Explicit names instead of Enum.Parse: numeric strings must not be accepted and the format must stay stable.
    private static string FormatAnchor(OverlayAnchor anchor) => anchor switch
    {
        OverlayAnchor.TopLeft => nameof(OverlayAnchor.TopLeft),
        OverlayAnchor.TopCenter => nameof(OverlayAnchor.TopCenter),
        OverlayAnchor.TopRight => nameof(OverlayAnchor.TopRight),
        OverlayAnchor.BottomLeft => nameof(OverlayAnchor.BottomLeft),
        OverlayAnchor.BottomCenter => nameof(OverlayAnchor.BottomCenter),
        OverlayAnchor.BottomRight => nameof(OverlayAnchor.BottomRight),
        _ => throw new ArgumentOutOfRangeException(nameof(anchor), anchor, "Unknown overlay anchor."),
    };

    private static bool TryParseAnchor(string value, out OverlayAnchor anchor)
    {
        ReadOnlySpan<char> name = value.AsSpan().Trim();
        foreach (OverlayAnchor candidate in OverlayAnchors.All)
        {
            if (name.Equals(FormatAnchor(candidate), StringComparison.OrdinalIgnoreCase))
            {
                anchor = candidate;
                return true;
            }
        }

        anchor = default;
        return false;
    }

    private static string FormatWidgetSize(OverlayWidgetSize size) => size switch
    {
        OverlayWidgetSize.Small => nameof(OverlayWidgetSize.Small),
        OverlayWidgetSize.Medium => nameof(OverlayWidgetSize.Medium),
        OverlayWidgetSize.Large => nameof(OverlayWidgetSize.Large),
        _ => throw new ArgumentOutOfRangeException(nameof(size), size, "Unknown overlay widget size."),
    };

    private static bool TryParseWidgetSize(string value, out OverlayWidgetSize size)
    {
        ReadOnlySpan<char> name = value.AsSpan().Trim();
        foreach (OverlayWidgetSize candidate in (ReadOnlySpan<OverlayWidgetSize>)[OverlayWidgetSize.Small, OverlayWidgetSize.Medium, OverlayWidgetSize.Large])
        {
            if (name.Equals(FormatWidgetSize(candidate), StringComparison.OrdinalIgnoreCase))
            {
                size = candidate;
                return true;
            }
        }

        size = default;
        return false;
    }

    private static CameraMonitorId? ToMonitor(CameraMonitorDocument? document, string subject, List<string> warnings)
    {
        if (document is null)
        {
            return null;
        }

        if (TryGetWholeNumber(document.Width, out int width)
            && TryGetWholeNumber(document.Height, out int height)
            && CameraMonitorId.IsValid(document.DisplayName, width, height))
        {
            string? devicePath = document.DevicePath;
            if (devicePath is not null && !CameraMonitorId.IsValidDevicePath(devicePath))
            {
                // The name and resolution still identify the monitor (as in files written before the path was stored).
                warnings.Add($"{subject}: invalid monitor device path; the monitor is matched by its name.");
                return new CameraMonitorId(document.DisplayName!, width, height);
            }

            return new CameraMonitorId(document.DisplayName!, width, height, devicePath, ToKnownTwinPaths(document.KnownTwinPaths, devicePath, subject, warnings));
        }

        warnings.Add($"{subject}: invalid monitor; the primary monitor is used.");
        return null;
    }

    /// <summary>
    /// The usable known twin paths (T-071 Q5): valid device paths, each once and not the monitor's own (case-insensitive),
    /// at most <see cref="CameraMonitorId.MaxKnownTwinPaths"/>, in file order; none without a device path. Anything left out
    /// gives one warning without the values.
    /// </summary>
    private static List<string> ToKnownTwinPaths(List<string?>? document, string? devicePath, string subject, List<string> warnings)
    {
        List<string> twins = [];
        if (document is null || document.Count == 0)
        {
            return twins;
        }

        bool leftOut = devicePath is null;
        if (!leftOut)
        {
            foreach (string? path in document)
            {
                if (twins.Count == CameraMonitorId.MaxKnownTwinPaths
                    || !CameraMonitorId.IsValidDevicePath(path)
                    || string.Equals(path, devicePath, StringComparison.OrdinalIgnoreCase)
                    || twins.Contains(path!, StringComparer.OrdinalIgnoreCase))
                {
                    leftOut = true;
                    continue;
                }

                twins.Add(path!);
            }
        }

        if (leftOut)
        {
            warnings.Add($"{subject}: invalid or too many known twin monitor paths were left out.");
        }

        return twins;
    }

    private static CameraWindowLayout ToLayout(CameraWindowLayoutDocument? document, CameraWindowLayout fallback, string subject, List<string> warnings)
    {
        if (document is null)
        {
            return fallback;
        }

        CameraWindowSize size = fallback.Size;
        if (document.Width is not null || document.Height is not null)
        {
            if (TryGetWholeNumber(document.Width, out int width)
                && TryGetWholeNumber(document.Height, out int height)
                && CameraWindowSize.IsValid(width, height))
            {
                size = new CameraWindowSize(width, height);
            }
            else
            {
                warnings.Add(
                    $"{subject} size is invalid (whole numbers, width {CameraWindowSize.MinWidth}–{CameraWindowSize.MaxSize}, height {CameraWindowSize.MinHeight}–{CameraWindowSize.MaxSize}); {fallback.Size.Width} × {fallback.Size.Height} is used.");
            }
        }

        CameraWindowCorner corner = fallback.Corner;
        if (document.Corner is not null && !TryParseCorner(document.Corner, out corner))
        {
            warnings.Add($"{subject} has an unknown position; '{FormatCorner(fallback.Corner)}' is used.");
            corner = fallback.Corner;
        }

        CameraWindowLayout layout = new(size, corner);
        return layout == fallback ? fallback : layout;
    }

    /// <summary>
    /// The mini preview (T-089 Q4): as <see cref="ToLayout(CameraWindowLayoutDocument?, CameraWindowLayout, string, List{string})"/>,
    /// and a usable width below <see cref="CameraWindowSize.MinMiniWidth"/> (saved before the info strip existed) is raised
    /// to it with one warning. The image keeps its aspect ratio, like an old remembered mini preview does: the height is
    /// scaled by the same factor (rounded half away from zero, at most <see cref="CameraWindowSize.MaxSize"/>).
    /// </summary>
    private static CameraWindowLayout ToMiniLayout(CameraWindowLayoutDocument? document, CameraWindowLayout fallback, string subject, List<string> warnings)
    {
        CameraWindowLayout layout = ToLayout(document, fallback, subject, warnings);
        if (layout.Size.Width >= CameraWindowSize.MinMiniWidth)
        {
            return layout;
        }

        // The width is at least CameraWindowSize.MinWidth, so the factor is at most 1.5 and the height only grows.
        double height = Math.Round((double)layout.Size.Height * CameraWindowSize.MinMiniWidth / layout.Size.Width, MidpointRounding.AwayFromZero);
        int scaledHeight = (int)Math.Min(height, CameraWindowSize.MaxSize);
        warnings.Add($"{subject} is narrower than {CameraWindowSize.MinMiniWidth}; it is widened to {CameraWindowSize.MinMiniWidth} keeping its aspect ratio.");
        return layout with { Size = new CameraWindowSize(CameraWindowSize.MinMiniWidth, scaledHeight) };
    }

    /// <summary>Whole number that fits an <see cref="int"/>; fractions, non-finite and missing values are rejected.</summary>
    private static bool TryGetWholeNumber(double? value, out int result)
    {
        result = 0;
        if (value is not { } number || !double.IsFinite(number) || number != Math.Floor(number) || number < int.MinValue || number > int.MaxValue)
        {
            return false;
        }

        result = (int)number;
        return true;
    }

    private static string FormatStartMode(CameraWindowStartMode mode) => mode switch
    {
        CameraWindowStartMode.Last => nameof(CameraWindowStartMode.Last),
        CameraWindowStartMode.Normal => nameof(CameraWindowStartMode.Normal),
        CameraWindowStartMode.Mini => nameof(CameraWindowStartMode.Mini),
        CameraWindowStartMode.FullScreen => nameof(CameraWindowStartMode.FullScreen),
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown camera window start mode."),
    };

    private static bool TryParseStartMode(string value, out CameraWindowStartMode mode)
    {
        // Explicit names instead of Enum.Parse: numeric strings must not be accepted and the format must stay stable.
        ReadOnlySpan<char> name = value.AsSpan().Trim();
        foreach (CameraWindowStartMode candidate in (ReadOnlySpan<CameraWindowStartMode>)[CameraWindowStartMode.Last, CameraWindowStartMode.Normal, CameraWindowStartMode.Mini, CameraWindowStartMode.FullScreen])
        {
            if (name.Equals(FormatStartMode(candidate), StringComparison.OrdinalIgnoreCase))
            {
                mode = candidate;
                return true;
            }
        }

        mode = default;
        return false;
    }

    private static string FormatCorner(CameraWindowCorner corner) => corner switch
    {
        CameraWindowCorner.TopLeft => nameof(CameraWindowCorner.TopLeft),
        CameraWindowCorner.TopRight => nameof(CameraWindowCorner.TopRight),
        CameraWindowCorner.BottomLeft => nameof(CameraWindowCorner.BottomLeft),
        CameraWindowCorner.BottomRight => nameof(CameraWindowCorner.BottomRight),
        CameraWindowCorner.Center => nameof(CameraWindowCorner.Center),
        _ => throw new ArgumentOutOfRangeException(nameof(corner), corner, "Unknown camera window corner."),
    };

    private static bool TryParseCorner(string value, out CameraWindowCorner corner)
    {
        ReadOnlySpan<char> name = value.AsSpan().Trim();
        foreach (CameraWindowCorner candidate in (ReadOnlySpan<CameraWindowCorner>)[CameraWindowCorner.TopLeft, CameraWindowCorner.TopRight, CameraWindowCorner.BottomLeft, CameraWindowCorner.BottomRight, CameraWindowCorner.Center])
        {
            if (name.Equals(FormatCorner(candidate), StringComparison.OrdinalIgnoreCase))
            {
                corner = candidate;
                return true;
            }
        }

        corner = default;
        return false;
    }

    // Warnings are shown in the UI; a garbage file must not be able to push huge strings there.
    private static string? Shorten(string? value) =>
        value is { Length: > MaxEchoedValueLength } ? string.Concat(value.AsSpan(0, MaxEchoedValueLength), "…") : value;

    // Explicit names instead of Enum.Parse: numeric strings must not be accepted and the format must stay stable.
    private static string FormatConnectionType(ConnectionType connectionType) => connectionType switch
    {
        ConnectionType.PrusaLink => nameof(ConnectionType.PrusaLink),
        ConnectionType.PrusaConnect => nameof(ConnectionType.PrusaConnect),
        _ => throw new ArgumentOutOfRangeException(nameof(connectionType), connectionType, "Unknown connection type."),
    };

    private static bool TryParseConnectionType(string? value, out ConnectionType connectionType)
    {
        ReadOnlySpan<char> name = value.AsSpan().Trim();
        if (name.Equals(nameof(ConnectionType.PrusaLink), StringComparison.OrdinalIgnoreCase))
        {
            connectionType = ConnectionType.PrusaLink;
            return true;
        }

        if (name.Equals(nameof(ConnectionType.PrusaConnect), StringComparison.OrdinalIgnoreCase))
        {
            connectionType = ConnectionType.PrusaConnect;
            return true;
        }

        connectionType = default;
        return false;
    }

    /// <summary>The values of one widget entry as read by <see cref="TryReadWidget"/>; <see langword="null"/> = missing.</summary>
    private readonly record struct WidgetValues(string Id, JsonElement? Anchor, JsonElement? Size, JsonElement? Emphasized, JsonElement? Visible);
}
