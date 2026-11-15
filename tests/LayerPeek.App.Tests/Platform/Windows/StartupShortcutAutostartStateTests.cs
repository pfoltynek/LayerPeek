using Microsoft.Extensions.Logging.Abstractions;

using LayerPeek.App.Platform;
using LayerPeek.App.Platform.Windows;

namespace LayerPeek.App.Tests.Platform.Windows;

/// <summary>
/// T-201 review: the migration of the autostart choice needs to tell "definitely off" (no shortcut) from "unknown" (a
/// shortcut that cannot be read, e.g. held by an antivirus scan, or one of another copy of the app), so that it never stores
/// "off" while a shortcut exists. <see cref="IAutostart.IsEnabled"/> keeps its answer for the settings window. In memory only.
/// </summary>
public sealed class StartupShortcutAutostartStateTests
{
    private const string ShortcutPath = @"C:\Users\u\Startup\LayerPeek.lnk";
    private const string Exe = @"C:\Users\u\AppData\Local\LayerPeek\current\LayerPeek.exe";

    private readonly InMemoryShortcutStore _store = new();

    [Fact]
    public void WithoutAShortcutItIsOff()
    {
        Assert.Equal(AutostartState.Off, Autostart().QueryState());
        Assert.False(Autostart().IsEnabled());
    }

    [Fact]
    public void AShortcutOfThisExeIsOn()
    {
        _store.Shortcuts[ShortcutPath] = new AppShortcut(Exe, "LayerPeek");

        Assert.Equal(AutostartState.On, Autostart().QueryState());
        Assert.True(Autostart().IsEnabled());
    }

    [Fact]
    public void AShortcutOfAnotherCopyIsUnknown()
    {
        _store.Shortcuts[ShortcutPath] = new AppShortcut(@"C:\Tools\LayerPeek\LayerPeek.exe", "LayerPeek");

        Assert.Equal(AutostartState.Unknown, Autostart().QueryState());
        // Unchanged for the settings window: off (switching on replaces it).
        Assert.False(Autostart().IsEnabled());
        Assert.Equal(0, _store.WriteCount);
        Assert.Empty(_store.Deleted);
    }

    [Fact]
    public void AShortcutThatCannotBeReadIsUnknown()
    {
        _store.Shortcuts[ShortcutPath] = new AppShortcut(Exe, "LayerPeek");
        _store.ReadFailure = new IOException("held by another process");

        Assert.Equal(AutostartState.Unknown, Autostart().QueryState());
        Assert.False(Autostart().IsEnabled());
        Assert.Equal(0, _store.WriteCount);
        Assert.Empty(_store.Deleted);
    }

    private StartupShortcutAutostart Autostart() =>
        new(_store, ShortcutPath, new AppShortcut(Exe, "LayerPeek"), NullLogger<StartupShortcutAutostart>.Instance);
}
