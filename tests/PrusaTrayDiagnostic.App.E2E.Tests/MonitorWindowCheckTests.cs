using System.Drawing;
using System.Drawing.Imaging;
using System.Text.Json.Nodes;

using PrusaTrayDiagnostic.App.E2E.Tests.Infrastructure;

namespace PrusaTrayDiagnostic.App.E2E.Tests;

/// <summary>
/// Unit tests of the pure parts of the monitor, always-on-top and scale checks of T-071 (placement and margin, expected
/// monitor choices from the resx, jump detection, the screen area guard, the cover window geometry, the Tab order):
/// they run in every <c>dotnet test</c> and read or change nothing of the system.
/// </summary>
public sealed class MonitorWindowCheckTests
{
    private const string DevicePath = @"\\?\DISPLAY#DEL415A#5&224edec0&0&UID160002#{e6f07b5f-ee97-4a90-b076-33f57bf4eaa7}";

    private static readonly Native.Rect Work = Rect(0, 0, 2560, 1392);

    public static TheoryData<string> Languages => [.. TestSettings.Languages];

    [Theory]
    [InlineData(96u, 16)]
    [InlineData(120u, 20)]
    [InlineData(144u, 24)]
    [InlineData(168u, 28)]
    public void MarginIs16DipAtTheDpiOfTheMonitor(uint dpi, int pixels) =>
        Assert.Equal(pixels, CameraPlacement.MarginPx(dpi));

    [Theory]
    [InlineData("TopLeft", 20, 20)]
    [InlineData("TopRight", 2560 - 20 - 400, 20)]
    [InlineData("BottomLeft", 20, 1392 - 20 - 300)]
    [InlineData("BottomRight", 2560 - 20 - 400, 1392 - 20 - 300)]
    [InlineData("Center", 1080, 546)]
    public void FrameInItsPlaceWithinTheToleranceIsAccepted(string corner, int left, int top)
    {
        Assert.Null(CameraPlacement.Problem(Rect(left, top, 400, 300), Work, corner, 120));
        Assert.Null(CameraPlacement.Problem(Rect(left + 2, top - 2, 400, 300), Work, corner, 120));
    }

    [Theory]
    [InlineData("TopLeft", 23, 20)]
    [InlineData("BottomRight", 2560 - 20 - 400, 1392 - 20 - 300 + 3)]
    [InlineData("Center", 1085, 546)]
    public void FrameOffItsPlaceIsReportedWithTheOffset(string corner, int left, int top)
    {
        string? problem = CameraPlacement.Problem(Rect(left, top, 400, 300), Work, corner, 120);

        Assert.NotNull(problem);
        Assert.Contains(corner, problem, StringComparison.Ordinal);
        Assert.Contains("margin 20 px at 120 dpi", problem, StringComparison.Ordinal);
    }

    [Fact]
    public void MarginFollowsTheDpiOfTheMonitor()
    {
        // 16 px is right at 100 % but 8 px short at 150 %.
        Native.Rect frame = Rect(16, 16, 400, 300);

        Assert.Null(CameraPlacement.Problem(frame, Work, "TopLeft", 96));
        Assert.NotNull(CameraPlacement.Problem(frame, Work, "TopLeft", 144));
    }

    [Fact]
    public void UnknownPlacementIsRefused() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => CameraPlacement.Problem(Rect(0, 0, 10, 10), Work, "Middle", 96));

    [Fact]
    public void CameraWindowSectionTurnsRememberingOffAndCarriesTheChosenMonitor()
    {
        JsonObject monitor = Monitor("DELL U2719D", 2560, 0, 1440, 2560).CameraMonitorJson();

        JsonObject window = CameraPlacement.Window(monitor, "Mini", normalCorner: "TopLeft", miniCorner: "Center", topmostNormal: true, topmostMini: false);

        Assert.False(window["rememberLastPlacement"]!.GetValue<bool>());
        Assert.Equal("Mini", window["startMode"]!.GetValue<string>());
        Assert.Equal("TopLeft", window["normal"]!["corner"]!.GetValue<string>());
        Assert.Equal("Center", window["mini"]!["corner"]!.GetValue<string>());
        Assert.True(window["topmostNormal"]!.GetValue<bool>());
        Assert.False(window["topmostMini"]!.GetValue<bool>());
        Assert.Equal("""{"displayName":"DELL U2719D","width":1440,"height":2560,"devicePath":"\\\\?\\DISPLAY#DEL415A#5\u0026224edec0\u00260\u0026UID160002#{e6f07b5f-ee97-4a90-b076-33f57bf4eaa7}"}""", window["monitor"]!.ToJsonString());
        Assert.Null(CameraPlacement.Window(null, "Normal")["monitor"]);
    }

    [Fact]
    public void MonitorWithoutAFriendlyNameIsStoredUnderItsGdiName()
    {
        TestMonitor monitor = Monitor(string.Empty, 0, 0, 1920, 1080);

        Assert.Equal(@"\\.\DISPLAY1", monitor.AppName);
        Assert.Equal(@"\\.\DISPLAY1", monitor.CameraMonitorJson()["displayName"]!.GetValue<string>());
        Assert.True(monitor.Primary);
    }

    [Fact]
    public void ExpectedChoicesNumberTheMonitorsFromLeftToRight()
    {
        TestMonitor right = Monitor("DELL U2719D", 2560, 0, 1440, 2560, gdi: @"\\.\DISPLAY2");
        TestMonitor left = Monitor("DELL U2719D", 0, 0, 2560, 1440);

        List<string> choices = TestMonitors.ExpectedChoices("en", [right, left]);

        Assert.Equal(["Primary monitor", "Monitor 1 – DELL U2719D (2560\u00A0×\u00A01440)", "Monitor 2 – DELL U2719D (1440\u00A0×\u00A02560)"], choices);
    }

    [Fact]
    public void MonitorAboveAnotherIsNumberedAfterIt()
    {
        TestMonitor below = Monitor("Below", 0, 1440, 2560, 1440, gdi: @"\\.\DISPLAY2");
        TestMonitor primary = Monitor("Primary", 0, 0, 2560, 1440);
        TestMonitor leftOf = Monitor("Left", -1920, 0, 1920, 1080, gdi: @"\\.\DISPLAY3");

        Assert.Equal(["Left", "Primary", "Below"], TestMonitors.Ordered([below, primary, leftOf]).Select(static m => m.FriendlyName));
    }

    [Fact]
    public void MonitorWithoutAFriendlyNameUsesTheUnnamedChoice()
    {
        List<string> choices = TestMonitors.ExpectedChoices("cs", [Monitor(string.Empty, 0, 0, 1920, 1080)]);

        Assert.Equal(["Primární monitor", "Monitor 1 (1920\u00A0×\u00A01080)"], choices);
    }

    [Theory]
    [MemberData(nameof(Languages))]
    public void ExpectedChoicesUseTheTextsOfEveryLanguage(string language)
    {
        List<string> choices = TestMonitors.ExpectedChoices(language, [Monitor("DELL U2719D", 0, 0, 2560, 1440), Monitor(string.Empty, 2560, 0, 1440, 2560, gdi: @"\\.\DISPLAY2")]);
        string notConnected = TestMonitors.NotConnectedChoice(language, "E2E Missing Monitor", 1234, 567);

        Assert.Equal(3, choices.Count);
        Assert.Equal(AppStrings.Get(language, "SettingsCameraWindowMonitorPrimary"), choices[0]);
        Assert.Contains("DELL U2719D", choices[1], StringComparison.Ordinal);
        Assert.Contains("2560\u00A0×\u00A01440", choices[1], StringComparison.Ordinal);
        Assert.Contains("1440\u00A0×\u00A02560", choices[2], StringComparison.Ordinal);
        Assert.StartsWith("E2E Missing Monitor (1234\u00A0×\u00A0567) – ", notConnected, StringComparison.Ordinal);
        Assert.All(choices.Append(notConnected), static choice => Assert.DoesNotContain("{", choice, StringComparison.Ordinal));
    }

    [Fact]
    public void NotConnectedChoiceFollowsTheResx()
    {
        Assert.Equal("E2E Missing Monitor (1234\u00A0×\u00A0567) – not connected", TestMonitors.NotConnectedChoice("en", "E2E Missing Monitor", 1234, 567));
        Assert.Equal("E2E Missing Monitor (1234\u00A0×\u00A0567) – nepřipojený", TestMonitors.NotConnectedChoice("cs", "E2E Missing Monitor", 1234, 567));
        // A saved GDI name is no name people recognise: the generic word instead.
        Assert.Equal("Bildschirm (1920\u00A0×\u00A01080) – nicht angeschlossen", TestMonitors.NotConnectedChoice("de", @"\\.\DISPLAY7", 1920, 1080));
    }

    [Fact]
    public void SwitchedOffMonitorMatchesOnlyByItsDevicePath()
    {
        // T-071 Q4 (2026-10-23): a known device path alone decides, a twin with the same name is another monitor.
        TestMonitor saved = Monitor("DELL U2719D", 2560, 0, 1440, 2560, gdi: @"\\.\DISPLAY2") with { DevicePath = "path-2" };
        TestMonitor twin = Monitor("DELL U2719D", 0, 0, 2560, 1440) with { DevicePath = "path-1" };
        TestMonitor rotatedTwin = Monitor("DELL U2719D", -1440, 0, 1440, 2560, gdi: @"\\.\DISPLAY3") with { DevicePath = "path-3" };
        TestMonitor other = Monitor("Other", 0, 0, 1920, 1080) with { DevicePath = "path-4" };

        Assert.Equal(saved, TestMonitors.Matching(saved, [twin, saved]));
        Assert.Equal("PATH-2", TestMonitors.Matching(saved, [twin, saved with { DevicePath = "PATH-2" }])?.DevicePath);
        Assert.Null(TestMonitors.Matching(saved, [twin, rotatedTwin]));
        Assert.Null(TestMonitors.Matching(saved, [twin, other]));
        Assert.Null(TestMonitors.Matching(saved, [other]));
    }

    [Fact]
    public void OldSettingsWithoutDevicePathMatchByNameAndResolutionThenName()
    {
        TestMonitor saved = Monitor("DELL U2719D", 2560, 0, 1440, 2560, gdi: @"\\.\DISPLAY2") with { DevicePath = string.Empty };
        TestMonitor twin = Monitor("DELL U2719D", 0, 0, 2560, 1440) with { DevicePath = "path-1" };
        TestMonitor rotatedTwin = Monitor("DELL U2719D", -1440, 0, 1440, 2560, gdi: @"\\.\DISPLAY3") with { DevicePath = "path-3" };
        TestMonitor other = Monitor("Other", 0, 0, 1920, 1080) with { DevicePath = "path-4" };

        Assert.Equal(rotatedTwin, TestMonitors.Matching(saved, [twin, rotatedTwin]));
        Assert.Equal(twin, TestMonitors.Matching(saved, [twin, other]));
        Assert.Null(TestMonitors.Matching(saved, [other]));
    }

    [Fact]
    public void UnknownTextIsReportedWithItsFile()
    {
        KeyNotFoundException error = Assert.Throws<KeyNotFoundException>(() => AppStrings.Get("fr", "NoSuchText"));

        Assert.Contains("Strings.fr.resx", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void MovesAfterTheFirstShowOfTheWindowAreItsJumps()
    {
        nint camera = 0x100;
        nint other = 0x200;
        WindowEvent[] events =
        [
            Event(WinEventRecorder.EventObjectLocationChange, camera, 0),
            Event(WinEventRecorder.EventObjectShow, other, 0),
            Event(WinEventRecorder.EventObjectShow, camera, 10),
            Event(WinEventRecorder.EventObjectLocationChange, other, 10),
            Event(WinEventRecorder.EventObjectLocationChange, camera, 20),
            Event(WinEventRecorder.EventObjectShow, camera, 20),
        ];

        List<WindowEvent> moves = WinEventRecorder.MovesAfterShow(events, camera);

        WindowEvent move = Assert.Single(moves);
        Assert.Equal(20, move.Rect.Left);
        Assert.True(WinEventRecorder.WasShown(events, camera));
    }

    [Fact]
    public void WindowThatWasNeverShownIsReportedAsNotSeen()
    {
        WindowEvent[] events = [Event(WinEventRecorder.EventObjectLocationChange, 0x100, 0)];

        Assert.Empty(WinEventRecorder.MovesAfterShow(events, 0x100));
        Assert.False(WinEventRecorder.WasShown(events, 0x100));
        Assert.Contains("no EVENT_OBJECT_SHOW", WinEventRecorder.JumpProblem(events, 0x100), StringComparison.Ordinal);
    }

    [Fact]
    public void WindowThatKeepsStillAfterTheShowHasNoJump()
    {
        nint camera = 0x100;
        WindowEvent[] events =
        [
            Event(WinEventRecorder.EventObjectLocationChange, camera, 0),
            Event(WinEventRecorder.EventObjectShow, camera, 10),
            Event(WinEventRecorder.EventObjectLocationChange, 0x200, 30),
        ];

        Assert.Null(WinEventRecorder.JumpProblem(events, camera));
    }

    [Fact]
    public void ChangeNotificationWithTheShownRectangleIsNoJump()
    {
        // T-071 Q3 (decided 2026-10-23): Avalonia / DWM notify a frameless window again after the show without moving it.
        nint camera = 0x100;
        WindowEvent[] events =
        [
            Event(WinEventRecorder.EventObjectShow, camera, 10),
            Event(WinEventRecorder.EventObjectLocationChange, camera, 10),
            Event(WinEventRecorder.EventObjectLocationChange, camera, 10),
        ];

        Assert.Null(WinEventRecorder.JumpProblem(events, camera));
    }

    [Theory]
    [InlineData(1, 0, 0, 0)]
    [InlineData(0, 1, 0, 0)]
    [InlineData(0, 0, 1, 0)]
    [InlineData(0, 0, 0, 1)]
    [InlineData(-2, 0, 0, 0)]
    public void MoveOrResizeAfterTheShowIsAJumpEvenByOnePixel(int dx, int dy, int dWidth, int dHeight)
    {
        nint camera = 0x100;
        Native.Rect shown = Rect(2869, 1005, 820, 489);
        Native.Rect moved = Rect(2869 + dx, 1005 + dy, 820 + dWidth, 489 + dHeight);
        WindowEvent[] events =
        [
            new(WinEventRecorder.EventObjectShow, camera, shown, DateTime.UtcNow),
            new(WinEventRecorder.EventObjectLocationChange, camera, shown, DateTime.UtcNow),
            new(WinEventRecorder.EventObjectLocationChange, camera, moved, DateTime.UtcNow),
            new(WinEventRecorder.EventObjectLocationChange, camera, shown, DateTime.UtcNow),
        ];

        string? problem = WinEventRecorder.JumpProblem(events, camera);

        // Moving back does not undo what the user saw; only the changed rectangles are listed.
        Assert.NotNull(problem);
        Assert.StartsWith("1 move(s) after the window was shown", problem, StringComparison.Ordinal);
        Assert.Contains(moved.ToString(), problem, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MessageLoopThreadSetsUpAndTearsDownOnItsOwnThread()
    {
        int setUpThread = 0;
        int tearDownThread = 0;
        string? tornDown = null;

        MessageLoopThread<string> loop = await MessageLoopThread<string>.StartAsync(
            "E2E loop test",
            () =>
            {
                setUpThread = Environment.CurrentManagedThreadId;
                return "state";
            },
            state =>
            {
                tearDownThread = Environment.CurrentManagedThreadId;
                tornDown = state;
            });
        Assert.Equal("state", loop.State);
        Assert.Null(tornDown);
        loop.Dispose();

        Assert.Equal("state", tornDown);
        Assert.Equal(setUpThread, tearDownThread);
        Assert.NotEqual(Environment.CurrentManagedThreadId, setUpThread);
    }

    [Fact]
    public async Task MessageLoopThreadReportsAFailedSetUp()
    {
        bool tornDown = false;

        InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(() => MessageLoopThread<int>.StartAsync(
            "E2E loop test",
            static () => throw new InvalidOperationException("no hook"),
            _ => tornDown = true));

        Assert.Equal("no hook", error.Message);
        Assert.False(tornDown);
    }

    [Fact]
    public void ProbeInTheMiddleOfACameraWindowIsAllowedByTheCaptureGuard() =>
        // The 64 × 64 px probe of CameraTopmostTests on the portrait monitor (the guard itself is tested with T-069/T-070).
        Assert.Null(ScreenArea.Refusal(new Rectangle(3248, 1248, 64, 64), new Rectangle(2560, 0, 1440, 2560)));

    [Fact]
    public void FractionNearCountsThePixelsOfTheColor()
    {
        using Bitmap bitmap = new(10, 10, PixelFormat.Format32bppArgb);
        using (Graphics graphics = Graphics.FromImage(bitmap))
        {
            graphics.Clear(Color.Black);
            using SolidBrush brush = new(Color.FromArgb(250, 10, 245));
            graphics.FillRectangle(brush, 0, 0, 10, 3);
        }

        Assert.Equal(0.3, Screenshots.FractionNear(bitmap, CoverWindow.Color), 3);
        Assert.Equal(0.7, Screenshots.FractionNear(bitmap, Color.Black), 3);
    }

    [Fact]
    public void CoverGrowsAroundTheFrameWithinTheMonitor()
    {
        Native.Rect monitor = Rect(2560, 0, 1440, 2560);

        Assert.Equal(Rect(2560, 0, 400 + 48 + 16, 300 + 48 + 16), CoverWindow.BoundsFor(Rect(2576, 16, 400, 300), monitor, 48));
        Assert.Equal(monitor, CoverWindow.BoundsFor(monitor, monitor, 48));
    }

    [Fact]
    public void ClickCandidatesStartInTheCentreAndStayInsideTheCover()
    {
        Native.Rect cover = Rect(2560, 0, 464, 364);

        List<Point> candidates = CoverWindow.ClickCandidates(cover);

        Assert.Equal(new Point(2560 + 232, 182), candidates[0]);
        Assert.All(candidates, point => Assert.True(point.X >= cover.Left && point.X < cover.Right && point.Y >= cover.Top && point.Y < cover.Bottom, $"{point} is outside {cover}"));
        Assert.Contains(new Point(2560 + 8, 8), candidates);
    }

    [Fact]
    public void ClickPointsStayInTheTitleBarAwayFromTheCaptionButtons()
    {
        Native.Rect frame = Rect(800, 400, 976, 760);

        List<Point> points = WindowActivation.ClickPoints(frame, hasCaption: true, scaling: 1.25);

        Assert.Equal([new Point(1288, 410), new Point(1044, 410), new Point(922, 410), new Point(1576, 410)], points);
    }

    [Fact]
    public void ClickPointsOfAWindowWithoutCaptionAreInItsMiddleRow()
    {
        List<Point> points = WindowActivation.ClickPoints(Rect(0, 0, 320, 180), hasCaption: false, scaling: 1);

        Assert.Equal([new Point(160, 90), new Point(80, 90), new Point(240, 90)], points);
    }

    [Theory]
    [InlineData("A,B,C")]
    [InlineData("B,C,A,B")]
    [InlineData("C,A,B")]
    public void TabOrderThatGoesRoundTheCycleIsAccepted(string seen) =>
        Assert.True(WindowActivation.FollowsCycle(seen.Split(','), ["A", "B", "C"]));

    [Theory]
    [InlineData("A,C,B")]
    [InlineData("A,B")]
    [InlineData("A,B,X")]
    [InlineData("X,A,B")]
    public void TabOrderThatSkipsOrLeavesTheCycleIsRefused(string seen) =>
        Assert.False(WindowActivation.FollowsCycle(seen.Split(','), ["A", "B", "C"]));

    private static TestMonitor Monitor(string name, int left, int top, int width, int height, string gdi = @"\\.\DISPLAY1") =>
        new(gdi, name, DevicePath, Rect(left, top, width, height), Rect(left, top, width, height - 48), 96);

    private static WindowEvent Event(uint kind, nint window, int left) =>
        new(kind, window, Rect(left, 0, 100, 100), DateTime.UtcNow);

    private static Native.Rect Rect(int left, int top, int width, int height) =>
        new() { Left = left, Top = top, Right = left + width, Bottom = top + height };
}
