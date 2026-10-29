using Avalonia.Controls;
using Avalonia.Threading;

using Microsoft.Extensions.Logging.Abstractions;

using LayerPeek.App.Tray;
using LayerPeek.App.Tray.Icons;
using LayerPeek.App.UiTests.Infrastructure;
using LayerPeek.Core.Printers;

using LayerPeek.App.Tests.Tray;

using static LayerPeek.App.Tests.Tray.TestPrinters;

namespace LayerPeek.App.UiTests.Tray;

/// <summary>
/// The tray icon wired like <c>TrayHost</c> does it (T-018): icon set by OS, template flag, presenter, status view model.
/// A separate <see cref="TrayIcon"/>, so the app's own one keeps its static icon. How AppKit draws it stays a manual check.
/// </summary>
public sealed class MenuBarTrayIconUiTests(HeadlessSession session)
{
    [Fact]
    public Task TemplateSetMarksTheTrayIconAndFollowsTheMonitor() =>
        session.RunAsync(static scope =>
        {
            TrayIcon trayIcon = scope.Defer(new TrayIcon());
            using TemplateTrayIconSet icons = new();
            TrayIconSets.Prepare(trayIcon, icons);
            Assert.True(MacOSProperties.GetIsTemplateIcon(trayIcon));

            FakePrinterMonitor monitor = new();
            TrayStatusViewModel status = scope.Defer(new TrayStatusViewModel(monitor, new AvaloniaUiDispatcher()));
            scope.Defer(new TrayIconPresenter<WindowIcon>(new AvaloniaTrayIconTarget(trayIcon), status, icons.GetIcon, NullLogger.Instance));
            Assert.Same(icons.GetIcon(TrayIconKey.For(TrayStatus.Offline, null)), trayIcon.Icon);

            status.SetPrinters([Definition()]);
            monitor.Raise(Snapshot(PrinterState.Printing, Job(42, TimeSpan.FromMinutes(10))));
            Dispatcher.UIThread.RunJobs();

            Assert.Same(icons.GetIcon(TrayIconKey.For(TrayStatus.Printing, 40)), trayIcon.Icon);
            Assert.Equal(status.ToolTipText, trayIcon.ToolTipText);
            Assert.Contains("42", trayIcon.ToolTipText, StringComparison.Ordinal);

            monitor.Raise(Snapshot(PrinterState.Error));
            Dispatcher.UIThread.RunJobs();

            Assert.Same(icons.GetIcon(TrayIconKey.For(TrayStatus.Error, null)), trayIcon.Icon);
            Assert.True(MacOSProperties.GetIsTemplateIcon(trayIcon));
            return Task.CompletedTask;
        });

    [Fact]
    public Task ColouredSetLeavesTheTrayIconUntouched() =>
        session.RunAsync(static scope =>
        {
            TrayIcon trayIcon = scope.Defer(new TrayIcon());
            using ColorTrayIconSet icons = new(multiSize: true);

            TrayIconSets.Prepare(trayIcon, icons);

            Assert.False(MacOSProperties.GetIsTemplateIcon(trayIcon));
            Assert.False(trayIcon.IsSet(MacOSProperties.IsTemplateIconProperty));
            return Task.CompletedTask;
        });
}
