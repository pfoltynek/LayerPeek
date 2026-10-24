using System.Runtime.Versioning;

using Microsoft.Extensions.Logging;

using LayerPeek.App.Platform.Windows;
using LayerPeek.App.Tests.Tray;

namespace LayerPeek.App.Tests.Platform.Windows;

/// <summary>
/// What the toast platform registers at startup (T-010, T-068): the COM toast activator first, then the Start menu
/// shortcut with the AppUserModelID and, only when the activator is registered, its CLSID. In-memory fakes stand in for
/// the Start menu and the registry; nothing opens WinRT.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class ToastAppRegistrationTests
{
    private const string Exe = @"C:\Apps\LayerPeek.exe";
    private const string ShortcutPath = @"C:\Users\test\Start Menu\Programs\LayerPeek.lnk";
    private const string CommandLine = "\"C:\\Apps\\LayerPeek.exe\" -ToastActivated";
    private static readonly Guid Clsid = new("0B8D1A2C-3E4F-4A5B-9C6D-7E8F9A0B1C2D");

    private readonly InMemoryShortcutStore _shortcuts = new();
    private readonly InMemoryComServerRegistry _registry = new();
    private readonly ListLogger<WindowsToastPlatform> _logger = new();

    public ToastAppRegistrationTests()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Toasts exist only on Windows.");
    }

    [Fact]
    public void TheActivatorIsRegisteredAndTheShortcutCarriesItsClsid()
    {
        Platform(new ToastActivatorSettings(Clsid, CommandLine)).Register();

        Assert.Equal(CommandLine, _registry.Servers[Clsid]);
        Assert.Equal(new AppShortcut(Exe, "LayerPeek", Clsid), _shortcuts.Shortcuts[ShortcutPath]);
    }

    [Fact]
    public void ASecondStartWritesNothing()
    {
        WindowsToastPlatform platform = Platform(new ToastActivatorSettings(Clsid, CommandLine));
        platform.Register();

        platform.Register();

        Assert.Equal(1, _registry.WriteCount);
        Assert.Equal(1, _shortcuts.WriteCount);
    }

    [Fact]
    public void WithoutARegisteredActivatorTheShortcutHasNoClsid()
    {
        // A CLSID nobody serves would make Windows swallow clicks in the notification center.
        _registry.WriteFailure = new UnauthorizedAccessException("denied");
        _shortcuts.Shortcuts[ShortcutPath] = new AppShortcut(Exe, "LayerPeek", Clsid);

        Platform(new ToastActivatorSettings(Clsid, CommandLine)).Register();

        Assert.Equal(new AppShortcut(Exe, "LayerPeek"), _shortcuts.Shortcuts[ShortcutPath]);
        Assert.Contains(_logger.Entries, static entry => entry.Level == LogLevel.Warning && entry.Exception is UnauthorizedAccessException);
    }

    [Fact]
    public void WithoutAnActivatorOnlyTheShortcutIsWritten()
    {
        Platform(activator: null).Register();

        Assert.Empty(_registry.Servers);
        Assert.Equal(new AppShortcut(Exe, "LayerPeek"), _shortcuts.Shortcuts[ShortcutPath]);
    }

    [Fact]
    public void AFailingShortcutIsLoggedAndTheActivatorStays()
    {
        _shortcuts.WriteFailure = new IOException("disk");

        Platform(new ToastActivatorSettings(Clsid, CommandLine)).Register();

        Assert.Equal(CommandLine, _registry.Servers[Clsid]);
        Assert.Contains(_logger.Entries, static entry => entry.Exception is IOException);
    }

    private WindowsToastPlatform Platform(ToastActivatorSettings? activator) => new(
        new AppShortcutRegistration(_shortcuts, new ListLogger<AppShortcutRegistration>()),
        static () => ShortcutPath,
        Exe,
        _logger,
        "LayerPeek",
        new ToastActivatorRegistration(_registry, new ListLogger<ToastActivatorRegistration>()),
        activator);
}
