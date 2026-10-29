#if DEBUG
using Microsoft.Extensions.DependencyInjection;

using LayerPeek.App.Composition;
using LayerPeek.App.Logging;
using LayerPeek.App.Tests.Notifications;
using LayerPeek.App.Tests.Tray;
using LayerPeek.App.Tray;
using LayerPeek.App.Tray.Menu;

namespace LayerPeek.App.Tests.Composition;

/// <summary>
/// T-099: the real composition split. The services start without any UI object (on the startup dispatcher, as while macOS
/// waits for a display), their UI-thread work waits for the main thread, and the tray attaches to the same instances
/// without a second monitoring session. Test mode in a temporary data folder, no printers: no network, no user data.
/// </summary>
public sealed class ServiceHostTests
{
    private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(3);

    [Fact]
    public async Task TheTrayAttachesToTheServicesThatRanBeforeTheUi()
    {
        string folder = TestDirectories.NewDirectoryPath();
        Directory.CreateDirectory(folder);
        try
        {
            AppEnvironment environment = AppEnvironment.ForTests(folder);
            StartupUiDispatcher dispatcher = new();
            ServiceHost core = await ServiceHost.CreateAsync(environment, AppLogging.Disabled(), dispatcher);
            Assert.False(core.IsMonitoring);
            Assert.Same(dispatcher, core.Services.GetRequiredService<IUiDispatcher>());

            core.StartMonitoring();
            Assert.True(core.IsMonitoring);
            MonitoringSession session = core.Services.GetRequiredService<MonitoringSession>();
            TrayStatusViewModel status = core.Services.GetRequiredService<TrayStatusViewModel>();
            // The first run (no printers) updates the tray through the UI thread's dispatcher, which waits for the main thread.
            await Waiting.ForAsync(() => dispatcher.PendingCount > 0);
            Assert.True(dispatcher.RunPending() > 0);

            // The display woke: Avalonia's dispatcher takes over and the tray attaches; a second Start of the session would throw.
            ManualDispatcher avalonia = new();
            dispatcher.AttachTo(avalonia);
            TrayHost tray = TrayHost.Attach(core, trayIcon: null, new NoCommands(), environment);

            Assert.Same(core, tray.Core);
            Assert.Same(session, tray.Core.Services.GetRequiredService<MonitoringSession>());
            Assert.Same(status, tray.Core.Services.GetRequiredService<TrayStatusViewModel>());
            await tray.StopAsync(StopTimeout);
            avalonia.RunAll();
        }
        finally
        {
            TestDirectories.Delete(folder);
        }
    }

    [Fact]
    public async Task TheNormalStartCreatesAndStartsTheServicesWithTheTray()
    {
        string folder = TestDirectories.NewDirectoryPath();
        Directory.CreateDirectory(folder);
        try
        {
            AppEnvironment environment = AppEnvironment.ForTests(folder);
            ServiceHost core = await ServiceHost.CreateAsync(environment, AppLogging.Disabled(), new ManualDispatcher());

            TrayHost tray = TrayHost.Attach(core, trayIcon: null, new NoCommands(), environment);

            Assert.True(core.IsMonitoring);
            // Stopping before anything ran on the UI thread is fine, and stopping twice too.
            await tray.StopAsync(StopTimeout);
            await core.StopAsync(StopTimeout);
        }
        finally
        {
            TestDirectories.Delete(folder);
        }
    }

    [Fact]
    public async Task ServicesThatNeverStartedMonitoringStopCleanly()
    {
        string folder = TestDirectories.NewDirectoryPath();
        Directory.CreateDirectory(folder);
        try
        {
            ServiceHost core = await ServiceHost.CreateAsync(AppEnvironment.ForTests(folder), AppLogging.Disabled(), new StartupUiDispatcher());

            await core.StopAsync(StopTimeout);

            Assert.False(core.IsMonitoring);
            // Ignored once stopped: no session is started after quitting.
            core.StartMonitoring();
            Assert.False(core.IsMonitoring);
        }
        finally
        {
            TestDirectories.Delete(folder);
        }
    }

    private sealed class NoCommands : ITrayAppCommands
    {
        public void ShowAbout()
        {
        }

        public void ShowDiagnostics(string? printerId)
        {
        }

        public void ShowSettings()
        {
        }

        public void AddPrinter()
        {
        }

        public void ToggleCamera(string printerId)
        {
        }

        public void Quit()
        {
        }
    }
}
#endif
