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
            },
            Printers = [.. settings.Printers.Select(static printer => new PrinterDocument
            {
                Id = printer.Id,
                Name = printer.Name,
                ConnectionType = FormatConnectionType(printer.ConnectionType),
                Address = printer.Address,
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
            Notifications = ToNotifications(document.Notifications),
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

    private static NotificationSettings ToNotifications(NotificationsDocument? document)
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
        };
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
                printers.Add(printer!);
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
