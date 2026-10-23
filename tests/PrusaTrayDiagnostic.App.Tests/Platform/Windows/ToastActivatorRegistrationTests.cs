using System.Runtime.Versioning;

using Microsoft.Extensions.Logging;
using Microsoft.Win32;

using PrusaTrayDiagnostic.App.Platform.Windows;
using PrusaTrayDiagnostic.App.Tests.Tray;

namespace PrusaTrayDiagnostic.App.Tests.Platform.Windows;

/// <summary>
/// The registration of the COM toast activator (T-068): <c>HKCU\Software\Classes\CLSID\{clsid}\LocalServer32</c> points to
/// the exe and is written only when it differs; the Start menu shortcut carries the CLSID only when the server is
/// registered. Fakes stand in for the registry and the Start menu; the real registry only under the test key.
/// </summary>
public sealed class ToastActivatorRegistrationTests
{
    private static readonly Guid Clsid = new("0B8D1A2C-3E4F-4A5B-9C6D-7E8F9A0B1C2D");
    private const string CommandLine = "\"C:\\Apps\\PrusaTrayDiagnostic.exe\" -ToastActivated";

    private readonly InMemoryComServerRegistry _registry = new();
    private readonly ListLogger<ToastActivatorRegistration> _logger = new();

    [Fact]
    public void MissingServerIsRegistered()
    {
        Assert.Equal(ShortcutAction.Create, new ToastActivatorRegistration(_registry, _logger).Ensure(Clsid, CommandLine));

        Assert.Equal(CommandLine, _registry.Servers[Clsid]);
        Assert.Equal(1, _registry.WriteCount);
    }

    [Fact]
    public void RegisteredServerIsOnlyRead()
    {
        _registry.Servers[Clsid] = CommandLine;

        Assert.Equal(ShortcutAction.UpToDate, new ToastActivatorRegistration(_registry, _logger).Ensure(Clsid, CommandLine));

        Assert.Equal(0, _registry.WriteCount);
    }

    [Theory]
    [InlineData("\"C:\\Old\\PrusaTrayDiagnostic.exe\" -ToastActivated")]
    [InlineData("\"C:\\Apps\\PrusaTrayDiagnostic.exe\"")]
    [InlineData("")]
    public void ServerOfAnotherExeOrCommandLineIsUpdated(string registered)
    {
        // The exe moved (dev build vs. installed app), or the command line changed: the last started exe wins, like the shortcut.
        _registry.Servers[Clsid] = registered;

        Assert.Equal(ShortcutAction.Update, new ToastActivatorRegistration(_registry, _logger).Ensure(Clsid, CommandLine));

        Assert.Equal(CommandLine, _registry.Servers[Clsid]);
    }

    [Fact]
    public void UnreadableServerIsRewritten()
    {
        _registry.ReadFailure = new UnauthorizedAccessException("denied");

        Assert.Equal(ShortcutAction.Update, new ToastActivatorRegistration(_registry, _logger).Ensure(Clsid, CommandLine));

        Assert.Equal(CommandLine, _registry.Servers[Clsid]);
        Assert.Contains(_logger.Entries, static entry => entry.Level == LogLevel.Warning && entry.Exception is UnauthorizedAccessException);
    }

    [Fact]
    public void WriteFailureReachesTheCaller()
    {
        _registry.WriteFailure = new UnauthorizedAccessException("denied");

        Assert.Throws<UnauthorizedAccessException>(() => new ToastActivatorRegistration(_registry, _logger).Ensure(Clsid, CommandLine));
    }

    [Fact]
    public void ArgumentsAreChecked()
    {
        ToastActivatorRegistration registration = new(_registry, _logger);

        Assert.Throws<ArgumentException>(() => registration.Ensure(Guid.Empty, CommandLine));
        Assert.Throws<ArgumentException>(() => registration.Ensure(Clsid, " "));
        Assert.Throws<ArgumentNullException>(() => new ToastActivatorRegistration(null!, _logger));
        Assert.Throws<ArgumentNullException>(() => new ToastActivatorRegistration(_registry, null!));
    }

    [Fact]
    [SupportedOSPlatform("windows")]
    public void TheRegistryKeepsTheServerUnderItsClsid()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "The registry exists only on Windows.");
        string root = TestRegistry.NewSubKey();
        try
        {
            WindowsComServerRegistry registry = new(root);
            Assert.Null(registry.ReadLocalServer(Clsid));

            registry.WriteLocalServer(Clsid, CommandLine);

            Assert.Equal(CommandLine, registry.ReadLocalServer(Clsid));
            using (RegistryKey? key = Registry.CurrentUser.OpenSubKey($@"{root}\{{0B8D1A2C-3E4F-4A5B-9C6D-7E8F9A0B1C2D}}\LocalServer32"))
            {
                Assert.NotNull(key);
                Assert.Equal(CommandLine, key.GetValue(null));
                Assert.Equal(RegistryValueKind.String, key.GetValueKind(null));
            }

            using (RegistryKey? key = Registry.CurrentUser.OpenSubKey($@"{root}\{{0B8D1A2C-3E4F-4A5B-9C6D-7E8F9A0B1C2D}}"))
            {
                Assert.Equal("Prusa Tray Diagnostic", key?.GetValue(null));
            }

            Assert.True(registry.Delete(Clsid));
            Assert.Null(registry.ReadLocalServer(Clsid));
            Assert.False(registry.Delete(Clsid));
            using RegistryKey? parent = Registry.CurrentUser.OpenSubKey(root);
            Assert.Empty(parent?.GetSubKeyNames() ?? []);
        }
        finally
        {
            TestRegistry.Delete(root);
        }
    }

    [Fact]
    [SupportedOSPlatform("windows")]
    public void ProductionRegistryLivesInTheUsersClasses()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "The registry exists only on Windows.");

        Assert.Equal(@"Software\Classes\CLSID", WindowsComServerRegistry.DefaultSubKey);
    }
}

/// <summary>The registry of COM servers in memory: nothing touches the real registry.</summary>
internal sealed class InMemoryComServerRegistry : IComServerRegistry
{
    public Dictionary<Guid, string> Servers { get; } = [];

    public Exception? ReadFailure { get; set; }

    public Exception? WriteFailure { get; set; }

    public Exception? DeleteFailure { get; set; }

    public int WriteCount { get; private set; }

    public List<Guid> Deleted { get; } = [];

    public string? ReadLocalServer(Guid clsid) =>
        ReadFailure is not null ? throw ReadFailure : Servers.GetValueOrDefault(clsid);

    public void WriteLocalServer(Guid clsid, string commandLine)
    {
        if (WriteFailure is not null)
        {
            throw WriteFailure;
        }

        WriteCount++;
        Servers[clsid] = commandLine;
    }

    public bool Delete(Guid clsid)
    {
        if (DeleteFailure is not null)
        {
            throw DeleteFailure;
        }

        Deleted.Add(clsid);
        return Servers.Remove(clsid);
    }
}
