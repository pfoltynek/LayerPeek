using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;

using FlaUI.Core.AutomationElements;
using Keyboard = FlaUI.Core.Input.Keyboard;
using FlaUI.Core.WindowsAPI;

using PrusaTrayDiagnostic.App.E2E.Tests.Infrastructure;
using PrusaTrayDiagnostic.Simulator.Camera;

namespace PrusaTrayDiagnostic.App.E2E.Tests;

/// <summary>
/// The camera window on the monitors of this machine (T-071, manual checks of T-035): the chosen monitor and corner,
/// full screen, a missing and a switched-off monitor, the real monitor names, two identical monitors, no jump when the
/// window opens and the 16 DIP margin at 100/125/150 %. The test finds the monitors on its own and writes the chosen one
/// into <c>settings.json</c>; every case starts a fresh app, so the window is really opened (not just moved by Apply).
/// A report with the monitors, the frames and the WinEvents goes to the results folder.
/// </summary>
public sealed class CameraMonitorTests(E2EFixture fixture)
{
    private const string MissingName = "E2E Missing Monitor";
    private const int MissingWidth = 1234;
    private const int MissingHeight = 567;
    private const string MissingDevicePath = @"\\?\DISPLAY#E2E0000#0&0&UID0#{e6f07b5f-ee97-4a90-b076-33f57bf4eaa7}";

    /// <summary>A normal window size that fits with its margins even on a 1440 px wide monitor at 150 %.</summary>
    private static readonly (int Width, int Height) NormalSize = (640, 360);

    private static readonly (int Width, int Height) MiniSize = (320, 180);

    /// <summary>How long the window must keep still after it was shown (T-071).</summary>
    private static readonly TimeSpan SettleTime = TimeSpan.FromSeconds(1);

    private static readonly TimeSpan WindowTimeout = TimeSpan.FromSeconds(10);

    private static readonly string[] Modes = ["Normal", "Mini"];

    private readonly List<string> _problems = [];
    private readonly StringBuilder _report = new();

    public static TheoryData<string> Languages => [.. TestSettings.Languages];

    [Fact]
    public async Task CameraOpensInEveryCornerOfTheChosenMonitor()
    {
        E2EFixture.Require();
        await using RtspCamera camera = Simulators.StartCamera();
        TestMonitor chosen = Chosen();
        try
        {
            foreach (string mode in Modes)
            {
                foreach (string corner in CameraPlacement.Corners)
                {
                    await CheckOpeningAsync(camera, $"corner-{mode}-{corner}", chosen, mode, corner);
                }
            }
        }
        finally
        {
            SaveReport("camera-corners-report");
        }

        AssertNoProblems();
    }

    [Fact]
    public async Task FullScreenStartModeUsesTheChosenMonitor()
    {
        E2EFixture.Require();
        await using RtspCamera camera = Simulators.StartCamera();
        TestMonitor chosen = Chosen();
        await using AppInstance app = await CameraWindowSettings.StartAsync(
            fixture,
            "camera-full-screen-monitor",
            CameraPlacement.Window(chosen.CameraMonitorJson(), "FullScreen", normalWidth: NormalSize.Width, normalHeight: NormalSize.Height),
            camera);

        nint window = await CameraWindowSettings.OpenCameraAsync(app);
        await Wait.UntilAsync(() => WindowRect(window).Equals(chosen.Bounds), WindowTimeout, $"full screen over {chosen} (now {WindowRect(window)})");
        Assert.False(CameraWindowSettings.IsTopmost(window), "Full screen is never always on top (T-037).");
        Screenshots.Save(window, "camera-full-screen-chosen-monitor");

        // Esc with the real keyboard: back to the normal window, on the same monitor, in its placement (centre).
        await WindowActivation.ActivateAsync(window, new System.Drawing.Point(chosen.Bounds.Left + (chosen.Bounds.Width / 2), chosen.Bounds.Top + (chosen.Bounds.Height / 2)));
        Keyboard.Type(VirtualKeyShort.ESCAPE);
        await Wait.UntilAsync(() => WindowChecks.ClientSizeDip(window) == NormalSize && (Native.Style(window) & Native.WsCaption) == Native.WsCaption, WindowTimeout, "the normal window after Esc");
        TestMonitor now = Fresh(chosen);
        await Wait.StableAsync(() => Native.FrameBounds(window), WindowTimeout, "the normal window to settle");
        Assert.Equal(now.Bounds, Native.MonitorOf(window).Monitor);
        Assert.Null(CameraPlacement.Problem(Native.FrameBounds(window), now.Work, "Center", now.Dpi));
        Screenshots.Save(window, "camera-full-screen-esc-normal");
    }

    /// <summary>
    /// A saved monitor that does not exist (name and device path of no monitor): the window opens on the primary monitor and
    /// the settings offer the saved monitor as "… – not connected" (selected), in every language.
    /// </summary>
    [Theory]
    [MemberData(nameof(Languages))]
    public async Task MissingMonitorFallsBackToThePrimary(string language)
    {
        E2EFixture.Require();
        JsonObject missing = new()
        {
            ["displayName"] = MissingName,
            ["width"] = MissingWidth,
            ["height"] = MissingHeight,
            ["devicePath"] = MissingDevicePath,
        };
        await using AppInstance app = await CameraWindowSettings.StartAsync(
            fixture,
            $"camera-missing-monitor-{language}",
            CameraPlacement.Window(missing, "Normal", normalWidth: NormalSize.Width, normalHeight: NormalSize.Height),
            language: language);

        nint window = await CameraWindowSettings.OpenCameraAsync(app);
        await ExpectNormalWindowOnAsync(window, TestMonitors.Primary(TestMonitors.All()), $"missing monitor ({language})");
        await app.CloseWindowAsync(window);

        List<string> expected = [.. TestMonitors.ExpectedChoices(language, TestMonitors.All()), TestMonitors.NotConnectedChoice(language, MissingName, MissingWidth, MissingHeight)];
        await ExpectMonitorChoicesAsync(app, expected, expected.Count - 1, $"camera-missing-monitor-settings-{language}");
    }

    [Theory]
    [MemberData(nameof(Languages))]
    public async Task SettingsListsTheRealMonitorNames(string language)
    {
        E2EFixture.Require();
        await using AppInstance app = await CameraWindowSettings.StartAsync(fixture, $"camera-monitor-names-{language}", CameraPlacement.Window(null, "Normal"), language: language);

        List<TestMonitor> monitors = TestMonitors.All();
        Screenshots.SaveText(string.Join(Environment.NewLine, monitors), $"camera-monitor-names-{language}-monitors");
        await ExpectMonitorChoicesAsync(app, TestMonitors.ExpectedChoices(language, monitors), 0, $"camera-monitor-names-settings-{language}");
    }

    /// <summary>
    /// Two monitors with the same name (2× DELL U2719D here) are two choices (T-035 Q8), each stored with its device path,
    /// and the camera opens on the chosen one. Skipped without two monitors of the same name.
    /// </summary>
    [Fact]
    public async Task TwoIdenticalMonitorsAreTwoChoices()
    {
        E2EFixture.Require();
        List<TestMonitor> monitors = TestMonitors.All();
        List<TestMonitor>? twins = monitors.GroupBy(static monitor => monitor.AppName, StringComparer.Ordinal).FirstOrDefault(static group => group.Count() >= 2)?.ToList();
        Assert.SkipWhen(twins is null, $"Needs two monitors with the same name (found: {string.Join("; ", monitors)}).");

        await using RtspCamera camera = Simulators.StartCamera();
        await using AppInstance app = await CameraWindowSettings.StartAsync(
            fixture,
            "camera-identical-monitors",
            CameraPlacement.Window(null, "Normal", normalWidth: NormalSize.Width, normalHeight: NormalSize.Height),
            camera);
        List<string> choices = TestMonitors.ExpectedChoices("en", monitors);
        List<TestMonitor> ordered = TestMonitors.Ordered(monitors);

        // The non-primary twin first (a change from the primary default), then the other one.
        foreach (TestMonitor target in twins!.OrderBy(static monitor => monitor.Primary))
        {
            string choice = choices[ordered.IndexOf(target) + 1];
            (nint settings, AutomationElement element) = await CameraWindowSettings.OpenTabAsync(app);
            (List<string> items, _) = await CameraWindowSettings.MonitorChoicesAsync(element);
            Assert.Equal(choices, items);
            await Ui.SelectComboItemAsync(element, "PrinterCameraWindowMonitor", (text, _) => text == choice, $"'{choice}'");
            await CameraWindowSettings.CloseAsync(settings, element, save: true);
            await Wait.UntilAsync(
                () => string.Equals(CameraWindowSettings.SavedWindow(app)?["monitor"]?["devicePath"]?.GetValue<string>(), target.DevicePath, StringComparison.OrdinalIgnoreCase),
                WindowTimeout,
                $"settings.json to store the device path of '{choice}' ({target.DevicePath})");

            nint window = await CameraWindowSettings.OpenCameraAsync(app);
            await ExpectNormalWindowOnAsync(window, target, $"'{choice}'");
            Screenshots.Save(window, $"camera-identical-monitors-{target.GdiName}");
            await app.CloseWindowAsync(window);
        }
    }

    [Fact]
    public async Task CameraWindowDoesNotJumpWhenItOpens()
    {
        E2EFixture.Require();
        await using RtspCamera camera = Simulators.StartCamera();
        TestMonitor chosen = Chosen();
        try
        {
            await CheckOpeningAsync(camera, "jump-Normal", chosen, "Normal", "Center");
            await CheckOpeningAsync(camera, "jump-Mini", chosen, "Mini", "BottomRight");
        }
        finally
        {
            SaveReport("camera-jump-report");
        }

        AssertNoProblems();
    }

    /// <summary><see cref="CameraWindowDoesNotJumpWhenItOpens"/> with the chosen monitor at 100, 125 and 150 % (T-066).</summary>
    [Fact]
    [Trait(SystemChanges.Trait, SystemChangeTraits.DisplayScale)]
    public async Task CameraWindowDoesNotJumpWhenItOpensAtEveryScale()
    {
        SystemChanges.Require();
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using RtspCamera camera = Simulators.StartCamera();
        TestMonitor chosen = Chosen();
        try
        {
            await using SystemChange<DisplayScaleState> scale = await SystemChanges.DisplayScaleAsync(cancellationToken);
            foreach (int percent in new[] { 100, 125, 150 })
            {
                await SetScaleAsync(scale, chosen, percent, cancellationToken);
                await CheckOpeningAsync(camera, $"jump-{percent}-Normal", chosen, "Normal", "Center");
                await CheckOpeningAsync(camera, $"jump-{percent}-Mini", chosen, "Mini", "BottomRight");
            }
        }
        finally
        {
            SaveReport("camera-jump-scales-report");
        }

        AssertNoProblems();
    }

    /// <summary>Every corner and the centre of both modes at 125 and 150 % of the chosen monitor (T-066), also without a jump.</summary>
    [Fact]
    [Trait(SystemChanges.Trait, SystemChangeTraits.DisplayScale)]
    public async Task CornerMarginIs16DipAtEveryScale()
    {
        SystemChanges.Require();
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using RtspCamera camera = Simulators.StartCamera();
        TestMonitor chosen = Chosen();
        try
        {
            await using SystemChange<DisplayScaleState> scale = await SystemChanges.DisplayScaleAsync(cancellationToken);
            foreach (int percent in new[] { 125, 150 })
            {
                await SetScaleAsync(scale, chosen, percent, cancellationToken);
                foreach (string mode in Modes)
                {
                    foreach (string corner in CameraPlacement.Corners)
                    {
                        await CheckOpeningAsync(camera, $"margin-{percent}-{mode}-{corner}", chosen, mode, corner);
                    }
                }
            }
        }
        finally
        {
            SaveReport("camera-margin-scales-report");
        }

        AssertNoProblems();
    }

    /// <summary>
    /// The chosen second monitor really switched off while the app runs (T-066): the camera opens on the primary monitor
    /// and the settings show the monitor as "not connected"; when it is back (a new settings window, the list is read once
    /// per window, T-035 Q10) it is connected again and the camera opens on it.
    /// </summary>
    [Fact]
    [Trait(SystemChanges.Trait, SystemChangeTraits.MonitorOff)]
    public async Task DisconnectedMonitorFallsBackAndComesBack()
    {
        SystemChanges.Require();
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        List<TestMonitor> monitors = TestMonitors.All();
        TestMonitor? second = monitors.FirstOrDefault(static monitor => !monitor.Primary);
        Assert.SkipWhen(second is null, "Needs a second (not primary) monitor.");
        Assert.False(string.IsNullOrEmpty(second!.DevicePath), $"No device path of {second}.");
        await using RtspCamera camera = Simulators.StartCamera();
        await using AppInstance app = await CameraWindowSettings.StartAsync(
            fixture,
            "camera-monitor-off",
            CameraPlacement.Window(second.CameraMonitorJson(), "Normal", normalWidth: NormalSize.Width, normalHeight: NormalSize.Height),
            camera);
        List<string> connected = TestMonitors.ExpectedChoices("en", monitors);
        int secondChoice = TestMonitors.Ordered(monitors).IndexOf(second) + 1;
        _report.AppendLine("Before:").Append(DisplayConfig.Report());
        try
        {
            await using SystemChange<DisplayTopologyState> topology = await SystemChanges.MonitorDisconnectAsync(cancellationToken);
            MonitorOff off = await topology.SwitchOffAsync(second.DevicePath, cancellationToken);
            _report.Append(MonitorDisconnectChanges.Describe(off)).AppendLine("Monitor off:").Append(DisplayConfig.Report());

            nint window = await CameraWindowSettings.OpenCameraAsync(app);
            await ExpectNormalWindowOnAsync(window, TestMonitors.Primary(TestMonitors.All()), "monitor off");
            Screenshots.Save(window, "camera-monitor-off-primary");
            await app.CloseWindowAsync(window);
            List<string> disconnected = [.. TestMonitors.ExpectedChoices("en", TestMonitors.All()), TestMonitors.NotConnectedChoice("en", second.AppName, second.Bounds.Width, second.Bounds.Height)];
            await ExpectMonitorChoicesAsync(app, disconnected, disconnected.Count - 1, "camera-monitor-off-settings");

            List<string> notBack = await topology.RestoreAndCheckWindowsAsync(off, cancellationToken);
            await Wait.UntilAsync(() => TestMonitors.All().Count == monitors.Count, WindowTimeout, "the monitor to be back");
            _report.AppendLine("Monitor back:").Append(DisplayConfig.Report());
            // The app learns about the monitor from Windows a moment later: a settings window opened too early lists it as missing.
            await RetryAsync(() => ExpectMonitorChoicesAsync(app, connected, secondChoice, "camera-monitor-back-settings"), "the monitor connected again in a new settings window");

            window = await CameraWindowSettings.OpenCameraAsync(app);
            await ExpectNormalWindowOnAsync(window, Fresh(second), "monitor back");
            Screenshots.Save(window, "camera-monitor-back");
            await app.CloseWindowAsync(window);
            Assert.True(notBack.Count == 0, $"Windows did not move these windows of other apps back (the test never moves them; is 'Remember window locations based on monitor connection' on?):{Environment.NewLine}{string.Join(Environment.NewLine, notBack)}");
        }
        finally
        {
            _report.AppendLine("After:").Append(DisplayConfig.Report());
            SaveReport("camera-monitor-off-report");
        }
    }

    /// <summary>
    /// Starts a fresh app whose camera window opens in <paramref name="mode"/> at <paramref name="corner"/> of
    /// <paramref name="monitor"/>, opens it and records what is wrong: the monitor, the corner and its 16 DIP margin, the
    /// size, always on top, and any move of the window after it was first shown (WinEvent hook, 1 s).
    /// </summary>
    private async Task CheckOpeningAsync(RtspCamera camera, string label, TestMonitor monitor, string mode, string corner)
    {
        (int Width, int Height) size = mode == "Mini" ? MiniSize : NormalSize;
        await using AppInstance app = await CameraWindowSettings.StartAsync(
            fixture,
            label,
            CameraPlacement.Window(monitor.CameraMonitorJson(), mode, normalCorner: corner, miniCorner: corner, normalWidth: NormalSize.Width, normalHeight: NormalSize.Height),
            camera);
        using WinEventRecorder recorder = WinEventRecorder.Start(app.Process.Id);
        nint window = await CameraWindowSettings.OpenCameraAsync(app);
        try
        {
            await Wait.UntilAsync(() => WindowChecks.ClientSizeDip(window) == size, WindowTimeout, $"{label}: {size.Width}×{size.Height} DIP (now {WindowChecks.ClientSizeDip(window)})");
        }
        catch (TimeoutException ex)
        {
            _problems.Add($"{label}: {ex.Message}");
        }

        await Task.Delay(SettleTime, TestContext.Current.CancellationToken);
        IReadOnlyList<WindowEvent> events = recorder.Events;
        TestMonitor now = Fresh(monitor);
        Native.Rect frame = Native.FrameBounds(window);
        Native.Rect on = Native.MonitorOf(window).Monitor;
        _report.AppendLine(CultureInfo.InvariantCulture, $"{label}: monitor {now}; frame {frame}; window {WindowRect(window)}; topmost {CameraWindowSettings.IsTopmost(window)}");
        foreach (WindowEvent windowEvent in events.Where(e => e.Window == window))
        {
            _report.AppendLine(CultureInfo.InvariantCulture, $"    {windowEvent}");
        }

        Record(label, on.Equals(now.Bounds) ? null : $"on the monitor {on}, expected {now}");
        Record(label, CameraPlacement.Problem(frame, now.Work, corner, now.Dpi));
        Record(label, CameraWindowSettings.IsTopmost(window) == (mode == "Mini") ? null : $"always on top is {CameraWindowSettings.IsTopmost(window)} (defaults: only the mini preview)");
        Record(label, JumpProblem(label, events, window));
        Screenshots.Save(window, label);
    }

    /// <summary>The normal window of <see cref="NormalSize"/> centred in the work area of <paramref name="monitor"/>.</summary>
    private static async Task ExpectNormalWindowOnAsync(nint window, TestMonitor monitor, string context)
    {
        await Wait.UntilAsync(() => WindowChecks.ClientSizeDip(window) == NormalSize, WindowTimeout, $"{context}: the normal window {NormalSize.Width}×{NormalSize.Height} DIP");
        Native.Rect frame = await Wait.StableAsync(() => Native.FrameBounds(window), WindowTimeout, $"{context}: the window to settle");
        Assert.True(Native.MonitorOf(window).Monitor.Equals(monitor.Bounds), $"{context}: the camera window {frame} is not on {monitor}.");
        Assert.Null(CameraPlacement.Problem(frame, monitor.Work, "Center", monitor.Dpi));
    }

    /// <summary>A new settings window lists exactly <paramref name="expected"/> with <paramref name="selected"/> chosen; closed with Cancel.</summary>
    private static async Task ExpectMonitorChoicesAsync(AppInstance app, IReadOnlyList<string> expected, int selected, string screenshot)
    {
        (nint settings, AutomationElement element) = await CameraWindowSettings.OpenTabAsync(app);
        try
        {
            (List<string> items, int chosen) = await CameraWindowSettings.MonitorChoicesAsync(element);
            Screenshots.Save(settings, screenshot);
            Assert.True(
                items.SequenceEqual(expected) && chosen == selected,
                $"Monitor choices{Environment.NewLine}  {string.Join(Environment.NewLine + "  ", items)}{Environment.NewLine}(selected {chosen}), expected{Environment.NewLine}  {string.Join(Environment.NewLine + "  ", expected)}{Environment.NewLine}(selected {selected}).");
        }
        finally
        {
            await CameraWindowSettings.CloseAsync(settings, element, save: false);
        }
    }

    private static async Task RetryAsync(Func<Task> check, string what)
    {
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                await check();
                return;
            }
            catch (Exception ex) when (attempt < 3 && ex is Xunit.Sdk.XunitException)
            {
                await Task.Delay(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
            }
            catch (Exception ex) when (ex is Xunit.Sdk.XunitException)
            {
                throw new InvalidOperationException($"Not after {attempt} attempts: {what}.", ex);
            }
        }
    }

    private static async Task SetScaleAsync(SystemChange<DisplayScaleState> scale, TestMonitor monitor, int percent, CancellationToken cancellationToken)
    {
        Assert.False(string.IsNullOrEmpty(monitor.DevicePath), $"No device path of {monitor}.");
        uint dpi = await scale.SetPercentAsync(monitor.DevicePath, percent, cancellationToken);
        Assert.Equal(DisplayScaleSteps.ExpectedDpi(percent), dpi);
    }

    /// <summary>The camera tests' monitor: the second one when there is one (T-071), with the report header.</summary>
    private TestMonitor Chosen()
    {
        List<TestMonitor> monitors = TestMonitors.All();
        TestMonitor chosen = TestMonitors.ChosenForCamera(monitors);
        _report.Append(DisplayConfig.Report());
        _report.AppendLine(CultureInfo.InvariantCulture, $"Monitors: {string.Join("; ", monitors)}");
        _report.AppendLine(CultureInfo.InvariantCulture, $"Chosen: {chosen}");
        return chosen;
    }

    /// <summary>
    /// <paramref name="monitor"/> read again (work area and DPI change with the scale; the GDI name may change when a
    /// monitor comes back), found by its device path.
    /// </summary>
    private static TestMonitor Fresh(TestMonitor monitor) =>
        TestMonitors.All().FirstOrDefault(m => monitor.DevicePath.Length > 0
            ? string.Equals(m.DevicePath, monitor.DevicePath, StringComparison.OrdinalIgnoreCase)
            : string.Equals(m.GdiName, monitor.GdiName, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"The monitor {monitor} is no longer active.");

    /// <summary>
    /// A move or resize of the window after it was first shown (a jump the user sees); notifications that leave the
    /// rectangle as it was are only noted in the report (see <see cref="WinEventRecorder.JumpsAfterShow"/>).
    /// </summary>
    private string? JumpProblem(string label, IReadOnlyList<WindowEvent> events, nint window)
    {
        if (!WinEventRecorder.WasShown(events, window))
        {
            return "the WinEvent hook saw no EVENT_OBJECT_SHOW of the window";
        }

        List<WindowEvent> notifications = WinEventRecorder.MovesAfterShow(events, window);
        List<WindowEvent> jumps = WinEventRecorder.JumpsAfterShow(events, window);
        if (notifications.Count > jumps.Count)
        {
            _report.AppendLine(CultureInfo.InvariantCulture, $"  {label}: {notifications.Count - jumps.Count} LOCATIONCHANGE after SHOW with the unchanged rectangle (no move)");
        }

        return jumps.Count == 0 ? null : $"the window moved {jumps.Count}× after it was shown: {string.Join(", ", jumps)}";
    }

    private static Native.Rect WindowRect(nint hwnd)
    {
        Assert.True(Native.GetWindowRect(hwnd, out Native.Rect rect));
        return rect;
    }

    private void Record(string label, string? problem)
    {
        if (problem is not null)
        {
            _problems.Add($"{label}: {problem}");
        }
    }

    private void AssertNoProblems() => Assert.True(_problems.Count == 0, string.Join(Environment.NewLine, _problems));

    private void SaveReport(string name)
    {
        if (_problems.Count > 0)
        {
            _report.AppendLine("Problems:").AppendLine(string.Join(Environment.NewLine, _problems));
        }

        Screenshots.SaveText(_report.ToString(), name);
    }
}
