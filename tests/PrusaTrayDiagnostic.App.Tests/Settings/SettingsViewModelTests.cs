using PrusaTrayDiagnostic.App.Settings;
using PrusaTrayDiagnostic.App.Tests.Tray;
using PrusaTrayDiagnostic.Core.Printers;
using PrusaTrayDiagnostic.Core.Settings;

using static PrusaTrayDiagnostic.App.Tests.Settings.SettingsTestData;

namespace PrusaTrayDiagnostic.App.Tests.Settings;

public sealed class SettingsViewModelTests : IDisposable
{
    private static readonly PasswordPolicy Supported = new(IsSupported: true, MaxBytes: 2560);

    private readonly CultureScope _culture = new("en-US");
    private readonly FakeEditorStore _store = new();
    private readonly FakeConnectionTester _tester = new();
    private readonly Queue<string> _ids = new(["new1", "new2", "new3", "new4"]);
    private SettingsViewModel? _viewModel;
    private int _closeRequests;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        _viewModel?.Dispose();
        _culture.Dispose();
    }

    // ---- Loading ----

    [Fact]
    public async Task LoadShowsPrintersAndGeneralSettings()
    {
        SettingsViewModel vm = await LoadAsync(new AppSettings
        {
            PollingInterval = TimeSpan.FromSeconds(7),
            Notifications = new NotificationSettings { PrintPaused = false, Offline = false, RemainingTimeMinutes = 15, Sound = NotificationSound.All },
            Printers = [LinkPrinter("a", "Alpha"), LinkPrinter("b", "Beta", "192.168.1.21")],
        });

        Assert.False(vm.IsLoading);
        Assert.True(vm.IsEditable);
        Assert.Equal(["Alpha", "Beta"], vm.Printers.Select(static p => p.Name));
        Assert.Same(vm.Printers[0], vm.SelectedPrinter);
        Assert.Equal("192.168.1.21", vm.Printers[1].Address);
        Assert.Equal(7m, vm.PollingIntervalSeconds);
        Assert.True(vm.NotificationsEnabled);
        Assert.True(vm.NotifyPrintStarted);
        Assert.False(vm.NotifyPrintPaused);
        Assert.False(vm.NotifyOffline);
        Assert.Equal(15m, vm.RemainingTimeMinutes);
        Assert.Equal(NotificationSound.All, vm.SelectedSound.Value);
        Assert.Equal(["Off", "Important notifications only", "All notifications"], vm.SoundOptions.Select(static o => o.Text));
        Assert.False(vm.IsDirty);
        Assert.False(vm.HasNotice);
        Assert.False(vm.HasNoPrinters);
    }

    [Fact]
    public void BeforeLoadingNothingIsEditable()
    {
        SettingsViewModel vm = Create();

        Assert.True(vm.IsLoading);
        Assert.False(vm.IsEditable);
        Assert.False(vm.SaveCommand.CanExecute(null));
        Assert.False(vm.AddPrinterCommand.CanExecute(null));
    }

    [Fact]
    public async Task EmptyConfigurationSaysSo()
    {
        SettingsViewModel vm = await LoadAsync(AppSettings.Default);

        Assert.True(vm.HasNoPrinters);
        Assert.Null(vm.SelectedPrinter);
        Assert.False(vm.RemovePrinterCommand.CanExecute(null));
    }

    [Theory]
    [InlineData(SettingsLoadStatus.Corrupted, "damaged")]
    [InlineData(SettingsLoadStatus.Unreadable, "could not be read")]
    public async Task UnusableSettingsFileIsExplained(SettingsLoadStatus status, string expected)
    {
        _store.Data = new SettingsEditorData(AppSettings.Default, status, [], null);
        SettingsViewModel vm = Create();

        await vm.LoadAsync();

        Assert.True(vm.HasNotice);
        Assert.Contains(expected, vm.Notice, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CorrectedSettingsFileIsExplained()
    {
        _store.Data = new SettingsEditorData(AppSettings.Default, SettingsLoadStatus.Loaded, ["Printer 2 skipped"], null);
        SettingsViewModel vm = Create();

        await vm.LoadAsync();

        Assert.Contains("invalid", vm.Notice, StringComparison.Ordinal);
        // Technical warnings are for the log, not for the window.
        Assert.DoesNotContain("Printer 2 skipped", vm.Notice, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ClosingWhileLoadingIsHarmless()
    {
        _store.LoadGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        SettingsViewModel vm = Create();
        Task load = vm.LoadAsync();

        vm.Dispose();
        await load.WaitAsync(TestTimeouts.Wait, Ct);

        Assert.Empty(vm.Printers);
    }

    [Fact]
    public async Task PasswordPlaceholderTellsWhetherAPasswordIsSaved()
    {
        SettingsViewModel vm = await LoadAsync(new AppSettings { Printers = [LinkPrinter("a", "Alpha"), LinkPrinter("b", "Beta")] }, withPassword: Ids("A"));

        Assert.Equal("Unchanged", vm.Printers[0].PasswordWatermark);
        Assert.Equal("Leave empty to keep the saved password.", vm.Printers[0].PasswordHint);
        Assert.Equal("Not set", vm.Printers[1].PasswordWatermark);
    }

    [Fact]
    public async Task UnknownPasswordStatusSaysUnchanged()
    {
        _store.Data = new SettingsEditorData(new AppSettings { Printers = [LinkPrinter("a", "Alpha")] }, SettingsLoadStatus.Loaded, [], null);
        SettingsViewModel vm = Create();
        await vm.LoadAsync();

        Assert.Equal("Unchanged", vm.Printers[0].PasswordWatermark);
    }

    // ---- Add / edit / remove ----

    [Fact]
    public async Task AddedPrinterIsNewSelectedAndHasAFreshId()
    {
        SettingsViewModel vm = await LoadAsync(new AppSettings { Printers = [LinkPrinter("a", "Alpha")] });

        vm.AddPrinterCommand.Execute(null);

        PrinterEditorViewModel added = Assert.IsType<PrinterEditorViewModel>(vm.SelectedPrinter);
        Assert.Same(vm.Printers[^1], added);
        Assert.True(added.IsNew);
        Assert.Equal("new1", added.Id);
        Assert.Equal("New printer", added.DisplayName);
        Assert.Equal(ConnectionType.PrusaLink, added.SelectedConnectionType.Value);
        Assert.Equal(string.Empty, added.PasswordWatermark);
        Assert.True(vm.IsDirty);
        // Errors of an untouched new printer are not shouted at the user yet.
        Assert.True(added.HasErrors);
        Assert.Null(added.NameError);
        Assert.Null(added.AddressError);
    }

    [Fact]
    public async Task DefaultIdsAreUniqueGuids()
    {
        SettingsViewModel vm = new(_store, _tester, Supported);
        _viewModel = vm;
        await vm.LoadAsync();

        vm.AddPrinterCommand.Execute(null);
        vm.AddPrinterCommand.Execute(null);

        Assert.True(Guid.TryParseExact(vm.Printers[0].Id, "N", out _));
        Assert.NotEqual(vm.Printers[0].Id, vm.Printers[1].Id);
    }

    [Fact]
    public async Task GeneratedIdNeverReusesAnExistingOrRemovedId()
    {
        _ids.Clear();
        foreach (string id in new[] { "A", "x", "b" })
        {
            _ids.Enqueue(id);
        }

        SettingsViewModel vm = await LoadAsync(new AppSettings { Printers = [LinkPrinter("a", "Alpha"), LinkPrinter("x", "Removed")] });
        vm.SelectedPrinter = vm.Printers[1];
        vm.RemovePrinterCommand.Execute(null);

        vm.AddPrinterCommand.Execute(null);

        // "A" collides with "a" (ids are case-insensitive), "x" belonged to a removed printer whose password is deleted on save.
        Assert.Equal("b", vm.SelectedPrinter!.Id);
    }

    [Fact]
    public async Task RenamedPrinterKeepsItsId()
    {
        SettingsViewModel vm = await LoadAsync(new AppSettings { Printers = [LinkPrinter("a", "Alpha")] });

        vm.Printers[0].Name = "  Renamed  ";
        await vm.SaveCommand.ExecuteAsync(null);

        PrinterDefinition saved = Assert.Single(Assert.Single(_store.Saves).Updated.Printers);
        Assert.Equal("a", saved.Id);
        Assert.Equal("Renamed", saved.Name);
    }

    [Fact]
    public async Task RemovingSelectsTheNeighbour()
    {
        SettingsViewModel vm = await LoadAsync(new AppSettings { Printers = [LinkPrinter("a", "Alpha"), LinkPrinter("b", "Beta"), LinkPrinter("c", "Gamma")] });
        vm.SelectedPrinter = vm.Printers[2];

        vm.RemovePrinterCommand.Execute(null);
        Assert.Equal("b", vm.SelectedPrinter?.Id);

        vm.SelectedPrinter = vm.Printers[0];
        vm.RemovePrinterCommand.Execute(null);
        Assert.Equal("b", vm.SelectedPrinter?.Id);

        vm.RemovePrinterCommand.Execute(null);
        Assert.Null(vm.SelectedPrinter);
        Assert.True(vm.HasNoPrinters);
    }

    [Fact]
    public async Task RemovedPrinterIsLeftOutOfTheSavedSettings()
    {
        AppSettings loaded = new() { Printers = [LinkPrinter("a", "Alpha"), LinkPrinter("b", "Beta")] };
        SettingsViewModel vm = await LoadAsync(loaded);
        vm.SelectedPrinter = vm.Printers[0];

        vm.RemovePrinterCommand.Execute(null);
        await vm.SaveCommand.ExecuteAsync(null);

        SettingsChange change = Assert.Single(_store.Saves);
        Assert.Same(loaded, change.Original);
        Assert.Equal(["b"], change.Updated.Printers.Select(static p => p.Id));
    }

    // ---- Validation ----

    [Theory]
    [InlineData("", "Enter a name.")]
    [InlineData("   ", "Enter a name.")]
    [InlineData("Alpha", null)]
    public async Task NameIsRequired(string name, string? expected)
    {
        PrinterEditorViewModel printer = await LoadOneAsync();

        printer.Name = name;

        Assert.Equal(expected, printer.NameError);
    }

    [Fact]
    public async Task NameHasAMaximumLength()
    {
        PrinterEditorViewModel printer = await LoadOneAsync();

        printer.Name = new string('x', SettingsViewModel.MaxNameLength);
        Assert.Null(printer.NameError);

        printer.Name = new string('x', SettingsViewModel.MaxNameLength + 1);
        Assert.Equal("The name can have at most 100 characters.", printer.NameError);
    }

    [Fact]
    public async Task DuplicateNamesAreMarkedOnBothPrintersUntilOneChanges()
    {
        SettingsViewModel vm = await LoadAsync(new AppSettings { Printers = [LinkPrinter("a", "Alpha"), LinkPrinter("b", "Beta")] });

        vm.Printers[1].Name = " ALPHA ";

        Assert.Equal("Another printer already has this name.", vm.Printers[0].NameError);
        Assert.Equal("Another printer already has this name.", vm.Printers[1].NameError);
        Assert.True(vm.Printers[0].ShowsErrorMarker);

        vm.Printers[0].Name = "Alpha 1";

        Assert.Null(vm.Printers[0].NameError);
        Assert.Null(vm.Printers[1].NameError);
    }

    [Theory]
    [InlineData("", "Enter the printer address.")]
    [InlineData("ftp://printer", AddressInvalid)]
    [InlineData("http://maker:secret@printer", AddressInvalid)]
    [InlineData("printer/?x=1", AddressInvalid)]
    [InlineData("printer#top", AddressInvalid)]
    [InlineData("192.168.1.5", null)]
    [InlineData("prusa-mk4.local", null)]
    [InlineData("https://printer.example:8443/prusa", null)]
    public async Task AddressFollowsThePrusaLinkRules(string address, string? expected)
    {
        PrinterEditorViewModel printer = await LoadOneAsync();

        printer.Address = address;

        Assert.Equal(expected, printer.AddressError);
    }

    [Fact]
    public async Task PasswordLimitsComeFromTheSecretStore()
    {
        PrinterEditorViewModel printer = await LoadOneAsync(new PasswordPolicy(IsSupported: true, MaxBytes: 4));

        printer.Password = "čč";
        Assert.Null(printer.PasswordError);

        printer.Password = "ččx";
        Assert.Equal("The password is too long (at most 4 bytes in UTF-8).", printer.PasswordError);

        printer.Password = "a\uD800";
        Assert.Equal("The password contains characters that cannot be saved.", printer.PasswordError);
    }

    [Fact]
    public async Task NewPrinterErrorsAppearOnceEdited()
    {
        SettingsViewModel vm = await LoadAsync(AppSettings.Default);
        vm.AddPrinterCommand.Execute(null);
        PrinterEditorViewModel added = vm.SelectedPrinter!;

        added.Name = "MK4";

        Assert.Null(added.NameError);
        Assert.Equal("Enter the printer address.", added.AddressError);
        Assert.True(added.ShowsErrorMarker);
    }

    [Theory]
    [InlineData(null, "Enter a number from 1 to 300.")]
    [InlineData("0", "Enter a number from 1 to 300.")]
    [InlineData("0.5", "Enter a number from 1 to 300.")]
    [InlineData("301", "Enter a number from 1 to 300.")]
    [InlineData("1", null)]
    [InlineData("2.5", null)]
    [InlineData("300", null)]
    public async Task PollingIntervalIsWithinTheAllowedRange(string? seconds, string? expected)
    {
        SettingsViewModel vm = await LoadAsync(AppSettings.Default);

        vm.PollingIntervalSeconds = seconds is null ? null : decimal.Parse(seconds, System.Globalization.CultureInfo.InvariantCulture);

        Assert.Equal(expected, vm.PollingIntervalError);
    }

    [Theory]
    [InlineData(null, "Enter a whole number from 0 to 1440 (0 = off).")]
    [InlineData("-1", "Enter a whole number from 0 to 1440 (0 = off).")]
    [InlineData("1441", "Enter a whole number from 0 to 1440 (0 = off).")]
    [InlineData("2.5", "Enter a whole number from 0 to 1440 (0 = off).")]
    [InlineData("0", null)]
    [InlineData("1440", null)]
    public async Task RemainingTimeMinutesAreBounded(string? minutes, string? expected)
    {
        SettingsViewModel vm = await LoadAsync(AppSettings.Default);

        vm.RemainingTimeMinutes = minutes is null ? null : decimal.Parse(minutes, System.Globalization.CultureInfo.InvariantCulture);

        Assert.Equal(expected, vm.RemainingTimeError);
    }

    [Fact]
    public async Task ErrorsFollowUiCulture()
    {
        using CultureScope czech = new("cs-CZ");
        PrinterEditorViewModel printer = await LoadOneAsync();

        printer.Name = string.Empty;
        printer.Address = string.Empty;

        Assert.Equal("Zadejte název.", printer.NameError);
        Assert.Equal("Zadejte adresu tiskárny.", printer.AddressError);
    }

    // ---- Saving ----

    [Fact]
    public async Task SaveWritesEverythingThatWasEdited()
    {
        AppSettings loaded = new() { Printers = [LinkPrinter("a", "Alpha")] };
        SettingsViewModel vm = await LoadAsync(loaded, withPassword: Ids("a"));
        vm.PollingIntervalSeconds = 2.5m;
        vm.NotificationsEnabled = false;
        vm.NotifyPrintStarted = false;
        vm.NotifyPrintFinished = false;
        vm.NotifyPrintPaused = false;
        vm.NotifyPrintResumed = false;
        vm.NotifyPrintStopped = false;
        vm.NotifyError = false;
        vm.NotifyAttention = false;
        vm.NotifyOffline = false;
        vm.RemainingTimeMinutes = 0;
        vm.SelectedSound = vm.SoundOptions[0];
        vm.Printers[0].Address = " http://prusa.local ";
        vm.AddPrinterCommand.Execute(null);
        vm.SelectedPrinter!.Name = "MK4";
        vm.SelectedPrinter.Address = "192.168.1.30";
        vm.SelectedPrinter.Password = "pw-new";

        await vm.SaveCommand.ExecuteAsync(null);

        SettingsChange change = Assert.Single(_store.Saves);
        Assert.Same(loaded, change.Original);
        Assert.Equal(TimeSpan.FromSeconds(2.5), change.Updated.PollingInterval);
        Assert.Equal(
            new NotificationSettings
            {
                Enabled = false,
                PrintStarted = false,
                PrintFinished = false,
                PrintPaused = false,
                PrintResumed = false,
                PrintStopped = false,
                Error = false,
                Attention = false,
                Offline = false,
                RemainingTimeMinutes = 0,
                Sound = NotificationSound.Off,
            },
            change.Updated.Notifications);
        Assert.Equal([LinkPrinter("a", "Alpha", "http://prusa.local"), LinkPrinter("new1", "MK4", "192.168.1.30")], change.Updated.Printers);
        Assert.Equal(new Dictionary<string, string> { ["new1"] = "pw-new" }, change.NewPasswords);
        Assert.Equal(1, _closeRequests);
        Assert.Equal(string.Empty, vm.Printers[1].Password);
    }

    [Fact]
    public async Task EmptyPasswordKeepsTheSavedOne()
    {
        SettingsViewModel vm = await LoadAsync(new AppSettings { Printers = [LinkPrinter("a", "Alpha")] }, withPassword: Ids("a"));

        vm.Printers[0].Name = "Alpha 2";
        await vm.SaveCommand.ExecuteAsync(null);

        Assert.Empty(Assert.Single(_store.Saves).NewPasswords);
    }

    [Fact]
    public async Task ChangedPasswordOfExistingPrinterIsSaved()
    {
        SettingsViewModel vm = await LoadAsync(new AppSettings { Printers = [LinkPrinter("a", "Alpha")] }, withPassword: Ids("a"));

        vm.Printers[0].Password = "changed";
        await vm.SaveCommand.ExecuteAsync(null);

        Assert.Equal("changed", Assert.Single(_store.Saves).NewPasswords["a"]);
    }

    [Fact]
    public async Task SaveWithInvalidPrinterSelectsItAndSavesNothing()
    {
        SettingsViewModel vm = await LoadAsync(new AppSettings { Printers = [LinkPrinter("a", "Alpha")] });
        vm.AddPrinterCommand.Execute(null);
        vm.SelectedPrinter = vm.Printers[0];
        vm.SelectedTabIndex = 1;

        await vm.SaveCommand.ExecuteAsync(null);

        Assert.Empty(_store.Saves);
        Assert.Same(vm.Printers[1], vm.SelectedPrinter);
        Assert.Equal(0, vm.SelectedTabIndex);
        Assert.Equal("Enter a name.", vm.Printers[1].NameError);
        Assert.True(vm.StatusIsError);
        Assert.Equal("Some values are not valid. Fix the highlighted fields.", vm.StatusText);
        Assert.Equal(0, _closeRequests);
    }

    [Fact]
    public async Task SaveWithInvalidGeneralSettingsShowsTheGeneralTab()
    {
        SettingsViewModel vm = await LoadAsync(new AppSettings { Printers = [LinkPrinter("a", "Alpha")] });
        vm.PollingIntervalSeconds = null;

        await vm.SaveCommand.ExecuteAsync(null);

        Assert.Empty(_store.Saves);
        Assert.Equal(1, vm.SelectedTabIndex);
    }

    [Fact]
    public async Task SaveWithoutChangesJustCloses()
    {
        SettingsViewModel vm = await LoadAsync(new AppSettings { Printers = [LinkPrinter("a", "Alpha")] });

        await vm.SaveCommand.ExecuteAsync(null);

        Assert.Empty(_store.Saves);
        Assert.Equal(1, _closeRequests);
    }

    [Theory]
    [InlineData(SettingsSaveStatus.SettingsNotSaved, false, "The settings could not be saved. Nothing was changed.")]
    [InlineData(SettingsSaveStatus.PasswordNotSaved, false, "The password could not be saved to the system credential store. Nothing was changed.")]
    [InlineData(SettingsSaveStatus.SettingsNotSaved, true, "The settings could not be saved, and a previously saved password may have changed. Check the printer passwords.")]
    public async Task FailedSaveKeepsTheWindowAndTheEdits(SettingsSaveStatus status, bool rollbackIncomplete, string expected)
    {
        _store.SaveResult = new SettingsSaveResult(status, rollbackIncomplete);
        SettingsViewModel vm = await LoadAsync(new AppSettings { Printers = [LinkPrinter("a", "Alpha")] });
        vm.Printers[0].Password = "pw";

        await vm.SaveCommand.ExecuteAsync(null);

        Assert.Equal(0, _closeRequests);
        Assert.True(vm.StatusIsError);
        Assert.Equal(expected, vm.StatusText);
        Assert.Equal("pw", vm.Printers[0].Password);
        Assert.True(vm.IsDirty);
        Assert.False(vm.IsSaving);
        Assert.True(vm.IsEditable);
    }

    [Fact]
    public async Task UndeletedPasswordOfRemovedPrinterIsReportedAndTheWindowStays()
    {
        _store.SaveResult = new SettingsSaveResult(SettingsSaveStatus.Saved, UndeletedPasswords: 1);
        SettingsViewModel vm = await LoadAsync(new AppSettings { Printers = [LinkPrinter("a", "Alpha"), LinkPrinter("b", "Beta")] });
        vm.SelectedPrinter = vm.Printers[1];
        vm.RemovePrinterCommand.Execute(null);
        vm.AddPrinterCommand.Execute(null);
        vm.SelectedPrinter!.Name = "MK4";
        vm.SelectedPrinter.Address = "192.168.1.30";
        vm.SelectedPrinter.Password = "pw";

        await vm.SaveCommand.ExecuteAsync(null);

        Assert.Equal(0, _closeRequests);
        Assert.False(vm.StatusIsError);
        Assert.Equal("Settings saved. The password of a removed printer could not be deleted from the system credential store.", vm.StatusText);
        // The saved state is the new baseline.
        Assert.False(vm.IsDirty);
        Assert.False(vm.Printers[1].IsNew);
        Assert.Equal("Unchanged", vm.Printers[1].PasswordWatermark);

        await vm.SaveCommand.ExecuteAsync(null);
        Assert.Single(_store.Saves);
    }

    [Fact]
    public async Task SaveRunsOnlyOnceAtATime()
    {
        _store.SaveGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        SettingsViewModel vm = await LoadAsync(new AppSettings { Printers = [LinkPrinter("a", "Alpha")] });
        vm.Printers[0].Name = "Renamed";

        Task first = vm.SaveCommand.ExecuteAsync(null);
        Assert.True(vm.IsSaving);
        Assert.False(vm.IsEditable);
        Assert.False(vm.SaveCommand.CanExecute(null));
        Task second = vm.SaveCommand.ExecuteAsync(null);
        _store.SaveGate.SetResult();
        await Task.WhenAll(first, second).WaitAsync(TestTimeouts.Wait, Ct);

        Assert.Single(_store.Saves);
        Assert.Equal(1, _closeRequests);
    }

    [Fact]
    public async Task ClosingByTheUserIsBlockedWhileSaving()
    {
        _store.SaveGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        SettingsViewModel vm = await LoadAsync(new AppSettings { Printers = [LinkPrinter("a", "Alpha")] });
        Assert.False(vm.BlocksClosing);
        vm.Printers[0].Name = "Renamed";

        Task save = vm.SaveCommand.ExecuteAsync(null);
        // A failure must stay visible: the window cannot be closed by the user until the save finished.
        Assert.True(vm.BlocksClosing);
        Assert.False(vm.CancelCommand.CanExecute(null));

        _store.SaveGate.SetResult();
        await save.WaitAsync(TestTimeouts.Wait, Ct);
        Assert.False(vm.BlocksClosing);
    }

    [Fact]
    public async Task SwitchingToPrusaConnectForgetsATypedPassword()
    {
        SettingsViewModel vm = await LoadAsync(new AppSettings { Printers = [CloudPrinter("c", "Cloud")] });
        PrinterEditorViewModel printer = vm.Printers[0];
        printer.SelectedConnectionType = printer.ConnectionTypes.Single(static o => o.Value == ConnectionType.PrusaLink);
        printer.Address = "192.168.1.40";
        printer.Password = "typed";

        printer.SelectedConnectionType = printer.ConnectionTypes.Single(static o => o.Value == ConnectionType.PrusaConnect);

        Assert.Equal(string.Empty, printer.Password);
        Assert.False(printer.HasNewPassword);
    }

    [Fact]
    public async Task CancelDiscardsEverything()
    {
        SettingsViewModel vm = await LoadAsync(new AppSettings { Printers = [LinkPrinter("a", "Alpha")] });
        vm.Printers[0].Name = "Changed";
        vm.Printers[0].Password = "pw";

        vm.CancelCommand.Execute(null);

        Assert.Equal(1, _closeRequests);
        Assert.Empty(_store.Saves);
    }

    [Fact]
    public async Task DisposeForgetsTypedPasswords()
    {
        SettingsViewModel vm = await LoadAsync(new AppSettings { Printers = [LinkPrinter("a", "Alpha")] });
        vm.Printers[0].Password = "pw";

        vm.Dispose();

        Assert.Equal(string.Empty, vm.Printers[0].Password);
    }

    // ---- Dirty tracking ----

    [Fact]
    public async Task DirtyTracksRealDifferencesOnly()
    {
        SettingsViewModel vm = await LoadAsync(new AppSettings { Printers = [LinkPrinter("a", "Alpha")] });
        List<string?> changes = [];
        vm.PropertyChanged += (_, e) => changes.Add(e.PropertyName);

        vm.Printers[0].Name = "Alpha 2";
        Assert.True(vm.IsDirty);
        vm.Printers[0].Name = "Alpha ";
        Assert.False(vm.IsDirty);

        vm.Printers[0].Password = "x";
        Assert.True(vm.IsDirty);
        vm.Printers[0].Password = string.Empty;
        Assert.False(vm.IsDirty);

        vm.PollingIntervalSeconds = 6;
        Assert.True(vm.IsDirty);
        vm.PollingIntervalSeconds = 5;
        Assert.False(vm.IsDirty);

        vm.SelectedSound = vm.SoundOptions[2];
        Assert.True(vm.IsDirty);
        vm.SelectedSound = vm.SoundOptions[1];
        Assert.False(vm.IsDirty);

        vm.AddPrinterCommand.Execute(null);
        Assert.True(vm.IsDirty);
        vm.RemovePrinterCommand.Execute(null);
        Assert.False(vm.IsDirty);

        Assert.Contains(nameof(SettingsViewModel.IsDirty), changes);
    }

    // ---- Passwords on an OS without a secret store ----

    [Fact]
    public async Task UnsupportedSecretStoreDisablesPasswords()
    {
        SettingsViewModel vm = await LoadAsync(new AppSettings { Printers = [LinkPrinter("a", "Alpha")] }, policy: PasswordPolicy.Unsupported, withPassword: null);

        Assert.False(vm.IsPasswordSupported);
        Assert.False(vm.Printers[0].IsPasswordEnabled);
        Assert.Equal(
            "Passwords cannot be saved on this system yet. Printers that require a password will report that it is missing.",
            vm.Printers[0].PasswordHint);

        vm.Printers[0].Password = "pw";
        vm.Printers[0].Name = "Renamed";
        await vm.SaveCommand.ExecuteAsync(null);

        Assert.Empty(Assert.Single(_store.Saves).NewPasswords);
    }

    // ---- Prusa Connect ----

    [Fact]
    public async Task PrusaConnectIsOfferedButNotSelectableForNewPrinters()
    {
        SettingsViewModel vm = await LoadAsync(AppSettings.Default);
        vm.AddPrinterCommand.Execute(null);

        ConnectionTypeOption connect = vm.SelectedPrinter!.ConnectionTypes.Single(static o => o.Value == ConnectionType.PrusaConnect);
        Assert.False(connect.IsEnabled);
        Assert.Equal("Prusa Connect (not available yet)", connect.Text);
        Assert.True(vm.SelectedPrinter.ConnectionTypes.Single(static o => o.Value == ConnectionType.PrusaLink).IsEnabled);
    }

    [Fact]
    public async Task ExistingPrusaConnectPrinterIsKeptAsIs()
    {
        PrinterDefinition cloud = CloudPrinter("c", "Cloud");
        SettingsViewModel vm = await LoadAsync(new AppSettings { Printers = [cloud] });
        PrinterEditorViewModel printer = vm.Printers[0];

        Assert.Equal(ConnectionType.PrusaConnect, printer.SelectedConnectionType.Value);
        Assert.False(printer.IsPrusaLink);
        Assert.False(printer.IsPasswordEnabled);
        Assert.False(printer.TestConnectionCommand.CanExecute(null));
        Assert.Null(printer.AddressError);

        printer.Name = "Cloud 2";
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.Equal(cloud with { Name = "Cloud 2" }, Assert.Single(Assert.Single(_store.Saves).Updated.Printers));
    }

    // ---- PrusaLink user name ----

    [Fact]
    public async Task LoadShowsTheSavedUserName()
    {
        SettingsViewModel vm = await LoadAsync(new AppSettings { Printers = [LinkPrinter("a", "Alpha", userName: "operator")] });

        Assert.Equal("operator", vm.Printers[0].UserName);
        Assert.Null(vm.Printers[0].UserNameError);
        Assert.False(vm.IsDirty);
    }

    [Fact]
    public async Task NewPrinterStartsWithTheDefaultUserName()
    {
        SettingsViewModel vm = await LoadAsync(AppSettings.Default);
        vm.AddPrinterCommand.Execute(null);
        PrinterEditorViewModel added = vm.SelectedPrinter!;

        Assert.Equal("maker", added.UserName);
        Assert.Null(added.UserNameError);
        Assert.Equal("PrusaLink user name from the printer settings. The default is “maker”.", added.UserNameHint);
        Assert.Equal("PrusaLink password from the printer settings.", added.PasswordHint);
    }

    [Fact]
    public async Task SavingOtherChangesKeepsTheLoadedUserName()
    {
        SettingsViewModel vm = await LoadAsync(new AppSettings { Printers = [LinkPrinter("a", "Alpha", userName: "operator")] });

        vm.Printers[0].Name = "Alpha 2";
        await vm.SaveCommand.ExecuteAsync(null);

        Assert.Equal(LinkPrinter("a", "Alpha 2", userName: "operator"), Assert.Single(Assert.Single(_store.Saves).Updated.Printers));
    }

    [Fact]
    public async Task ChangedUserNameIsSavedTrimmed()
    {
        SettingsViewModel vm = await LoadAsync(new AppSettings { Printers = [LinkPrinter("a", "Alpha")] });

        vm.Printers[0].UserName = " operator ";
        Assert.True(vm.IsDirty);
        await vm.SaveCommand.ExecuteAsync(null);

        Assert.Equal(LinkPrinter("a", "Alpha", userName: "operator"), Assert.Single(Assert.Single(_store.Saves).Updated.Printers));
        Assert.Equal(1, _closeRequests);
    }

    [Fact]
    public async Task NewPrinterIsSavedWithTheEnteredUserName()
    {
        SettingsViewModel vm = await LoadAsync(AppSettings.Default);
        vm.AddPrinterCommand.Execute(null);
        vm.SelectedPrinter!.Name = "MK4";
        vm.SelectedPrinter.Address = "192.168.1.30";
        vm.SelectedPrinter.UserName = "operator";

        await vm.SaveCommand.ExecuteAsync(null);

        Assert.Equal(LinkPrinter("new1", "MK4", "192.168.1.30", "operator"), Assert.Single(Assert.Single(_store.Saves).Updated.Printers));
    }

    [Fact]
    public async Task UserNameOnlyDiffersInSurroundingSpacesIsNotDirty()
    {
        SettingsViewModel vm = await LoadAsync(new AppSettings { Printers = [LinkPrinter("a", "Alpha")] });

        vm.Printers[0].UserName = "operator";
        Assert.True(vm.IsDirty);
        vm.Printers[0].UserName = " maker ";
        Assert.False(vm.IsDirty);
    }

    [Theory]
    [InlineData("", "Enter a user name.")]
    [InlineData("   ", "Enter a user name.")]
    [InlineData("a\tb", "The user name must not contain tabs, line breaks or other control characters.")]
    [InlineData("a\nb", "The user name must not contain tabs, line breaks or other control characters.")]
    [InlineData("a:b", "The user name must not contain a colon (:).")]
    [InlineData("a\"b", UserNameUnsupported)]
    [InlineData("a\\b", UserNameUnsupported)]
    [InlineData("tomáš", UserNameUnsupported)]
    [InlineData("operator", null)]
    [InlineData(" maker ", null)]
    [InlineData("user.name-1_@x", null)]
    public async Task UserNameFollowsTheSharedRules(string userName, string? expected)
    {
        PrinterEditorViewModel printer = await LoadOneAsync();

        printer.UserName = userName;

        Assert.Equal(expected, printer.UserNameError);
        Assert.Equal(expected is not null, printer.HasErrors);
        Assert.Equal(expected is not null, printer.ShowsErrorMarker);
    }

    [Fact]
    public async Task UserNameHasAMaximumLength()
    {
        PrinterEditorViewModel printer = await LoadOneAsync();

        printer.UserName = new string('x', PrinterUserName.MaxLength);
        Assert.Null(printer.UserNameError);

        printer.UserName = new string('x', PrinterUserName.MaxLength + 1);
        Assert.Equal("The user name can have at most 64 characters.", printer.UserNameError);
    }

    [Fact]
    public async Task UserNameErrorsFollowUiCulture()
    {
        using CultureScope czech = new("cs-CZ");
        PrinterEditorViewModel printer = await LoadOneAsync();

        printer.UserName = string.Empty;

        Assert.Equal("Zadejte uživatelské jméno.", printer.UserNameError);
    }

    [Fact]
    public async Task SaveWithInvalidUserNameSelectsThePrinterAndSavesNothing()
    {
        SettingsViewModel vm = await LoadAsync(new AppSettings { Printers = [LinkPrinter("a", "Alpha"), LinkPrinter("b", "Beta")] });
        vm.Printers[1].UserName = "a:b";
        vm.SelectedPrinter = vm.Printers[0];

        await vm.SaveCommand.ExecuteAsync(null);

        Assert.Empty(_store.Saves);
        Assert.Same(vm.Printers[1], vm.SelectedPrinter);
        Assert.True(vm.StatusIsError);
        Assert.Equal("Some values are not valid. Fix the highlighted fields.", vm.StatusText);
        Assert.Equal(0, _closeRequests);
    }

    [Fact]
    public async Task ExistingPrusaConnectPrinterIgnoresTheUserName()
    {
        PrinterDefinition cloud = CloudPrinter("c", "Cloud");
        SettingsViewModel vm = await LoadAsync(new AppSettings { Printers = [cloud] });
        PrinterEditorViewModel printer = vm.Printers[0];

        Assert.False(printer.IsPrusaLink);
        Assert.Equal("Prusa Connect does not use a printer user name.", printer.UserNameHint);

        // The disabled field is not validated and never changes the saved definition.
        printer.UserName = string.Empty;
        Assert.Null(printer.UserNameError);
        Assert.False(printer.HasErrors);
        printer.Name = "Cloud 2";
        await vm.SaveCommand.ExecuteAsync(null);

        Assert.Equal(cloud with { Name = "Cloud 2" }, Assert.Single(Assert.Single(_store.Saves).Updated.Printers));
    }

    [Fact]
    public async Task SwitchingToPrusaConnectRestoresTheSavedUserName()
    {
        SettingsViewModel vm = await LoadAsync(new AppSettings { Printers = [CloudPrinter("c", "Cloud")] });
        PrinterEditorViewModel printer = vm.Printers[0];
        printer.SelectedConnectionType = printer.ConnectionTypes.Single(static o => o.Value == ConnectionType.PrusaLink);
        printer.Address = "192.168.1.40";
        printer.UserName = "a:b";
        Assert.NotNull(printer.UserNameError);

        printer.SelectedConnectionType = printer.ConnectionTypes.Single(static o => o.Value == ConnectionType.PrusaConnect);
        printer.Address = "c0ffee00-0000-4000-8000-000000000001";

        Assert.Equal("maker", printer.UserName);
        Assert.Null(printer.UserNameError);
        Assert.False(printer.HasErrors);
        Assert.False(vm.IsDirty);
    }

    [Fact]
    public async Task UserNameStaysEditableWithoutASecretStore()
    {
        SettingsViewModel vm = await LoadAsync(new AppSettings { Printers = [LinkPrinter("a", "Alpha")] }, policy: PasswordPolicy.Unsupported, withPassword: null);

        vm.Printers[0].UserName = "operator";
        await vm.SaveCommand.ExecuteAsync(null);

        Assert.Equal("operator", Assert.Single(Assert.Single(_store.Saves).Updated.Printers).UserName);
    }

    [Fact]
    public async Task TestUsesTheEnteredUserName()
    {
        PrinterEditorViewModel printer = await LoadOneAsync();
        _tester.Immediate = new ConnectionTestResult(ConnectionTestOutcome.Success);
        printer.UserName = " operator ";

        await printer.TestConnectionCommand.ExecuteAsync(null);

        Assert.Equal("operator", Assert.Single(_tester.Calls).Printer.UserName);
        Assert.Empty(_store.Saves);
    }

    [Fact]
    public async Task InvalidUserNameIsReportedWithoutTesting()
    {
        PrinterEditorViewModel printer = await LoadOneAsync();
        printer.UserName = "a:b";

        await printer.TestConnectionCommand.ExecuteAsync(null);

        Assert.Empty(_tester.Calls);
        Assert.Equal("The user name must not contain a colon (:).", printer.TestResultText);
        Assert.True(printer.TestFailed);
    }

    [Fact]
    public async Task EditingTheUserNameClearsAnOldResult()
    {
        PrinterEditorViewModel printer = await LoadOneAsync();
        _tester.Immediate = new ConnectionTestResult(ConnectionTestOutcome.PasswordRejected);
        await printer.TestConnectionCommand.ExecuteAsync(null);
        Assert.True(printer.HasTestResult);

        printer.UserName = "operator";

        Assert.False(printer.HasTestResult);
    }

    // ---- Test connection ----

    [Fact]
    public async Task SuccessfulTestShowsModelAndFirmware()
    {
        PrinterEditorViewModel printer = await LoadOneAsync();
        _tester.Immediate = new ConnectionTestResult(ConnectionTestOutcome.Success, "7.0.0", "6.9.2+10123", "prusa");

        await printer.TestConnectionCommand.ExecuteAsync(null);

        Assert.Equal("Connection works. Model code: 7.0.0, firmware: 6.9.2+10123.", printer.TestResultText);
        Assert.True(printer.TestSucceeded);
        Assert.False(printer.TestFailed);
        Assert.False(printer.IsTesting);
    }

    [Fact]
    public async Task SuccessWithoutDetailsShowsDashes()
    {
        PrinterEditorViewModel printer = await LoadOneAsync();
        _tester.Immediate = new ConnectionTestResult(ConnectionTestOutcome.Success);

        await printer.TestConnectionCommand.ExecuteAsync(null);

        Assert.Equal("Connection works. Model code: –, firmware: –.", printer.TestResultText);
    }

    [Fact]
    public async Task TestUsesTheUnsavedFormValues()
    {
        SettingsViewModel vm = await LoadAsync(new AppSettings { Printers = [LinkPrinter("a", "Alpha")] }, withPassword: Ids("a"));
        PrinterEditorViewModel printer = vm.Printers[0];
        _tester.Immediate = new ConnectionTestResult(ConnectionTestOutcome.Success);
        printer.Address = "192.168.1.99";

        await printer.TestConnectionCommand.ExecuteAsync(null);
        printer.Password = "typed";
        await printer.TestConnectionCommand.ExecuteAsync(null);

        Assert.Equal(LinkPrinter("a", "Alpha", "192.168.1.99"), _tester.Calls[0].Printer);
        // Empty field on a saved printer = its saved password (null); typed = the typed one.
        Assert.Null(_tester.Calls[0].Password);
        Assert.Equal("typed", _tester.Calls[1].Password);
        Assert.Empty(_store.Saves);
    }

    [Fact]
    public async Task NewPrinterWithoutPasswordIsTestedWithoutOne()
    {
        SettingsViewModel vm = await LoadAsync(AppSettings.Default);
        vm.AddPrinterCommand.Execute(null);
        PrinterEditorViewModel printer = vm.SelectedPrinter!;
        printer.Address = "192.168.1.30";
        _tester.Immediate = new ConnectionTestResult(ConnectionTestOutcome.PasswordMissing);

        await printer.TestConnectionCommand.ExecuteAsync(null);

        (PrinterDefinition tested, string? password) = Assert.Single(_tester.Calls);
        Assert.Equal(string.Empty, password);
        Assert.Equal("new1", tested.Id);
        Assert.Equal("The printer requires a password. Enter it above.", printer.TestResultText);
    }

    [Theory]
    [InlineData(ConnectionTestOutcome.NotReachable, "The printer is not reachable. Check the address and that the printer is switched on and connected to the network.")]
    [InlineData(ConnectionTestOutcome.Timeout, "The printer did not respond in time.")]
    [InlineData(ConnectionTestOutcome.SecureConnectionFailed, "The secure (https) connection failed. Try the address with http:// or check the printer's certificate.")]
    [InlineData(ConnectionTestOutcome.PasswordRejected, "The printer rejected the user name or password.")]
    [InlineData(ConnectionTestOutcome.AccessDenied, "The printer denied access.")]
    [InlineData(ConnectionTestOutcome.PasswordUnavailable, "The saved password could not be read.")]
    [InlineData(ConnectionTestOutcome.UnsupportedAuthentication, "The printer requires an unsupported sign-in method.")]
    [InlineData(ConnectionTestOutcome.UnexpectedResponse, "Something answers at this address, but it does not look like a PrusaLink printer.")]
    [InlineData(ConnectionTestOutcome.InvalidAddress, AddressInvalid)]
    [InlineData(ConnectionTestOutcome.NotSupported, "Testing is not available for Prusa Connect yet.")]
    public async Task FailedTestIsExplained(ConnectionTestOutcome outcome, string expected)
    {
        PrinterEditorViewModel printer = await LoadOneAsync();
        _tester.Immediate = new ConnectionTestResult(outcome);

        await printer.TestConnectionCommand.ExecuteAsync(null);

        Assert.Equal(expected, printer.TestResultText);
        Assert.True(printer.TestFailed);
        Assert.False(printer.TestSucceeded);
    }

    [Fact]
    public async Task InvalidAddressIsReportedWithoutTesting()
    {
        PrinterEditorViewModel printer = await LoadOneAsync();
        printer.Address = "ftp://printer";

        await printer.TestConnectionCommand.ExecuteAsync(null);

        Assert.Empty(_tester.Calls);
        Assert.Equal(AddressInvalid, printer.TestResultText);
        Assert.True(printer.TestFailed);
    }

    [Fact]
    public async Task RunningTestShowsProgressAndCannotStartTwice()
    {
        PrinterEditorViewModel printer = await LoadOneAsync();

        Task first = printer.TestConnectionCommand.ExecuteAsync(null);
        Assert.True(printer.IsTesting);
        Assert.Equal("Testing connection…", printer.TestResultText);
        Assert.False(printer.TestConnectionCommand.CanExecute(null));
        Task second = printer.TestConnectionCommand.ExecuteAsync(null);
        _tester.Respond(new ConnectionTestResult(ConnectionTestOutcome.Success));
        await Task.WhenAll(first, second).WaitAsync(TestTimeouts.Wait, Ct);

        Assert.Single(_tester.Calls);
        Assert.False(printer.IsTesting);
        Assert.True(printer.TestConnectionCommand.CanExecute(null));
    }

    [Fact]
    public async Task RunningTestCanBeCancelled()
    {
        PrinterEditorViewModel printer = await LoadOneAsync();
        Task test = printer.TestConnectionCommand.ExecuteAsync(null);

        printer.CancelTestCommand.Execute(null);
        await test.WaitAsync(TestTimeouts.Wait, Ct);

        Assert.True(_tester.LastToken.IsCancellationRequested);
        Assert.Equal("Test cancelled.", printer.TestResultText);
        Assert.False(printer.IsTesting);
    }

    [Fact]
    public async Task EditingDuringATestCancelsItAndClearsTheResult()
    {
        PrinterEditorViewModel printer = await LoadOneAsync();
        Task test = printer.TestConnectionCommand.ExecuteAsync(null);

        printer.Address = "192.168.1.77";
        await test.WaitAsync(TestTimeouts.Wait, Ct);

        Assert.True(_tester.LastToken.IsCancellationRequested);
        Assert.Null(printer.TestResultText);
        Assert.False(printer.TestFailed);
    }

    [Fact]
    public async Task EditingClearsAnOldResult()
    {
        PrinterEditorViewModel printer = await LoadOneAsync();
        _tester.Immediate = new ConnectionTestResult(ConnectionTestOutcome.PasswordRejected);
        await printer.TestConnectionCommand.ExecuteAsync(null);

        printer.Password = "other";

        Assert.Null(printer.TestResultText);
        Assert.False(printer.HasTestResult);
    }

    [Fact]
    public async Task ClosingTheWindowCancelsARunningTest()
    {
        PrinterEditorViewModel printer = await LoadOneAsync();
        Task test = printer.TestConnectionCommand.ExecuteAsync(null);

        _viewModel!.Dispose();
        await test.WaitAsync(TestTimeouts.Wait, Ct);

        Assert.True(_tester.LastToken.IsCancellationRequested);
    }

    [Fact]
    public async Task RemovingAPrinterCancelsItsTest()
    {
        SettingsViewModel vm = await LoadAsync(new AppSettings { Printers = [LinkPrinter("a", "Alpha")] });
        Task test = vm.Printers[0].TestConnectionCommand.ExecuteAsync(null);

        vm.RemovePrinterCommand.Execute(null);
        await test.WaitAsync(TestTimeouts.Wait, Ct);

        Assert.True(_tester.LastToken.IsCancellationRequested);
    }

    [Fact]
    public async Task UnexpectedTesterFailureIsShownNotThrown()
    {
        _store.Data = new SettingsEditorData(new AppSettings { Printers = [LinkPrinter("a", "Alpha")] }, SettingsLoadStatus.Loaded, [], Ids());
        SettingsViewModel vm = new(_store, new ThrowingTester(), Supported, NextId);
        _viewModel = vm;
        await vm.LoadAsync();

        await vm.Printers[0].TestConnectionCommand.ExecuteAsync(null);

        Assert.Equal("The test failed unexpectedly.", vm.Printers[0].TestResultText);
        Assert.True(vm.Printers[0].TestFailed);
        Assert.False(vm.Printers[0].IsTesting);
    }

    private const string AddressInvalid =
        "Enter an IP address or host name, optionally with http:// or https://, a port and a path. A user name, password, query (?) or fragment (#) is not allowed.";

    private const string UserNameUnsupported =
        "The user name can contain only letters without diacritics, digits and common symbols, except \" and \\.";

    private string NextId() => _ids.Dequeue();

    private SettingsViewModel Create(PasswordPolicy? policy = null)
    {
        _viewModel?.Dispose();
        SettingsViewModel vm = new(_store, _tester, policy ?? Supported, NextId);
        vm.CloseRequested += (_, _) => _closeRequests++;
        _viewModel = vm;
        return vm;
    }

    private async Task<SettingsViewModel> LoadAsync(AppSettings settings, PasswordPolicy? policy = null, IReadOnlySet<string>? withPassword = null)
    {
        _store.Data = new SettingsEditorData(settings, SettingsLoadStatus.Loaded, [], withPassword ?? (policy is { IsSupported: false } ? null : Ids()));
        SettingsViewModel vm = Create(policy);
        await vm.LoadAsync();
        return vm;
    }

    private async Task<PrinterEditorViewModel> LoadOneAsync(PasswordPolicy? policy = null) =>
        (await LoadAsync(new AppSettings { Printers = [LinkPrinter("a", "Alpha")] }, policy)).Printers[0];

    private sealed class ThrowingTester : IConnectionTester
    {
        public Task<ConnectionTestResult> TestAsync(PrinterDefinition printer, string? password, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("boom");
    }
}
