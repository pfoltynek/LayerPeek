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

    // Value equality including the printer list (the synthesized one would compare the list by reference).
    public bool Equals(AppSettings? other) =>
        other is not null
        && PollingInterval == other.PollingInterval
        && Notifications == other.Notifications
        && Printers.SequenceEqual(other.Printers);

    public override int GetHashCode() => HashCode.Combine(PollingInterval, Notifications, Printers.Count);

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
