using System.Globalization;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;

using LayerPeek.App.Cameras;
using LayerPeek.App.Cameras.Mini;
using LayerPeek.App.Cameras.Overlay;
using LayerPeek.App.Localization;
using LayerPeek.App.Tests.Cameras;
using LayerPeek.App.UiTests.Infrastructure;
using LayerPeek.App.Windows;
using LayerPeek.Core.Cameras;
using LayerPeek.Core.Printers;
using LayerPeek.Core.Settings;

using static LayerPeek.App.Tests.Tray.TestPrinters;

namespace LayerPeek.App.UiTests.Cameras;

/// <summary>
/// The finish time and the cool-down variants of the mini preview strip's summary row (T-103, design 3) in the real camera
/// window: the row draws the first variant that fits whole in the current font (here the bundled Inter), never trims the
/// finish time, keeps 8 DIP from the toggles and its height of 30. The fake clock of the windows is in UTC;
/// <see cref="Now"/> is Friday 2026-10-16 12:00.
/// </summary>
public sealed class CameraWindowMiniStripFinishUiTests(HeadlessSession session)
{
    private const uint Grey = 0xFF406080;
    private const double Strip = 33;
    private const double PollSeconds = 5;

    private static readonly CameraWindowDefaults MiniFirst = CameraWindowDefaults.Default with { StartMode = CameraWindowStartMode.Mini };

    /// <summary>The region (numbers and times) that belongs to each app language.</summary>
    private static readonly Dictionary<string, string> Regions = new(StringComparer.Ordinal)
    {
        ["en"] = "en-US", ["cs"] = "cs-CZ", ["sk"] = "sk-SK", ["de"] = "de-DE", ["es"] = "es-ES", ["fr"] = "fr-FR", ["it"] = "it-IT", ["pl"] = "pl-PL",
    };

    private enum Phase
    {
        SameDay,
        AnotherDay,
        Cooling,
        Cooled,
    }

    private static CameraViewModel ViewModel(CameraWindow window) => (CameraViewModel)window.DataContext!;

    private static Rect BoundsIn(Control control, Visual root) =>
        new(control.TranslatePoint(default, root) ?? throw new InvalidOperationException("Not in the window."), control.Bounds.Size);

    /// <summary>Printing: 2 h 35 min left (the same day) or 19 h 50 min (Saturday morning).</summary>
    private static PrinterSnapshot Printing(double seconds, Phase phase) =>
        Snapshot(PrinterState.Printing, Job(19.9, phase == Phase.SameDay ? TimeSpan.FromMinutes(154.2) : TimeSpan.FromMinutes(1189.2)), "a") with
        {
            Timestamp = Now.AddSeconds(seconds),
            Telemetry = Hot(215, 215),
        };

    private static PrinterSnapshot Ended(double seconds, double nozzle) =>
        Snapshot(PrinterState.Finished, Job(100), "a") with { Timestamp = Now.AddSeconds(seconds), Telemetry = Hot(nozzle, 0) };

    /// <summary>A nozzle that cools like a real one (Newton's law), so that the monitor soon has an estimate.</summary>
    private static double Newton(double seconds) => 23 + (192 * Math.Exp(-seconds / 60));

    private static Telemetry Hot(double nozzle, double target) => new() { Nozzle = new Temperature(nozzle, target), Bed = new Temperature(55, 0) };

    private static void UseLanguage(CameraHarness harness, string language, string region)
    {
        CultureInfo culture = CultureInfo.GetCultureInfo(region);
        // The body's culture and the default of the thread (work posted to the UI thread); the session restores both.
        CultureInfo.CurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentCulture = culture;
        Assert.True(new UiLanguage().Apply(language));
        harness.Windows.RefreshTexts();
    }

    private static async Task<(CameraHarness Harness, CameraWindow Window)> OpenAsync(double width)
    {
        CameraHarness harness = new(screens: FakeScreenCatalog.ThreeMonitors(), window: MiniFirst);
        harness.Time.SetUtcNow(Now);
        harness.Time.SetLocalTimeZone(TimeZoneInfo.Utc);
        (CameraWindow window, _) = await harness.OpenStreamingAsync(1280, 720, Grey);
        CameraHarness.ResizeMiniWithGrip(window, width, 600);
        Assert.Equal(width, window.ClientSize.Width);
        return (harness, window);
    }

    private static void Show(CameraHarness harness, CameraWindow window, PrinterSnapshot snapshot)
    {
        harness.Monitor.Raise(snapshot);
        Dispatcher.UIThread.RunJobs();
        window.Settle();
    }

    /// <summary>The print, its end and readings every 5 s from <paramref name="t"/> until the monitor has an estimate.</summary>
    private static double CoolUntilEstimate(CameraHarness harness, CameraWindow window, double t)
    {
        Show(harness, window, Printing(t, Phase.SameDay));
        double start = t;
        for (t += PollSeconds; harness.Cooldown.GetState("a").EstimatedRemaining is null; t += PollSeconds)
        {
            Assert.True(t - start < 120, "no estimate");
            Show(harness, window, Ended(t, Newton(t - start - PollSeconds)));
        }

        Assert.True(ViewModel(window).MiniStrip.ShowsCoolingIcon);
        Assert.NotNull(ViewModel(window).MiniStrip.ShownContent.Minutes);
        return t;
    }

    private static double CoolDown(CameraHarness harness, CameraWindow window, double t)
    {
        foreach (double nozzle in new[] { 70.0, 45.0, 40.0 })
        {
            Show(harness, window, Ended(t, nozzle));
            t += PollSeconds;
        }

        Assert.True(ViewModel(window).MiniStrip.ShowsCooledIcon);
        return t;
    }

    [Theory]
    [InlineData(240)]
    [InlineData(320)]
    [InlineData(400)]
    public Task FirstVariantThatFitsIsDrawnWholeInEveryLanguage(double width) =>
        session.RunAsync(async scope =>
        {
            (CameraHarness harness, CameraWindow window) = await OpenAsync(width);
            await using CameraHarness _ = harness;
            Dictionary<(string Language, string Region, Phase Phase), int> shown = [];
            double t = 0;
            foreach (string language in AppLanguages.Supported)
            {
                foreach (string region in new[] { Regions[language], "en-US" }.Distinct())
                {
                    UseLanguage(harness, language, region);
                    foreach (Phase phase in new[] { Phase.SameDay, Phase.AnotherDay })
                    {
                        Show(harness, window, Printing(t, phase));
                        t += PollSeconds;
                        shown[(language, region, phase)] = AssertRow(window, $"{language} {region} {width} {phase}");
                    }

                    t = CoolUntilEstimate(harness, window, t);
                    shown[(language, region, Phase.Cooling)] = AssertRow(window, $"{language} {region} {width} cooling");
                    t = CoolDown(harness, window, t);
                    shown[(language, region, Phase.Cooled)] = AssertRow(window, $"{language} {region} {width} cooled");
                }
            }

            // The design's estimates for Inter (T-103 2, 3, 7). The rule itself is measured above, these pin the outcome.
            foreach (((string language, string region, Phase phase), int index) in shown)
            {
                string context = $"{language} {region} {width} {phase}: variant {index}";
                switch (width, phase)
                {
                    case (240, Phase.SameDay or Phase.AnotherDay):
                        // Q4: the remaining time stays, the finish time is only in the tooltip (L3).
                        Assert.True(index == 2, context);
                        break;
                    case (240, Phase.Cooling):
                        Assert.True(index >= 1, context);
                        break;
                    case (240, Phase.Cooled):
                        Assert.True(index == 1, context);
                        break;
                }
            }

            if (width == 320)
            {
                Assert.Equal(1, shown[("cs", "cs-CZ", Phase.SameDay)]);
                Assert.Equal(0, shown[("fr", "fr-FR", Phase.SameDay)]);
                Assert.Equal(1, shown[("cs", "cs-CZ", Phase.AnotherDay)]);
                Assert.Equal(0, shown[("cs", "cs-CZ", Phase.Cooled)]);
                Assert.Equal(1, shown[("de", "de-DE", Phase.Cooled)]);
            }

            if (width == 400)
            {
                Assert.Equal(0, shown[("cs", "cs-CZ", Phase.SameDay)]);
                Assert.Equal(0, shown[("es", "es-ES", Phase.SameDay)]);
                Assert.Equal(0, shown[("en", "en-US", Phase.SameDay)]);
            }
        });

    [Fact]
    public Task TooltipAndNameHaveTheWholeWordingWhateverIsDrawn() =>
        session.RunAsync(async scope =>
        {
            (CameraHarness harness, CameraWindow window) = await OpenAsync(240);
            await using CameraHarness _ = harness;
            UseLanguage(harness, "cs", "cs-CZ");
            Grid summary = window.Find<Grid>("CameraMiniSummary");

            Show(harness, window, Printing(0, Phase.SameDay));
            Assert.Null(MiniStripParts.FinishText(window));
            Assert.Equal("Tiskne, 19 %, Zbývá 2 h 35 min, Hotovo v 14:35", ToolTip.GetTip(summary));
            Assert.Equal("Tiskne, 19 %, Zbývá 2 h 35 min, Hotovo v 14:35", Avalonia.Automation.AutomationProperties.GetName(summary));

            CoolDown(harness, window, CoolUntilEstimate(harness, window, PollSeconds));
            Assert.Equal("Vychladlo · 40 °C", MiniStripParts.SummaryText(window).Text);
            Assert.Equal("Vychladlo – můžete vypnout, tryska 40 °C", ToolTip.GetTip(summary));

            // Only the drawn variant is in the automation tree of the summary.
            Avalonia.Automation.Peers.AutomationPeer panel = Avalonia.Automation.Peers.ControlAutomationPeer.CreatePeerForElement(MiniStripParts.SummaryPanel(window));
            Assert.Single(panel.GetChildren());
        });

    [Fact]
    public Task TopBarShowsTheCoolDownProgressThenFullGreen() =>
        session.RunAsync(async scope =>
        {
            (CameraHarness harness, CameraWindow window) = await OpenAsync(320);
            await using CameraHarness _ = harness;
            OverlayProgressBar bar = window.Find<OverlayProgressBar>("CameraMiniProgress");

            double t = CoolUntilEstimate(harness, window, 0);
            Assert.Contains("cooling", bar.Classes);
            Assert.DoesNotContain("idle", bar.Classes);
            Assert.InRange(bar.Value, 1, 99);
            Assert.Equal(Avalonia.Media.Color.Parse("#6CB4FF"), ((Avalonia.Media.ISolidColorBrush)bar.Foreground!).Color);

            CoolDown(harness, window, t);
            Assert.Contains("cooled", bar.Classes);
            Assert.Equal(100, bar.Value);
            Assert.Equal(Avalonia.Media.Color.Parse("#6CCB5F"), ((Avalonia.Media.ISolidColorBrush)bar.Foreground!).Color);

            // The next print: the job's progress in the accent colour again.
            Show(harness, window, Printing(t + 60, Phase.SameDay));
            Assert.DoesNotContain("cooled", bar.Classes);
            Assert.Equal(19, bar.Value);
            Assert.Equal(Avalonia.Media.Color.Parse("#FA6831"), ((Avalonia.Media.ISolidColorBrush)bar.Foreground!).Color);
        });

    [Fact]
    public Task SnapshotOfTheFinishTimeAt320() =>
        session.RunAsync(async scope =>
        {
            (CameraHarness harness, CameraWindow window) = await OpenAsync(320);
            await using CameraHarness _ = harness;
            UseLanguage(harness, "cs", "cs-CZ");
            Show(harness, window, Printing(0, Phase.SameDay));

            Assert.Equal("14:35", MiniStripParts.FinishText(window)?.Text);
            HideWindowButtons(window);
            SnapshotAssert.Matches(window, "camera-mini-strip-finish-320");
        });

    [Fact]
    public Task SnapshotOfTheWholeFinishWordingAt400InSpanish() =>
        session.RunAsync(async scope =>
        {
            (CameraHarness harness, CameraWindow window) = await OpenAsync(400);
            await using CameraHarness _ = harness;
            UseLanguage(harness, "es", "es-ES");
            Show(harness, window, Printing(0, Phase.SameDay));

            Assert.Equal("Termina a las 14:35", MiniStripParts.FinishText(window)?.Text);
            HideWindowButtons(window);
            SnapshotAssert.Matches(window, "camera-mini-strip-finish-400-es");
        });

    [Fact]
    public Task SnapshotOfCoolingAt240InItalian() =>
        session.RunAsync(async scope =>
        {
            (CameraHarness harness, CameraWindow window) = await OpenAsync(240);
            await using CameraHarness _ = harness;
            UseLanguage(harness, "it", "it-IT");
            CoolUntilEstimate(harness, window, 0);

            Assert.Equal(1, MiniStripParts.SummaryPanel(window).ShownIndex);
            HideWindowButtons(window);
            SnapshotAssert.Matches(window, "camera-mini-strip-cooling-240-it");
        });

    /// <summary>The pointer leaves the window: its buttons fade out at once (the fade runs on real time, skipped).</summary>
    private static void HideWindowButtons(CameraWindow window)
    {
        StackPanel buttons = window.Find<StackPanel>("CameraWindowButtons");
        buttons.Transitions = null;
        window.MouseMove(new Point(-10, -10));
        window.Settle();
    }

    /// <summary>
    /// The row draws the first variant that fits whole: every longer one is wider than the panel, the drawn one fits (or is
    /// the last, trimmed); the finish time is never trimmed and keeps 8 from the toggles; the strip keeps its height.
    /// </summary>
    private static int AssertRow(CameraWindow window, string context)
    {
        CameraMiniSummaryPanel panel = MiniStripParts.SummaryPanel(window);
        IReadOnlyList<Control> variants = MiniStripParts.Variants(window);
        int index = panel.ShownIndex;
        Rect toggles = BoundsIn(window.Find<StackPanel>("CameraMiniToggles"), window);
        Rect panelBounds = BoundsIn(panel, window);
        Assert.True(index >= 0, context);
        Assert.Same(variants[index], panel.Shown);
        Assert.True(panelBounds.Right <= toggles.Left - 8 + 0.01, $"{context}: panel {panelBounds} reaches the toggles {toggles}");
        for (int i = 0; i < index; i++)
        {
            Assert.True(variants[i].DesiredSize.Width > panelBounds.Width, $"{context}: variant {i} ({variants[i].DesiredSize.Width}) fits {panelBounds.Width} but {index} is drawn");
        }

        TextBlock summary = MiniStripParts.SummaryText(window);
        Rect summaryBounds = BoundsIn(summary, window);
        Assert.True(summaryBounds.Left >= 10 + 8 + 6 - 0.01, $"{context}: '{summary.Text}' starts at {summaryBounds.Left}");
        Assert.True(summaryBounds.Right <= toggles.Left - 8 + 0.01, $"{context}: '{summary.Text}' {summaryBounds} reaches the toggles {toggles}");
        Assert.Single(summary.TextLayout.TextLines);
        if (!panel.IsTrimmed)
        {
            Assert.True(variants[index].DesiredSize.Width <= panelBounds.Width + 0.01, context);
            Assert.False(summary.TextLayout.TextLines[0].HasCollapsed, $"{context}: '{summary.Text}' trimmed");
        }
        else
        {
            Assert.Equal(variants.Count - 1, index);
        }

        if (MiniStripParts.FinishText(window) is { } finish)
        {
            Rect finishBounds = BoundsIn(finish, window);
            Assert.False(panel.IsTrimmed, context);
            Assert.False(finish.TextLayout.TextLines[0].HasCollapsed, $"{context}: '{finish.Text}' trimmed");
            Assert.True(finish.TextLayout.WidthIncludingTrailingWhitespace <= finishBounds.Width + 0.5, $"{context}: '{finish.Text}' overflows");
            Assert.True(finishBounds.Right <= toggles.Left - 8 + 0.01, $"{context}: '{finish.Text}' {finishBounds} reaches the toggles {toggles}");
            Assert.True(finishBounds.Left >= summaryBounds.Right + 10 + 14 + 4 - 0.5, $"{context}: '{finish.Text}' {finishBounds} too close to the summary {summaryBounds}");
        }

        Assert.Equal(Strip, MiniStripParts.Strip(window).Bounds.Height);
        return index;
    }
}
