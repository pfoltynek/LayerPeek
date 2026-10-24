using PrusaTrayDiagnostic.App.E2E.Tests.Infrastructure;

namespace PrusaTrayDiagnostic.App.E2E.Tests;

/// <summary>
/// Unit tests of how the end-to-end tests open the notification center (T-073): through the clock button of the taskbar,
/// never Win+N (another program can take that hotkey, e.g. OneNote), and a window of another program that comes up
/// instead is recognized (and then left alone). They run in every <c>dotnet test</c> and touch nothing of the system.
/// </summary>
public sealed class NotificationCenterTests
{
    [Theory]
    [InlineData("SystemTrayIcon", "SystemTray.OmniButton", true)]
    // Volume/network (OmniButtonRight), input indicator and "Show hidden icons" (NormalButton), "Show desktop".
    [InlineData("SystemTrayIcon", "SystemTray.OmniButtonRight", false)]
    [InlineData("SystemTrayIcon", "SystemTray.NormalButton", false)]
    [InlineData("SystemTrayIcon", "SystemTray.ShowDesktopButton", false)]
    [InlineData("NotifyItemIcon", "SystemTray.OmniButton", false)]
    [InlineData(null, null, false)]
    public void OnlyTheClockOpensTheNotificationCenter(string? automationId, string? className, bool expected) =>
        Assert.Equal(expected, NotificationCenter.IsClockButton(automationId, className));

    [Theory]
    [InlineData("ONENOTE", "Framework::CFrame", true)]
    [InlineData("ONENOTEM", "OneNote Quick Note", true)]
    [InlineData("ShellExperienceHost", "Windows.UI.Core.CoreWindow", false)]
    [InlineData("ShellHost", "ControlCenterWindow", false)]
    [InlineData("explorer", "Shell_TrayWnd", false)]
    [InlineData("explorer", "TopLevelWindowForOverflowXamlIsland", false)]
    [InlineData("PrusaTrayDiagnostic", "Avalonia-9e23af31", false)]
    public void WindowsOfOtherProgramsAreForeign(string process, string className, bool expected) =>
        Assert.Equal(expected, NotificationCenter.IsForeign(process, className, processId: 1));

    [Fact]
    public void WindowsOfTheTestProcessAreNotForeign() =>
        Assert.False(NotificationCenter.IsForeign("anything", "AnyClass", Environment.ProcessId));

    [Fact]
    public void NoTestPressesWinN()
    {
        // The hotkey another program can take (T-073): the notification center opens from the clock only. Built from two
        // parts so that this file does not match itself.
        const string WinNKey = "VirtualKeyShort." + "KEY_N";
        string folder = Path.Combine(PrusaTrayDiagnostic.Tests.Shared.TestWorktree.Root, "tests", "PrusaTrayDiagnostic.App.E2E.Tests");
        IEnumerable<string> files = Directory.EnumerateFiles(folder, "*.cs", SearchOption.AllDirectories)
            .Where(static file => !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal));

        Assert.DoesNotContain(files, static file => File.ReadAllText(file).Contains(WinNKey, StringComparison.Ordinal));
    }
}
