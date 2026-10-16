using PrusaTrayDiagnostic.App.Tray.Menu;

namespace PrusaTrayDiagnostic.App.Tests.Tray.Menu;

public sealed class TrayMenuTargetTests
{
    private static readonly TrayMenuEntry About = new(TrayMenuCommand.About, "About", IsEnabled: true);
    private static readonly TrayMenuEntry Quit = new(TrayMenuCommand.Quit, "Quit", IsEnabled: true);

    private readonly FakeNativeMenu _menu = new();
    private readonly List<TrayMenuEntry> _clicked = [];
    private readonly TrayMenuTarget _target;

    public TrayMenuTargetTests()
    {
        _target = new TrayMenuTarget(_menu);
        _target.ItemClicked += (_, entry) => _clicked.Add(entry);
    }

    [Fact]
    public void FixedItemsFollowSeparatorAfterPrinters()
    {
        _target.SetFixedItems([About, TrayMenuEntry.Separator, Quit]);

        Assert.Equal(["---", "About", "---", "Quit"], _menu.Layout());
    }

    [Fact]
    public void PrinterIndexIsMenuIndex()
    {
        _target.SetFixedItems([About, Quit]);

        _target.InsertPrinterItem(0, Printer("B"));
        _target.InsertPrinterItem(0, Printer("A"));
        _target.InsertPrinterItem(2, Printer("C"));
        Assert.Equal(["A", "B", "C", "---", "About", "Quit"], _menu.Layout());

        _target.RemovePrinterItem(1);
        Assert.Equal(["A", "C", "---", "About", "Quit"], _menu.Layout());

        _target.ClearPrinterItems();
        Assert.Equal(["---", "About", "Quit"], _menu.Layout());
    }

    [Fact]
    public void ClickAfterUpdateActsOnNewEntry()
    {
        _target.SetFixedItems([Quit]);
        _target.InsertPrinterItem(0, Printer("MK4", "http://old/"));
        _target.UpdatePrinterItem(0, Printer("MK4", "http://new/"));

        _menu.Items[0].Click();

        Assert.Equal(new Uri("http://new/"), Assert.Single(_clicked).WebUri);
    }

    [Fact]
    public void ClickOnFixedItemReportsItsEntry()
    {
        _target.SetFixedItems([About, Quit]);

        _menu.Items[^1].Click();

        Assert.Same(Quit, Assert.Single(_clicked));
    }

    [Fact]
    public void UpdateSetsOnlyChangedProperties()
    {
        _target.SetFixedItems([]);
        TrayMenuEntry first = Printer("MK4: Idle", "http://a/");
        _target.InsertPrinterItem(0, first);
        FakeNativeItem item = _menu.Items[0];
        item.Writes.Clear();

        _target.UpdatePrinterItem(0, first with { });
        Assert.Empty(item.Writes);

        _target.UpdatePrinterItem(0, first with { Header = "MK4: Printing – 1%" });
        Assert.Equal(["header=MK4: Printing – 1%"], item.Writes);

        item.Writes.Clear();
        _target.UpdatePrinterItem(0, first with { Header = "MK4: Printing – 1%", IsEnabled = false, ToolTip = "Invalid" });
        Assert.Equal(["enabled=False", "tooltip=Invalid"], item.Writes);
    }

    [Fact]
    public void HeadersAreEscapedForAccessKeys()
    {
        _target.SetFixedItems([]);

        _target.InsertPrinterItem(0, Printer("MK4_test"));

        Assert.Equal("MK4__test", _menu.Items[0].Header);
    }

    [Fact]
    public void RemovedItemNoLongerReportsClicks()
    {
        _target.SetFixedItems([]);
        _target.InsertPrinterItem(0, Printer("A"));
        FakeNativeItem removed = _menu.Items[0];

        _target.RemovePrinterItem(0);
        removed.Click();

        Assert.True(removed.Detached);
        Assert.Empty(_clicked);
    }

    [Fact]
    public void SettingFixedItemsAgainReplacesWholeMenu()
    {
        _target.SetFixedItems([About]);
        _target.InsertPrinterItem(0, Printer("A"));
        FakeNativeItem oldAbout = _menu.Items[^1];

        _target.SetFixedItems([Quit]);

        Assert.Equal(["---", "Quit"], _menu.Layout());
        Assert.True(oldAbout.Detached);
    }

    private static TrayMenuEntry Printer(string header, string? uri = "http://printer/") =>
        new(TrayMenuCommand.OpenWeb, header, IsEnabled: uri is not null, "Open", uri is null ? null : new Uri(uri));

    private sealed class FakeNativeMenu : INativeTrayMenu
    {
        private readonly List<FakeNativeItem?> _entries = [];

        /// <summary>Real items in menu order (separators skipped).</summary>
        public List<FakeNativeItem> Items => [.. _entries.OfType<FakeNativeItem>()];

        public INativeTrayMenuItem CreateItem(Action clicked) => new FakeNativeItem(clicked);

        public void Insert(int index, INativeTrayMenuItem item) => _entries.Insert(index, (FakeNativeItem)item);

        public void Add(INativeTrayMenuItem item) => _entries.Add((FakeNativeItem)item);

        public void AddSeparator() => _entries.Add(null);

        public void RemoveAt(int index) => _entries.RemoveAt(index);

        public void Clear() => _entries.Clear();

        public string[] Layout() => [.. _entries.Select(entry => entry?.Header ?? "---")];
    }

    private sealed class FakeNativeItem(Action clicked) : INativeTrayMenuItem
    {
        public List<string> Writes { get; } = [];

        public string Header { get; private set; } = string.Empty;

        public bool Detached { get; private set; }

        public void SetHeader(string header)
        {
            Header = header;
            Writes.Add("header=" + header);
        }

        public void SetEnabled(bool enabled) => Writes.Add("enabled=" + enabled);

        public void SetToolTip(string? toolTip) => Writes.Add("tooltip=" + toolTip);

        public void Detach() => Detached = true;

        public void Click() => clicked();
    }
}
