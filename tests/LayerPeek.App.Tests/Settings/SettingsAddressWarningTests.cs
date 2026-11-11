using LayerPeek.App.Resources;
using LayerPeek.App.Settings;
using LayerPeek.App.Tests.Tray.Menu;
using LayerPeek.Core.Cameras;
using LayerPeek.Core.Settings;

using static LayerPeek.App.Tests.Settings.SettingsTestData;

namespace LayerPeek.App.Tests.Settings;

/// <summary>
/// T-182 (user decisions 2026-11-11): the warning about an unencrypted address outside the local network under the printer
/// address and the camera address: in place of the hint, below an error, as the help text of the field, informative only;
/// the "More information" link only with an address of the security chapter and something to open it.
/// </summary>
public sealed class SettingsAddressWarningTests : IDisposable
{
    private static readonly PasswordPolicy Supported = new(IsSupported: true, MaxBytes: 2560);
    private static readonly Uri Chapter = new("https://example.invalid/manual.md#security");

    private readonly FakeEditorStore _store = new();
    private SettingsViewModel? _viewModel;

    public void Dispose() => _viewModel?.Dispose();

    private async Task<PrinterEditorViewModel> LoadAsync(string address = "192.168.1.20", string camera = "rtsp://192.168.0.227/live", FakeUrlLauncher? launcher = null, Uri? chapter = null)
    {
        AppSettings settings = new() { Printers = [LinkPrinter("a", "Alpha", address) with { Camera = new CameraSettings(enabled: true, new Uri(camera)) }] };
        _store.Data = new SettingsEditorData(settings, SettingsLoadStatus.Loaded, [], Ids("a"));
        _viewModel = new SettingsViewModel(_store, new FakeConnectionTester(), Supported, static () => Guid.NewGuid().ToString("N"))
        {
            UrlLauncher = launcher,
            SecurityHelpUri = () => chapter,
        };
        await _viewModel.LoadAsync();
        return _viewModel.Printers[0];
    }

    [Fact]
    public async Task AnAddressInTheLocalNetworkHasTheHint()
    {
        PrinterEditorViewModel printer = await LoadAsync();

        Assert.Null(printer.AddressWarning);
        Assert.False(printer.HasAddressWarning);
        Assert.True(printer.ShowsAddressHint);
        Assert.Equal(Strings.SettingsAddressHint, printer.AddressHelpText);
        Assert.Null(printer.CameraUrlWarning);
        Assert.True(printer.ShowsCameraUrlHint);
    }

    [Fact]
    public async Task APublicPrinterAddressGetsTheWarningInPlaceOfTheHint()
    {
        PrinterEditorViewModel printer = await LoadAsync();
        List<string?> changed = [];
        printer.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        printer.Address = "8.8.8.8";

        Assert.Equal(Strings.SettingsAddressUntrustedNetwork, printer.AddressWarning);
        Assert.True(printer.HasAddressWarning);
        Assert.False(printer.ShowsAddressHint);
        Assert.Equal(Strings.SettingsAddressUntrustedNetwork, printer.AddressHelpText);
        Assert.Contains(nameof(PrinterEditorViewModel.HasAddressWarning), changed);
        Assert.Contains(nameof(PrinterEditorViewModel.AddressHelpText), changed);
        // Informative only: nothing blocks saving.
        Assert.False(printer.HasErrors);

        printer.Address = "https://8.8.8.8";
        Assert.False(printer.HasAddressWarning);
        Assert.True(printer.ShowsAddressHint);
    }

    [Fact]
    public async Task AnErrorTakesThePlaceOfTheWarning()
    {
        PrinterEditorViewModel printer = await LoadAsync(address: "http://203.0.113.10");
        Assert.True(printer.HasAddressWarning);

        printer.Address = "http://203.0.113.10/?query";

        Assert.NotNull(printer.AddressError);
        Assert.False(printer.HasAddressWarning);
        Assert.False(printer.ShowsAddressHint);
        Assert.Equal(printer.AddressError, printer.AddressHelpText);
    }

    [Fact]
    public async Task APublicCameraAddressGetsTheWarningAlsoWhileTheCameraIsOff()
    {
        PrinterEditorViewModel printer = await LoadAsync(camera: "rtsp://203.0.113.10/live");

        Assert.Equal(Strings.SettingsCameraUrlUntrustedNetwork, printer.CameraUrlWarning);
        Assert.False(printer.ShowsCameraUrlHint);
        Assert.Equal(Strings.SettingsCameraUrlUntrustedNetwork, printer.CameraUrlHelpText);

        printer.CameraEnabled = false;
        Assert.True(printer.HasCameraUrlWarning);
        Assert.False(printer.HasCameraErrors);

        printer.CameraUrl = "rtsp://camera.lan/live";
        Assert.False(printer.HasCameraUrlWarning);
        Assert.Equal(Strings.SettingsCameraUrlHint, printer.CameraUrlHelpText);
    }

    [Fact]
    public async Task SavingKeepsAnAddressWithTheWarning()
    {
        PrinterEditorViewModel printer = await LoadAsync();
        printer.Address = "http://printer.example.com";
        printer.CameraUrl = "rtsp://camera.example.com/live";

        await _viewModel!.SaveCommand.ExecuteAsync(null);

        SettingsChange saved = Assert.Single(_store.Saves);
        Assert.Equal("http://printer.example.com", saved.Updated.Printers[0].Address);
        Assert.Equal(new Uri("rtsp://camera.example.com/live"), saved.Updated.Printers[0].Camera.StreamUri);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task TheLinkNeedsTheChapterAndALauncher(bool hasLauncher, bool hasChapter)
    {
        PrinterEditorViewModel printer = await LoadAsync(address: "http://203.0.113.10", launcher: hasLauncher ? new FakeUrlLauncher() : null, chapter: hasChapter ? Chapter : null);

        Assert.False(printer.ShowsSecurityHelp);
        printer.OpenSecurityHelpCommand.Execute(null);
    }

    [Fact]
    public async Task TheLinkOpensTheChapter()
    {
        FakeUrlLauncher launcher = new();
        PrinterEditorViewModel printer = await LoadAsync(address: "http://203.0.113.10", launcher: launcher, chapter: Chapter);

        Assert.True(printer.ShowsSecurityHelp);
        printer.OpenSecurityHelpCommand.Execute(null);

        Assert.Equal([Chapter], launcher.Opened);
    }
}
