using PrusaTrayDiagnostic.App.Tray;
using PrusaTrayDiagnostic.Core.Printers;

using static PrusaTrayDiagnostic.App.Tests.Tray.TestPrinters;

namespace PrusaTrayDiagnostic.App.Tests.Tray;

public sealed class TrayToolTipFormatterTests
{
    private static readonly TimeSpan OneHour23 = new(1, 23, 0);

    [Fact]
    public void PrintingShowsNameStateProgressRemainingAndFileInEnglish()
    {
        using CultureScope culture = new("en-US");

        string text = TrayToolTipFormatter.Format(Printer(PrinterState.Printing, Job(45.6, OneHour23)), otherPrinterCount: 0);

        Assert.Equal("MK4\nPrinting – 45%\n1 h 23 min remaining\nbenchy.bgcode", text);
    }

    [Fact]
    public void PrintingShowsNameStateProgressRemainingAndFileInCzech()
    {
        using CultureScope culture = new("cs-CZ");

        string text = TrayToolTipFormatter.Format(Printer(PrinterState.Printing, Job(45.6, OneHour23)), otherPrinterCount: 0);

        Assert.Equal("MK4\nTiskne – 45 %\nZbývá 1 h 23 min\nbenchy.bgcode", text);
    }

    [Fact]
    public void UnsupportedCultureFallsBackToEnglish()
    {
        using CultureScope culture = new("de-DE");

        string text = TrayToolTipFormatter.Format(Printer(PrinterState.Idle), otherPrinterCount: 0);

        Assert.Equal("MK4\nReady", text);
    }

    [Fact]
    public void ProgressIsRoundedDownSoUnfinishedPrintNeverShowsHundred()
    {
        using CultureScope culture = new("en-US");

        string text = TrayToolTipFormatter.Format(Printer(PrinterState.Printing, Job(99.95, fileName: null)), otherPrinterCount: 0);

        Assert.Equal("MK4\nPrinting – 99%", text);
    }

    [Fact]
    public void PrintingWithoutJobShowsOnlyState()
    {
        using CultureScope culture = new("en-US");

        Assert.Equal("MK4\nPrinting", TrayToolTipFormatter.Format(Printer(PrinterState.Printing), otherPrinterCount: 0));
    }

    [Fact]
    public void PausedShowsProgressAndRemaining()
    {
        using CultureScope culture = new("en-US");

        string text = TrayToolTipFormatter.Format(Printer(PrinterState.Paused, Job(10, TimeSpan.FromMinutes(5))), otherPrinterCount: 0);

        Assert.Equal("MK4\nPaused – 10%\n5 min remaining\nbenchy.bgcode", text);
    }

    [Fact]
    public void FinishedShowsFileButNoProgressOrRemaining()
    {
        using CultureScope culture = new("en-US");

        string text = TrayToolTipFormatter.Format(Printer(PrinterState.Finished, Job(100, TimeSpan.Zero)), otherPrinterCount: 0);

        Assert.Equal("MK4\nFinished\nbenchy.bgcode", text);
    }

    [Theory]
    [InlineData(PrinterState.Offline, "Offline")]
    [InlineData(PrinterState.Unknown, "Unknown state")]
    [InlineData(PrinterState.Unauthorized, "Invalid credentials")]
    [InlineData(PrinterState.Error, "Error")]
    [InlineData(PrinterState.Attention, "Needs attention")]
    [InlineData(PrinterState.Busy, "Busy")]
    [InlineData(PrinterState.Stopped, "Stopped")]
    public void ShowsStateName(PrinterState state, string expected)
    {
        using CultureScope culture = new("en-US");

        Assert.Equal($"MK4\n{expected}", TrayToolTipFormatter.Format(Printer(state), otherPrinterCount: 0));
    }

    [Fact]
    public void UndefinedStateIsShownAsUnknown()
    {
        using CultureScope culture = new("en-US");

        Assert.Equal("MK4\nUnknown state", TrayToolTipFormatter.Format(Printer((PrinterState)999), otherPrinterCount: 0));
    }

    [Fact]
    public void PrinterWithoutSnapshotIsConnecting()
    {
        using CultureScope culture = new("en-US");

        Assert.Equal("MK4\nConnecting…", TrayToolTipFormatter.Format(Printer(state: null), otherPrinterCount: 0));
    }

    [Fact]
    public void OfflinePrinterDoesNotShowStaleJob()
    {
        using CultureScope culture = new("en-US");

        Assert.Equal("MK4\nOffline", TrayToolTipFormatter.Format(Printer(PrinterState.Offline, Job(50, OneHour23)), otherPrinterCount: 0));
    }

    [Fact]
    public void RemainingIsExtrapolatedWhenPrinterReportsNone()
    {
        using CultureScope culture = new("en-US");
        PrintJob job = new() { ProgressPercent = 50, Elapsed = TimeSpan.FromMinutes(30), FileName = "a.gcode" };

        string text = TrayToolTipFormatter.Format(Printer(PrinterState.Printing, job), otherPrinterCount: 0);

        Assert.Equal("MK4\nPrinting – 50%\n30 min remaining\na.gcode", text);
    }

    [Fact]
    public void OtherPrintersAreCounted()
    {
        using CultureScope culture = new("en-US");

        Assert.Equal("MK4\nReady\nOther printers: 2", TrayToolTipFormatter.Format(Printer(PrinterState.Idle), otherPrinterCount: 2));
    }

    [Theory]
    [InlineData(0, "< 1 min")]
    [InlineData(20, "1 min")]
    [InlineData(60, "1 min")]
    [InlineData(61, "2 min")]
    [InlineData(59 * 60, "59 min")]
    [InlineData(60 * 60, "1 h 0 min")]
    [InlineData((23 * 3600) + (59 * 60) + 1, "1 d 0 h")]
    [InlineData((2 * 86400) + (5 * 3600), "2 d 5 h")]
    public void FormatsDurationRoundedUpToMinutes(int seconds, string expected)
    {
        using CultureScope culture = new("en-US");

        Assert.Equal(expected, TrayToolTipFormatter.FormatDuration(TimeSpan.FromSeconds(seconds)));
    }

    [Fact]
    public void FormatsHugeDurationWithoutOverflow()
    {
        using CultureScope culture = new("en-US");

        string text = TrayToolTipFormatter.FormatDuration(TimeSpan.MaxValue);

        Assert.EndsWith(" h", text, StringComparison.Ordinal);
    }

    [Fact]
    public void LongFileNameIsShortenedToFitWindowsLimit()
    {
        using CultureScope culture = new("en-US");
        string fileName = new string('x', 300) + ".bgcode";

        string text = TrayToolTipFormatter.Format(Printer(PrinterState.Printing, Job(45, OneHour23, fileName)), otherPrinterCount: 0);

        Assert.True(text.Length <= TrayToolTipFormatter.MaxLength, $"Length {text.Length}");
        Assert.StartsWith("MK4\nPrinting – 45%\n1 h 23 min remaining\nxxx", text, StringComparison.Ordinal);
        Assert.EndsWith("…", text, StringComparison.Ordinal);
        Assert.Equal(TrayToolTipFormatter.MaxLength, text.Length);
    }

    [Fact]
    public void LongPrinterNameIsTruncated()
    {
        using CultureScope culture = new("en-US");
        string name = new('N', 200);

        string text = TrayToolTipFormatter.Format(Printer(PrinterState.Printing, Job(45, OneHour23), name: name), otherPrinterCount: 0);

        Assert.Equal(TrayToolTipFormatter.MaxLength, text.Length);
        Assert.EndsWith("…", text, StringComparison.Ordinal);
    }

    [Fact]
    public void TruncationNeverSplitsSurrogatePair()
    {
        using CultureScope culture = new("en-US");
        // 4 + 18 + 1 = 23 chars before the file name line; emoji take two chars each.
        string fileName = string.Concat(Enumerable.Repeat("🐎", 100));

        string text = TrayToolTipFormatter.Format(Printer(PrinterState.Printing, Job(45, fileName: fileName)), otherPrinterCount: 0);

        Assert.True(text.Length <= TrayToolTipFormatter.MaxLength);
        Assert.False(char.IsHighSurrogate(text[^2]), "A dangling high surrogate precedes the ellipsis.");
    }

    [Fact]
    public void ApplicationLevelTexts()
    {
        using (new CultureScope("en-US"))
        {
            Assert.Equal("Prusa Tray Diagnostic\nStarting…", TrayToolTipFormatter.Starting());
            Assert.Equal("Prusa Tray Diagnostic\nNo printer configured", TrayToolTipFormatter.NotConfigured());
            Assert.Equal("Prusa Tray Diagnostic\nMonitoring stopped due to an error", TrayToolTipFormatter.MonitoringFailed());
        }

        using (new CultureScope("cs-CZ"))
        {
            Assert.Equal("Prusa Tray Diagnostic\nNení nastavena žádná tiskárna", TrayToolTipFormatter.NotConfigured());
        }
    }
}
