using System.Globalization;

using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;

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
/// What the mini preview strip shows by the printer's settings (T-110, design T-104 3–5) in the real camera window: the
/// summary row gains the room of hidden toggles (W = width − 100 / − 68 / − 34), the drawn variant stays 8 from the toggles
/// (10 from the edge without any), hidden toggles are neither Tab stops nor in the automation tree, the strip height
/// follows the available rows, and settings saved while the window is open apply to it right away. The fake clock of the
/// windows is in UTC; <see cref="Now"/> is Friday 2026-10-16 12:00.
/// </summary>
public sealed class CameraWindowMiniStripItemsUiTests(HeadlessSession session)
{
    private const uint Grey = 0xFF406080;

    private static readonly CameraWindowDefaults MiniFirst = CameraWindowDefaults.Default with { StartMode = CameraWindowStartMode.Mini };

    private static readonly CameraMiniStripSettings NoRows = new() { NameRow = false, ChartRow = false };
    private static readonly CameraMiniStripSettings NoRemaining = new() { Remaining = false };
    private static readonly CameraMiniStripSettings NoChart = new() { ChartRow = false };
    private static readonly CameraMiniStripSettings Minimum = new() { Percent = false, Remaining = false, Finish = false, Cooldown = false };

    private static readonly Dictionary<string, string> Regions = new(StringComparer.Ordinal)
    {
        ["en"] = "en-US", ["cs"] = "cs-CZ", ["sk"] = "sk-SK", ["de"] = "de-DE", ["es"] = "es-ES", ["fr"] = "fr-FR", ["it"] = "it-IT", ["pl"] = "pl-PL",
    };

    public static TheoryData<double> Widths => [240, 320, 400];

    private static CameraViewModel ViewModel(CameraWindow window) => (CameraViewModel)window.DataContext!;

    private static Rect BoundsIn(Control control, Visual root) =>
        new(control.TranslatePoint(default, root) ?? throw new InvalidOperationException("Not in the window."), control.Bounds.Size);

    private static PrinterSnapshot Printing() =>
        Snapshot(PrinterState.Printing, Job(19.9, TimeSpan.FromMinutes(154.2), "benchy_0.4n_0.2mm_PLA_MK4S_2h35m.bgcode"), "a") with
        {
            Telemetry = new Telemetry { Nozzle = new Temperature(214.6, 215), Bed = new Temperature(44, 0) },
        };

    private static void UseLanguage(CameraHarness harness, string language)
    {
        CultureInfo culture = CultureInfo.GetCultureInfo(Regions[language]);
        CultureInfo.CurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentCulture = culture;
        Assert.True(new UiLanguage().Apply(language));
        harness.Windows.RefreshTexts();
    }

    private static async Task<(CameraHarness Harness, CameraWindow Window)> OpenAsync(double width, CameraMiniStripSettings items)
    {
        CameraHarness harness = new(screens: FakeScreenCatalog.ThreeMonitors(), window: MiniFirst, miniStrip: items);
        harness.Time.SetUtcNow(Now);
        harness.Time.SetLocalTimeZone(TimeZoneInfo.Utc);
        (CameraWindow window, _) = await harness.OpenStreamingAsync(1280, 720, Grey);
        CameraHarness.ResizeMiniWithGrip(window, width, 600);
        Assert.Equal(width, window.ClientSize.Width);
        Show(harness, window, Printing());
        return (harness, window);
    }

    /// <summary>
    /// The window buttons at their final opacity: the fade after the grip resize runs on real time, so it is skipped for a
    /// stable frame (the window corrected its height, so the pointer is outside it and the buttons are hidden).
    /// </summary>
    private static void ShowButtonsAtOnce(CameraWindow window)
    {
        CameraHarness.SkipWindowButtonFade(window);
        window.Settle();
        Assert.Equal(0, window.Find<Button>("CameraFullScreenButton").Opacity);
    }

    private static void Show(CameraHarness harness, CameraWindow window, PrinterSnapshot snapshot)
    {
        harness.Monitor.Raise(snapshot);
        Dispatcher.UIThread.RunJobs();
        window.Settle();
    }

    /// <summary>The automation ids of every element below <paramref name="peer"/>.</summary>
    private static IEnumerable<string?> AutomationIds(AutomationPeer peer)
    {
        foreach (AutomationPeer child in peer.GetChildren())
        {
            yield return child.GetAutomationId();
            foreach (string? id in AutomationIds(child))
            {
                yield return id;
            }
        }
    }

    /// <summary>The automation ids of the Tab stops of the window: Tab pressed until the focus comes back (at most 30 times).</summary>
    private static List<string?> TabStops(CameraWindow window)
    {
        List<string?> stops = [];
        window.Focus();
        object? start = window.FocusManager?.GetFocusedElement();
        for (int i = 0; i < 30; i++)
        {
            window.KeyPressQwerty(PhysicalKey.Tab, RawInputModifiers.None);
            window.KeyReleaseQwerty(PhysicalKey.Tab, RawInputModifiers.None);
            window.Settle();
            object? focused = window.FocusManager?.GetFocusedElement();
            if (focused is null || ReferenceEquals(focused, start) || (focused is Control seen && stops.Contains(AutomationProperties.GetAutomationId(seen))))
            {
                break;
            }

            stops.Add(focused is Control control ? AutomationProperties.GetAutomationId(control) : null);
        }

        return stops;
    }

    /// <summary>The summary row and the toggles by <paramref name="items"/> at <paramref name="width"/> (T-104 3).</summary>
    private static List<string> CheckRow(CameraWindow window, CameraMiniStripSettings items, double width, string context)
    {
        List<string> problems = [];
        CameraMiniStripViewModel strip = ViewModel(window).MiniStrip;
        ToggleButton name = window.Find<ToggleButton>("CameraMiniToggleName");
        ToggleButton chart = window.Find<ToggleButton>("CameraMiniToggleChart");
        int toggles = (items.NameRow ? 1 : 0) + (items.ChartRow ? 1 : 0);
        double expected = width - toggles switch { 2 => 100, 1 => 68, _ => 34 };
        CameraMiniSummaryPanel panel = MiniStripParts.SummaryPanel(window);
        if (Math.Abs(panel.Bounds.Width - expected) > 0.5)
        {
            problems.Add($"{context}: the summary has {panel.Bounds.Width}, expected {expected}.");
        }

        if (name.IsEffectivelyVisible != items.NameRow || chart.IsEffectivelyVisible != items.ChartRow)
        {
            problems.Add($"{context}: toggles visible {name.IsEffectivelyVisible} / {chart.IsEffectivelyVisible}.");
        }

        TextBlock text = MiniStripParts.SummaryText(window);
        Control variant = MiniStripParts.ShownVariant(window);
        Rect drawn = BoundsIn(variant, window);
        double right = BoundsIn(text, window).Left + Math.Min(text.Bounds.Width, text.TextLayout.WidthIncludingTrailingWhitespace);
        if (MiniStripParts.FinishOf(variant) is { } finish)
        {
            right = Math.Max(right, BoundsIn(finish, window).Right);
        }

        double limit = toggles > 0 ? BoundsIn(window.Find<StackPanel>("CameraMiniToggles"), window).Left - 8 : width - 10;
        if (right > limit + 0.01 || drawn.Left < 10 + 8 + 6 - 0.01)
        {
            problems.Add($"{context}: '{text.Text}' {drawn} ends at {right}, limit {limit}.");
        }

        if (text.TextLayout.TextLines.Count != 1)
        {
            problems.Add($"{context}: '{text.Text}' wraps.");
        }

        if (Math.Abs(MiniStripParts.Strip(window).Bounds.Height - strip.StripHeight) > 0.01)
        {
            problems.Add($"{context}: the strip is {MiniStripParts.Strip(window).Bounds.Height} high, StripHeight {strip.StripHeight}.");
        }

        return problems;
    }

    public static TheoryData<double, string> Cases => new()
    {
        { 240, "no rows" }, { 320, "no rows" }, { 400, "no rows" },
        { 240, "no remaining" }, { 320, "no remaining" }, { 400, "no remaining" },
        { 240, "no chart" }, { 320, "no chart" }, { 400, "no chart" },
        { 240, "minimum" }, { 320, "minimum" }, { 400, "minimum" },
    };

    private static CameraMiniStripSettings ItemsOf(string name) => name switch
    {
        "no rows" => NoRows,
        "no remaining" => NoRemaining,
        "no chart" => NoChart,
        "minimum" => Minimum,
        _ => throw new ArgumentOutOfRangeException(nameof(name)),
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public Task CustomStripFitsInEveryLanguage(double width, string combination) =>
        session.RunAsync(async scope =>
        {
            CameraMiniStripSettings items = ItemsOf(combination);
            (CameraHarness harness, CameraWindow window) = await OpenAsync(width, items);
            await using CameraHarness _ = harness;
            List<string> problems = [];
            foreach (string language in AppLanguages.Supported)
            {
                UseLanguage(harness, language);
                window.Settle();
                problems.AddRange(CheckRow(window, items, width, $"{width} {combination} {language}"));
                string accessible = AutomationProperties.GetName(window.Find<Grid>("CameraMiniSummary")) ?? string.Empty;
                if (!items.Remaining && accessible.Contains(OverlayFormat.Duration(155), StringComparison.Ordinal))
                {
                    problems.Add($"{language}: the tooltip names the switched-off remaining time: {accessible}");
                }

                if (combination == "minimum" && MiniStripParts.SummaryText(window).Text != OverlayFormat.StateName(PrinterState.Printing))
                {
                    problems.Add($"{language}: the minimum is '{MiniStripParts.SummaryText(window).Text}'.");
                }
            }

            Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));

            // Hidden toggles are neither Tab stops nor elements of UI Automation.
            List<string?> stops = TabStops(window);
            List<string?> elements = [.. AutomationIds(ControlAutomationPeer.CreatePeerForElement(window))];
            foreach ((string id, bool shown) in new[] { ("CameraMiniToggleName", items.NameRow), ("CameraMiniToggleChart", items.ChartRow) })
            {
                if (shown)
                {
                    Assert.Contains(id, stops);
                    Assert.Contains(id, elements);
                }
                else
                {
                    Assert.DoesNotContain(id, stops);
                    Assert.DoesNotContain(id, elements);
                }
            }

            // The strip and the window: the image (16:9 of the width) plus the strip height.
            Assert.Equal(Math.Round(width * 9 / 16) + ViewModel(window).MiniStrip.StripHeight, window.ClientSize.Height, 1.0);
        });

    [Fact]
    public Task MinimumKeepsTheStateNameAndTheBarWhileItIsOn() =>
        session.RunAsync(async scope =>
        {
            (CameraHarness harness, CameraWindow window) = await OpenAsync(240, Minimum);
            await using CameraHarness _ = harness;

            Assert.Equal("Printing", MiniStripParts.SummaryText(window).Text);
            Assert.Equal("Printing", ToolTip.GetTip(window.Find<Grid>("CameraMiniSummary")));
            OverlayProgressBar bar = window.Find<OverlayProgressBar>("CameraMiniProgress");
            Assert.Equal(19, bar.Value);
            Assert.DoesNotContain("idle", bar.Classes);
            Assert.True(MiniStripParts.Strip(window).GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Ellipse>().Single().IsEffectivelyVisible);
        });

    [Fact]
    public Task SavedSettingsApplyToTheOpenWindowAtOnce() =>
        session.RunAsync(async scope =>
        {
            CameraWindowState remembered = new(CameraWindowMode.Mini, null, null, new CameraMiniRows(Name: true, Chart: true));
            (CameraHarness harness, CameraWindow window) = await OpenAsync(320, CameraMiniStripSettings.Default);
            await using CameraHarness _ = harness;
            CameraMiniStripViewModel strip = ViewModel(window).MiniStrip;
            strip.IsNameRowExpanded = true;
            strip.IsChartRowExpanded = true;
            window.Settle();
            Assert.Equal(180 + 131, window.ClientSize.Height);
            Assert.True(window.Find<LayerPeek.App.Diagnostics.Chart.TemperatureChart>("CameraMiniChart").IsSubscribedToHistory);
            Assert.Equal(remembered.MiniRows, harness.States.Get("a").MiniRows);
            // The window grows and shrinks away from the taskbar: its bottom edge stays (T-089).
            int bottom = window.Position.Y + (int)window.ClientSize.Height;

            harness.SaveMiniStrip(NoRows with { ProgressBar = false, Finish = false });
            window.Settle();

            Assert.False(window.Find<ToggleButton>("CameraMiniToggleName").IsEffectivelyVisible);
            Assert.False(window.Find<ToggleButton>("CameraMiniToggleChart").IsEffectivelyVisible);
            Assert.Equal(new Size(320, 180 + 33), window.ClientSize);
            // The chart row switched off: its chart no longer listens to the history (T-104 5).
            Assert.False(window.Find<LayerPeek.App.Diagnostics.Chart.TemperatureChart>("CameraMiniChart").IsSubscribedToHistory);
            Assert.Equal(33, MiniStripParts.Strip(window).Bounds.Height);
            Assert.Equal(320 - 34, MiniStripParts.SummaryPanel(window).Bounds.Width, 0.5);
            Assert.Null(MiniStripParts.FinishText(window));
            Assert.Contains("idle", window.Find<OverlayProgressBar>("CameraMiniProgress").Classes);
            Assert.Equal(bottom, window.Position.Y + (int)window.ClientSize.Height);
            // The remembered expansion is kept (T-104 5).
            Assert.Equal(remembered.MiniRows, harness.States.Get("a").MiniRows);

            harness.SaveMiniStrip(CameraMiniStripSettings.Default);
            window.Settle();

            Assert.True(window.Find<ToggleButton>("CameraMiniToggleName").IsEffectivelyVisible);
            Assert.True(window.Find<ToggleButton>("CameraMiniToggleChart").IsChecked);
            Assert.Equal(new Size(320, 180 + 131), window.ClientSize);
            Assert.NotNull(MiniStripParts.FinishText(window));
            Assert.True(window.Find<LayerPeek.App.Diagnostics.Chart.TemperatureChart>("CameraMiniChart").IsSubscribedToHistory);
            Assert.Equal(bottom, window.Position.Y + (int)window.ClientSize.Height);
        });

    [Fact]
    public Task CustomStripAt240() =>
        session.RunAsync(async scope =>
        {
            // The remaining time off: the finish time already fits at 240 (T-104 3).
            (CameraHarness harness, CameraWindow window) = await OpenAsync(240, NoRemaining);
            await using CameraHarness _ = harness;

            Assert.Equal("19%", MiniStripParts.SummaryText(window).Text);
            Assert.NotNull(MiniStripParts.FinishText(window));
            ShowButtonsAtOnce(window);
            SnapshotAssert.Matches(window, "camera-mini-strip-custom-240");
        });

    [Fact]
    public Task StripWithoutRowsAt320() =>
        session.RunAsync(async scope =>
        {
            (CameraHarness harness, CameraWindow window) = await OpenAsync(320, NoRows);
            await using CameraHarness _ = harness;

            Assert.Equal(320 - 34, MiniStripParts.SummaryPanel(window).Bounds.Width, 0.5);
            ShowButtonsAtOnce(window);
            SnapshotAssert.Matches(window, "camera-mini-strip-no-rows-320");
        });
}
