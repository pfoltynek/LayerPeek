using LayerPeek.App.Composition;
using LayerPeek.App.Platform.Windows;
using LayerPeek.App.Tests.Tray;

namespace LayerPeek.App.Tests.Platform.Windows;

/// <summary>
/// Start after logon (T-017, user decision 2026-10-24): the app's own shortcut in the Startup folder, off by default. Its
/// target is the running exe, inside the installation folder for the installed app, so Velopack removes it on uninstall
/// (the uninstall hook too). In memory only: nothing touches the user's Startup folder.
/// </summary>
public sealed class StartupShortcutAutostartTests
{
    private const string ShortcutPath = @"C:\Users\u\AppData\Roaming\Microsoft\Windows\Start Menu\Programs\Startup\LayerPeek.lnk";
    private const string Exe = @"C:\Users\u\AppData\Local\LayerPeek\current\LayerPeek.exe";

    private static readonly AppShortcut Desired = new(Exe, "LayerPeek", new Guid("EC47413C-984D-447A-866A-5F780E408DE7"));

    private readonly InMemoryShortcutStore _store = new();

    [Fact]
    public void OffWithoutTheShortcut()
    {
        Assert.False(Autostart().IsEnabled());
    }

    [Fact]
    public void OnWhenTheShortcutStartsThisExe()
    {
        _store.Shortcuts[ShortcutPath] = Desired;

        Assert.True(Autostart().IsEnabled());
    }

    /// <summary>A shortcut of another copy (a <c>.zip</c> or a development build) does not start this one.</summary>
    [Fact]
    public void OffWhenTheShortcutStartsAnotherCopy()
    {
        _store.Shortcuts[ShortcutPath] = Desired with { TargetPath = @"C:\Tools\LayerPeek\LayerPeek.exe" };

        Assert.False(Autostart().IsEnabled());
    }

    [Fact]
    public void SwitchingOnWritesTheShortcutWithTheIdentityOfTheStartMenuShortcut()
    {
        Autostart().SetEnabled(true);

        // The same AppUserModelID and activator as the Start menu shortcut: a Velopack update rewrites the id of every
        // shortcut into the installation anyway, and Windows may take either shortcut for the toasts of the id.
        Assert.Equal(Desired, _store.Shortcuts[ShortcutPath]);
    }

    [Fact]
    public void SwitchingOnAgainWritesNothing()
    {
        _store.Shortcuts[ShortcutPath] = Desired;

        Autostart().SetEnabled(true);

        Assert.Equal(0, _store.WriteCount);
    }

    [Fact]
    public void SwitchingOnReplacesTheShortcutOfAnotherCopy()
    {
        _store.Shortcuts[ShortcutPath] = Desired with { TargetPath = @"C:\Tools\LayerPeek\LayerPeek.exe" };

        Autostart().SetEnabled(true);

        Assert.Equal(Desired, _store.Shortcuts[ShortcutPath]);
    }

    [Fact]
    public void SwitchingOffDeletesTheShortcut()
    {
        _store.Shortcuts[ShortcutPath] = Desired;

        Autostart().SetEnabled(false);

        Assert.Empty(_store.Shortcuts);
        Assert.Equal([ShortcutPath], _store.Deleted);
    }

    [Fact]
    public void SwitchingOffWithoutTheShortcutIsFine()
    {
        Autostart().SetEnabled(false);

        Assert.Empty(_store.Shortcuts);
    }

    [Fact]
    public void FailuresReachTheCaller()
    {
        _store.WriteFailure = new UnauthorizedAccessException("denied");

        Assert.Throws<UnauthorizedAccessException>(() => Autostart().SetEnabled(true));
    }

    [Fact]
    public void AnUnreadableShortcutCountsAsOff()
    {
        _store.ReadFailure = new IOException("damaged");

        Assert.False(Autostart().IsEnabled());
    }

    [Fact]
    public void TheShortcutHasTheNameOfTheApp()
    {
        Assert.Equal("LayerPeek.lnk", StartupShortcutAutostart.ShortcutFileName);
        Assert.Equal(AppShortcutRegistration.ShortcutFileName, StartupShortcutAutostart.ShortcutFileName);
    }

#if DEBUG
    /// <summary>The test mode (T-044) never touches the user's Startup folder: its shortcut lives in the test data folder.</summary>
    [Fact]
    public void TheTestModeKeepsItsAutostartShortcutInItsDataFolder()
    {
        string data = Path.Combine(Path.GetTempPath(), "LayerPeek.Autostart." + Guid.NewGuid().ToString("N"));

        string path = AppEnvironment.ForTests(data).AutostartShortcutPath();

        Assert.Equal(Path.Combine(data, AppEnvironment.TestStartupFolderName, "LayerPeek.lnk"), path);
        Assert.False(Directory.Exists(data));
    }
#endif

    [Fact]
    public void ProductionUsesTheStartupFolderOfTheUser()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Skip("The Startup folder exists on Windows only.");
        }

        Assert.Equal(
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup), "LayerPeek.lnk"),
            AppEnvironment.Production.AutostartShortcutPath());
    }

    private StartupShortcutAutostart Autostart() => new(_store, ShortcutPath, Desired, new ListLogger<StartupShortcutAutostart>());
}
