using LayerPeek.App.Tests.Cameras.Recording;
using LayerPeek.App.Tray;
using LayerPeek.Core.Cameras.Recording;
using LayerPeek.Core.Printers;

using static LayerPeek.App.Tests.Tray.TestPrinters;

namespace LayerPeek.App.Tests.Tray;

/// <summary>
/// "Recording" in the tray tooltip (T-146, user decision 2026-11-03; the icon does not change): a line of its own for a single
/// printer, ", recording" after the state in the multi-printer tooltip (kept when the remaining times are dropped). Follows
/// the recording events (any thread) and the state when the printers are set (recordings outlive a restart of monitoring).
/// </summary>
public sealed class TrayRecordingToolTipTests : IDisposable
{
    private readonly CultureScope _culture = new("en-US");
    private readonly FakePrinterMonitor _monitor = new();
    private readonly ManualDispatcher _dispatcher = new();
    private readonly FakeCameraRecordings _recordings = new();
    private readonly TrayStatusViewModel _viewModel;

    public TrayRecordingToolTipTests() => _viewModel = new TrayStatusViewModel(_monitor, _dispatcher, _recordings);

    public void Dispose()
    {
        _viewModel.Dispose();
        _culture.Dispose();
    }

    [Fact]
    public async Task ASinglePrinterGetsARecordingLineAndTheIconStaysTheSame()
    {
        _viewModel.SetPrinters([Definition()]);
        _monitor.Raise(Snapshot(PrinterState.Printing, Job(42, TimeSpan.FromMinutes(10))));
        _dispatcher.RunAll();
        TrayIconKey icon = _viewModel.IconKey;
        Assert.Equal("MK4\nPrinting – 42%\n10 min remaining\nbenchy.bgcode", _viewModel.ToolTipText);

        await Task.Run(() => _recordings.SetActivity("mk4", CameraRecordingActivity.Recording), TestContext.Current.CancellationToken);
        _dispatcher.RunAll();

        Assert.Equal("MK4\nPrinting – 42%\nRecording\n10 min remaining\nbenchy.bgcode", _viewModel.ToolTipText);
        Assert.Equal(icon, _viewModel.IconKey);

        // A later snapshot keeps the line.
        _monitor.Raise(Snapshot(PrinterState.Printing, Job(43, TimeSpan.FromMinutes(9))));
        _dispatcher.RunAll();
        Assert.Contains("\nRecording\n", _viewModel.ToolTipText, StringComparison.Ordinal);

        _recordings.SetActivity("mk4", CameraRecordingActivity.Idle);
        _dispatcher.RunAll();
        Assert.DoesNotContain("Recording", _viewModel.ToolTipText, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(CameraRecordingActivity.Starting)]
    [InlineData(CameraRecordingActivity.Stopping)]
    public void StartingAndStoppingCountAsRecording(CameraRecordingActivity activity)
    {
        _viewModel.SetPrinters([Definition()]);
        _recordings.SetActivity("mk4", activity);
        _dispatcher.RunAll();

        Assert.Equal("MK4\nConnecting…\nRecording", _viewModel.ToolTipText);
    }

    [Fact]
    public void SeveralPrintersSayItAfterTheState()
    {
        _viewModel.SetPrinters([Definition("a", "Alpha"), Definition("b", "Beta")]);
        _monitor.Raise(Snapshot(PrinterState.Printing, Job(45, TimeSpan.FromMinutes(83)), "a"));
        _monitor.Raise(Snapshot(PrinterState.Idle, printerId: "b"));
        _recordings.SetActivity("a", CameraRecordingActivity.Recording);
        _recordings.SetActivity("b", CameraRecordingActivity.Recording);
        _dispatcher.RunAll();

        Assert.Equal("Alpha: Printing – 45%, recording, 1 h 23 min left\nBeta: Ready, recording", _viewModel.ToolTipText);
    }

    [Fact]
    public void ARunningRecordingShowsWhenThePrintersAreSet()
    {
        _recordings.SetActivity("mk4", CameraRecordingActivity.Recording, raise: false);

        _viewModel.SetPrinters([Definition()]);
        _dispatcher.RunAll();

        Assert.Equal("MK4\nConnecting…\nRecording", _viewModel.ToolTipText);
    }

    [Fact]
    public void AnUnchangedOrUnknownRecordingPostsNothing()
    {
        _viewModel.SetPrinters([Definition()]);
        _dispatcher.RunAll();
        int posts = _dispatcher.PostCount;

        _recordings.Raise("mk4");
        _recordings.SetActivity("other", CameraRecordingActivity.Recording);

        Assert.Equal(posts, _dispatcher.PostCount);
    }

    [Fact]
    public void DisposeUnsubscribes()
    {
        Assert.Equal(1, _recordings.ChangedSubscribers);

        _viewModel.Dispose();

        Assert.Equal(0, _recordings.ChangedSubscribers);
    }

    [Fact]
    public void TheFormatterKeepsTheRecordingWhenTheRemainingTimesDoNotFit()
    {
        string longName = new('x', 24);
        ToolTipContent[] printers =
        [
            .. Enumerable.Range(0, 3).Select(i => new ToolTipContent(longName + i, PrinterState.Printing, 50, 600, null, 0, IsRecording: i == 0)),
        ];

        string text = TrayToolTipFormatter.FormatSummary(printers);

        Assert.True(text.Length <= TrayToolTipFormatter.MaxLength);
        Assert.DoesNotContain("left", text, StringComparison.Ordinal);
        Assert.Contains(", recording", text, StringComparison.Ordinal);
    }
}
