using System.Globalization;

using LayerPeek.App.Resources;
using LayerPeek.App.Tests.Cameras.Recording;
using LayerPeek.App.Tray.Menu;
using LayerPeek.Core.Cameras;
using LayerPeek.Core.Cameras.Recording;
using LayerPeek.Core.Printers;
using LayerPeek.Core.Settings;

using static LayerPeek.App.Tests.Tray.TestPrinters;

namespace LayerPeek.App.Tests.Tray.Menu;

/// <summary>
/// The recording items of a printer submenu (T-146, user decisions 2026-11-03): after a separator, "Start recording" /
/// "Stop recording" (one access key in both states; disabled with the reason while it starts or stops and in a build without
/// the recorder), "Play last recording" (disabled until there is one) and "Open recordings folder"; only for printers with a
/// camera and only with the app's recordings. A change of a recording (any thread) formats only that printer again, in
/// place; clicks act on what the item showed.
/// </summary>
public sealed class TrayMenuRecordingTests : IDisposable
{
    private static readonly Uri StreamUri = new("rtsp://192.168.0.227/live");

    private readonly CultureScope _culture = new("en-US");
    private readonly FakePrinterMonitor _monitor = new();
    private readonly ManualDispatcher _dispatcher = new();
    private readonly FakeCameraRecordings _recordings = new();
    private readonly FakeMenuTarget _target = new();
    private readonly FakeAppCommands _commands = new();
    private readonly ListLogger<TrayMenuRecordingTests> _logger = new();

    public static TheoryData<string> Languages => [.. AppLanguages.Supported];

    public void Dispose() => _culture.Dispose();

    private static PrinterDefinition WithCamera(PrinterDefinition printer) => printer with { Camera = new CameraSettings(enabled: true, StreamUri) };

    private TrayMenuViewModel Create(params PrinterDefinition[] printers)
    {
        TrayMenuViewModel viewModel = new(_monitor, _dispatcher, TestWebOpener.Create(new FakeUrlLauncher()), TrayMenuOptions.ForCurrentPlatform, recordings: _recordings);
        viewModel.SetPrinters(printers);
        _dispatcher.RunAll();
        return viewModel;
    }

    private static TrayMenuEntry Item(TrayMenuViewModel viewModel, TrayMenuCommand command, int printer = 0) =>
        Assert.Single(viewModel.PrinterItems[printer].Submenu!, entry => entry.Command == command);

    [Fact]
    public void APrinterWithACameraGetsTheRecordingItemsAfterASeparator()
    {
        using TrayMenuViewModel viewModel = Create(WithCamera(Definition()), Definition("b", "NoCamera"));

        TrayMenuEntryList submenu = viewModel.PrinterItems[0].Submenu!;
        Assert.Collection(
            submenu,
            web => Assert.Equal(TrayMenuCommand.OpenWeb, web.Command),
            camera => Assert.Equal(TrayMenuCommand.Camera, camera.Command),
            separator => Assert.True(separator.IsSeparator),
            start => Assert.Equal((TrayMenuCommand.StartRecording, "Start _recording", true, null, "mk4"), (start.Command, start.Header, start.IsEnabled, start.ToolTip, start.PrinterId)),
            play => Assert.Equal((TrayMenuCommand.PlayLastRecording, "_Play last recording", false, Strings.MenuNoRecordingYet), (play.Command, play.Header, play.IsEnabled, play.ToolTip)),
            folder => Assert.Equal((TrayMenuCommand.OpenRecordingsFolder, "Open recordings fol_der", true), (folder.Command, folder.Header, folder.IsEnabled)));
        Assert.All(submenu.Where(static item => !item.IsSeparator), static item => Assert.True(item.HasAccessKey));

        // No camera, nothing to record.
        Assert.DoesNotContain(viewModel.PrinterItems[1].Submenu!, static item => item.Command is TrayMenuCommand.StartRecording or TrayMenuCommand.OpenRecordingsFolder);
    }

    [Fact]
    public void WithoutRecordingsTheSubmenuStaysAsBefore()
    {
        using TrayMenuViewModel viewModel = new(_monitor, _dispatcher, TestWebOpener.Create(new FakeUrlLauncher()), TrayMenuOptions.ForCurrentPlatform);
        viewModel.SetPrinters([WithCamera(Definition())]);
        _dispatcher.RunAll();

        Assert.Equal([TrayMenuCommand.OpenWeb, TrayMenuCommand.Camera], viewModel.PrinterItems[0].Submenu!.Select(static item => item.Command));
    }

    [Fact]
    public async Task TheItemFollowsTheRecordingInPlaceAndOnlyForItsPrinter()
    {
        using TrayMenuViewModel viewModel = Create(WithCamera(Definition()), WithCamera(Definition("b", "Beta")));
        TrayMenuEntry beta = viewModel.PrinterItems[1];
        using TrayMenuPresenter presenter = new(_target, viewModel, _commands, _logger);
        _target.Operations.Clear();

        // From another thread, like the service raises it.
        await Task.Run(() => _recordings.SetActivity("mk4", CameraRecordingActivity.Recording, lastFile: "/rec/mk4/2026-11-03_10-00-00.mp4"), TestContext.Current.CancellationToken);
        _dispatcher.RunAll();

        TrayMenuEntry stop = Item(viewModel, TrayMenuCommand.StopRecording);
        Assert.Equal(("Stop _recording", true, null), (stop.Header, stop.IsEnabled, stop.ToolTip));
        Assert.True(Item(viewModel, TrayMenuCommand.PlayLastRecording).IsEnabled);
        // One printer item updated in place; the other one untouched.
        Assert.Equal(["update 0"], _target.Operations);
        Assert.Same(beta, viewModel.PrinterItems[1]);

        _recordings.SetActivity("mk4", CameraRecordingActivity.Idle, lastFile: "/rec/mk4/2026-11-03_10-00-00.mp4");
        _dispatcher.RunAll();
        Assert.Equal(TrayMenuCommand.StartRecording, Item(viewModel, TrayMenuCommand.StartRecording).Command);
    }

    [Theory]
    [InlineData(CameraRecordingActivity.Starting)]
    [InlineData(CameraRecordingActivity.Stopping)]
    public void WhileItStartsOrStopsTheItemIsDisabledWithTheReason(CameraRecordingActivity activity)
    {
        using TrayMenuViewModel viewModel = Create(WithCamera(Definition()));

        _recordings.SetActivity("mk4", activity);
        _dispatcher.RunAll();

        // T-145 review: a Start during "stopping" would be lost silently, so neither can be clicked.
        TrayMenuEntry item = Item(viewModel, TrayMenuCommand.StopRecording);
        Assert.False(item.IsEnabled);
        Assert.Equal(activity == CameraRecordingActivity.Starting ? Strings.RecordingStarting : Strings.RecordingStopping, item.ToolTip);
    }

    [Fact]
    public void WithoutTheRecorderStartIsDisabledWithTheReason()
    {
        _recordings.IsAvailable = false;
        using TrayMenuViewModel viewModel = Create(WithCamera(Definition()));

        TrayMenuEntry start = Item(viewModel, TrayMenuCommand.StartRecording);
        Assert.False(start.IsEnabled);
        Assert.Equal(Strings.RecordingUnavailable, start.ToolTip);
        // Older recordings stay where they are.
        Assert.True(Item(viewModel, TrayMenuCommand.OpenRecordingsFolder).IsEnabled);
    }

    [Fact]
    public void ClicksActOnWhatTheItemShowed()
    {
        using TrayMenuViewModel viewModel = Create(WithCamera(Definition()));
        using TrayMenuPresenter presenter = new(_target, viewModel, _commands, _logger);
        TrayMenuEntry start = Item(viewModel, TrayMenuCommand.StartRecording);

        _target.Click(start);
        _recordings.SetActivity("mk4", CameraRecordingActivity.Recording, lastFile: "/rec/mk4/a.mp4");
        _dispatcher.RunAll();
        // A Start clicked in a menu that was built before the recording began starts again (the service ignores it), never stops.
        _target.Click(start);
        _target.Click(Item(viewModel, TrayMenuCommand.StopRecording));
        _target.Click(Item(viewModel, TrayMenuCommand.PlayLastRecording));
        _target.Click(Item(viewModel, TrayMenuCommand.OpenRecordingsFolder));

        Assert.Equal(["mk4", "mk4"], _recordings.Started);
        Assert.Equal(["mk4"], _recordings.Stopped);
        Assert.Equal(["mk4"], _recordings.Played);
        Assert.Empty(_recordings.OpenedFolders);
        Assert.Equal(["mk4"], _recordings.OpenedPrinterFolders);
        Assert.Empty(_logger.Entries);
    }

    [Fact]
    public void ADisabledItemDoesNothing()
    {
        using TrayMenuViewModel viewModel = Create(WithCamera(Definition()));

        viewModel.RunRecordingCommand(Item(viewModel, TrayMenuCommand.PlayLastRecording));
        viewModel.RunRecordingCommand(Item(viewModel, TrayMenuCommand.StartRecording) with { IsEnabled = false });

        Assert.Empty(_recordings.Played);
        Assert.Empty(_recordings.Started);
    }

    [Fact]
    public void AChangeOfAnUnknownPrinterOrAfterDisposeDoesNothing()
    {
        TrayMenuViewModel viewModel = Create(WithCamera(Definition()));
        int posts = _dispatcher.PostCount;

        _recordings.SetActivity("other", CameraRecordingActivity.Recording);
        Assert.Equal(posts, _dispatcher.PostCount);

        viewModel.Dispose();
        Assert.Equal(0, _recordings.ChangedSubscribers);
    }

    [Theory]
    [MemberData(nameof(Languages))]
    public void TheRecordingItemsHaveUniqueAccessKeysAndStartAndStopShareOne(string language)
    {
        using CultureScope culture = new(language);
        CultureInfo info = CultureInfo.GetCultureInfo(language);
        PrinterDefinition connect = new() { Id = "c", Name = "Connect", ConnectionType = ConnectionType.PrusaConnect, Address = "uuid", Camera = new CameraSettings(enabled: true, StreamUri) };
        using TrayMenuViewModel viewModel = Create(WithCamera(Definition()), connect);
        // The longest submenu: the camera window is open ("Bring camera to front" too).
        viewModel.SetCameraWindowOpen("mk4", isOpen: true);
        viewModel.SetCameraWindowOpen("c", isOpen: true);
        char?[] startKeys = [.. Enumerable.Range(0, 2).Select(i => AccessKey(Item(viewModel, TrayMenuCommand.StartRecording, i), info))];

        _recordings.SetActivity("mk4", CameraRecordingActivity.Recording);
        _recordings.SetActivity("c", CameraRecordingActivity.Recording);
        _dispatcher.RunAll();

        for (int i = 0; i < 2; i++)
        {
            TrayMenuEntryList submenu = viewModel.PrinterItems[i].Submenu!;
            Assert.Equal(7, submenu.Count);
            char?[] keys = [.. submenu.Where(static item => !item.IsSeparator).Select(item => AccessKey(item, info))];
            Assert.All(keys, key => Assert.True(key is { } k && char.IsLetterOrDigit(k), $"{language}: {string.Join(", ", keys)}"));
            Assert.True(keys.Distinct().Count() == keys.Length, $"{language}: access keys {string.Join(", ", keys)} are not unique.");
            Assert.Equal(startKeys[i], AccessKey(Item(viewModel, TrayMenuCommand.StopRecording, i), info));
            Assert.All(submenu.Where(static item => !item.IsSeparator), item => Assert.Equal(1, NativeMenuText.HeaderOf(item).Count(static c => c == '_')));
        }
    }

    private static char? AccessKey(TrayMenuEntry item, CultureInfo culture) =>
        NativeMenuText.AccessKeyOf(NativeMenuText.HeaderOf(item)) is { } key ? char.ToUpper(key, culture) : null;
}
