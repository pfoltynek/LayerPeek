using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using LayerPeek.App.Composition;
using LayerPeek.App.Installer;
using LayerPeek.App.Platform;
using LayerPeek.App.Platform.Windows;
using LayerPeek.App.Tests.Tray;
using LayerPeek.Core.Settings;

namespace LayerPeek.App.Tests.Installer;

/// <summary>
/// T-201, user decision 2026-11-12: a one-time migration at the start of the installed app on Windows, so that an autostart
/// switched off in 0.x (which stored no choice) survives the first reinstall. Only without a stored choice and only when
/// the autostart is off is "off" stored; the shortcut is never touched. Fakes only.
/// </summary>
public sealed class AutostartChoiceMigrationTests
{
    private readonly FakeAutostartState _autostart = new();
    private readonly FakeChoiceStore _choices = new();
    private readonly ListLogger<AutostartChoiceMigration> _logger = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AnAutostartSwitchedOffWithoutAChoiceStoresOff()
    {
        _autostart.Enabled = false;

        await Migration().RunAsync(Ct);

        Assert.Equal([false], _choices.Stored);
        Assert.False(_choices.Choice);
        Assert.Empty(_autostart.Changes);
        Assert.Contains(_logger.Entries, static entry => entry.Level == LogLevel.Information);
    }

    [Fact]
    public async Task AnAutostartSwitchedOnStoresNothing()
    {
        _autostart.Enabled = true;

        await Migration().RunAsync(Ct);

        Assert.Empty(_choices.Stored);
        Assert.Null(_choices.Choice);
        Assert.Empty(_autostart.Changes);
    }

    /// <summary>
    /// T-201 review: a shortcut that cannot be read (antivirus lock) or one of another copy is no "off": nothing is stored,
    /// the next start tries again.
    /// </summary>
    [Fact]
    public async Task AnUnknownAutostartStoresNothing()
    {
        _autostart.Unknown = true;

        await Migration().RunAsync(Ct);

        Assert.Empty(_choices.Stored);
        Assert.Null(_choices.Choice);
        Assert.Empty(_autostart.Changes);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task AStoredChoiceStaysAndTheAutostartIsNotRead(bool choice, bool enabled)
    {
        _choices.Choice = choice;
        _autostart.Enabled = enabled;
        _autostart.ReadFailure = new InvalidOperationException("must not be read");

        await Migration().RunAsync(Ct);

        Assert.Empty(_choices.Stored);
        Assert.Equal(choice, _choices.Choice);
        Assert.Empty(_autostart.Changes);
        Assert.Empty(_logger.Entries);
    }

    [Fact]
    public async Task AFailedStoreIsOnlyLogged()
    {
        _choices.StoreFailure = new IOException("disk full");

        await Migration().RunAsync(Ct);

        Assert.Contains(_logger.Entries, static entry => entry.Level == LogLevel.Warning && entry.Exception is IOException);
        Assert.Empty(_autostart.Changes);
    }

    [Fact]
    public async Task UnreadableSettingsStoreNothing()
    {
        _choices.ReadFailure = new System.Text.Json.JsonException("damaged");

        await Migration().RunAsync(Ct);

        Assert.Empty(_choices.Stored);
        Assert.Contains(_logger.Entries, static entry => entry.Level == LogLevel.Warning && entry.Exception is System.Text.Json.JsonException);
    }

    [Fact]
    public async Task AnUnreadableAutostartStoresNothing()
    {
        _autostart.ReadFailure = new UnauthorizedAccessException("denied");

        await Migration().RunAsync(Ct);

        Assert.Empty(_choices.Stored);
        Assert.Contains(_logger.Entries, static entry => entry.Level == LogLevel.Warning && entry.Exception is UnauthorizedAccessException);
    }

    /// <summary>The store found a choice written meanwhile (the settings window): nothing reported as stored.</summary>
    [Fact]
    public async Task AChoiceWrittenMeanwhileIsKept()
    {
        _choices.WrittenMeanwhile = true;

        await Migration().RunAsync(Ct);

        Assert.Equal(true, _choices.Choice);
        Assert.DoesNotContain(_logger.Entries, static entry => entry.Level == LogLevel.Information);
    }

    [Fact]
    public async Task QuittingCancelsQuietly()
    {
        using CancellationTokenSource quit = new();
        await quit.CancelAsync();
        _choices.StoreFailure = new OperationCanceledException(quit.Token);

        await Migration().RunAsync(quit.Token);

        Assert.Empty(_logger.Entries);
    }

    /// <summary>Only the installed app on Windows with the production identity migrates: not the test mode, a .zip or macOS.</summary>
    [Fact]
    public void OnlyTheInstalledProductionAppOnWindowsMigrates()
    {
        string directory = TestDirectories.NewDirectoryPath();
        using JsonSettingsStore store = new(directory);
        NullLogger<AutostartChoiceMigration> logger = new();
        AppEnvironment isolated = AppEnvironment.FromVariables(name => name == AppEnvironment.DataDirectoryVariable ? directory : null);
        Assert.True(isolated.IsIsolated);

        Assert.Equal(OperatingSystem.IsWindows(), AppServices.CreateAutostartChoiceMigration(AppEnvironment.Production, windows: true, _autostart, store, logger) is not null);
        Assert.Null(AppServices.CreateAutostartChoiceMigration(isolated, windows: true, _autostart, store, logger));
        Assert.Null(AppServices.CreateAutostartChoiceMigration(AppEnvironment.Production, windows: false, _autostart, store, logger));
        // Not installed (.zip, development build) or a test host: no autostart.
        Assert.Null(AppServices.CreateAutostartChoiceMigration(AppEnvironment.Production, windows: true, autostart: null, store, logger));
        Assert.Null(AppServices.CreateAutostartChoiceMigration(AppEnvironment.Production, windows: true, _autostart, store: null, logger));
        Assert.False(Directory.Exists(directory));
    }

    /// <summary>An autostart without the three states (macOS Login Item) never migrates.</summary>
    [Fact]
    public void AnAutostartWithoutTheThreeStatesDoesNotMigrate()
    {
        string directory = TestDirectories.NewDirectoryPath();
        using JsonSettingsStore store = new(directory);

        Assert.Null(AppServices.CreateAutostartChoiceMigration(AppEnvironment.Production, windows: true, new FakeAutostart(), store, NullLogger<AutostartChoiceMigration>.Instance));
    }

    /// <summary>The <c>.zip</c> build gets no autostart (AppServices.CreateAutostart), so it never migrates.</summary>
    [Fact]
    public void ThePortableBuildDoesNotMigrate()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "The Windows rules of the autostart.");
        AutostartOption zip = AppServices.CreateAutostart(AppEnvironment.Production, windows: true, NullLogger<StartupShortcutAutostart>.Instance, @"C:\Tools\LayerPeek\LayerPeek.exe", static _ => false);

        Assert.Null(AppServices.CreateAutostartChoiceMigration(AppEnvironment.Production, windows: true, zip.Autostart, store: null, NullLogger<AutostartChoiceMigration>.Instance));
        Assert.Null(zip.Autostart);
    }

    private AutostartChoiceMigration Migration() => new(_autostart, _choices, _logger);

    /// <summary>An autostart that also tells "unknown" (<see cref="IAutostartState"/>), in memory.</summary>
    private sealed class FakeAutostartState : IAutostart, IAutostartState
    {
        public bool Enabled { get; set; }

        public bool Unknown { get; set; }

        public Exception? ReadFailure { get; set; }

        public List<bool> Changes { get; } = [];

        public bool IsEnabled() => throw new InvalidOperationException("The migration asks for the three states.");

        public AutostartState QueryState() =>
            ReadFailure is not null ? throw ReadFailure
            : Unknown ? AutostartState.Unknown
            : Enabled ? AutostartState.On
            : AutostartState.Off;

        public void SetEnabled(bool enabled) => Changes.Add(enabled);
    }

    private sealed class FakeChoiceStore : IAutostartChoiceStore
    {
        public bool? Choice { get; set; }

        public Exception? ReadFailure { get; set; }

        public Exception? StoreFailure { get; set; }

        /// <summary>The settings window stored "on" between the read and the store.</summary>
        public bool WrittenMeanwhile { get; set; }

        public List<bool> Stored { get; } = [];

        public bool? Read() => ReadFailure is not null ? throw ReadFailure : Choice;

        public Task<bool> StoreIfUnsetAsync(bool choice, CancellationToken cancellationToken)
        {
            if (StoreFailure is not null)
            {
                throw StoreFailure;
            }

            if (WrittenMeanwhile)
            {
                Choice = true;
            }

            if (Choice is not null)
            {
                return Task.FromResult(false);
            }

            Stored.Add(choice);
            Choice = choice;
            return Task.FromResult(true);
        }
    }
}
