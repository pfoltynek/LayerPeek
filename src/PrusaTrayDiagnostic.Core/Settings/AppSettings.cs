using System.Collections.ObjectModel;

using PrusaTrayDiagnostic.Core.Printers;

namespace PrusaTrayDiagnostic.Core.Settings;

/// <summary>
/// Application settings persisted between runs. Never contains secrets (see <see cref="Secrets.ISecretStore"/>).
/// </summary>
public sealed record AppSettings
{
    public static TimeSpan DefaultPollingInterval { get; } = TimeSpan.FromSeconds(5);

    public static TimeSpan MinPollingInterval { get; } = TimeSpan.FromSeconds(1);

    public static TimeSpan MaxPollingInterval { get; } = TimeSpan.FromMinutes(5);

    /// <summary>Settings used on first run or when the stored settings cannot be read.</summary>
    public static AppSettings Default { get; } = new();

    /// <summary>How often printers are polled; within <see cref="MinPollingInterval"/>–<see cref="MaxPollingInterval"/>.</summary>
    public TimeSpan PollingInterval
    {
        get;
        init
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, MinPollingInterval);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(value, MaxPollingInterval);
            field = value;
        }
    } = DefaultPollingInterval;

    /// <summary>
    /// Language of the texts: one of <see cref="AppLanguages.Supported"/>, or <see langword="null"/> to follow the
    /// system (the default). Number and date formats always follow the system.
    /// </summary>
    public string? Language
    {
        get;
        init
        {
            if (value is not null && !AppLanguages.IsSupported(value))
            {
                throw new ArgumentException($"Unsupported language code '{value}'.", nameof(value));
            }

            field = value;
        }
    }

    /// <summary>
    /// Detailed logging (T-047): the log file takes <c>Debug</c> messages too, applied at load and on every save, without a
    /// restart.
    /// Off by default (<c>Information</c>); meant to be turned on only while troubleshooting.
    /// </summary>
    public bool DebugLogging { get; init; }

    public NotificationSettings Notifications
    {
        get;
        init
        {
            ArgumentNullException.ThrowIfNull(value);
            field = value;
        }
    } = new();

    /// <summary>Configured printers with unique (case-insensitive) ids. The list is a read-only copy.</summary>
    public IReadOnlyList<PrinterDefinition> Printers
    {
        get;
        init => field = CopyValidated(value);
    } = ReadOnlyCollection<PrinterDefinition>.Empty;

    /// <summary>
    /// These settings with <paramref name="updated"/> as the camera window monitor of printer <paramref name="printerId"/>
    /// (case-insensitive) while it still is <paramref name="expected"/> (user decision 2026-10-23: the device path follows
    /// the monitor to another connector); the same instance when the printer is not configured, has another monitor or
    /// nothing would change. Nothing else changes.
    /// </summary>
    public AppSettings WithCameraMonitor(string printerId, Cameras.CameraMonitorId expected, Cameras.CameraMonitorId updated)
    {
        ArgumentNullException.ThrowIfNull(printerId);
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(updated);
        for (int i = 0; i < Printers.Count; i++)
        {
            PrinterDefinition printer = Printers[i];
            if (string.Equals(printer.Id, printerId, StringComparison.OrdinalIgnoreCase))
            {
                Cameras.CameraWindowDefaults window = printer.Camera.Window;
                if (window.Monitor != expected || expected == updated)
                {
                    return this;
                }

                PrinterDefinition[] printers = [.. Printers];
                printers[i] = printer with { Camera = printer.Camera with { Window = window with { Monitor = updated } } };
                return this with { Printers = printers };
            }
        }

        return this;
    }

    /// <summary>
    /// These settings with <paramref name="code"/> as the detected model of printer <paramref name="printerId"/>
    /// (case-insensitive); the same instance when the printer is not configured or already has that code, so that callers
    /// save only real changes. Nothing else changes (the image choice stays as the user set it).
    /// </summary>
    public AppSettings WithDetectedModel(string printerId, PrinterModelCode code)
    {
        ArgumentNullException.ThrowIfNull(printerId);
        for (int i = 0; i < Printers.Count; i++)
        {
            PrinterDefinition printer = Printers[i];
            if (string.Equals(printer.Id, printerId, StringComparison.OrdinalIgnoreCase))
            {
                if (printer.DetectedModel == code)
                {
                    return this;
                }

                PrinterDefinition[] printers = [.. Printers];
                printers[i] = printer with { DetectedModel = code };
                return this with { Printers = printers };
            }
        }

        return this;
    }

    // Value equality including the printer list (the synthesized one would compare the list by reference).
    public bool Equals(AppSettings? other) =>
        other is not null
        && PollingInterval == other.PollingInterval
        && string.Equals(Language, other.Language, StringComparison.Ordinal)
        && DebugLogging == other.DebugLogging
        && Notifications == other.Notifications
        && Printers.SequenceEqual(other.Printers);

    public override int GetHashCode() => HashCode.Combine(PollingInterval, Language, DebugLogging, Notifications, Printers.Count);

    private static ReadOnlyCollection<PrinterDefinition> CopyValidated(IReadOnlyList<PrinterDefinition> printers)
    {
        ArgumentNullException.ThrowIfNull(printers);

        PrinterDefinition[] copy = [.. printers];
        // Ids are compared case-insensitively because they become secret keys and some OS credential stores ignore case.
        HashSet<string> ids = new(copy.Length, StringComparer.OrdinalIgnoreCase);
        foreach (PrinterDefinition? printer in copy)
        {
            if (printer is null)
            {
                throw new ArgumentException("Printer list must not contain null entries.", nameof(printers));
            }

            if (!ids.Add(printer.Id))
            {
                throw new ArgumentException($"Duplicate printer id '{printer.Id}'.", nameof(printers));
            }
        }

        return new ReadOnlyCollection<PrinterDefinition>(copy);
    }
}
