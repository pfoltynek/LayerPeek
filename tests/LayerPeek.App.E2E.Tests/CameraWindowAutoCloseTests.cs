using LayerPeek.App.E2E.Tests.Infrastructure;
using LayerPeek.Simulator.Camera;
using LayerPeek.Simulator.Printers;

namespace LayerPeek.App.E2E.Tests;

/// <summary>
/// T-151 on the real app against the simulator (PrusaLink printer and RTSP camera on 127.0.0.1), with the delays shortened by
/// the Debug switches <see cref="AppSwitches.TestCameraCloseAfterVariable"/> / <see cref="AppSwitches.TestCameraOpenAfterVariable"/>:
/// the camera window of a printer that goes offline closes, and opens again in the same place once it is back, without
/// taking the foreground from the active window of another process (user decision 2026-11-03 #1); a window the user closed
/// does not open again.
/// </summary>
public sealed class CameraWindowAutoCloseTests(E2EFixture fixture)
{
    private const string PrinterId = "e2e-auto-close";

    private static readonly TimeSpan CloseAfter = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan OpenAfter = TimeSpan.FromSeconds(2);

    /// <summary>Enough for the delay, the poll interval and the window to appear or go.</summary>
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(40);

    [Fact]
    public async Task OfflineClosesTheWindowOnlineReopensItInPlaceWithoutTheFocusAndAUserCloseIsFinal()
    {
        E2EFixture.Require();
        await using SimulatedPrinter printer = await Simulators.StartPrinterAsync(PrinterScenario.Idle);
        await using RtspCamera camera = Simulators.StartCamera();
        await using AppInstance app = await AppInstance.StartAsync(
            fixture,
            "camera-auto-close",
            TestSettings.Json("en", [new TestPrinter(PrinterId, "Core One", printer.Address(), Camera: camera.StreamUri)]),
            Simulators.Secrets(PrinterId, printer),
            switches: new Dictionary<string, string>
            {
                [AppSwitches.TestCameraCloseAfterVariable] = ((int)CloseAfter.TotalMilliseconds).ToString(System.Globalization.CultureInfo.InvariantCulture),
                [AppSwitches.TestCameraOpenAfterVariable] = ((int)OpenAfter.TotalMilliseconds).ToString(System.Globalization.CultureInfo.InvariantCulture),
            });

        nint window = await app.OpenAsync($"camera:{PrinterId}", AppWindowKind.Camera);
        // The printer answers (a window opened before that would wait for it, T-150 decision 7).
        int requests = printer.RequestCount;
        await Wait.UntilAsync(() => printer.RequestCount > requests + 2, Timeout, "the printer to be polled");
        Assert.True(Native.GetWindowRect(window, out Native.Rect placed));
        Screenshots.Save(window, "camera-auto-close-1-open");

        printer.Simulation.SetOffline(true);
        await Wait.UntilAsync(() => !Native.IsWindowVisible(window), Timeout, "the camera window to close automatically");
        await Wait.UntilAsync(() => app.ReadLog().Contains($"Closing the camera window of printer {PrinterId}: the printer is offline.", StringComparison.Ordinal), TimeSpan.FromSeconds(5), "the automatic close in the log");
        await Wait.UntilAsync(() => File.ReadAllText(Path.Combine(app.DataDirectory, "camera-windows.json")).Contains("\"autoClosed\"", StringComparison.Ordinal), TimeSpan.FromSeconds(10), "the flag in camera-windows.json");

        using (ForegroundHolder holder = ForegroundHolder.Create())
        {
            // The test's window is in the foreground, and the app may take it (as if it were allowed to): only the silent
            // open keeps it here.
            await holder.ActivateWithMouseAsync();
            ForegroundHolder.AllowForeground(app.Process.Id);

            printer.Simulation.SetOffline(false);
            nint reopened = await app.WaitForWindowAsync(AppWindowKind.Camera, timeout: Timeout);
            await Task.Delay(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);
            Screenshots.Save(reopened, "camera-auto-close-2-reopened");

            Assert.Equal(holder.Handle, Native.GetForegroundWindow());
            Assert.True(Native.GetWindowRect(reopened, out Native.Rect again));
            Assert.Equal((placed.Left, placed.Top, placed.Right, placed.Bottom), (again.Left, again.Top, again.Right, again.Bottom));
            Assert.Contains($"Opening the camera window of printer {PrinterId} again: the printer is online.", app.ReadLog(), StringComparison.Ordinal);

            // Closed by the user: it stays closed after the next outage.
            await app.CloseWindowAsync(reopened);
        }

        printer.Simulation.SetOffline(true);
        await Task.Delay(CloseAfter * 2, TestContext.Current.CancellationToken);
        printer.Simulation.SetOffline(false);
        await Task.Delay(OpenAfter * 4, TestContext.Current.CancellationToken);
        Assert.DoesNotContain(app.Windows(), hwnd => app.KindOf(hwnd) == AppWindowKind.Camera);
        Assert.DoesNotContain("\"autoClosed\"", File.ReadAllText(Path.Combine(app.DataDirectory, "camera-windows.json")), StringComparison.Ordinal);
    }
}
