namespace PrusaTrayDiagnostic.App.Tray.Menu;

/// <summary>
/// Keeps a platform menu (<see cref="INativeTrayMenu"/>) in line with the presenter's entries. Items are created once
/// and updated in place; a property is written only when its value changed, so an unchanged printer never touches the
/// platform menu. Every item remembers the entry it currently shows, so a click acts on the latest state (e.g. a new
/// address). Layout: printer items, separator, fixed items; the printer index therefore equals the menu index.
/// A printer item with a submenu (several printers) keeps its submenu items and updates them in place too; only a change
/// of structure (plain item ↔ submenu, another number of submenu items) replaces the platform item at the same index.
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

            Slot slot = new(this, _menu, entry);
            _fixedSlots.Add(slot);
            _menu.Add(slot.Item);
        }
    }

    public void InsertPrinterItem(int index, TrayMenuEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentOutOfRangeException.ThrowIfGreaterThan((uint)index, (uint)_printerSlots.Count, nameof(index));
        Slot slot = new(this, _menu, entry);
        _printerSlots.Insert(index, slot);
        _menu.Insert(index, slot.Item);
    }

    public void UpdatePrinterItem(int index, TrayMenuEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        Slot slot = _printerSlots[index];
        if (slot.HasSameStructure(entry))
        {
            slot.Update(entry);
            return;
        }

        // Happens only when the printer set changes (one printer ↔ several printers), never on a poll.
        RemovePrinterItem(index);
        InsertPrinterItem(index, entry);
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

    /// <summary>One platform item (with its submenu items, if any) and the entry it currently shows (what a click acts on).</summary>
    private sealed class Slot
    {
        private readonly TrayMenuTarget _owner;
        private readonly Slot[]? _children;
        private TrayMenuEntry _entry;
        private bool _detached;

        public Slot(TrayMenuTarget owner, INativeTrayMenu menu, TrayMenuEntry entry)
        {
            _owner = owner;
            _entry = entry;
            Item = menu.CreateItem(OnClicked);
            Item.SetHeader(NativeMenuText.EscapeAccessKeys(entry.Header));
            Item.SetEnabled(entry.IsEnabled);
            Item.SetToolTip(entry.ToolTip);
            if (entry.Icon is not null)
            {
                Item.SetIcon(entry.Icon, entry.PrinterId);
            }

            if (entry.IsChecked is not null)
            {
                Item.SetChecked(entry.IsChecked);
            }

            if (entry.Submenu is { } submenu)
            {
                INativeTrayMenu native = Item.CreateSubmenu();
                _children = new Slot[submenu.Count];
                for (int i = 0; i < _children.Length; i++)
                {
                    if (submenu[i].IsSeparator || submenu[i].Submenu is not null)
                    {
                        throw new ArgumentException("Submenu items must be plain items.", nameof(entry));
                    }

                    _children[i] = new Slot(owner, native, submenu[i]);
                    native.Add(_children[i].Item);
                }
            }
        }

        public INativeTrayMenuItem Item { get; }

        /// <summary>Whether <paramref name="entry"/> can be shown by this platform item (same submenu shape).</summary>
        public bool HasSameStructure(TrayMenuEntry entry) =>
            entry.Submenu is null ? _children is null : _children is not null && _children.Length == entry.Submenu.Count;

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

            if (!Equals(entry.Icon, _entry.Icon))
            {
                Item.SetIcon(entry.Icon, entry.PrinterId);
            }

            if (entry.IsChecked != _entry.IsChecked)
            {
                Item.SetChecked(entry.IsChecked);
            }

            // The view model reuses the submenu list while only the state changes, so a poll skips the children.
            if (_children is not null && entry.Submenu is { } submenu && !ReferenceEquals(submenu, _entry.Submenu))
            {
                for (int i = 0; i < _children.Length; i++)
                {
                    _children[i].Update(submenu[i]);
                }
            }

            // Also when nothing visible changed: the click target (e.g. the address) may have.
            _entry = entry;
        }

        public void Detach()
        {
            _detached = true;
            Item.Detach();
            if (_children is not null)
            {
                foreach (Slot child in _children)
                {
                    child.Detach();
                }
            }
        }

        private void OnClicked()
        {
            // A click can still be in flight from an open menu while the item is being removed; a submenu header only
            // opens its submenu.
            if (!_detached && _entry.Submenu is null)
            {
                _owner.ItemClicked?.Invoke(_owner, _entry);

                // A platform may flip the check mark of a clicked check item by itself; the entry (updated by the click, e.g.
                // the camera window opened, or not) decides what it shows (T-040).
                if (!_detached && _entry.IsChecked is not null)
                {
                    Item.SetChecked(_entry.IsChecked);
                }
            }
        }
    }
}
