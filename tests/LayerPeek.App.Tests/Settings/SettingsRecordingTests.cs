using LayerPeek.App.Resources;
using LayerPeek.App.Settings;
using LayerPeek.App.Tests.Cameras.Recording;
using LayerPeek.Core.Cameras;
using LayerPeek.Core.Cameras.Recording;
using LayerPeek.Core.Printers;
using LayerPeek.Core.Settings;

using static LayerPeek.App.Tests.Settings.SettingsTestData;

namespace LayerPeek.App.Tests.Settings;

/// <summary>
/// The recording settings of the settings window (T-146, user decisions 2026-11-03): the folder (system dialog, checked before
/// it is taken; "Default" brings back the video folder), the size limit in whole GB, "Open recordings folder", and "Record
/// prints automatically" per printer; disabled with the reason when this build cannot record. Saved by Save.
/// </summary>
public sealed class SettingsRecordingTests : IDisposable
{
    private static readonly PasswordPolicy Supported = new(IsSupported: true, MaxBytes: 2560);
    private static readonly CameraSettings Camera = new(enabled: true, new Uri("rtsp://192.168.0.227/live"));

    private readonly FakeEditorStore _store = new();
    private readonly FakeCameraRecordings _recordings = new();
    private readonly FakeRecordingFolderPicker _picker = new();
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "LayerPeek.SettingsRecordingTests." + Guid.NewGuid().ToString("N"));
    private SettingsViewModel? _viewModel;

    public SettingsRecordingTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        _viewModel?.Dispose();
        Directory.Delete(_folder, recursive: true);
    }

    private static PrinterDefinition Printer(CameraSettings camera) => LinkPrinter("a", "Alpha") with { Camera = camera };

    private async Task<SettingsViewModel> LoadAsync(AppSettings settings, Func<string, bool>? canWrite = null)
    {
        _store.Data = new SettingsEditorData(settings, SettingsLoadStatus.Loaded, [], Ids([.. settings.Printers.Select(static p => p.Id)]));
        _viewModel?.Dispose();
        _viewModel = new SettingsViewModel(_store, new FakeConnectionTester(), Supported, static () => Guid.NewGuid().ToString("N"))
        {
            Recordings = _recordings,
            CanWriteFolder = canWrite ?? SettingsViewModel.CanWriteTo,
        };
        _viewModel.RecordingFolderPicker = _picker;
        await _viewModel.LoadAsync();
        return _viewModel;
    }

    [Fact]
    public async Task LoadShowsTheDefaultFolderAndLimit()
    {
        SettingsViewModel vm = await LoadAsync(new AppSettings { Printers = [Printer(Camera)] });

        Assert.Equal(FakeCameraRecordings.FixedDefaultFolder, vm.RecordingFolderText);
        Assert.False(vm.HasCustomRecordingFolder);
        Assert.Equal(CameraRecordingSettings.DefaultSizeLimitGigabytes, vm.RecordingLimitGigabytes);
        Assert.True(vm.ShowsRecordingFolderHint);
        Assert.True(vm.ShowsRecordingLimitHint);
        Assert.Equal(Strings.SettingsRecordingLimitHint.Replace("{0}", "5", StringComparison.Ordinal), vm.RecordingLimitHint);
        Assert.True(vm.ChooseRecordingFolderCommand.CanExecute(null));
        Assert.False(vm.ResetRecordingFolderCommand.CanExecute(null));
        Assert.False(vm.Printers[0].RecordPrints);
        Assert.True(vm.Printers[0].CanEditRecordPrints);
        Assert.False(vm.IsDirty);
    }

    [Fact]
    public async Task AChosenFolderTheLimitAndRecordPrintsAreSaved()
    {
        SettingsViewModel vm = await LoadAsync(new AppSettings { Printers = [Printer(Camera)] });
        _picker.Answer = new PickedFolder(_folder);

        await vm.ChooseRecordingFolderCommand.ExecuteAsync(null);
        vm.RecordingLimitGigabytes = 12;
        vm.Printers[0].RecordPrints = true;

        Assert.Equal(_folder, vm.RecordingFolderText);
        Assert.True(vm.HasCustomRecordingFolder);
        Assert.True(vm.ResetRecordingFolderCommand.CanExecute(null));
        Assert.True(vm.IsDirty);
        // The dialog opens where the folder is now.
        Assert.Equal([FakeCameraRecordings.FixedDefaultFolder], _picker.StartDirectories);
        await vm.SaveCommand.ExecuteAsync(null);

        AppSettings saved = Assert.Single(_store.Saves).Updated;
        Assert.Equal(new CameraRecordingSettings { Folder = _folder, SizeLimitGigabytes = 12 }, saved.Recording);
        Assert.True(saved.Printers[0].Camera.RecordPrints);
        // The probe file of the check is gone.
        Assert.Empty(Directory.EnumerateFileSystemEntries(_folder));
    }

    [Fact]
    public async Task DefaultBringsBackTheVideoFolderAndTheDefaultChosenInTheDialogIsNoFolderOfItsOwn()
    {
        SettingsViewModel vm = await LoadAsync(new AppSettings { Printers = [Printer(Camera)], Recording = new CameraRecordingSettings { Folder = _folder } });
        Assert.True(vm.HasCustomRecordingFolder);

        vm.ResetRecordingFolderCommand.Execute(null);
        Assert.Equal(FakeCameraRecordings.FixedDefaultFolder, vm.RecordingFolderText);
        Assert.True(vm.IsDirty);

        // Choosing the default folder itself (it exists here) keeps "no folder of the user's own".
        _recordings.DefaultFolder = _folder;
        _picker.Answer = new PickedFolder(_folder + Path.DirectorySeparatorChar);
        await vm.ChooseRecordingFolderCommand.ExecuteAsync(null);
        Assert.False(vm.HasCustomRecordingFolder);
        await vm.SaveCommand.ExecuteAsync(null);

        Assert.Null(Assert.Single(_store.Saves).Updated.Recording.Folder);
    }

    [Fact]
    public async Task ACancelledDialogChangesNothing()
    {
        SettingsViewModel vm = await LoadAsync(new AppSettings { Printers = [Printer(Camera)] });

        await vm.ChooseRecordingFolderCommand.ExecuteAsync(null);

        Assert.False(vm.HasCustomRecordingFolder);
        Assert.Null(vm.RecordingFolderError);
        Assert.False(vm.IsDirty);
    }

    public static TheoryData<string> RefusedFolders => ["not-local", "too-long", "not-writable"];

    [Theory]
    [MemberData(nameof(RefusedFolders))]
    public async Task AFolderThatCannotBeUsedIsNotTakenAndSaysWhy(string kind)
    {
        SettingsViewModel vm = await LoadAsync(new AppSettings { Printers = [Printer(Camera)] }, canWrite: static _ => false);
        (PickedFolder answer, string expected) = kind switch
        {
            "not-local" => (new PickedFolder(null), Strings.SettingsRecordingFolderNotLocal),
            "too-long" => (new PickedFolder(Path.Combine(_folder, new string('x', CameraRecordingSettings.MaxFolderLength))), Strings.SettingsRecordingFolderInvalid.Replace("{0}", "200", StringComparison.Ordinal)),
            _ => (new PickedFolder(_folder), Strings.SettingsRecordingFolderNotWritable),
        };
        _picker.Answer = answer;

        await vm.ChooseRecordingFolderCommand.ExecuteAsync(null);

        Assert.Equal(expected, vm.RecordingFolderError);
        Assert.Equal(expected, vm.RecordingFolderHelpText);
        Assert.False(vm.ShowsRecordingFolderHint);
        Assert.False(vm.HasCustomRecordingFolder);
        Assert.False(vm.IsDirty);

        // A good folder clears the error.
        vm.ResetRecordingFolderCommand.Execute(null);
        Assert.Null(vm.RecordingFolderError);
        Assert.True(vm.ShowsRecordingFolderHint);
    }

    [Fact]
    public void TheWriteCheckLeavesNothingAndRefusesAMissingFolder()
    {
        Assert.True(SettingsViewModel.CanWriteTo(_folder));
        Assert.Empty(Directory.EnumerateFileSystemEntries(_folder));
        Assert.False(SettingsViewModel.CanWriteTo(Path.Combine(_folder, "missing")));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(100_001)]
    [InlineData(1.5)]
    public async Task AnInvalidLimitBlocksSaveAndSelectsItsTab(double limit)
    {
        SettingsViewModel vm = await LoadAsync(new AppSettings { Printers = [Printer(Camera)] });
        List<string> focused = [];
        vm.FieldFocusRequested += (_, field) => focused.Add(field);

        vm.RecordingLimitGigabytes = (decimal)limit;
        Assert.True(vm.HasRecordingLimitError);
        Assert.False(vm.ShowsRecordingLimitHint);
        Assert.Equal(vm.RecordingLimitError, vm.RecordingLimitHelpText);
        await vm.SaveCommand.ExecuteAsync(null);

        Assert.Empty(_store.Saves);
        Assert.Equal(SettingsViewModel.RecordingTab, vm.SelectedTabIndex);
        Assert.Equal([SettingsFieldIds.SettingsRecordingLimit], focused);
    }

    [Fact]
    public async Task WithoutTheRecorderTheOptionsAreDisabledWithTheReasonAndTheFolderCanStillBeOpened()
    {
        _recordings.IsAvailable = false;
        SettingsViewModel vm = await LoadAsync(new AppSettings { Printers = [Printer(Camera with { RecordPrints = true })] });

        Assert.False(vm.IsRecordingAvailable);
        Assert.False(vm.ChooseRecordingFolderCommand.CanExecute(null));
        Assert.False(vm.ShowsRecordingFolderHint);
        Assert.Equal(Strings.RecordingUnavailable, vm.RecordingFolderHelpText);
        Assert.False(vm.Printers[0].CanEditRecordPrints);
        Assert.Equal(Strings.RecordingUnavailable, vm.Printers[0].RecordPrintsHint);
        // The saved choice is kept.
        Assert.True(vm.Printers[0].RecordPrints);

        vm.OpenRecordingsFolderCommand.Execute(null);
        Assert.Equal([FakeCameraRecordings.FixedDefaultFolder], _recordings.OpenedFolders);
    }

    [Fact]
    public async Task RecordPrintsNeedsTheCameraAndIsKeptWhileItIsOff()
    {
        SettingsViewModel vm = await LoadAsync(new AppSettings { Printers = [Printer(Camera)] });
        PrinterEditorViewModel printer = vm.Printers[0];
        List<string?> changes = [];
        printer.PropertyChanged += (_, e) => changes.Add(e.PropertyName);
        printer.RecordPrints = true;

        printer.CameraEnabled = false;

        Assert.False(printer.CanEditRecordPrints);
        Assert.Contains(nameof(PrinterEditorViewModel.CanEditRecordPrints), changes);
        Assert.Equal(Strings.SettingsRecordPrintsHint, printer.RecordPrintsHint);
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.True(Assert.Single(_store.Saves).Updated.Printers[0].Camera.RecordPrints);
    }

    [Fact]
    public async Task OpenRecordingsFolderOpensTheFolderInTheFieldAlsoBeforeSave()
    {
        SettingsViewModel vm = await LoadAsync(new AppSettings { Printers = [Printer(Camera)] });
        _picker.Answer = new PickedFolder(_folder);
        await vm.ChooseRecordingFolderCommand.ExecuteAsync(null);

        vm.OpenRecordingsFolderCommand.Execute(null);

        Assert.Equal([_folder], _recordings.OpenedFolders);
    }

    [Fact]
    public async Task WithoutRecordingsOrADialogTheButtonsAreDisabled()
    {
        _store.Data = new SettingsEditorData(new AppSettings(), SettingsLoadStatus.Loaded, [], Ids());
        using SettingsViewModel vm = new(_store, new FakeConnectionTester(), Supported);
        await vm.LoadAsync();

        Assert.True(vm.IsRecordingAvailable);
        Assert.False(vm.ChooseRecordingFolderCommand.CanExecute(null));
        Assert.False(vm.OpenRecordingsFolderCommand.CanExecute(null));
        Assert.Equal(CameraRecordingFolders.DefaultRoot(), vm.RecordingFolderText);
    }
}
