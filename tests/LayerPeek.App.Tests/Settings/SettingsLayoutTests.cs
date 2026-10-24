using LayerPeek.App.Settings;
using LayerPeek.App.Tests.Tray;
using LayerPeek.Core.Cameras;
using LayerPeek.Core.Printers;
using LayerPeek.Core.Settings;

using static LayerPeek.App.Tests.Settings.SettingsTestData;

namespace LayerPeek.App.Tests.Settings;

/// <summary>
/// The layout decisions of T-033 that live in the view models: printer sub-tabs (kept across printers, error markers,
/// navigation of a failed save), keyboard tab switching, errors replacing hints and the texts of refused tests.
/// </summary>
public sealed class SettingsLayoutTests : IDisposable
{
    private const string FixErrors = "Some values are not valid. Fix the highlighted fields.";

    private static readonly PasswordPolicy Supported = new(IsSupported: true, MaxBytes: 2560);

    private readonly CultureScope _culture = new("en-US");
    private readonly FakeEditorStore _store = new();
    private readonly FakeConnectionTester _tester = new();
    private readonly FakeCameraTester _cameraTester = new();
    private readonly Queue<string> _ids = new(["new1", "new2", "new3"]);
    private SettingsViewModel? _viewModel;

    public void Dispose()
    {
        _viewModel?.Dispose();
        _culture.Dispose();
    }

    // ---- Sub-tabs ----

    [Fact]
    public async Task SubTabIsKeptWhenAnotherPrinterIsSelected()
    {
        SettingsViewModel vm = await LoadAsync(LinkPrinter("a", "Alpha"), LinkPrinter("b", "Beta", "192.168.1.21"));
        Assert.Equal(SettingsViewModel.ConnectionPrinterTab, vm.SelectedPrinterTabIndex);

        vm.SelectedPrinterTabIndex = SettingsViewModel.CameraPrinterTab;
        vm.SelectedPrinter = vm.Printers[1];

        Assert.Equal(SettingsViewModel.CameraPrinterTab, vm.SelectedPrinterTabIndex);
    }

    [Fact]
    public async Task SubTabIsKeptWhenPrintersAreAddedAndRemoved()
    {
        SettingsViewModel vm = await LoadAsync(LinkPrinter("a", "Alpha"));
        vm.SelectedPrinterTabIndex = SettingsViewModel.CameraPrinterTab;

        vm.AddPrinterCommand.Execute(null);
        Assert.Equal(SettingsViewModel.CameraPrinterTab, vm.SelectedPrinterTabIndex);
        vm.RemovePrinterCommand.Execute(null);

        Assert.Equal(SettingsViewModel.CameraPrinterTab, vm.SelectedPrinterTabIndex);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(SettingsViewModel.PrinterTabCount)]
    public async Task SubTabOutsideTheRangeIsIgnored(int index)
    {
        // A tab control may write -1 while its items change.
        SettingsViewModel vm = await LoadAsync(LinkPrinter("a", "Alpha"));
        vm.SelectedPrinterTabIndex = SettingsViewModel.CameraPrinterTab;

        vm.SelectedPrinterTabIndex = index;

        Assert.Equal(SettingsViewModel.CameraPrinterTab, vm.SelectedPrinterTabIndex);
    }

    [Fact]
    public async Task ErrorMarkersFollowTheSubTabOfTheField()
    {
        SettingsViewModel vm = await LoadAsync(LinkPrinter("a", "Alpha"));
        PrinterEditorViewModel printer = vm.Printers[0];
        Assert.False(printer.ShowsConnectionErrorMarker);
        Assert.False(printer.ShowsCameraErrorMarker);

        printer.Address = string.Empty;
        Assert.True(printer.ShowsConnectionErrorMarker);
        Assert.False(printer.ShowsCameraErrorMarker);

        printer.Address = "192.168.1.20";
        printer.CameraEnabled = true;
        Assert.False(printer.ShowsConnectionErrorMarker);
        Assert.True(printer.ShowsCameraErrorMarker);

        printer.CameraUrl = "rtsp://192.168.0.227/live";
        Assert.False(printer.ShowsCameraErrorMarker);
        Assert.False(printer.ShowsErrorMarker);
    }

    [Theory]
    [InlineData("name")]
    [InlineData("userName")]
    [InlineData("password")]
    public async Task EveryFieldOfThePrinterSubTabMarksIt(string field)
    {
        SettingsViewModel vm = await LoadAsync([LinkPrinter("a", "Alpha")], new PasswordPolicy(IsSupported: true, MaxBytes: 4));
        PrinterEditorViewModel printer = vm.Printers[0];

        switch (field)
        {
            case "name":
                printer.Name = " ";
                break;
            case "userName":
                printer.UserName = "a:b";
                break;
            default:
                printer.Password = "too long";
                break;
        }

        Assert.True(printer.HasConnectionErrors);
        Assert.True(printer.ShowsConnectionErrorMarker);
        Assert.False(printer.HasCameraErrors);
    }

    [Fact]
    public async Task UntouchedNewPrinterShowsNoMarkersUntilSaved()
    {
        SettingsViewModel vm = await LoadAsync();
        vm.AddPrinterCommand.Execute(null);
        PrinterEditorViewModel printer = vm.SelectedPrinter!;
        Assert.True(printer.HasConnectionErrors);
        Assert.False(printer.ShowsConnectionErrorMarker);

        await vm.SaveCommand.ExecuteAsync(null);

        Assert.True(printer.ShowsConnectionErrorMarker);
        Assert.False(printer.ShowsCameraErrorMarker);
    }

    [Fact]
    public async Task SubTabNamesTellAboutErrorsAndFollowTheLanguage()
    {
        SettingsViewModel vm = await LoadAsync(LinkPrinter("a", "Alpha"));
        PrinterEditorViewModel printer = vm.Printers[0];
        Assert.Equal("Printer", printer.ConnectionTabName);
        Assert.Equal("Camera", printer.CameraTabName);
        List<string?> changes = [];
        printer.PropertyChanged += (_, e) => changes.Add(e.PropertyName);

        printer.Address = string.Empty;
        printer.CameraEnabled = true;

        Assert.Equal("Printer – contains errors", printer.ConnectionTabName);
        Assert.Equal("Camera – contains errors", printer.CameraTabName);
        Assert.Contains(nameof(PrinterEditorViewModel.ConnectionTabName), changes);
        Assert.Contains(nameof(PrinterEditorViewModel.CameraTabName), changes);

        using (new CultureScope("cs-CZ"))
        {
            vm.RefreshTexts();
            Assert.Equal("Tiskárna – obsahuje chyby", printer.ConnectionTabName);
            Assert.Equal("Kamera – obsahuje chyby", printer.CameraTabName);
        }
    }

    // ---- Save with errors ----

    [Fact]
    public async Task SaveWithAPrinterErrorShowsThePrinterSubTab()
    {
        SettingsViewModel vm = await LoadAsync(LinkPrinter("a", "Alpha"), LinkPrinter("b", "Beta", "192.168.1.21"));
        vm.Printers[1].Address = string.Empty;
        vm.SelectedTabIndex = SettingsViewModel.GeneralTab;
        vm.SelectedPrinterTabIndex = SettingsViewModel.CameraPrinterTab;

        await vm.SaveCommand.ExecuteAsync(null);

        Assert.Empty(_store.Saves);
        Assert.Same(vm.Printers[1], vm.SelectedPrinter);
        Assert.Equal(SettingsViewModel.PrintersTab, vm.SelectedTabIndex);
        Assert.Equal(SettingsViewModel.ConnectionPrinterTab, vm.SelectedPrinterTabIndex);
    }

    [Fact]
    public async Task SaveWithACameraErrorShowsTheCameraSubTab()
    {
        SettingsViewModel vm = await LoadAsync(LinkPrinter("a", "Alpha"), LinkPrinter("b", "Beta", "192.168.1.21"));
        vm.Printers[1].CameraEnabled = true;
        vm.SelectedPrinter = vm.Printers[0];
        vm.SelectedTabIndex = SettingsViewModel.GeneralTab;

        await vm.SaveCommand.ExecuteAsync(null);

        Assert.Empty(_store.Saves);
        Assert.Same(vm.Printers[1], vm.SelectedPrinter);
        Assert.Equal(SettingsViewModel.PrintersTab, vm.SelectedTabIndex);
        Assert.Equal(SettingsViewModel.CameraPrinterTab, vm.SelectedPrinterTabIndex);
        Assert.Equal(FixErrors, vm.StatusText);
    }

    [Fact]
    public async Task SaveWithErrorsOnBothSubTabsShowsThePrinterSubTabFirst()
    {
        SettingsViewModel vm = await LoadAsync(LinkPrinter("a", "Alpha"));
        vm.Printers[0].CameraEnabled = true;
        vm.Printers[0].Name = string.Empty;
        vm.SelectedPrinterTabIndex = SettingsViewModel.CameraPrinterTab;

        await vm.SaveCommand.ExecuteAsync(null);

        Assert.Equal(SettingsViewModel.ConnectionPrinterTab, vm.SelectedPrinterTabIndex);
    }

    [Fact]
    public async Task SaveWithInvalidGeneralSettingsShowsTheGeneralTabAndKeepsTheSubTab()
    {
        SettingsViewModel vm = await LoadAsync(LinkPrinter("a", "Alpha"));
        vm.SelectedPrinterTabIndex = SettingsViewModel.CameraPrinterTab;
        vm.RemainingTimeMinutes = null;

        await vm.SaveCommand.ExecuteAsync(null);

        Assert.Empty(_store.Saves);
        Assert.Equal(SettingsViewModel.GeneralTab, vm.SelectedTabIndex);
        Assert.Equal(SettingsViewModel.CameraPrinterTab, vm.SelectedPrinterTabIndex);
    }

    // ---- Keyboard: Ctrl+Tab (main tabs), Ctrl+PageDown / PageUp (sub-tabs) ----

    [Fact]
    public async Task AdjacentMainTabWrapsAround()
    {
        SettingsViewModel vm = await LoadAsync(LinkPrinter("a", "Alpha"));

        Assert.True(vm.SelectAdjacentTab(forward: true));
        Assert.Equal(SettingsViewModel.GeneralTab, vm.SelectedTabIndex);
        Assert.True(vm.SelectAdjacentTab(forward: true));
        Assert.Equal(SettingsViewModel.PrintersTab, vm.SelectedTabIndex);
        Assert.True(vm.SelectAdjacentTab(forward: false));
        Assert.Equal(SettingsViewModel.GeneralTab, vm.SelectedTabIndex);
    }

    [Fact]
    public async Task AdjacentSubTabWrapsAroundOnThePrintersTab()
    {
        SettingsViewModel vm = await LoadAsync(LinkPrinter("a", "Alpha"));

        Assert.True(vm.SelectAdjacentPrinterTab(forward: true));
        Assert.Equal(SettingsViewModel.CameraPrinterTab, vm.SelectedPrinterTabIndex);
        Assert.True(vm.SelectAdjacentPrinterTab(forward: true));
        Assert.Equal(SettingsViewModel.CameraWindowPrinterTab, vm.SelectedPrinterTabIndex);
        Assert.True(vm.SelectAdjacentPrinterTab(forward: true));
        Assert.Equal(SettingsViewModel.OverlayPrinterTab, vm.SelectedPrinterTabIndex);
        Assert.True(vm.SelectAdjacentPrinterTab(forward: true));
        Assert.Equal(SettingsViewModel.ConnectionPrinterTab, vm.SelectedPrinterTabIndex);
        Assert.True(vm.SelectAdjacentPrinterTab(forward: false));
        Assert.Equal(SettingsViewModel.OverlayPrinterTab, vm.SelectedPrinterTabIndex);
        Assert.True(vm.SelectAdjacentPrinterTab(forward: false));
        Assert.Equal(SettingsViewModel.CameraWindowPrinterTab, vm.SelectedPrinterTabIndex);
    }

    [Fact]
    public async Task OverlaySubTabIsTheFourthAndCanBeSelected()
    {
        SettingsViewModel vm = await LoadAsync(LinkPrinter("a", "Alpha"));

        vm.SelectedPrinterTabIndex = SettingsViewModel.OverlayPrinterTab;

        Assert.Equal(3, SettingsViewModel.OverlayPrinterTab);
        Assert.Equal(4, SettingsViewModel.PrinterTabCount);
        Assert.Equal(SettingsViewModel.OverlayPrinterTab, vm.SelectedPrinterTabIndex);
        vm.SelectedPrinterTabIndex = SettingsViewModel.PrinterTabCount;
        Assert.Equal(SettingsViewModel.OverlayPrinterTab, vm.SelectedPrinterTabIndex);
    }

    [Fact]
    public async Task SubTabShortcutDoesNothingWithoutVisibleSubTabs()
    {
        SettingsViewModel vm = await LoadAsync(LinkPrinter("a", "Alpha"));
        vm.SelectedTabIndex = SettingsViewModel.GeneralTab;
        Assert.False(vm.SelectAdjacentPrinterTab(forward: true));
        Assert.Equal(SettingsViewModel.ConnectionPrinterTab, vm.SelectedPrinterTabIndex);

        SettingsViewModel empty = await LoadAsync();
        Assert.False(empty.SelectAdjacentPrinterTab(forward: true));
        Assert.Equal(SettingsViewModel.ConnectionPrinterTab, empty.SelectedPrinterTabIndex);
    }

    [Fact]
    public void ShortcutsDoNothingBeforeLoading()
    {
        SettingsViewModel vm = Create();

        Assert.False(vm.SelectAdjacentTab(forward: true));
        Assert.False(vm.SelectAdjacentPrinterTab(forward: true));
        Assert.Equal(SettingsViewModel.PrintersTab, vm.SelectedTabIndex);
    }

    // ---- An error replaces the hint of its field (Q1) ----

    [Fact]
    public async Task AddressErrorReplacesTheHintUntilFixed()
    {
        PrinterEditorViewModel printer = (await LoadAsync(LinkPrinter("a", "Alpha"))).Printers[0];
        Assert.True(printer.ShowsAddressHint);
        List<string?> changes = [];
        printer.PropertyChanged += (_, e) => changes.Add(e.PropertyName);

        printer.Address = "ftp://printer";
        Assert.NotNull(printer.AddressError);
        Assert.False(printer.ShowsAddressHint);
        Assert.Contains(nameof(PrinterEditorViewModel.ShowsAddressHint), changes);

        printer.Address = "192.168.1.30";
        Assert.True(printer.ShowsAddressHint);
    }

    [Fact]
    public async Task UserNameAndPasswordErrorsReplaceTheirHintsOnly()
    {
        SettingsViewModel vm = await LoadAsync([LinkPrinter("a", "Alpha")], new PasswordPolicy(IsSupported: true, MaxBytes: 4));
        PrinterEditorViewModel printer = vm.Printers[0];

        printer.UserName = "a:b";
        Assert.False(printer.ShowsUserNameHint);
        Assert.True(printer.ShowsPasswordHint);
        Assert.True(printer.ShowsAddressHint);

        printer.Password = "too long";
        Assert.False(printer.ShowsPasswordHint);

        printer.UserName = "maker";
        printer.Password = "ok";
        Assert.True(printer.ShowsUserNameHint);
        Assert.True(printer.ShowsPasswordHint);
    }

    [Fact]
    public async Task AddressHintIsNotShownForPrusaConnect()
    {
        PrinterEditorViewModel printer = (await LoadAsync(CloudPrinter("c", "Cloud"))).Printers[0];

        Assert.False(printer.ShowsAddressHint);
        Assert.True(printer.ShowsUserNameHint);
        Assert.True(printer.ShowsPasswordHint);
    }

    [Fact]
    public async Task HiddenErrorsOfANewPrinterKeepTheHints()
    {
        SettingsViewModel vm = await LoadAsync();
        vm.AddPrinterCommand.Execute(null);
        PrinterEditorViewModel printer = vm.SelectedPrinter!;

        Assert.True(printer.ShowsAddressHint);
        Assert.True(printer.ShowsUserNameHint);
        Assert.True(printer.ShowsCameraUrlHint);
    }

    [Fact]
    public async Task CameraAddressErrorReplacesItsHint()
    {
        PrinterEditorViewModel printer = (await LoadAsync(LinkPrinter("a", "Alpha"))).Printers[0];
        Assert.True(printer.ShowsCameraUrlHint);

        printer.CameraUrl = "http://camera/live";
        Assert.False(printer.ShowsCameraUrlHint);

        printer.CameraUrl = "rtsp://camera/live";
        Assert.True(printer.ShowsCameraUrlHint);
    }

    [Fact]
    public async Task PollingIntervalErrorReplacesItsHint()
    {
        SettingsViewModel vm = await LoadAsync(LinkPrinter("a", "Alpha"));
        Assert.True(vm.ShowsPollingIntervalHint);
        List<string?> changes = [];
        vm.PropertyChanged += (_, e) => changes.Add(e.PropertyName);

        vm.PollingIntervalSeconds = 0;
        Assert.False(vm.ShowsPollingIntervalHint);
        Assert.Contains(nameof(SettingsViewModel.ShowsPollingIntervalHint), changes);

        vm.PollingIntervalSeconds = 5;
        Assert.True(vm.ShowsPollingIntervalHint);
    }

    // ---- Test results (Q2, success style) ----

    [Theory]
    [InlineData("ftp://printer", "maker")]
    [InlineData("192.168.1.30", "a:b")]
    public async Task RefusedConnectionTestShowsOnlyTheShortText(string address, string userName)
    {
        PrinterEditorViewModel printer = (await LoadAsync(LinkPrinter("a", "Alpha"))).Printers[0];
        printer.Address = address;
        printer.UserName = userName;

        await printer.TestConnectionCommand.ExecuteAsync(null);

        Assert.Empty(_tester.Calls);
        Assert.Equal(FixErrors, printer.TestResultText);
        Assert.True(printer.TestFailed);
        Assert.False(printer.TestSucceeded);
        Assert.True(printer.AddressError is not null || printer.UserNameError is not null);
    }

    [Fact]
    public async Task RefusedTestOfANewPrinterRevealsTheFieldErrors()
    {
        SettingsViewModel vm = await LoadAsync();
        vm.AddPrinterCommand.Execute(null);
        PrinterEditorViewModel printer = vm.SelectedPrinter!;

        await printer.TestConnectionCommand.ExecuteAsync(null);

        Assert.Equal(FixErrors, printer.TestResultText);
        Assert.Equal("Enter the printer address.", printer.AddressError);
        Assert.False(printer.ShowsAddressHint);
    }

    [Fact]
    public async Task RefusedCameraTestShowsOnlyTheShortText()
    {
        PrinterEditorViewModel printer = (await LoadAsync(LinkPrinter("a", "Alpha"))).Printers[0];
        printer.CameraUrl = "http://camera/live";

        await printer.TestCameraCommand.ExecuteAsync(null);

        Assert.Empty(_cameraTester.Calls);
        Assert.Equal(FixErrors, printer.CameraTestResultText);
        Assert.Equal("Only rtsp:// addresses are supported.", printer.CameraUrlError);
        Assert.True(printer.CameraTestFailed);
    }

    [Fact]
    public async Task RefusedCameraTestWithoutAFieldErrorSaysWhatIsMissing()
    {
        // A turned-off camera may stay without an address, so the empty field shows no error to point at.
        PrinterEditorViewModel printer = (await LoadAsync(LinkPrinter("a", "Alpha"))).Printers[0];

        await printer.TestCameraCommand.ExecuteAsync(null);

        Assert.Null(printer.CameraUrlError);
        Assert.Equal("Enter the stream address, e.g. rtsp://192.168.0.227/live.", printer.CameraTestResultText);
        Assert.True(printer.CameraTestFailed);
    }

    [Fact]
    public async Task SuccessfulTestsUseTheSuccessStyleNotTheErrorStyle()
    {
        PrinterEditorViewModel printer = (await LoadAsync(LinkPrinter("a", "Alpha") with { Camera = new CameraSettings(enabled: true, new Uri("rtsp://camera/live")) })).Printers[0];
        _tester.Immediate = new ConnectionTestResult(ConnectionTestOutcome.Success, "1.4.0", "6.2.0");
        _cameraTester.Immediate = new CameraTestResult(CameraTestOutcome.Success, 1920, 1080);

        await printer.TestConnectionCommand.ExecuteAsync(null);
        await printer.TestCameraCommand.ExecuteAsync(null);

        Assert.True(printer.TestSucceeded);
        Assert.False(printer.TestFailed);
        Assert.True(printer.CameraTestSucceeded);
        Assert.False(printer.CameraTestFailed);
        Assert.Equal("The camera works (1920\u00A0×\u00A01080\u00A0px).", printer.CameraTestResultText);
    }

    [Fact]
    public async Task RunningAndCancelledTestsUseNeitherStyle()
    {
        PrinterEditorViewModel printer = (await LoadAsync(LinkPrinter("a", "Alpha"))).Printers[0];
        Task test = printer.TestConnectionCommand.ExecuteAsync(null);
        Assert.False(printer.TestSucceeded);
        Assert.False(printer.TestFailed);

        printer.CancelTestCommand.Execute(null);
        await test.WaitAsync(TestTimeouts.Wait, TestContext.Current.CancellationToken);

        Assert.Equal("Test cancelled.", printer.TestResultText);
        Assert.False(printer.TestSucceeded);
        Assert.False(printer.TestFailed);
    }

    // ---- Camera credentials notice (Q3) ----

    [Fact]
    public async Task CredentialsNoticeDisappearsWhenTheCameraIsTurnedOn()
    {
        PrinterEditorViewModel printer = await LoadWithRemovedCredentialsAsync();

        printer.Name = "Renamed";
        printer.CameraEnabled = false;
        Assert.True(printer.CameraCredentialsRemoved);

        printer.CameraEnabled = true;
        Assert.False(printer.CameraCredentialsRemoved);
    }

    [Fact]
    public async Task CredentialsNoticeDisappearsWhenTheAddressIsEdited()
    {
        PrinterEditorViewModel printer = await LoadWithRemovedCredentialsAsync();

        printer.CameraUrl = "rtsp://192.168.0.227/live2";

        Assert.False(printer.CameraCredentialsRemoved);
    }

    [Fact]
    public async Task CredentialsNoticeDisappearsOnlyAfterASuccessfulTest()
    {
        PrinterEditorViewModel printer = await LoadWithRemovedCredentialsAsync();
        _cameraTester.Immediate = new CameraTestResult(CameraTestOutcome.Failed, Reason: "401 Unauthorized");

        await printer.TestCameraCommand.ExecuteAsync(null);
        Assert.True(printer.CameraCredentialsRemoved);

        _cameraTester.Immediate = new CameraTestResult(CameraTestOutcome.Success, 640, 360);
        await printer.TestCameraCommand.ExecuteAsync(null);
        Assert.False(printer.CameraCredentialsRemoved);
    }

    private async Task<PrinterEditorViewModel> LoadWithRemovedCredentialsAsync()
    {
        PrinterDefinition printer = LinkPrinter("a", "Alpha") with { Camera = new CameraSettings(enabled: false, new Uri("rtsp://192.168.0.227/live")) };
        _store.Data = new SettingsEditorData(new AppSettings { Printers = [printer] }, SettingsLoadStatus.Loaded, [], Ids(), Ids("a"));
        SettingsViewModel vm = Create();
        await vm.LoadAsync();
        Assert.True(vm.Printers[0].CameraCredentialsRemoved);
        return vm.Printers[0];
    }

    private SettingsViewModel Create(PasswordPolicy? policy = null)
    {
        _viewModel?.Dispose();
        SettingsViewModel vm = new(_store, _tester, policy ?? Supported, _ids.Dequeue, _cameraTester);
        _viewModel = vm;
        return vm;
    }

    private Task<SettingsViewModel> LoadAsync(params PrinterDefinition[] printers) => LoadAsync(printers, policy: null);

    private async Task<SettingsViewModel> LoadAsync(PrinterDefinition[] printers, PasswordPolicy? policy)
    {
        _store.Data = new SettingsEditorData(new AppSettings { Printers = printers }, SettingsLoadStatus.Loaded, [], Ids());
        SettingsViewModel vm = Create(policy);
        await vm.LoadAsync();
        return vm;
    }
}
