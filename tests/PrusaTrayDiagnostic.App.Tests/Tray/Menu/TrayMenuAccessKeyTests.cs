using System.Globalization;

using PrusaTrayDiagnostic.App.Tray.Menu;
using PrusaTrayDiagnostic.Core.Cameras;
using PrusaTrayDiagnostic.Core.Printers;
using PrusaTrayDiagnostic.Core.Settings;

using static PrusaTrayDiagnostic.App.Tests.Tray.TestPrinters;

namespace PrusaTrayDiagnostic.App.Tests.Tray.Menu;

/// <summary>
/// Access keys of the tray menu (T-042) in every supported language: each fixed and submenu item has exactly one, they are
/// unique within each menu level, and printer names, states and messages never get one (an "_" in a name is escaped).
/// </summary>
public sealed class TrayMenuAccessKeyTests
{
    private static readonly Uri StreamUri = new("rtsp://192.168.0.227/live");

    public static TheoryData<string> Languages => [.. AppLanguages.Supported];

    [Fact]
    public void EveryAppLanguageIsChecked()
    {
        Assert.Equal(8, AppLanguages.Supported.Count);
    }

    [Theory]
    [MemberData(nameof(Languages))]
    public void MainMenuWithoutPrintersHasUniqueAccessKeys(string language)
    {
        using CultureScope culture = new(language);
        using Menu menu = new([]);

        IReadOnlyList<TrayMenuEntry> level = menu.MainLevel();

        Assert.Equal([TrayMenuCommand.None, TrayMenuCommand.AddPrinter, TrayMenuCommand.Settings, TrayMenuCommand.About, TrayMenuCommand.Quit], level.Select(static item => item.Command));
        AssertUniqueAccessKeys(level.Where(static item => item.Command != TrayMenuCommand.None), language);
        AssertNoAccessKey(level[0]);
    }

    [Theory]
    [MemberData(nameof(Languages))]
    public void MainMenuWithPrintersHasUniqueAccessKeysAndPrintersHaveNone(string language)
    {
        using CultureScope culture = new(language);
        using Menu menu = new([WithCamera(Definition("a", "_Lab_MK4")), Definition("b", "XL__2") with { Address = "ftp://invalid" }]);

        IReadOnlyList<TrayMenuEntry> level = menu.MainLevel();

        AssertUniqueAccessKeys(level.Where(static item => item.Command != TrayMenuCommand.None), language);
        // The names show unchanged: their "_" are escaped, not taken as access keys.
        Assert.Equal(["_Lab_MK4", "XL__2"], level.Where(static item => item.Submenu is not null).Select(static item => NamePart(item)));
        Assert.All(level.Where(static item => item.Submenu is not null), AssertNoAccessKey);
    }

    [Theory]
    [MemberData(nameof(Languages))]
    public void PrinterSubmenusHaveUniqueAccessKeys(string language)
    {
        using CultureScope culture = new(language);
        PrinterDefinition connect = new() { Id = "c", Name = "Connect", ConnectionType = ConnectionType.PrusaConnect, Address = "uuid" };
        using Menu menu = new([WithCamera(Definition("a", "Alpha")), WithCamera(connect), Definition("b", "Beta")]);

        foreach (TrayMenuEntry printer in menu.MainLevel().Where(static item => item.Submenu is not null))
        {
            Assert.All(printer.Submenu!, static item => Assert.Null(item.Icon));
            AssertUniqueAccessKeys(printer.Submenu!, language);
        }

        // Both web items ("Open PrusaLink…", "Open Prusa Connect…") share a submenu with the same items.
        Assert.Equal([3, 3, 2], menu.MainLevel().Where(static item => item.Submenu is not null).Select(static item => item.Submenu!.Count));
    }

    [Theory]
    [MemberData(nameof(Languages))]
    public void FixedTextsKeepTheirEllipsis(string language)
    {
        using CultureScope culture = new(language);
        using Menu menu = new([]);

        // T-042: "…" stays on Settings, Diagnostics and Add printer (they open a window).
        Assert.EndsWith("…", menu.ViewModel.FixedItems.Single(static item => item.Command == TrayMenuCommand.Settings).Header, StringComparison.Ordinal);
        Assert.EndsWith("…", menu.MainLevel().Single(static item => item.Command == TrayMenuCommand.AddPrinter).Header, StringComparison.Ordinal);
        Assert.EndsWith("…", Resources.Strings.MenuDiagnostics, StringComparison.Ordinal);
    }

    private static PrinterDefinition WithCamera(PrinterDefinition printer) => printer with { Camera = new CameraSettings(enabled: true, StreamUri) };

    /// <summary>The printer name as the user sees it (the header shows "Name: State", the name never contains ":").</summary>
    private static string NamePart(TrayMenuEntry printer)
    {
        string shown = NativeMenuText.DisplayTextOf(NativeMenuText.HeaderOf(printer));
        return shown[..shown.IndexOf(':', StringComparison.Ordinal)].TrimEnd(' ', (char)0x00A0, (char)0x202F);
    }

    private static void AssertNoAccessKey(TrayMenuEntry item)
    {
        Assert.False(item.HasAccessKey, item.Header);
        string native = NativeMenuText.HeaderOf(item);
        Assert.Null(NativeMenuText.AccessKeyOf(native));
        // Shown exactly as formatted.
        Assert.Equal(item.Header, NativeMenuText.DisplayTextOf(native));
    }

    private static void AssertUniqueAccessKeys(IEnumerable<TrayMenuEntry> items, string language)
    {
        CultureInfo culture = CultureInfo.GetCultureInfo(language);
        List<string> keys = [];
        foreach (TrayMenuEntry item in items)
        {
            Assert.True(item.HasAccessKey, $"{language}: '{item.Header}' has no access key.");
            string native = NativeMenuText.HeaderOf(item);
            char? key = NativeMenuText.AccessKeyOf(native);
            Assert.True(key is not null, $"{language}: '{item.Header}' has no access key.");
            Assert.True(char.IsLetterOrDigit(key!.Value), $"{language}: '{item.Header}' has the access key '{key}'.");
            Assert.Equal(1, native.Count(static c => c == '_'));
            keys.Add(char.ToUpper(key.Value, culture).ToString());
        }

        Assert.True(keys.Distinct(StringComparer.Ordinal).Count() == keys.Count, $"{language}: access keys {string.Join(", ", keys)} are not unique.");
    }

    /// <summary>A menu view model showing <c>printers</c>.</summary>
    private sealed class Menu : IDisposable
    {
        private readonly ManualDispatcher _dispatcher = new();

        public Menu(IReadOnlyList<PrinterDefinition> printers)
        {
            ViewModel = new TrayMenuViewModel(new FakePrinterMonitor(), _dispatcher, TestWebOpener.Create(new FakeUrlLauncher()), TrayMenuOptions.Default);
            ViewModel.SetPrinters(printers);
            _dispatcher.RunAll();
        }

        public TrayMenuViewModel ViewModel { get; }

        /// <summary>The items of the main menu (printer part and fixed part), separators skipped.</summary>
        public IReadOnlyList<TrayMenuEntry> MainLevel() => [.. ViewModel.PrinterItems, .. ViewModel.FixedItems.Where(static item => !item.IsSeparator)];

        public void Dispose() => ViewModel.Dispose();
    }
}
