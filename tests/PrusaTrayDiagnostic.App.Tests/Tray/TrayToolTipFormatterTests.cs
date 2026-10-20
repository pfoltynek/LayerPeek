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

        Assert.Equal("MK4\nTiskne – 45\u00A0%\nZbývá 1 h 23 min\nbenchy.bgcode", text);
    }

    [Fact]
    public void UnsupportedCultureFallsBackToEnglish()
    {
        using CultureScope culture = new("ja-JP");

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
    public void SummaryShowsOneLinePerPrinterMostSevereFirstInEnglish()
    {
        using CultureScope culture = new("en-US");

        string text = TrayToolTipFormatter.FormatSummary(
        [
            Content(PrinterState.Printing, Job(45.6, OneHour23), "Alpha"),
            Content(PrinterState.Idle, name: "Beta"),
            Content(PrinterState.Error, name: "Gamma"),
        ]);

        Assert.Equal("Gamma: Error\nAlpha: Printing – 45%, 1 h 23 min left\nBeta: Ready", text);
    }

    [Fact]
    public void SummaryShowsOneLinePerPrinterMostSevereFirstInCzech()
    {
        using CultureScope culture = new("cs-CZ");

        string text = TrayToolTipFormatter.FormatSummary(
        [
            Content(PrinterState.Printing, Job(45.6, OneHour23), "Alpha"),
            Content(PrinterState.Paused, Job(10), "Beta"),
            Content(state: null, name: "Gamma"),
        ]);

        Assert.Equal("Gamma: Připojování…\nBeta: Pozastaveno – 10\u00A0%\nAlpha: Tiskne – 45\u00A0%, zbývá 1 h 23 min", text);
    }

    [Fact]
    public void SummaryKeepsConfigurationOrderOnTie()
    {
        using CultureScope culture = new("en-US");

        string text = TrayToolTipFormatter.FormatSummary(
        [
            Content(PrinterState.Offline, name: "A"),
            Content(PrinterState.Unknown, name: "B"),
            Content(state: null, name: "C"),
            Content(PrinterState.Idle, name: "D"),
            Content(PrinterState.Stopped, name: "E"),
        ]);

        Assert.Equal("A: Offline\nB: Unknown state\nC: Connecting…\nD: Ready\nE: Stopped", text);
    }

    [Fact]
    public void SummaryDoesNotShowStaleJobOfUnreachablePrinter()
    {
        using CultureScope culture = new("en-US");

        string text = TrayToolTipFormatter.FormatSummary([Content(PrinterState.Offline, Job(50, OneHour23), "A"), Content(PrinterState.Idle, name: "B")]);

        Assert.Equal("A: Offline\nB: Ready", text);
    }

    [Fact]
    public void SummaryDropsRemainingTimesWhenTheyDoNotFit()
    {
        using CultureScope culture = new("en-US");
        ToolTipContent[] printers = [.. Enumerable.Range(1, 4).Select(i => Content(PrinterState.Printing, Job(10 * i, OneHour23), "Printer" + i))];

        string text = TrayToolTipFormatter.FormatSummary(printers);

        Assert.Equal("Printer1: Printing – 10%\nPrinter2: Printing – 20%\nPrinter3: Printing – 30%\nPrinter4: Printing – 40%", text);
    }

    [Fact]
    public void SummaryOfManyPrintersShowsMostSevereAndCountsTheRest()
    {
        using CultureScope culture = new("en-US");
        List<ToolTipContent> printers = [.. Enumerable.Range(1, 18).Select(i => Content(PrinterState.Idle, name: "Idle" + i))];
        printers.Insert(9, Content(PrinterState.Error, name: "Broken"));
        printers.Add(Content(PrinterState.Printing, Job(50), "Busy"));

        string text = TrayToolTipFormatter.FormatSummary(printers);

        Assert.True(text.Length <= TrayToolTipFormatter.MaxLength, $"Length {text.Length}");
        string[] lines = text.Split('\n');
        Assert.Equal("Broken: Error", lines[0]);
        Assert.Equal("Busy: Printing – 50%", lines[1]);
        Assert.Equal("Idle1: Ready", lines[2]);
        int shown = lines.Length - 1;
        Assert.Equal($"Other printers: {printers.Count - shown}", lines[^1]);
    }

    [Fact]
    public void SummaryOfTwentyPrintersInCzechFitsTheLimit()
    {
        using CultureScope culture = new("cs-CZ");
        ToolTipContent[] printers = [.. Enumerable.Range(1, 20).Select(i => Content(PrinterState.Printing, Job(i, OneHour23), "Tiskárna " + i))];

        string text = TrayToolTipFormatter.FormatSummary(printers);

        Assert.True(text.Length <= TrayToolTipFormatter.MaxLength, $"Length {text.Length}");
        Assert.StartsWith("Tiskárna 1: Tiskne – 1\u00A0%\n", text, StringComparison.Ordinal);
        Assert.Matches(@"\nDalší tiskárny: \d+$", text);
    }

    [Fact]
    public void SummaryShortensLongNamesPerLine()
    {
        using CultureScope culture = new("en-US");

        string text = TrayToolTipFormatter.FormatSummary([Content(PrinterState.Idle, name: new string('A', 200)), Content(PrinterState.Idle, name: "B")]);

        string[] lines = text.Split('\n');
        Assert.Equal(2, lines.Length);
        Assert.Equal(new string('A', TrayToolTipFormatter.MaxSummaryNameLength - 1) + "…: Ready", lines[0]);
        Assert.Equal("B: Ready", lines[1]);
    }

    [Fact]
    public void SummaryNeverSplitsSurrogatePairs()
    {
        using CultureScope culture = new("en-US");
        string horses = string.Concat(Enumerable.Repeat("🐎", 40));
        ToolTipContent[] printers = [.. Enumerable.Range(0, 12).Select(i => Content(PrinterState.Printing, Job(i, OneHour23), new string('x', 1 + (i % 2)) + horses))];

        string text = TrayToolTipFormatter.FormatSummary(printers);

        Assert.True(text.Length <= TrayToolTipFormatter.MaxLength, $"Length {text.Length}");
        for (int i = 0; i < text.Length; i++)
        {
            Assert.False(char.IsHighSurrogate(text[i]) && (i + 1 == text.Length || !char.IsLowSurrogate(text[i + 1])), $"Dangling high surrogate at {i}.");
            Assert.False(char.IsLowSurrogate(text[i]) && (i == 0 || !char.IsHighSurrogate(text[i - 1])), $"Dangling low surrogate at {i}.");
        }
    }

    [Theory]
    [InlineData(2)]
    [InlineData(5)]
    [InlineData(10)]
    [InlineData(20)]
    [InlineData(100)]
    public void SummaryAlwaysFitsWindowsLimitAndMentionsEveryPrinter(int count)
    {
        using CultureScope culture = new("cs-CZ");
        ToolTipContent[] printers = [.. Enumerable.Range(0, count).Select(i => Content(PrinterState.Paused, Job(i % 100, TimeSpan.FromDays(3)), new string('N', 60) + i))];

        string text = TrayToolTipFormatter.FormatSummary(printers);

        Assert.True(text.Length <= TrayToolTipFormatter.MaxLength, $"Length {text.Length}");
        string[] lines = text.Split('\n');
        int other = lines[^1].StartsWith("Další tiskárny: ", StringComparison.Ordinal) ? int.Parse(lines[^1]["Další tiskárny: ".Length..], System.Globalization.CultureInfo.InvariantCulture) : 0;
        int printerLines = other > 0 ? lines.Length - 1 : lines.Length;
        Assert.True(printerLines >= 1);
        Assert.Equal(count, printerLines + other);
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

    private static ToolTipContent Content(PrinterState? state, PrintJob? job = null, string name = "MK4") =>
        ToolTipContent.From(Printer(state, job, id: name, name: name), otherPrinterCount: 0);
}
