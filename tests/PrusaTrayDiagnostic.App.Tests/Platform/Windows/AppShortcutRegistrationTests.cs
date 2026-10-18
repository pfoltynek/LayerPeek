using Microsoft.Extensions.Logging;

using PrusaTrayDiagnostic.App.Platform.Windows;
using PrusaTrayDiagnostic.App.Tests.Tray;

namespace PrusaTrayDiagnostic.App.Tests.Platform.Windows;

/// <summary>Decision logic only: an in-memory store stands in for the Start menu, nothing touches the disk.</summary>
public sealed class AppShortcutRegistrationTests
{
    private const string ShortcutPath = @"C:\Users\test\AppData\Roaming\Microsoft\Windows\Start Menu\Programs\Prusa Tray Diagnostic.lnk";
    private static readonly AppShortcut Desired = new(@"C:\Apps\Prusa\PrusaTrayDiagnostic.exe", "PrusaTrayDiagnostic");

    private readonly InMemoryShortcutStore _store = new();
    private readonly ListLogger<AppShortcutRegistration> _logger = new();

    [Fact]
    public void MissingShortcutIsCreated()
    {
        Assert.Equal(ShortcutAction.Create, AppShortcutRegistration.Decide(null, Desired));
    }

    [Fact]
    public void MatchingShortcutIsUpToDate()
    {
        Assert.Equal(ShortcutAction.UpToDate, AppShortcutRegistration.Decide(Desired with { }, Desired));
    }

    [Fact]
    public void TargetPathIsComparedIgnoringCaseAndNormalized()
    {
        AppShortcut existing = Desired with { TargetPath = @"c:\apps\PRUSA\.\PrusaTrayDiagnostic.EXE" };

        Assert.Equal(ShortcutAction.UpToDate, AppShortcutRegistration.Decide(existing, Desired));
    }

    [Theory]
    [InlineData(@"C:\Old\PrusaTrayDiagnostic.exe", "PrusaTrayDiagnostic")]
    [InlineData(@"C:\Apps\Prusa\PrusaTrayDiagnostic.exe", null)]
    [InlineData(@"C:\Apps\Prusa\PrusaTrayDiagnostic.exe", "")]
    [InlineData(@"C:\Apps\Prusa\PrusaTrayDiagnostic.exe", "Other.App")]
    [InlineData(@"C:\Apps\Prusa\PrusaTrayDiagnostic.exe", "prusatraydiagnostic")]
    [InlineData("", "PrusaTrayDiagnostic")]
    public void DifferentShortcutIsUpdated(string targetPath, string? appUserModelId)
    {
        AppShortcut existing = new(targetPath, appUserModelId);

        Assert.Equal(ShortcutAction.Update, AppShortcutRegistration.Decide(existing, Desired));
    }

    [Fact]
    public void EnsureCreatesMissingShortcut()
    {
        ShortcutAction action = new AppShortcutRegistration(_store, _logger).Ensure(ShortcutPath, Desired);

        Assert.Equal(ShortcutAction.Create, action);
        Assert.Equal(Desired, _store.Shortcuts[ShortcutPath]);
        Assert.Equal(1, _store.WriteCount);
    }

    [Fact]
    public void EnsureLeavesUpToDateShortcutAlone()
    {
        _store.Shortcuts[ShortcutPath] = Desired;

        ShortcutAction action = new AppShortcutRegistration(_store, _logger).Ensure(ShortcutPath, Desired);

        Assert.Equal(ShortcutAction.UpToDate, action);
        Assert.Equal(0, _store.WriteCount);
    }

    [Fact]
    public void EnsureRewritesShortcutOfAMovedExecutable()
    {
        _store.Shortcuts[ShortcutPath] = Desired with { TargetPath = @"C:\Old\PrusaTrayDiagnostic.exe" };

        ShortcutAction action = new AppShortcutRegistration(_store, _logger).Ensure(ShortcutPath, Desired);

        Assert.Equal(ShortcutAction.Update, action);
        Assert.Equal(Desired, _store.Shortcuts[ShortcutPath]);
    }

    [Fact]
    public void UnreadableShortcutIsRewrittenAndLogged()
    {
        _store.ReadFailure = new IOException("corrupt");

        ShortcutAction action = new AppShortcutRegistration(_store, _logger).Ensure(ShortcutPath, Desired);

        Assert.Equal(ShortcutAction.Update, action);
        Assert.Equal(Desired, _store.Shortcuts[ShortcutPath]);
        Assert.Contains(_logger.Entries, e => e.Level == LogLevel.Warning && e.Exception?.Message == "corrupt");
    }

    [Fact]
    public void WriteFailurePropagates()
    {
        _store.WriteFailure = new UnauthorizedAccessException("denied");

        Assert.Throws<UnauthorizedAccessException>(() => new AppShortcutRegistration(_store, _logger).Ensure(ShortcutPath, Desired));
    }

    [Fact]
    public void InvalidArgumentsAreRejected()
    {
        AppShortcutRegistration registration = new(_store, _logger);

        Assert.Throws<ArgumentNullException>(() => new AppShortcutRegistration(null!, _logger));
        Assert.Throws<ArgumentNullException>(() => new AppShortcutRegistration(_store, null!));
        Assert.Throws<ArgumentException>(() => registration.Ensure(" ", Desired));
        Assert.Throws<ArgumentNullException>(() => registration.Ensure(ShortcutPath, null!));
        Assert.Throws<ArgumentException>(() => registration.Ensure(ShortcutPath, Desired with { TargetPath = "" }));
        Assert.Throws<ArgumentNullException>(() => registration.Ensure(ShortcutPath, Desired with { AppUserModelId = null }));
    }

    [Fact]
    public void DefaultShortcutLivesInTheUsersStartMenuPrograms()
    {
        string programs = Environment.GetFolderPath(Environment.SpecialFolder.Programs);
        Assert.SkipWhen(string.IsNullOrEmpty(programs), "No Start menu on this OS.");

        string path = AppShortcutRegistration.DefaultShortcutPath();

        // Per-user folder (no admin rights), not the all-users CommonPrograms.
        Assert.Equal(Path.Combine(programs, "Prusa Tray Diagnostic.lnk"), path);
    }

    private sealed class InMemoryShortcutStore : IShortcutStore
    {
        public Dictionary<string, AppShortcut> Shortcuts { get; } = new(StringComparer.OrdinalIgnoreCase);

        public Exception? ReadFailure { get; set; }

        public Exception? WriteFailure { get; set; }

        public int WriteCount { get; private set; }

        public AppShortcut? Read(string path) =>
            ReadFailure is not null ? throw ReadFailure : Shortcuts.GetValueOrDefault(path);

        public void Write(string path, AppShortcut shortcut)
        {
            if (WriteFailure is not null)
            {
                throw WriteFailure;
            }

            WriteCount++;
            Shortcuts[path] = shortcut;
        }
    }
}
