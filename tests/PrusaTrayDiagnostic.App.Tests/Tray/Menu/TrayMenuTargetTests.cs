using PrusaTrayDiagnostic.App.Printers;
using PrusaTrayDiagnostic.App.Tray.Menu;
using PrusaTrayDiagnostic.Core.Printers;

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
    public void PrinterIconIsSetOnCreationAndOnlyWhenItChanges()
    {
        // T-039: the printer picture as the item icon (Windows).
        PrinterPicture mk4 = PrinterPicture.FromImage(new PrinterImage(PrinterImageKind.Model, PrinterModel.Mk4S));
        _target.SetFixedItems([]);
        TrayMenuEntry entry = Printer("MK4: Idle") with { Icon = mk4 };

        _target.InsertPrinterItem(0, entry);
        FakeNativeItem item = _menu.Items[0];
        Assert.Same(mk4, item.Icon);
        Assert.Contains("icon=mk4", item.Writes);
        item.Writes.Clear();

        _target.UpdatePrinterItem(0, entry with { Header = "MK4: Printing – 1%", Icon = PrinterPicture.FromImage(new PrinterImage(PrinterImageKind.Model, PrinterModel.Mk4S)) });
        Assert.Equal(["header=MK4: Printing – 1%"], item.Writes);

        _target.UpdatePrinterItem(0, entry with { Icon = PrinterPicture.Generic });
        Assert.Contains("icon=generic", item.Writes);
        _target.UpdatePrinterItem(0, entry with { Icon = null });
        Assert.Null(item.Icon);
    }

    [Fact]
    public void ItemsWithoutAnIconNeverSetOne()
    {
        _target.SetFixedItems([About]);

        _target.InsertPrinterItem(0, Printer("MK4"));

        Assert.All(_menu.Items, static item => Assert.DoesNotContain(item.Writes, static write => write.StartsWith("icon=", StringComparison.Ordinal)));
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

    [Fact]
    public void PrinterWithSubmenuBuildsItsChildren()
    {
        _target.SetFixedItems([Quit]);

        _target.InsertPrinterItem(0, Parent("A: Idle", "a"));
        _target.InsertPrinterItem(1, Parent("B_x: Idle", "b"));

        Assert.Equal(["A: Idle", "B__x: Idle", "---", "Quit"], _menu.Layout());
        FakeNativeItem b = _menu.Items[1];
        Assert.NotNull(b.Submenu);
        Assert.Equal(["Open b", "Diagnostics…"], b.Submenu.Layout());
        Assert.Null(_menu.Items[^1].Submenu);
    }

    [Fact]
    public void ClickOnSubmenuChildReportsChildEntryAndHeaderClickIsIgnored()
    {
        _target.SetFixedItems([]);
        _target.InsertPrinterItem(0, Parent("A: Idle", "a"));
        _target.InsertPrinterItem(1, Parent("B: Idle", "b"));

        _menu.Items[1].Click();
        _menu.Items[1].Submenu!.Items[1].Click();
        _menu.Items[0].Submenu!.Items[0].Click();

        Assert.Collection(
            _clicked,
            entry => Assert.Equal((TrayMenuCommand.Diagnostics, "b"), (entry.Command, entry.PrinterId)),
            entry => Assert.Equal((TrayMenuCommand.OpenWeb, new Uri("http://a/")), (entry.Command, entry.WebUri)));
    }

    [Fact]
    public void StateChangeUpdatesOnlyTheSubmenuHeaderInPlace()
    {
        _target.SetFixedItems([]);
        TrayMenuEntry first = Parent("A: Idle", "a");
        _target.InsertPrinterItem(0, first);
        FakeNativeItem item = _menu.Items[0];
        FakeNativeItem[] children = [.. item.Submenu!.Items];
        int structureChanges = _menu.StructureChanges;
        item.Writes.Clear();
        foreach (FakeNativeItem child in children)
        {
            child.Writes.Clear();
        }

        _target.UpdatePrinterItem(0, first with { Header = "A: Printing – 3%" });

        Assert.Same(item, _menu.Items[0]);
        Assert.Equal(["header=A: Printing – 3%"], item.Writes);
        Assert.Equal(children, item.Submenu!.Items);
        Assert.All(children, child => Assert.Empty(child.Writes));
        Assert.Equal(structureChanges, _menu.StructureChanges);
    }

    [Fact]
    public void ChangedChildIsUpdatedInPlaceAndClickUsesIt()
    {
        _target.SetFixedItems([]);
        TrayMenuEntry first = Parent("A: Idle", "a");
        _target.InsertPrinterItem(0, first);
        FakeNativeItem web = _menu.Items[0].Submenu!.Items[0];
        web.Writes.Clear();

        TrayMenuEntry moved = first with { Submenu = new TrayMenuEntryList([first.Submenu![0] with { WebUri = new Uri("http://moved/") }, first.Submenu[1]]) };
        _target.UpdatePrinterItem(0, moved);
        web.Click();

        Assert.Empty(web.Writes);
        Assert.Equal(new Uri("http://moved/"), Assert.Single(_clicked).WebUri);
    }

    [Fact]
    public void SwitchingBetweenFlatAndSubmenuReplacesTheItemAtTheSameIndex()
    {
        _target.SetFixedItems([Quit]);
        _target.InsertPrinterItem(0, Printer("A"));
        _target.InsertPrinterItem(1, Printer("B"));
        FakeNativeItem flat = _menu.Items[0];

        _target.UpdatePrinterItem(0, Parent("A: Idle", "a"));

        Assert.True(flat.Detached);
        Assert.Equal(["A: Idle", "B", "---", "Quit"], _menu.Layout());
        Assert.NotNull(_menu.Items[0].Submenu);

        FakeNativeItem parent = _menu.Items[0];
        FakeNativeItem child = parent.Submenu!.Items[0];
        _target.UpdatePrinterItem(0, Printer("A"));

        Assert.True(parent.Detached);
        Assert.True(child.Detached);
        Assert.Null(_menu.Items[0].Submenu);
        Assert.Equal(["A", "B", "---", "Quit"], _menu.Layout());
    }

    [Fact]
    public void RemovedSubmenuNoLongerReportsChildClicks()
    {
        _target.SetFixedItems([]);
        _target.InsertPrinterItem(0, Parent("A: Idle", "a"));
        FakeNativeItem child = _menu.Items[0].Submenu!.Items[1];

        _target.RemovePrinterItem(0);
        child.Click();

        Assert.True(child.Detached);
        Assert.Empty(_clicked);
    }

    // ---- Check items (T-040: the camera item) ----

    private static TrayMenuEntry Camera(bool isChecked) =>
        new(TrayMenuCommand.Camera, "Camera", IsEnabled: true, PrinterId: "mk4") { IsChecked = isChecked };

    [Fact]
    public void PlainItemsAreNeverCheckItems()
    {
        _target.SetFixedItems([About]);
        _target.InsertPrinterItem(0, Printer("MK4"));

        Assert.All(_menu.Items, item => Assert.Null(item.IsChecked));
        Assert.All(_menu.Items, item => Assert.DoesNotContain(item.Writes, write => write.StartsWith("checked=", StringComparison.Ordinal)));
    }

    [Fact]
    public void CheckItemShowsItsStateAndUpdatesOnlyWhenItChanges()
    {
        _target.SetFixedItems([]);
        _target.InsertPrinterItem(0, Camera(isChecked: false));
        FakeNativeItem item = _menu.Items[0];
        Assert.False(item.IsChecked);
        item.Writes.Clear();

        _target.UpdatePrinterItem(0, Camera(isChecked: false));
        Assert.Empty(item.Writes);

        _target.UpdatePrinterItem(0, Camera(isChecked: true));
        Assert.Equal(["checked=True"], item.Writes);
        Assert.True(item.IsChecked);
    }

    [Fact]
    public void CheckItemInASubmenuFollowsItsEntry()
    {
        _target.SetFixedItems([]);
        TrayMenuEntry header = new(TrayMenuCommand.None, "MK4", IsEnabled: true) { Submenu = new TrayMenuEntryList([Printer("Open"), Camera(isChecked: false)]) };
        _target.InsertPrinterItem(0, header);
        FakeNativeItem camera = _menu.Items[0].Submenu!.Items[1];
        Assert.False(camera.IsChecked);

        _target.UpdatePrinterItem(0, header with { Submenu = new TrayMenuEntryList([Printer("Open"), Camera(isChecked: true)]) });

        Assert.True(camera.IsChecked);
        Assert.Same(camera, _menu.Items[0].Submenu!.Items[1]);
    }

    [Fact]
    public void ClickedCheckItemShowsTheStateOfItsEntryAfterTheClick()
    {
        // A platform may flip the check mark itself; the entry decides (here the click did not open anything).
        _target.SetFixedItems([]);
        _target.InsertPrinterItem(0, Camera(isChecked: false));
        FakeNativeItem item = _menu.Items[0];
        item.SetChecked(true);

        item.Click();

        Assert.False(item.IsChecked);
        Assert.Equal(TrayMenuCommand.Camera, Assert.Single(_clicked).Command);
    }

    [Fact]
    public void ClickedCheckItemShowsTheStateSetDuringTheClick()
    {
        _target.SetFixedItems([]);
        _target.InsertPrinterItem(0, Camera(isChecked: false));
        FakeNativeItem item = _menu.Items[0];
        // The click opens the camera window, which checks the item before the click returns.
        _target.ItemClicked += (_, _) => _target.UpdatePrinterItem(0, Camera(isChecked: true));

        item.Click();

        Assert.True(item.IsChecked);
    }

    private static TrayMenuEntry Printer(string header, string? uri = "http://printer/") =>
        new(TrayMenuCommand.OpenWeb, header, IsEnabled: uri is not null, "Open", uri is null ? null : new Uri(uri));

    private static TrayMenuEntry Parent(string header, string id) =>
        new(TrayMenuCommand.None, header, IsEnabled: true, PrinterId: id)
        {
            Submenu = new TrayMenuEntryList(
            [
                new TrayMenuEntry(TrayMenuCommand.OpenWeb, "Open " + id, IsEnabled: true, WebUri: new Uri($"http://{id}/"), PrinterId: id),
                new TrayMenuEntry(TrayMenuCommand.Diagnostics, "Diagnostics…", IsEnabled: true, PrinterId: id),
            ]),
        };

    private sealed class FakeNativeMenu : INativeTrayMenu
    {
        private readonly List<FakeNativeItem?> _entries = [];

        /// <summary>Real items in menu order (separators skipped).</summary>
        public List<FakeNativeItem> Items => [.. _entries.OfType<FakeNativeItem>()];

        /// <summary>Inserts, adds, removals and clears (structure changes of this menu level).</summary>
        public int StructureChanges { get; private set; }

        public INativeTrayMenuItem CreateItem(Action clicked) => new FakeNativeItem(clicked);

        public void Insert(int index, INativeTrayMenuItem item)
        {
            StructureChanges++;
            _entries.Insert(index, (FakeNativeItem)item);
        }

        public void Add(INativeTrayMenuItem item)
        {
            StructureChanges++;
            _entries.Add((FakeNativeItem)item);
        }

        public void AddSeparator() => _entries.Add(null);

        public void RemoveAt(int index)
        {
            StructureChanges++;
            _entries.RemoveAt(index);
        }

        public void Clear()
        {
            StructureChanges++;
            _entries.Clear();
        }

        public string[] Layout() => [.. _entries.Select(entry => entry?.Header ?? "---")];
    }

    private sealed class FakeNativeItem(Action clicked) : INativeTrayMenuItem
    {
        public List<string> Writes { get; } = [];

        public string Header { get; private set; } = string.Empty;

        public bool Detached { get; private set; }

        public FakeNativeMenu? Submenu { get; private set; }

        public INativeTrayMenu CreateSubmenu()
        {
            Assert.Null(Submenu);
            Submenu = new FakeNativeMenu();
            return Submenu;
        }

        public void SetHeader(string header)
        {
            Header = header;
            Writes.Add("header=" + header);
        }

        public void SetEnabled(bool enabled) => Writes.Add("enabled=" + enabled);

        public void SetToolTip(string? toolTip) => Writes.Add("tooltip=" + toolTip);

        public PrinterPicture? Icon { get; private set; }

        public string? IconPrinterId { get; private set; }

        public void SetIcon(PrinterPicture? icon, string? printerId)
        {
            Icon = icon;
            IconPrinterId = printerId;
            Writes.Add("icon=" + icon?.IllustrationId);
        }

        public bool? IsChecked { get; private set; }

        public void SetChecked(bool? isChecked)
        {
            IsChecked = isChecked;
            Writes.Add("checked=" + isChecked);
        }

        public void Detach() => Detached = true;

        public void Click() => clicked();
    }
}
