using System.Diagnostics.CodeAnalysis;

using LayerPeek.Core.Printers;

namespace LayerPeek.Core.Monitoring;

/// <summary>
/// Latest snapshot per printer of one monitoring run. The layout is fixed at construction, so every printer
/// loop writes only its own slot and readers never take a lock.
/// </summary>
internal sealed class SnapshotTable
{
    private readonly PrinterSnapshot?[] _slots;
    private readonly Dictionary<string, int> _slotById;

    public SnapshotTable(IReadOnlyList<PrinterDefinition> printers)
    {
        _slots = new PrinterSnapshot?[printers.Count];
        _slotById = new Dictionary<string, int>(printers.Count, StringComparer.OrdinalIgnoreCase);
        for (int slot = 0; slot < printers.Count; slot++)
        {
            PrinterDefinition printer = printers[slot] ?? throw new ArgumentException("Printer list must not contain null entries.", nameof(printers));
            if (!_slotById.TryAdd(printer.Id, slot))
            {
                throw new ArgumentException($"Duplicate printer id '{printer.Id}'.", nameof(printers));
            }
        }
    }

    public static SnapshotTable Empty { get; } = new([]);

    public void Set(int slot, PrinterSnapshot snapshot) => Volatile.Write(ref _slots[slot], snapshot);

    public bool TryGet(string printerId, [NotNullWhen(true)] out PrinterSnapshot? snapshot)
    {
        snapshot = _slotById.TryGetValue(printerId, out int slot) ? Volatile.Read(ref _slots[slot]) : null;
        return snapshot is not null;
    }

    public PrinterSnapshot[] ToArray()
    {
        // Read every slot once, so that a concurrent first write cannot change the count while copying.
        PrinterSnapshot?[] current = new PrinterSnapshot?[_slots.Length];
        int count = 0;
        for (int slot = 0; slot < _slots.Length; slot++)
        {
            if ((current[slot] = Volatile.Read(ref _slots[slot])) is not null)
            {
                count++;
            }
        }

        if (count == current.Length)
        {
            return current!;
        }

        PrinterSnapshot[] snapshots = new PrinterSnapshot[count];
        int index = 0;
        foreach (PrinterSnapshot? snapshot in current)
        {
            if (snapshot is not null)
            {
                snapshots[index++] = snapshot;
            }
        }

        return snapshots;
    }
}
