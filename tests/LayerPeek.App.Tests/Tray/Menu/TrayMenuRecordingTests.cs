using System.Globalization;

using LayerPeek.App.Resources;
using LayerPeek.App.Tests.Cameras.Recording;
using LayerPeek.App.Tray.Menu;
using LayerPeek.Core.Cameras;
using LayerPeek.Core.Cameras.Recording;
using LayerPeek.Core.Cameras.Timelapse;
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
            timelapse => Assert.Equal((TrayMenuCommand.StartTimelapse, "Start _timelapse (experimental)", true, null, "mk4"), (timelapse.Command, timelapse.Header, timelapse.IsEnabled, timelapse.ToolTip, timelapse.PrinterId)),
            play => Assert.Equal((TrayMenuCommand.PlayLastRecording, "_Play last recording", false, Strings.MenuNoRecordingYet), (play.Command, play.Header, play.IsEnabled, play.ToolTip)),
            playTimelapse => Assert.Equal((TrayMenuCommand.PlayLastTimelapse, "Play _last timelapse", false, Strings.MenuNoTimelapseYet), (playTimelapse.Command, playTimelapse.Header, playTimelapse.IsEnabled, playTimelapse.ToolTip)),
            export => Assert.Equal((TrayMenuCommand.ExportTimelapseJpeg, "_Export timelapse to JPEG… (experimental)", true, null, "mk4"), (export.Command, export.Header, export.IsEnabled, export.ToolTip, export.PrinterId)),
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
        char?[] timelapseKeys = [.. Enumerable.Range(0, 2).Select(i => AccessKey(Item(viewModel, TrayMenuCommand.StartTimelapse, i), info))];

        _recordings.SetActivity("mk4", CameraRecordingActivity.Recording);
        _recordings.SetActivity("c", CameraRecordingActivity.Recording);
        // T-148: with "Save the last N min" of the dashcam too.
        _recordings.SetDashcam("mk4", CameraDashcamActivity.Waiting, minutes: 10);
        _recordings.SetDashcam("c", CameraDashcamActivity.Waiting, minutes: 10);
        // T-161: and a running timelapse ("Stop timelapse", same key as "Start timelapse").
        _recordings.SetTimelapse("mk4", CameraRecordingActivity.Recording);
        _recordings.SetTimelapse("c", CameraRecordingActivity.Recording);
        char?[] exportKeys = [.. Enumerable.Range(0, 2).Select(i => AccessKey(Item(viewModel, TrayMenuCommand.ExportTimelapseJpeg, i), info))];
        // T-165: and a running JPEG export ("Cancel JPEG export", same key as "Export timelapse to JPEG…").
        _recordings.SetTimelapseExport(new CameraTimelapseExportStatus(true, "mk4", "/rec/mk4/2026-11-04_10-00-00_timelapse.mp4", 5, 10));
        _dispatcher.RunAll();

        for (int i = 0; i < 2; i++)
        {
            TrayMenuEntryList submenu = viewModel.PrinterItems[i].Submenu!;
            Assert.Equal(11, submenu.Count);
            Assert.Equal(exportKeys[i], AccessKey(Item(viewModel, TrayMenuCommand.CancelTimelapseJpegExport, i), info));
            char?[] keys = [.. submenu.Where(static item => !item.IsSeparator).Select(item => AccessKey(item, info))];
            Assert.All(keys, key => Assert.True(key is { } k && char.IsLetterOrDigit(k), $"{language}: {string.Join(", ", keys)}"));
            Assert.True(keys.Distinct().Count() == keys.Length, $"{language}: access keys {string.Join(", ", keys)} are not unique.");
            Assert.Equal(startKeys[i], AccessKey(Item(viewModel, TrayMenuCommand.StopRecording, i), info));
            Assert.Equal(timelapseKeys[i], AccessKey(Item(viewModel, TrayMenuCommand.StopTimelapse, i), info));
            Assert.All(submenu.Where(static item => !item.IsSeparator), item => Assert.Equal(1, NativeMenuText.HeaderOf(item).Count(static c => c == '_')));
        }
    }

    [Fact]
    public async Task TheJpegExportItemAsksForAFileAndTurnsIntoCancelWithTheProgressForEveryPrinter()
    {
        using TrayMenuViewModel viewModel = Create(WithCamera(Definition()), WithCamera(Definition("b", "Beta")));
        using TrayMenuPresenter presenter = new(_target, viewModel, _commands, _logger);

        viewModel.RunRecordingCommand(Item(viewModel, TrayMenuCommand.ExportTimelapseJpeg, 1));
        Assert.Equal(["b"], _recordings.TimelapseExportsRequested);

        // From another thread, like the exporter raises it: one export for all printers.
        await Task.Run(() => _recordings.SetTimelapseExport(new CameraTimelapseExportStatus(true, "b", "/rec/Beta/2026-11-04_10-00-00_timelapse.mp4", 42, 100)), TestContext.Current.CancellationToken);
        _dispatcher.RunAll();

        for (int i = 0; i < 2; i++)
        {
            TrayMenuEntry cancel = Item(viewModel, TrayMenuCommand.CancelTimelapseJpegExport, i);
            Assert.Equal(("Cancel JPEG _export (42%)", true, "Exporting 2026-11-04_10-00-00_timelapse.mp4"), (cancel.Header, cancel.IsEnabled, cancel.ToolTip));
            Assert.DoesNotContain(viewModel.PrinterItems[i].Submenu!, static item => item.Command == TrayMenuCommand.ExportTimelapseJpeg);
        }

        viewModel.RunRecordingCommand(Item(viewModel, TrayMenuCommand.CancelTimelapseJpegExport));
        Assert.Equal(1, _recordings.TimelapseExportCancels);

        _recordings.SetTimelapseExport(CameraTimelapseExportStatus.Idle);
        _dispatcher.RunAll();
        Assert.Equal("_Export timelapse to JPEG… (experimental)", Item(viewModel, TrayMenuCommand.ExportTimelapseJpeg).Header);
    }

    [Fact]
    public void WithoutTheExportTheItemIsDisabledWithTheReason()
    {
        _recordings.IsTimelapseExportAvailable = false;
        using TrayMenuViewModel viewModel = Create(WithCamera(Definition()));

        TrayMenuEntry export = Item(viewModel, TrayMenuCommand.ExportTimelapseJpeg);
        Assert.Equal((false, Strings.TimelapseExportUnavailable), (export.IsEnabled, export.ToolTip));
        viewModel.RunRecordingCommand(export);
        Assert.Empty(_recordings.TimelapseExportsRequested);
    }

    [Fact]
    public void APrinterWithADashcamCanSaveTheLastMinutesWhileItBuffers()
    {
        using TrayMenuViewModel viewModel = Create(WithCamera(Definition()), WithCamera(Definition("b", "Beta")));
        Assert.DoesNotContain(viewModel.PrinterItems[0].Submenu!, static item => item.Command == TrayMenuCommand.SaveDashcam);

        _recordings.SetDashcam("mk4", CameraDashcamActivity.Buffering, minutes: 5);
        _dispatcher.RunAll();

        TrayMenuEntryList submenu = viewModel.PrinterItems[0].Submenu!;
        TrayMenuEntry save = Item(viewModel, TrayMenuCommand.SaveDashcam);
        Assert.Equal(("_Save the last 5\u00a0min", true, null, "mk4", true), (save.Header, save.IsEnabled, save.ToolTip, save.PrinterId, save.HasAccessKey));
        // Right below "Start recording".
        Assert.Equal(TrayMenuCommand.StartRecording, submenu[submenu.ToList().IndexOf(save) - 1].Command);
        Assert.DoesNotContain(viewModel.PrinterItems[1].Submenu!, static item => item.Command == TrayMenuCommand.SaveDashcam);
    }

    [Theory]
    [InlineData(CameraDashcamActivity.Waiting, false)]
    [InlineData(CameraDashcamActivity.Waiting, true)]
    [InlineData(CameraDashcamActivity.Saving, false)]
    public void WhileTheDashcamHoldsNothingTheItemIsDisabledWithTheReason(CameraDashcamActivity activity, bool wholePrintRecorded)
    {
        using TrayMenuViewModel viewModel = Create(WithCamera(Definition()));

        if (wholePrintRecorded)
        {
            _recordings.SetActivity("mk4", CameraRecordingActivity.Recording);
        }

        _recordings.SetDashcam("mk4", activity);
        _dispatcher.RunAll();

        TrayMenuEntry save = Item(viewModel, TrayMenuCommand.SaveDashcam);
        Assert.False(save.IsEnabled);
        Assert.Equal(
            activity == CameraDashcamActivity.Saving ? Strings.DashcamSaving : wholePrintRecorded ? Strings.DashcamWholePrintRecorded : Strings.DashcamNotPrinting,
            save.ToolTip);
    }

    [Fact]
    public void AClickSavesTheLastMinutes()
    {
        using TrayMenuViewModel viewModel = Create(WithCamera(Definition()));
        using TrayMenuPresenter presenter = new(_target, viewModel, _commands, _logger);
        _recordings.SetDashcam("mk4", CameraDashcamActivity.Buffering);
        _dispatcher.RunAll();

        _target.Click(Item(viewModel, TrayMenuCommand.SaveDashcam));
        viewModel.RunRecordingCommand(Item(viewModel, TrayMenuCommand.SaveDashcam) with { IsEnabled = false });

        Assert.Equal(["mk4"], _recordings.DashcamSaves);
        Assert.Empty(_recordings.Started);
        Assert.Empty(_logger.Entries);
    }

    // ---- T-161: timelapse ----

    [Fact]
    public async Task TheTimelapseItemFollowsTheTimelapseInPlace()
    {
        using TrayMenuViewModel viewModel = Create(WithCamera(Definition()), WithCamera(Definition("b", "Beta")));
        TrayMenuEntry beta = viewModel.PrinterItems[1];
        using TrayMenuPresenter presenter = new(_target, viewModel, _commands, _logger);
        _target.Operations.Clear();

        await Task.Run(() => _recordings.SetTimelapse("mk4", CameraRecordingActivity.Recording), TestContext.Current.CancellationToken);
        _dispatcher.RunAll();

        TrayMenuEntry stop = Item(viewModel, TrayMenuCommand.StopTimelapse);
        Assert.Equal(("Stop _timelapse (experimental)", true, null), (stop.Header, stop.IsEnabled, stop.ToolTip));
        // The recording is not touched.
        Assert.Equal(TrayMenuCommand.StartRecording, Item(viewModel, TrayMenuCommand.StartRecording).Command);
        Assert.Equal(["update 0"], _target.Operations);
        Assert.Same(beta, viewModel.PrinterItems[1]);

        // Paused: still "Stop", with the tip that no frames are taken.
        _recordings.SetTimelapse("mk4", CameraRecordingActivity.Recording, capturing: false);
        _dispatcher.RunAll();
        stop = Item(viewModel, TrayMenuCommand.StopTimelapse);
        Assert.Equal((true, Strings.TimelapsePaused), (stop.IsEnabled, stop.ToolTip));

        _recordings.SetTimelapse("mk4", CameraRecordingActivity.Idle, lastFile: "/rec/mk4/2026-11-04_10-00-00_timelapse.mp4");
        _dispatcher.RunAll();
        Assert.Equal(TrayMenuCommand.StartTimelapse, Item(viewModel, TrayMenuCommand.StartTimelapse).Command);
        TrayMenuEntry play = Item(viewModel, TrayMenuCommand.PlayLastTimelapse);
        Assert.Equal((true, null), (play.IsEnabled, play.ToolTip));
        // "Play last recording" does not pick the timelapse (T-160).
        Assert.False(Item(viewModel, TrayMenuCommand.PlayLastRecording).IsEnabled);
    }

    [Theory]
    [InlineData(CameraRecordingActivity.Starting)]
    [InlineData(CameraRecordingActivity.Stopping)]
    public void WhileATimelapseStartsOrStopsTheItemIsDisabledWithTheReason(CameraRecordingActivity activity)
    {
        using TrayMenuViewModel viewModel = Create(WithCamera(Definition()));

        _recordings.SetTimelapse("mk4", activity);
        _dispatcher.RunAll();

        TrayMenuEntry item = Item(viewModel, TrayMenuCommand.StopTimelapse);
        Assert.False(item.IsEnabled);
        Assert.Equal(activity == CameraRecordingActivity.Starting ? Strings.TimelapseStarting : Strings.TimelapseStopping, item.ToolTip);
    }

    [Fact]
    public void WithoutTheTimelapseRecorderStartIsDisabledWithTheReason()
    {
        _recordings.IsTimelapseAvailable = false;
        using TrayMenuViewModel viewModel = Create(WithCamera(Definition()));

        TrayMenuEntry start = Item(viewModel, TrayMenuCommand.StartTimelapse);
        Assert.False(start.IsEnabled);
        Assert.Equal(Strings.TimelapseUnavailable, start.ToolTip);
        Assert.True(Item(viewModel, TrayMenuCommand.StartRecording).IsEnabled);
    }

    [Fact]
    public void TimelapseClicksActOnWhatTheItemShowed()
    {
        using TrayMenuViewModel viewModel = Create(WithCamera(Definition()));
        using TrayMenuPresenter presenter = new(_target, viewModel, _commands, _logger);
        TrayMenuEntry start = Item(viewModel, TrayMenuCommand.StartTimelapse);

        _target.Click(start);
        _recordings.SetTimelapse("mk4", CameraRecordingActivity.Recording, lastFile: "/rec/mk4/a_timelapse.mp4");
        _dispatcher.RunAll();
        // A Start of a menu built before the timelapse began starts again (the service ignores it), never stops.
        _target.Click(start);
        _target.Click(Item(viewModel, TrayMenuCommand.StopTimelapse));
        _target.Click(Item(viewModel, TrayMenuCommand.PlayLastTimelapse));
        viewModel.RunRecordingCommand(Item(viewModel, TrayMenuCommand.StopTimelapse) with { IsEnabled = false });

        Assert.Equal(["mk4", "mk4"], _recordings.TimelapsesStarted);
        Assert.Equal(["mk4"], _recordings.TimelapsesStopped);
        Assert.Equal(["mk4"], _recordings.TimelapsesPlayed);
        Assert.Empty(_recordings.Started);
        Assert.Empty(_recordings.Stopped);
        Assert.Empty(_recordings.Played);
        Assert.Empty(_logger.Entries);
    }

    private static char? AccessKey(TrayMenuEntry item, CultureInfo culture) =>
        NativeMenuText.AccessKeyOf(NativeMenuText.HeaderOf(item)) is { } key ? char.ToUpper(key, culture) : null;
}
