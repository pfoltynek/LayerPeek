using System.Diagnostics;

using PrusaTrayDiagnostic.App.E2E.Tests.Infrastructure;

namespace PrusaTrayDiagnostic.App.E2E.Tests;

/// <summary>
/// A second start of the real exe (T-043): it hands over to the running instance and exits without a second process;
/// the running instance brings its most recently used window to the front (restoring it when minimized), or opens the
/// settings when no window is open.
/// </summary>
public sealed class SingleInstanceTests(E2EFixture fixture)
{
    [Fact]
    public async Task SecondStartOpensTheSettingsAndThenActivatesTheMostRecentWindow()
    {
        E2EFixture.Require();
        await using AppInstance app = await AppInstance.StartAsync(fixture, "single-instance", TestSettings.Json("en", []));

        // No window open: the second start opens the settings.
        await StartSecondAndExpectHandoverAsync(app);
        nint settings = await app.WaitForWindowAsync(AppWindowKind.Settings);
        Screenshots.Save(settings, "single-instance-1-second-start-opens-settings");

        // Settings closed; About opened, then the settings again: the settings are the most recently used window. Both
        // minimized (About first), the second start restores the most recent one in front and leaves About minimized.
        await app.CloseWindowAsync(settings);
        nint about = await app.OpenAsync("about", AppWindowKind.Other);
        settings = await app.OpenAsync("settings", AppWindowKind.Settings);
        foreach (nint hwnd in new[] { about, settings })
        {
            Native.ShowWindow(hwnd, Native.SwMinimize);
            await Wait.UntilAsync(() => Native.IsIconic(hwnd), TimeSpan.FromSeconds(5), "the window minimized");
        }

        await StartSecondAndExpectHandoverAsync(app);
        await Wait.UntilAsync(
            () => !Native.IsIconic(settings) && app.Windows().FirstOrDefault() == settings,
            TimeSpan.FromSeconds(10),
            "the settings (most recently used) restored in front");
        Assert.True(Native.IsIconic(about), "About was not the most recent window and must stay minimized.");
        Screenshots.Save(settings, "single-instance-2-most-recent-restored");

        // Still one app process of this run.
        Assert.Equal([app.Process.Id], AppProcesses.FindMarked(app.RunDirectory));
    }

    /// <summary>The second start exits with 0 (within its 5 s budget) and leaves no second process.</summary>
    private static async Task StartSecondAndExpectHandoverAsync(AppInstance app)
    {
        using Process second = Process.Start(app.SecondStartInfo()) ?? throw new InvalidOperationException("The second start failed.");
        try
        {
            await second.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15));
        }
        finally
        {
            if (!second.HasExited)
            {
                second.Kill(entireProcessTree: true);
            }
        }

        Assert.Equal(0, second.ExitCode);
        Assert.Equal([app.Process.Id], AppProcesses.FindMarked(app.RunDirectory));

        // T-024: the second start wrote its records into the same log file before it exited; the first one keeps writing.
        string log = app.ReadLog();
        Assert.Contains($"[pid {second.Id}] PrusaTrayDiagnostic.App.Program: Another instance is running", log, StringComparison.Ordinal);
        Assert.Contains($"[pid {app.Process.Id}] ", log, StringComparison.Ordinal);
    }
}
