using System.Drawing;
using System.Globalization;
using System.Text;

using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;

using Microsoft.Extensions.Time.Testing;

using PrusaTrayDiagnostic.App.E2E.Tests.Infrastructure;
using PrusaTrayDiagnostic.Simulator.Camera;
using PrusaTrayDiagnostic.Simulator.Printers;
using PrusaTrayDiagnostic.Tests.Shared;

namespace PrusaTrayDiagnostic.App.E2E.Tests;

/// <summary>
/// The manual icon checks of T-025 and the tray menu illustration at 150 % of T-039, automated (T-070): the exe icon as
/// the shell (Explorer) draws it, the taskbar button and the title bars at 100 % and 150 %, the tray icon on a light and
/// a dark taskbar at 100 % and 150 % (size and visibility, states idle / printing / error), and the printer illustration in
/// the tray menu at 150 %. Theme, scale and the promoted tray icon go through the journal of T-065/T-066 and are restored
/// even after a crash. Only the element under test is captured from the screen (<see cref="ScreenArea"/>); windows of the
/// app with <c>PrintWindow</c>. Every capture and a report per test go to the results folder.
/// </summary>
public sealed class IconsThemeAndScalingTests(E2EFixture fixture)
{
    private const string PrinterId = "e2e-icons";

    private const string PrinterName = "Core One";

    /// <summary>The scale of the primary monitor in a theory: 100 % and 150 % (T-025, T-070).</summary>
    private const int Scaled = 150;

    private static readonly TimeSpan TaskbarTimeout = TimeSpan.FromSeconds(5);

    /// <summary>The app polls every 5 s (<see cref="TestSettings"/>); a state switch shows within two polls.</summary>
    private static readonly TimeSpan StateTimeout = TimeSpan.FromSeconds(20);

    /// <summary>
    /// The tray states of T-070 in the order they are shown: the printer starts idle, so the first switch after a scale
    /// change always changes the icon and makes the app render it again.
    /// </summary>
    private static readonly TrayState[] States =
    [
        new(PrinterScenario.Printing, "printing-35", HasBadge: false),
        new(PrinterScenario.Error, "error", HasBadge: true),
        new(PrinterScenario.Idle, "idle", HasBadge: false),
    ];

    /// <summary>Printer illustrations in the tray menu: 16 DIP (T-039).</summary>
    private const int MenuIconDip = 16;

    /// <summary>The pixel sizes of the illustration PNGs (T-039 <c>PrinterIllustrations</c>).</summary>
    private static readonly int[] IllustrationSizes = [16, 20, 24, 32, 48, 56, 64, 96, 112, 128, 256];

    [Fact]
    public void ExplorerShowsTheAppIconForTheExe()
    {
        E2EFixture.Require();
        StringBuilder report = new();
        List<string> problems = [];
        report.AppendLine(CultureInfo.InvariantCulture, $"Exe: {E2EFixture.AppPath}; system DPI {DisplayConfig.SystemDpi()}; app-icon.ico: {string.Join(", ", AppIconFile.Sizes)} px");

        void Check(string source, Pixels shown, int? requested = null)
        {
            string name = $"explorer-{source}";
            shown.Save(name);
            if (requested is { } size && (shown.Width != size || shown.Height != size))
            {
                problems.Add($"{source}: {shown.Width}×{shown.Height} instead of {size}×{size}.");
                return;
            }

            // The shell scales the nearest entry when the file has no entry of the size (e.g. 40 px at 125 %).
            int nearest = IconPixels.Nearest(AppIconFile.Sizes, shown.Width);
            Pixels expected = nearest == shown.Width ? AppIconFile.Entry(nearest) : IconPixels.Resize(AppIconFile.Entry(nearest), shown.Width, shown.Height);
            double difference = IconPixels.WholeDifference(shown, expected);
            report.AppendLine(CultureInfo.InvariantCulture, $"{source}: {shown.Width}×{shown.Height}, differs from the {nearest} px entry by {difference:P1}");
            if (difference > IconPixels.MatchTolerance)
            {
                problems.Add($"{source}: {shown.Width}×{shown.Height} differs from the {nearest} px entry of app-icon.ico by {difference:P1}.");
            }
        }

        try
        {
            Check("shgetfileinfo-large", ShellIcons.FileInfoIcon(E2EFixture.AppPath, large: true));
            Check("shgetfileinfo-small", ShellIcons.FileInfoIcon(E2EFixture.AppPath, large: false));
            foreach (int size in new[] { 16, 32, 48, 256 })
            {
                Check($"image-factory-{size}", ShellIcons.ItemIcon(E2EFixture.AppPath, size), size);
            }
        }
        finally
        {
            Screenshots.SaveText(report.ToString(), "explorer-icons-report");
        }

        Assert.True(
            problems.Count == 0,
            $"Explorer does not show the app icon (a stale shell icon cache shows an old one, T-017):{Environment.NewLine}{string.Join(Environment.NewLine, problems)}");
    }

    [Theory]
    [InlineData(100)]
    [InlineData(Scaled)]
    [Trait(SystemChanges.Trait, SystemChangeTraits.DisplayScale)]
    public async Task TaskbarButtonShowsTheAppIcon(int percent)
    {
        SystemChanges.Require();
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        StringBuilder report = new();
        await using SystemChange<DisplayScaleState> scale = await SystemChanges.DisplayScaleAsync(cancellationToken);
        try
        {
            await ScalePrimaryMonitorAsync(scale, percent, report, cancellationToken);
            await using AppInstance app = await AppInstance.StartAsync(fixture, $"icons-taskbar-{percent}", TestSettings.Json("en", []));
            await app.OpenAsync("settings", AppWindowKind.Settings);

            AutomationElement button = await TaskbarJumpList.WaitForButtonAsync(app.Automation);
            // The taskbar slides a new button in.
            Rectangle bounds = await Wait.StableAsync(() => button.BoundingRectangle, TimeSpan.FromSeconds(10), "the taskbar button to settle");
            Pixels capture = ScreenArea.Capture(bounds);
            capture.Save($"icons-taskbar-button-{percent}");

            // The taskbar shows 24 × scale px (T-025) of the primary monitor, where the taskbar is.
            int slot = (int)Math.Round(24 * percent / 100.0);
            List<(string Candidate, IconMatch Match)> matches = MatchAppIcon(capture, new Rectangle(0, 0, capture.Width, capture.Height), slot);
            report.AppendLine(CultureInfo.InvariantCulture, $"Button {bounds}, icon slot {slot} px:");
            foreach ((string candidate, IconMatch match) in matches)
            {
                report.AppendLine(CultureInfo.InvariantCulture, $"  {candidate}: {match}");
            }

            Assert.True(matches[0].Match.IsMatch, $"The taskbar button at {percent} % does not show the app icon (best: {matches[0].Candidate}, {matches[0].Match}).");
        }
        finally
        {
            Screenshots.SaveText(report.ToString(), $"icons-taskbar-button-{percent}-report");
        }
    }

    [Theory]
    [InlineData(100)]
    [InlineData(Scaled)]
    [Trait(SystemChanges.Trait, SystemChangeTraits.DisplayScale)]
    public async Task TitleBarsShowTheAppIcon(int percent)
    {
        SystemChanges.Require();
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        StringBuilder report = new();
        List<string> problems = [];
        await using SystemChange<DisplayScaleState> scale = await SystemChanges.DisplayScaleAsync(cancellationToken);
        try
        {
            await ScalePrimaryMonitorAsync(scale, percent, report, cancellationToken);
            await using RtspCamera camera = Simulators.StartCamera();
            await using AppInstance app = await AppInstance.StartAsync(
                fixture,
                $"icons-title-bars-{percent}",
                TestSettings.Json("en", [new TestPrinter(PrinterId, PrinterName, "127.0.0.1:1", Camera: camera.StreamUri)]));

            foreach ((string command, AppWindowKind kind, string name) in new[]
            {
                ("settings", AppWindowKind.Settings, "settings"),
                ("diagnostics", AppWindowKind.Diagnostics, "diagnostics"),
                ("about", AppWindowKind.Other, "about"),
                ($"camera:{PrinterId}", AppWindowKind.Camera, "camera"),
            })
            {
                nint window = await app.OpenAsync(command, kind);
                string? problem = await CheckTitleBarAsync(window, $"{name}-{percent}", percent, report);
                if (problem is not null)
                {
                    problems.Add($"{name}: {problem}");
                }

                await app.CloseWindowAsync(window);
            }
        }
        finally
        {
            Screenshots.SaveText(report.ToString(), $"icons-title-bars-{percent}-report");
        }

        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }

    [Theory]
    [InlineData(100)]
    [InlineData(Scaled)]
    [Trait(SystemChanges.Trait, SystemChangeTraits.Theme)]
    [Trait(SystemChanges.Trait, SystemChangeTraits.DisplayScale)]
    [Trait(SystemChanges.Trait, SystemChangeTraits.TrayPromotion)]
    public async Task TrayIconIsCrispAndVisibleOnLightAndDarkTaskbar(int percent)
    {
        SystemChanges.Require();
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        SystemChange<TrayIconPromotionState> tray = fixture.TrayIconPromotion
            ?? throw new InvalidOperationException("The run-wide tray icon change is missing although the warning listed TrayPromotion.");
        StringBuilder report = new();
        List<string> problems = [];
        List<string> findings = [];
        await using SystemChange<DisplayScaleState> scale = await SystemChanges.DisplayScaleAsync(cancellationToken);
        await using SystemChange<ThemeState> theme = await SystemChanges.ThemeAsync(cancellationToken);
        try
        {
            MonitorScaleState primary = scale.Original.Monitors.First(static monitor => monitor.Primary);
            report.AppendLine(CultureInfo.InvariantCulture, $"Theme at the start: {theme.Original}; ColorPrevalence={ThemeKind.ColorPrevalence()?.ToString(CultureInfo.InvariantCulture) ?? "(missing)"}");

            // A stopped clock: the print stays at 37.5 % (icon step 35 %) for as long as the checks take.
            FakeTimeProvider clock = new();
            await using SimulatedPrinter printer = await SimulatedPrinter.StartAsync(
                new SimulatedPrinterOptions { Scenario = PrinterScenario.Idle, TimeProvider = clock },
                cancellationToken);
            // Started at the original scale: at another scale, the icon must be rendered again for the new size.
            await using AppInstance app = await AppInstance.StartAsync(
                fixture,
                $"icons-tray-{percent}",
                TestSettings.Json("en", [new TestPrinter(PrinterId, PrinterName, printer.Address())]),
                Simulators.Secrets(PrinterId, printer));
            await tray.PromoteAsync(TimeSpan.FromSeconds(30), cancellationToken);
            await Wait.UntilAsync(() => NotificationArea.TestIconsOnTaskbar(app.Automation).Count > 0, TimeSpan.FromSeconds(10), "the test tray icon on the taskbar");

            await ScalePrimaryMonitorAsync(scale, percent, report, cancellationToken);
            bool scaleChangedAtRunTime = primary.Percent != percent;
            int size = TrayIconSize(percent);
            int startSize = TrayIconSize(primary.Percent);
            if (scaleChangedAtRunTime)
            {
                // Only for the report: what the taskbar shows right after the change, before the icon changes.
                await SaveTrayIconAsync(app, $"icons-tray-{percent}-right-after-the-scale-change");
            }

            double? lightBrightness = null;
            foreach (ThemeState mode in new[] { ThemeState.Light, ThemeState.Dark })
            {
                string modeName = mode.TaskbarIsLight ? "light" : "dark";
                await theme.ApplyAsync(mode, cancellationToken);
                double brightness = await ThemeKind.WaitForTaskbarAsync(mode.TaskbarIsLight, TaskbarTimeout, lightBrightness);
                lightBrightness ??= brightness;
                report.AppendLine(CultureInfo.InvariantCulture, $"{modeName} taskbar: median brightness {brightness:0}");

                foreach (TrayState state in States)
                {
                    ShowState(printer, clock, state.Scenario);
                    TrayIconResult result = await WaitForTrayIconAsync(app, state, size, scaleChangedAtRunTime ? startSize : null);
                    string label = $"{percent} % {modeName} {state.Snapshot}";
                    result.Capture?.Save($"icons-tray-{percent}-{modeName}-{state.Snapshot}");
                    report.AppendLine(CultureInfo.InvariantCulture, $"{label}: {result}");

                    if (!result.Size.IsExpectedSize)
                    {
                        if (scaleChangedAtRunTime && result.Size.Stale.IsMatch)
                        {
                            // T-070: a finding for the user, not fixed without a decision; the test checks visibility only.
                            findings.Add($"{label}: the app did not render the tray icon again in {size} px after the scale changed at run time; the taskbar stretches the {startSize} px icon ({result.Size}).");
                        }
                        else
                        {
                            problems.Add($"{label}: the tray icon is not the app's {size} px icon ({result.Size}).");
                        }
                    }

                    if (result.Visibility is not { IsVisible: true })
                    {
                        problems.Add($"{label}: the tray icon is not visible enough on the taskbar ({result.Visibility?.ToString() ?? "icon not found"}).");
                    }
                }
            }
        }
        finally
        {
            if (findings.Count > 0)
            {
                report.AppendLine().AppendLine("FINDING (T-070, ask the user):").AppendLine(string.Join(Environment.NewLine, findings));
            }

            Screenshots.SaveText(report.ToString(), $"icons-tray-{percent}-report");
        }

        foreach (string finding in findings)
        {
            TestContext.Current.AddWarning(finding);
        }

        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }

    [Fact]
    [Trait(SystemChanges.Trait, SystemChangeTraits.DisplayScale)]
    public async Task MenuIllustrationsAt150Percent()
    {
        SystemChanges.Require();
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        StringBuilder report = new();
        await using SystemChange<DisplayScaleState> scale = await SystemChanges.DisplayScaleAsync(cancellationToken);
        try
        {
            await ScalePrimaryMonitorAsync(scale, Scaled, report, cancellationToken);
            await using SimulatedPrinter printer = await Simulators.StartPrinterAsync(PrinterScenario.Idle);
            await using AppInstance app = await AppInstance.StartAsync(
                fixture,
                "icons-menu-150",
                TestSettings.Json("en", [new TestPrinter(PrinterId, PrinterName, printer.Address())]),
                Simulators.Secrets(PrinterId, printer));

            AutomationElement menu = await TrayMenuTests.OpenMenuAsync(app, "icons-menu-150");
            nint hwnd = menu.Properties.NativeWindowHandle.ValueOrDefault;
            AutomationElement item = menu.FindFirstDescendant(cf => cf.ByControlType(ControlType.MenuItem))
                ?? throw new InvalidOperationException("The tray menu has no item.");
            Assert.True(Native.GetWindowRect(hwnd, out Native.Rect window));
            uint menuDpi = Native.GetDpiForWindow(hwnd);
            uint systemDpi = DisplayConfig.SystemDpi();
            report.AppendLine(CultureInfo.InvariantCulture, $"Menu DPI {menuDpi}, system DPI {systemDpi} (fixed at sign-in), item {item.BoundingRectangle}");
            Assert.True(menuDpi == DisplayScaleSteps.ExpectedDpi(Scaled), $"The tray menu opened at {menuDpi} DPI, not on the primary monitor at {Scaled} %.");

            using Bitmap bitmap = Screenshots.Capture(hwnd);
            Pixels capture = Pixels.From(bitmap);
            Rectangle row = item.BoundingRectangle;
            row.Offset(-window.Left, -window.Top);
            int shown = (int)Math.Round(MenuIconDip * menuDpi / 96.0);
            Rectangle area = row with { Width = Math.Min(row.Width, 3 * shown) };
            capture.Crop(area).Save("icons-menu-150-printer-icon");

            // The bitmap has the pixels of 16 DIP at the system scale (T-039), drawn in 16 DIP of the menu's monitor.
            int bitmapSize = IllustrationSizes.First(size => size >= MenuIconDip * systemDpi / 96.0);
            bool sessionAtThisScale = systemDpi == menuDpi;
            Color background = IconPixels.Median(capture, row);
            List<(string Candidate, IconMatch Match, IconMatch Stretched16)> matches = [];
            foreach (string id in new[] { "core-one", "generic" })
            {
                Pixels illustration = Illustration(id, bitmapSize);
                Pixels reference = bitmapSize == shown ? illustration : IconPixels.Resize(illustration, shown, shown);
                Pixels stretched16 = IconPixels.Resize(Illustration(id, 16), shown, shown);
                matches.Add(($"{id}-{bitmapSize}.png{(bitmapSize == shown ? string.Empty : $" scaled to {shown} px")}", IconPixels.FindBest(capture, area, reference, background), IconPixels.FindBest(capture, area, stretched16, background)));
            }

            matches.Sort(static (a, b) => a.Match.Difference.CompareTo(b.Match.Difference));
            foreach ((string candidate, IconMatch match, IconMatch stretched16) in matches)
            {
                report.AppendLine(CultureInfo.InvariantCulture, $"  {candidate}: {match}; 16 px stretched to {shown} px: {stretched16}");
            }

            (string best, IconMatch bestMatch, IconMatch bestStretched) = matches[0];
            Assert.True(bestMatch.IsMatch, $"No printer illustration in the tray menu at {Scaled} % (best: {best}, {bestMatch}).");
            if (sessionAtThisScale)
            {
                // Signed in at 150 %: the 24 px illustration, sharp (not the 16 px one stretched).
                Assert.True(bestMatch.Difference < bestStretched.Difference, $"The illustration in the tray menu is not sharp: {best} {bestMatch}, 16 px stretched {bestStretched}.");
                report.AppendLine(CultureInfo.InvariantCulture, $"Sharp {shown} px illustration verified (session signed in at {Scaled} %).");
            }
            else
            {
                string note = $"Only the presence of the illustration is verified: the system DPI of this session is {systemDpi}, so the menu bitmap has {bitmapSize} px; "
                    + $"its sharpness at {Scaled} % can be verified only after signing in at {Scaled} % (recommended manual check, T-065 Remains manual 1).";
                report.AppendLine(note);
                TestContext.Current.SendDiagnosticMessage(note);
            }
        }
        finally
        {
            Screenshots.SaveText(report.ToString(), "icons-menu-150-report");
        }
    }

    /// <summary>
    /// Sets the primary monitor (the one with the taskbar and the tray) to <paramref name="percent"/> unless it is there
    /// already; skips the test when the scale is not available (T-066). Reports the displays before and after.
    /// </summary>
    private static async Task ScalePrimaryMonitorAsync(SystemChange<DisplayScaleState> scale, int percent, StringBuilder report, CancellationToken cancellationToken)
    {
        MonitorScaleState primary = scale.Original.Monitors.First(static monitor => monitor.Primary);
        report.AppendLine("Displays before:").Append(DisplayConfig.Report());
        if (primary.Percent != percent)
        {
            uint dpi = await scale.SetPercentAsync(primary.DevicePath, percent, cancellationToken);
            report.AppendLine(CultureInfo.InvariantCulture, $"{primary.Name}: {primary.Percent} % -> {percent} % at run time (monitorDpi {dpi}; the system DPI stays {DisplayConfig.SystemDpi()})");
            // Explorer lays the taskbar out again.
            await Wait.StableAsync(TaskbarBounds, TimeSpan.FromSeconds(10), "the taskbar after the scale change");
        }
        else
        {
            report.AppendLine(CultureInfo.InvariantCulture, $"{primary.Name} is at {percent} % already.");
        }
    }

    private static Rectangle TaskbarBounds()
    {
        nint taskbar = Native.FindWindow(NotificationArea.TaskbarClassName, null);
        return taskbar != 0 && Native.GetWindowRect(taskbar, out Native.Rect rect) ? Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom) : Rectangle.Empty;
    }

    /// <summary>
    /// Every entry of <c>app-icon.ico</c> searched for in <paramref name="area"/> as an icon of <paramref name="slot"/>
    /// pixels, best first: the entry nearest to the slot as it is (what Avalonia hands to the shell, T-025) and every
    /// entry scaled to the slot (the shell may scale its icon).
    /// </summary>
    private static List<(string Candidate, IconMatch Match)> MatchAppIcon(Pixels capture, Rectangle area, int slot)
    {
        Color background = IconPixels.Median(capture, area);
        int nearest = IconPixels.Nearest(AppIconFile.Sizes, slot);
        List<(string Candidate, Pixels Reference)> candidates = [($"{nearest} px entry (nearest to {slot} px) as it is", AppIconFile.Entry(nearest))];
        candidates.AddRange(AppIconFile.Sizes.Where(size => size != slot).Select(size => ($"{size} px entry scaled to {slot} px", IconPixels.Resize(AppIconFile.Entry(size), slot, slot))));
        return [.. candidates.Select(c => (c.Candidate, IconPixels.FindBest(capture, area, c.Reference, background))).OrderBy(static c => c.Item2.Difference)];
    }

    /// <summary>
    /// The icon in the title bar of <paramref name="window"/> (left of the caption, captured with <c>PrintWindow</c>) is the
    /// app icon of 16 × the window's scale pixels; returns the problem or <see langword="null"/>.
    /// </summary>
    private static async Task<string?> CheckTitleBarAsync(nint window, string name, int percent, StringBuilder report)
    {
        // The window settles at its monitor's DPI first.
        await Wait.StableAsync(() => (Native.GetDpiForWindow(window), Native.FrameBounds(window)), TimeSpan.FromSeconds(10), $"the {name} window to settle");
        uint dpi = Native.GetDpiForWindow(window);
        if ((Native.MonitorOf(window).Flags & 1) == 0)
        {
            return $"the window opened on a monitor that is not the primary one (scaled to {percent} %).";
        }

        if (dpi != DisplayScaleSteps.ExpectedDpi(percent))
        {
            return $"the window has {dpi} DPI, not {DisplayScaleSteps.ExpectedDpi(percent)}.";
        }

        Assert.True(Native.GetWindowRect(window, out Native.Rect outer));
        Native.Rect frame = Native.FrameBounds(window);
        Native.Rect client = Native.VisibleClientRect(window);
        int slot = (int)Math.Round(16 * dpi / 96.0);
        int caption = client.Top - frame.Top;
        if (caption < slot)
        {
            return $"no title bar ({caption} px above the client area).";
        }

        using Bitmap bitmap = Screenshots.Capture(window);
        Pixels capture = Pixels.From(bitmap);
        // The left of the caption: the icon and the start of the title, not the caption buttons.
        Rectangle area = new(frame.Left - outer.Left, frame.Top - outer.Top, Math.Min(frame.Width, (8 * slot) + 16), caption);
        capture.Crop(area).Save($"icons-title-bar-{name}");
        List<(string Candidate, IconMatch Match)> matches = MatchAppIcon(capture, area, slot);
        report.AppendLine(CultureInfo.InvariantCulture, $"{name}: {dpi} DPI, caption {caption} px, icon slot {slot} px; best {matches[0].Candidate}: {matches[0].Match}; next {matches[1].Candidate}: {matches[1].Match}");
        return matches[0].Match.IsMatch ? null : $"the title bar does not show the app icon (best: {matches[0].Candidate}, {matches[0].Match}).";
    }

    /// <summary>The tray icon size Avalonia asks for (T-025): <c>ceil(16 × scale)</c> of the monitor with the taskbar.</summary>
    private static int TrayIconSize(int percent) => (int)Math.Ceiling(16 * percent / 100.0);

    /// <summary>Switches the simulated printer; a print is held at 37.5 % (past the heating) by the stopped clock.</summary>
    private static void ShowState(SimulatedPrinter printer, FakeTimeProvider clock, PrinterScenario scenario)
    {
        printer.Simulation.SetScenario(scenario);
        if (scenario == PrinterScenario.Printing)
        {
            SimulationTiming timing = printer.Simulation.Timing;
            clock.Advance(timing.HeatUp + (timing.Print * 0.375));
        }
    }

    /// <summary>
    /// Captures the test tray icon on the taskbar until it shows <paramref name="state"/>: the snapshot of T-025 in
    /// <paramref name="size"/> px, or (after a scale change at run time) the one of <paramref name="startSize"/> px
    /// stretched by the taskbar. Gives the last capture after <see cref="StateTimeout"/>.
    /// </summary>
    private static async Task<TrayIconResult> WaitForTrayIconAsync(AppInstance app, TrayState state, int size, int? startSize)
    {
        Pixels expected = TrayIconSnapshot(state.Snapshot, size);
        Pixels? stale = startSize is { } start && start != size ? IconPixels.Resize(TrayIconSnapshot(state.Snapshot, start), size, size) : null;
        TrayIconResult result = TrayIconResult.NotFound;
        try
        {
            await Wait.UntilAsync(
                () =>
                {
                    result = CaptureTrayIcon(app, state, expected, stale);
                    return result.Size.IsExpectedSize || result.Size.Stale.IsMatch;
                },
                StateTimeout,
                $"the tray icon '{state.Snapshot}'");
        }
        catch (TimeoutException)
        {
            // The caller reports the last capture and why it does not fit.
        }

        return result;
    }

    /// <summary>The best of the test tray icons on the taskbar (a lingering icon of a killed instance may be there too).</summary>
    private static TrayIconResult CaptureTrayIcon(AppInstance app, TrayState state, Pixels expected, Pixels? stale)
    {
        TrayIconResult best = TrayIconResult.NotFound;
        Native.GetCursorPos(out Native.Point cursor);
        foreach (AutomationElement icon in NotificationArea.TestIconsOnTaskbar(app.Automation))
        {
            Rectangle bounds = icon.BoundingRectangle;
            if (bounds.IsEmpty)
            {
                continue;
            }

            Pixels capture = ScreenArea.Capture(bounds);
            Rectangle all = new(0, 0, capture.Width, capture.Height);
            Color background = IconPixels.Median(capture, all);
            TraySizeCheck size = TraySizeCheck.Of(capture, all, expected, stale, background);
            (Pixels shown, Point at) = size.IsExpectedSize || stale is null || !size.Stale.IsMatch ? (expected, size.Expected.Location) : (stale, size.Stale.Location);
            TrayVisibility? visibility = at.X >= 0 && (size.Expected.IsMatch || size.Stale.IsMatch)
                ? TrayVisibility.Of(capture, at, shown, background, state.HasBadge)
                : null;
            TrayIconResult result = new(capture, bounds, background, size, visibility, bounds.Contains(cursor.X, cursor.Y));
            if (best.Capture is null || result.Score < best.Score)
            {
                best = result;
            }
        }

        return best;
    }

    private static async Task SaveTrayIconAsync(AppInstance app, string name)
    {
        await Task.Delay(TimeSpan.FromSeconds(1));
        foreach (AutomationElement icon in NotificationArea.TestIconsOnTaskbar(app.Automation))
        {
            ScreenArea.Capture(icon.BoundingRectangle).Save(name);
        }
    }

    private static Pixels TrayIconSnapshot(string state, int size) =>
        Pixels.Load(Path.Combine(TestWorktree.Root, "tests", "PrusaTrayDiagnostic.App.UiTests", "Snapshots", "TrayIcons", $"{state}-{size}.png"));

    private static Pixels Illustration(string id, int size) =>
        Pixels.Load(Path.Combine(TestWorktree.Root, "src", "PrusaTrayDiagnostic.App", "Assets", "Printers", $"{id}-{size}.png"));

    /// <summary>A tray state of T-070: the scenario of the simulated printer and the name of its tray icon snapshot (T-025).</summary>
    private sealed record TrayState(PrinterScenario Scenario, string Snapshot, bool HasBadge);

    /// <summary>One capture of the tray icon with its checks.</summary>
    private sealed record TrayIconResult(Pixels? Capture, Rectangle Bounds, Color Background, TraySizeCheck Size, TrayVisibility? Visibility, bool CursorOnIcon)
    {
        public static TrayIconResult NotFound { get; } = new(null, Rectangle.Empty, Color.Empty, new TraySizeCheck(IconMatch.None, IconMatch.None), null, false);

        public double Score => Math.Min(Size.Expected.Difference, Size.Stale.Difference);

        public override string ToString() => Capture is null
            ? "no test tray icon on the taskbar"
            : string.Create(
                CultureInfo.InvariantCulture,
                $"button {Bounds}, taskbar #{Background.R:X2}{Background.G:X2}{Background.B:X2}; {Size}; {Visibility?.ToString() ?? "visibility not measured"}{(CursorOnIcon ? "; the mouse pointer was on the icon" : string.Empty)}");
    }
}
