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
                Camera = new CameraDocument
                {
                    Enabled = printer.Camera.Enabled,
                    StreamUrl = printer.Camera.StreamUri?.OriginalString ?? printer.Camera.InvalidStreamUrl,
                },
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
            string? problem = TryCreatePrinter(documents[i], out PrinterDefinition? printer);
            if (printer is not null && !ids.Add(printer.Id))
            {
                problem = $"duplicate id '{Shorten(printer.Id)}'";
            }

            if (problem is null)
            {
                // Only for accepted entries, so that a skipped printer does not also produce a camera warning.
                CameraSettings camera = ToCamera(documents[i]!.Camera, printer!.Id, warnings);
                printers.Add(camera == CameraSettings.Disabled ? printer : printer with { Camera = camera });
            }
            else
            {
                warnings.Add($"Printer entry {i + 1} skipped: {problem}.");
            }
        }

        return printers;
    }

    /// <returns><see langword="null"/> on success, otherwise the reason why the entry is invalid.</returns>
    private static string? TryCreatePrinter(PrinterDocument? document, out PrinterDefinition? printer)
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
        };
        return null;
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
