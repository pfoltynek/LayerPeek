using System.Globalization;

using Microsoft.Extensions.Logging.Abstractions;

using LayerPeek.App.Localization;
using LayerPeek.App.Previews;
using LayerPeek.App.Tests.Previews;
using LayerPeek.App.Tray;
using LayerPeek.Core.History;
using LayerPeek.Core.Previews;
using LayerPeek.Core.Printers;
using LayerPeek.Simulator.Printers;

using static LayerPeek.App.Tests.Tray.TestPrinters;

namespace LayerPeek.App.UiTests.Readme;

/// <summary>
/// The print shown in every README screenshot: a CORE One printing a PLA part from the simulator's files (with the G-code
/// preview the simulator generates for it) at about 60 %, at realistic PLA temperatures with the history of the print so
/// far. Neutral names only: no IP address, serial number or personal data.
/// </summary>
internal static class ReadmeScenario
{
    public const string PrinterName = "CORE One";

    /// <summary>A host name instead of an IP address (the settings window shows the address).</summary>
    public const string PrinterAddress = "http://prusa-coreone.local";

    public const string SecondPrinterName = "MK4S";

    public const string SecondPrinterAddress = "http://prusa-mk4s.local";

    public const string ThirdPrinterName = "MINI+";

    public const string ThirdPrinterAddress = "http://prusa-mini.local";

    /// <summary>What PrusaLink tells about the CORE One (the diagnostics window).</summary>
    public static readonly LayerPeek.PrusaLink.PrusaLinkPrinterInfo Info = new(
        Hostname: "prusa-coreone",
        Firmware: "6.4.0",
        ApiVersion: "2.0.0",
        PrinterModel: "7.1.0",
        NozzleDiameter: 0.4,
        HasMmu: false);

    /// <summary>The CORE One (model code 7.1.0, its picture in the settings and diagnostics windows).</summary>
    public static readonly PrinterModelCode CoreOne = new(7, 1, 0);

    public static readonly TimeSpan Elapsed = TimeSpan.FromMinutes(97);

    public static readonly TimeSpan Remaining = TimeSpan.FromMinutes(64);

    public const double Progress = 60.3;

    public const double NozzleTarget = 215;

    public const double BedTarget = 60;

    /// <summary>Polling interval of the recorded history (the default of the app).</summary>
    public static readonly TimeSpan HistoryInterval = LayerPeek.Core.Settings.AppSettings.DefaultPollingInterval;

    /// <summary>The time zone of the shown clock times, the one of the diagnostics window's fake clock (UTC+2).</summary>
    public static TimeZoneInfo TimeZone => LayerPeek.App.Tests.Diagnostics.FixedClock.Zone;

    /// <summary>
    /// The print file: the cover of the Makita battery slot, the flat part the recorded camera shows under the nozzle
    /// (PLA, 0.2 mm). The same file name in every language.
    /// </summary>
    public const string FileName = "makita-battery-cover_0.4n_0.2mm_PLA_COREONE_2h41m.bgcode";

    /// <summary>
    /// The G-code preview of the print: the one the simulator generates for its first file (a flat disc in Prusa
    /// orange; the simulator's previews are all flat discs and differ only in colour). No asset of its own.
    /// </summary>
    public static SimulatedFile PreviewFile { get; } = PrinterSimulation.AllFiles[0];

    private const string PreviewPath = "usb/MAKITA~1.BGC";

    /// <summary>The snapshot of the print for the printer <paramref name="printerId"/> at <see cref="TestPrinters.Now"/>.</summary>
    public static PrinterSnapshot Printing(string printerId, DateTimeOffset? timestamp = null) => new()
    {
        PrinterId = printerId,
        State = PrinterState.Printing,
        Timestamp = timestamp ?? Now,
        Job = new PrintJob
        {
            Id = 42,
            FileName = FileName,
            ProgressPercent = Progress,
            Elapsed = Elapsed,
            Remaining = Remaining,
            PreviewReference = new TestPreviewReference(PreviewPath),
        },
        Telemetry = new Telemetry
        {
            Nozzle = new Temperature(215.2, NozzleTarget),
            Bed = new Temperature(60.1, BedTarget),
            HotendFanRpm = 7350,
            PrintFanRpm = 4980,
            SpeedPercent = 100,
            FlowPercent = 100,
            AxisZ = 23.45,
        },
    };

    /// <summary>
    /// The temperatures since the print started (<see cref="Elapsed"/> ago) plus a few idle minutes before: the bed heats
    /// up first, then the nozzle; both then hold their targets with the small wobble of a real regulator.
    /// </summary>
    public static void RecordHistory(TelemetryHistory history, string printerId)
    {
        ArgumentNullException.ThrowIfNull(history);
        DateTimeOffset start = Now - Elapsed;
        for (DateTimeOffset time = start - TimeSpan.FromMinutes(5); time <= Now; time += HistoryInterval)
        {
            double minutes = (time - start).TotalMinutes;
            bool heating = minutes >= 0;
            double bed = !heating ? 24 : Math.Min(BedTarget, 24 + (12 * minutes)) + (minutes > 3 ? Wobble(minutes, 0.3, 2.9) : 0);
            double nozzle = !heating ? 26 : minutes < 3 ? 26 + (34 * minutes)
                : Math.Min(NozzleTarget, 128 + (45 * (minutes - 3))) + (minutes > 5.5 ? Wobble(minutes, 0.8, 1.6) : 0);
            history.Record(new PrinterSnapshot
            {
                PrinterId = printerId,
                State = heating ? PrinterState.Printing : PrinterState.Idle,
                Timestamp = time,
                Telemetry = new Telemetry
                {
                    Nozzle = new Temperature(nozzle, heating ? NozzleTarget : 0),
                    Bed = new Temperature(bed, heating ? BedTarget : 0),
                },
            });
        }
    }

    /// <summary>
    /// The deviation of a regulated temperature from its target after <paramref name="minutes"/>: a few slow waves of
    /// incommensurate periods (no visible pattern), within ± <paramref name="amplitude"/>; deterministic.
    /// </summary>
    private static double Wobble(double minutes, double amplitude, double period) =>
        amplitude * ((0.5 * Math.Sin(minutes / period)) + (0.3 * Math.Sin((minutes / (period * 2.3)) + 1.1)) + (0.2 * Math.Sin((minutes / (period * 0.6)) + 2.3)));

    /// <summary>The G-code previews of the print (the simulator's PNG), decoded by the app like the real ones.</summary>
    public static PrintThumbnails Thumbnails()
    {
        FakePreviewSource source = new();
        source.Previews[PreviewPath] = PrintPreview.FromPng(PreviewImages.For(PreviewFile, large: true));
        return new PrintThumbnails(source, new AvaloniaPrintThumbnailDecoder(), new AvaloniaUiDispatcher(), new Microsoft.Extensions.Time.Testing.FakeTimeProvider(Now), NullLogger<PrintThumbnails>.Instance);
    }

    /// <summary>
    /// Switches the texts to <paramref name="language"/> ("en" or "cs") and the number, date and time formats to its
    /// country (en-US, cs-CZ), like the app on a system in that language. The session restores both after the test.
    /// </summary>
    public static void UseLanguage(string language)
    {
        CultureInfo culture = CultureInfo.GetCultureInfo(language == "cs" ? "cs-CZ" : "en-US");
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        if (language != "en")
        {
            Assert.True(new UiLanguage().Apply(language));
        }
    }
}
