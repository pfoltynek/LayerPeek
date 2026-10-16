namespace PrusaTrayDiagnostic.App.Tray.Menu;

/// <summary>
/// Keeps a platform menu (<see cref="INativeTrayMenu"/>) in line with the presenter's entries. Items are created once
/// and updated in place; a property is written only when its value changed, so an unchanged printer never touches the
/// platform menu. Every item remembers the entry it currently shows, so a click acts on the latest state (e.g. a new
/// address). Layout: printer items, separator, fixed items; the printer index therefore equals the menu index.
/// Free of Avalonia so that it is testable; UI thread only.
/// </summary>
internal sealed class TrayMenuTarget : ITrayMenuTarget
{
    private readonly INativeTrayMenu _menu;
    private readonly List<Slot> _printerSlots = [];
    private readonly List<Slot> _fixedSlots = [];

    public TrayMenuTarget(INativeTrayMenu menu)
    {
        ArgumentNullException.ThrowIfNull(menu);
        _menu = menu;
    }

    public event EventHandler<TrayMenuEntry>? ItemClicked;

    public void SetFixedItems(IReadOnlyList<TrayMenuEntry> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        foreach (Slot slot in _printerSlots)
        {
            slot.Detach();
        }

        foreach (Slot slot in _fixedSlots)
        {
            slot.Detach();
        }

        _printerSlots.Clear();
        _fixedSlots.Clear();
        _menu.Clear();

        // Always present, so that printer items (inserted in front of it) keep menu index == printer index.
        _menu.AddSeparator();
        foreach (TrayMenuEntry entry in items)
        {
            if (entry.IsSeparator)
            {
                _menu.AddSeparator();
                continue;
            }

            Slot slot = new(this, entry);
            _fixedSlots.Add(slot);
            _menu.Add(slot.Item);
        }
    }

    public void InsertPrinterItem(int index, TrayMenuEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentOutOfRangeException.ThrowIfGreaterThan((uint)index, (uint)_printerSlots.Count, nameof(index));
        Slot slot = new(this, entry);
        _printerSlots.Insert(index, slot);
        _menu.Insert(index, slot.Item);
    }

    public void UpdatePrinterItem(int index, TrayMenuEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        _printerSlots[index].Update(entry);
    }

    public void RemovePrinterItem(int index)
    {
        Slot slot = _printerSlots[index];
        slot.Detach();
        _printerSlots.RemoveAt(index);
        _menu.RemoveAt(index);
    }

    public void ClearPrinterItems()
    {
        for (int i = _printerSlots.Count - 1; i >= 0; i--)
        {
            RemovePrinterItem(i);
        }
    }

    /// <summary>One platform item and the entry it currently shows (what a click acts on).</summary>
    private sealed class Slot
    {
        private readonly TrayMenuTarget _owner;
        private TrayMenuEntry _entry;
        private bool _detached;

        public Slot(TrayMenuTarget owner, TrayMenuEntry entry)
        {
            _owner = owner;
            _entry = entry;
            Item = owner._menu.CreateItem(OnClicked);
            Item.SetHeader(NativeMenuText.EscapeAccessKeys(entry.Header));
            Item.SetEnabled(entry.IsEnabled);
            Item.SetToolTip(entry.ToolTip);
        }

        public INativeTrayMenuItem Item { get; }

        public void Update(TrayMenuEntry entry)
        {
            if (entry.Header != _entry.Header)
            {
                Item.SetHeader(NativeMenuText.EscapeAccessKeys(entry.Header));
            }

            if (entry.IsEnabled != _entry.IsEnabled)
            {
                Item.SetEnabled(entry.IsEnabled);
            }

            if (entry.ToolTip != _entry.ToolTip)
            {
                Item.SetToolTip(entry.ToolTip);
            }

            // Also when nothing visible changed: the click target (e.g. the address) may have.
            _entry = entry;
        }

        public void Detach()
        {
            _detached = true;
            Item.Detach();
        }

        private void OnClicked()
        {
            // A click can still be in flight from an open menu while the item is being removed.
            if (!_detached)
            {
                _owner.ItemClicked?.Invoke(_owner, _entry);
            }
        }
    }
}
