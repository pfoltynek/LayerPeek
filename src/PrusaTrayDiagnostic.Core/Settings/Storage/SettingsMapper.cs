using PrusaTrayDiagnostic.Core.Cameras;
using PrusaTrayDiagnostic.Core.Printers;

namespace PrusaTrayDiagnostic.Core.Settings.Storage;

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
                RemainingTimeMinutes = notifications.RemainingTimeMinutes,
                Sound = FormatSound(notifications.Sound),
            },
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
            })],
        };
    }

    public static AppSettings ToSettings(SettingsDocument document, List<string> warnings)
    {
        if (document.Version > SettingsDocument.CurrentVersion)
        {
            warnings.Add($"Settings were written by a newer version (format {document.Version}); unknown values will be lost on save.");
        }

        return new AppSettings
        {
            PollingInterval = ToPollingInterval(document.PollingIntervalSeconds, warnings),
            Language = ToLanguage(document.Language, warnings),
            DebugLogging = document.DebugLogging ?? false,
            Notifications = ToNotifications(document.Notifications, warnings),
            Printers = ToPrinters(document.Printers, warnings),
        };
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

    private static List<PrinterDefinition> ToPrinters(List<PrinterDocument?>? documents, List<string> warnings)
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

                CameraOverlayOptions overlay = ToCameraOverlay(documents[i]!.Camera?.Overlay, printer.Id, warnings);
                if (!ReferenceEquals(overlay, CameraOverlayOptions.Default))
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
                ? new CameraMonitorDocument { DisplayName = monitor.DisplayName, Width = monitor.Width, Height = monitor.Height, DevicePath = monitor.DevicePath }
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
            Mini = ToLayout(document.Mini, defaults.Mini, $"{subject}: mini preview", warnings),
            TopmostNormal = document.TopmostNormal ?? defaults.TopmostNormal,
            TopmostMini = document.TopmostMini ?? defaults.TopmostMini,
        };

        return window == defaults ? defaults : window;
    }

    /// <summary>Not written while everything is the default, so that files stay as they were and follow future defaults.</summary>
    private static CameraOverlayDocument? ToDocument(CameraOverlayOptions overlay)
    {
        if (overlay == CameraOverlayOptions.Default)
        {
            return null;
        }

        return new CameraOverlayDocument
        {
            Corner = FormatOverlayCorner(overlay.Corner),
            Normal = FormatOverlayItems(overlay.Normal),
            Mini = FormatOverlayItems(overlay.Mini),
        };
    }

    /// <summary>
    /// A missing section or value means the default. An unknown corner falls back to the default corner, unknown item
    /// names are ignored (the others are kept); one warning per value, without echoing it.
    /// </summary>
    private static CameraOverlayOptions ToCameraOverlay(CameraOverlayDocument? document, string printerId, List<string> warnings)
    {
        if (document is null)
        {
            return CameraOverlayOptions.Default;
        }

        CameraOverlayOptions defaults = CameraOverlayOptions.Default;
        string subject = $"Camera overlay of printer '{Shorten(printerId)}'";
        CameraOverlayCorner corner = defaults.Corner;
        if (document.Corner is not null && !TryParseOverlayCorner(document.Corner, out corner))
        {
            warnings.Add($"{subject}: unknown corner; '{FormatOverlayCorner(defaults.Corner)}' is used.");
            corner = defaults.Corner;
        }

        CameraOverlayOptions overlay = new()
        {
            Corner = corner,
            Normal = ToOverlayItems(document.Normal, defaults.Normal, $"{subject}: normal window", warnings),
            Mini = ToOverlayItems(document.Mini, defaults.Mini, $"{subject}: mini preview", warnings),
        };

        return overlay == defaults ? defaults : overlay;
    }

    private static CameraOverlayItems ToOverlayItems(List<string?>? names, CameraOverlayItems fallback, string subject, List<string> warnings)
    {
        if (names is null)
        {
            return fallback;
        }

        CameraOverlayItems items = CameraOverlayItems.None;
        int unknown = 0;
        foreach (string? name in names)
        {
            if (name is not null && TryParseOverlayItem(name, out CameraOverlayItems item))
            {
                items |= item;
            }
            else
            {
                unknown++;
            }
        }

        if (unknown > 0)
        {
            warnings.Add(unknown == 1
                ? $"{subject}: 1 unknown item is ignored."
                : $"{subject}: {unknown} unknown items are ignored.");
        }

        return items;
    }

    /// <summary>The names of the set items in display order (<see cref="CameraOverlayOptions.Items"/>).</summary>
    private static List<string?> FormatOverlayItems(CameraOverlayItems items)
    {
        List<string?> names = [];
        foreach (CameraOverlayItems item in CameraOverlayOptions.Items)
        {
            if ((items & item) != 0)
            {
                names.Add(FormatOverlayItem(item));
            }
        }

        return names;
    }

    // Explicit names instead of Enum.Parse: numeric strings and combined flags ("State, Progress") must not be accepted.
    private static string FormatOverlayItem(CameraOverlayItems item) => item switch
    {
        CameraOverlayItems.PrinterName => nameof(CameraOverlayItems.PrinterName),
        CameraOverlayItems.State => nameof(CameraOverlayItems.State),
        CameraOverlayItems.NozzleTemperature => nameof(CameraOverlayItems.NozzleTemperature),
        CameraOverlayItems.BedTemperature => nameof(CameraOverlayItems.BedTemperature),
        CameraOverlayItems.Progress => nameof(CameraOverlayItems.Progress),
        CameraOverlayItems.RemainingTime => nameof(CameraOverlayItems.RemainingTime),
        CameraOverlayItems.FinishTime => nameof(CameraOverlayItems.FinishTime),
        CameraOverlayItems.PrintTime => nameof(CameraOverlayItems.PrintTime),
        CameraOverlayItems.FileName => nameof(CameraOverlayItems.FileName),
        _ => throw new ArgumentOutOfRangeException(nameof(item), item, "Unknown camera overlay item."),
    };

    private static bool TryParseOverlayItem(string value, out CameraOverlayItems item)
    {
        ReadOnlySpan<char> name = value.AsSpan().Trim();
        foreach (CameraOverlayItems candidate in CameraOverlayOptions.Items)
        {
            if (name.Equals(FormatOverlayItem(candidate), StringComparison.OrdinalIgnoreCase))
            {
                item = candidate;
                return true;
            }
        }

        item = CameraOverlayItems.None;
        return false;
    }

    private static string FormatOverlayCorner(CameraOverlayCorner corner) => corner switch
    {
        CameraOverlayCorner.TopLeft => nameof(CameraOverlayCorner.TopLeft),
        CameraOverlayCorner.TopRight => nameof(CameraOverlayCorner.TopRight),
        CameraOverlayCorner.BottomLeft => nameof(CameraOverlayCorner.BottomLeft),
        CameraOverlayCorner.BottomRight => nameof(CameraOverlayCorner.BottomRight),
        _ => throw new ArgumentOutOfRangeException(nameof(corner), corner, "Unknown camera overlay corner."),
    };

    private static bool TryParseOverlayCorner(string value, out CameraOverlayCorner corner)
    {
        ReadOnlySpan<char> name = value.AsSpan().Trim();
        foreach (CameraOverlayCorner candidate in (ReadOnlySpan<CameraOverlayCorner>)[CameraOverlayCorner.TopLeft, CameraOverlayCorner.TopRight, CameraOverlayCorner.BottomLeft, CameraOverlayCorner.BottomRight])
        {
            if (name.Equals(FormatOverlayCorner(candidate), StringComparison.OrdinalIgnoreCase))
            {
                corner = candidate;
                return true;
            }
        }

        corner = default;
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
                devicePath = null;
            }

            return new CameraMonitorId(document.DisplayName!, width, height, devicePath);
        }

        warnings.Add($"{subject}: invalid monitor; the primary monitor is used.");
        return null;
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
}
