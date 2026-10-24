using Microsoft.Extensions.Logging;

using LayerPeek.App.Settings;
using LayerPeek.App.Tests.Tray;
using LayerPeek.App.Tests.Tray.Menu;
using LayerPeek.Core.Printers;
using LayerPeek.Core.Secrets;
using LayerPeek.Core.Settings;

using static LayerPeek.App.Tests.Settings.SettingsTestData;

namespace LayerPeek.App.Tests.Settings;

public sealed class SettingsEditorStoreTests : IAsyncDisposable
{
    private static readonly PasswordPolicy Supported = new(IsSupported: true, MaxBytes: 2560);

    private readonly List<string> _log = [];
    private readonly FakeSecretStore _secrets;
    private readonly MemorySettingsStore _settings;
    private readonly RecordingApplier _applier;
    private readonly FakeWebGateway _gateway = new();
    private readonly ListLogger<SettingsEditorStore> _logger = new();

    public SettingsEditorStoreTests()
    {
        _secrets = new FakeSecretStore(_log);
        _settings = new MemorySettingsStore(_log);
        _applier = new RecordingApplier(_log);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public ValueTask DisposeAsync() => _gateway.DisposeAsync();

    [Fact]
    public async Task LoadReturnsSettingsStatusAndWarnings()
    {
        AppSettings stored = new() { Printers = [LinkPrinter("a", "Alpha")] };
        _settings.LoadResult = new SettingsLoadResult { Settings = stored, Status = SettingsLoadStatus.Loaded, Warnings = ["skipped printer 2"] };

        SettingsEditorData data = await Create().LoadAsync(Ct);

        Assert.Same(stored, data.Settings);
        Assert.Equal(SettingsLoadStatus.Loaded, data.Status);
        Assert.Equal(["skipped printer 2"], data.Warnings);
    }

    [Fact]
    public async Task LoadTellsWhichPrintersHaveASavedPassword()
    {
        _settings.LoadResult = Loaded(LinkPrinter("a", "Alpha"), LinkPrinter("b", "Beta"), CloudPrinter("c", "Cloud"));
        _secrets.Seed("a", "secret-a");

        SettingsEditorData data = await Create().LoadAsync(Ct);

        Assert.NotNull(data.PrintersWithPassword);
        Assert.Contains("A", data.PrintersWithPassword);
        Assert.DoesNotContain("b", data.PrintersWithPassword);
        // Prusa Connect printers have no per-printer password; the store is not asked for them.
        Assert.DoesNotContain(_secrets.Operations, operation => operation.EndsWith("/c", StringComparison.Ordinal));
    }

    [Fact]
    public async Task FailingSecretStoreMakesPasswordStatusUnknown()
    {
        _settings.LoadResult = Loaded(LinkPrinter("a", "Alpha"));
        _secrets.FailGet.Add(SecretKeys.ForPrinter("a"));

        SettingsEditorData data = await Create().LoadAsync(Ct);

        Assert.Null(data.PrintersWithPassword);
        Assert.Equal(SettingsLoadStatus.Loaded, data.Status);
    }

    [Fact]
    public async Task UnsupportedSecretStoreIsNotAskedOnLoad()
    {
        _settings.LoadResult = Loaded(LinkPrinter("a", "Alpha"));

        SettingsEditorData data = await Create(PasswordPolicy.Unsupported).LoadAsync(Ct);

        Assert.Null(data.PrintersWithPassword);
        Assert.Empty(_secrets.Operations);
    }

    [Fact]
    public async Task UnavailableSettingsStoreLoadsDefaultsAsUnreadable()
    {
        SettingsEditorStore store = new(() => throw new InvalidOperationException("no app data folder"), _secrets, Supported, _applier, _gateway, _logger);

        SettingsEditorData data = await store.LoadAsync(Ct);

        Assert.Equal(AppSettings.Default, data.Settings);
        Assert.Equal(SettingsLoadStatus.Unreadable, data.Status);
    }

    [Fact]
    public async Task LoadHonoursCancellation()
    {
        using CancellationTokenSource cancelled = new();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Create().LoadAsync(cancelled.Token));
    }

    [Fact]
    public async Task NewPrinterPasswordIsWrittenBeforeSettingsAndAppliedAfter()
    {
        AppSettings updated = new() { Printers = [LinkPrinter("new", "New")] };

        SettingsSaveResult result = await Create().SaveAsync(Change(AppSettings.Default, updated, ("new", "pw")));

        Assert.Equal(SettingsSaveStatus.Saved, result.Status);
        Assert.Equal("pw", _secrets.Secrets[SecretKeys.ForPrinter("new")]);
        Assert.Same(updated, _settings.Saved);
        Assert.Equal([updated], _applier.Applied);
        // Password first: the saved settings never point to a printer whose new password is missing.
        Assert.Equal(["get printer/new", "set printer/new", "save settings", "apply", "old run ended"], _log);
    }

    [Fact]
    public async Task PasswordIsNeverWrittenToSettingsOrLogs()
    {
        AppSettings updated = new() { Printers = [LinkPrinter("new", "New")] };
        _settings.SaveFailure = new IOException("disk full");

        await Create().SaveAsync(Change(AppSettings.Default, updated, ("new", "Sup3rSecret")));

        Assert.DoesNotContain(_logger.Entries, entry => entry.Message.Contains("Sup3rSecret", StringComparison.Ordinal));
    }

    [Fact]
    public async Task FailedPasswordWriteSavesNothing()
    {
        AppSettings original = new() { Printers = [LinkPrinter("a", "Alpha")] };
        AppSettings updated = new() { Printers = [LinkPrinter("a", "Alpha"), LinkPrinter("b", "Beta")] };
        _secrets.FailSet.Add(SecretKeys.ForPrinter("b"));

        SettingsSaveResult result = await Create().SaveAsync(Change(original, updated, ("b", "pw")));

        Assert.Equal(SettingsSaveStatus.PasswordNotSaved, result.Status);
        Assert.False(result.RollbackIncomplete);
        Assert.Null(_settings.Saved);
        Assert.Empty(_applier.Applied);
        Assert.False(_secrets.Secrets.ContainsKey(SecretKeys.ForPrinter("b")));
        Assert.Contains(_logger.Entries, entry => entry.Level == LogLevel.Error);
    }

    [Fact]
    public async Task FailedSecondPasswordRestoresTheFirst()
    {
        AppSettings original = new() { Printers = [LinkPrinter("a", "Alpha"), LinkPrinter("b", "Beta")] };
        _secrets.Seed("a", "old-a");
        _secrets.FailSet.Add(SecretKeys.ForPrinter("b"));

        SettingsSaveResult result = await Create().SaveAsync(Change(original, original, ("a", "new-a"), ("b", "new-b")));

        Assert.Equal(SettingsSaveStatus.PasswordNotSaved, result.Status);
        Assert.Equal("old-a", _secrets.Secrets[SecretKeys.ForPrinter("a")]);
        Assert.Null(_settings.Saved);
    }

    [Fact]
    public async Task FailedSettingsWriteUndoesPasswordChanges()
    {
        AppSettings original = new() { Printers = [LinkPrinter("a", "Alpha")] };
        AppSettings updated = new() { Printers = [LinkPrinter("a", "Alpha"), LinkPrinter("new", "New")] };
        _secrets.Seed("a", "old-a");
        _settings.SaveFailure = new UnauthorizedAccessException("denied");

        SettingsSaveResult result = await Create().SaveAsync(Change(original, updated, ("a", "new-a"), ("new", "pw")));

        Assert.Equal(SettingsSaveStatus.SettingsNotSaved, result.Status);
        Assert.False(result.RollbackIncomplete);
        // The changed password is restored, the new printer's password does not stay behind as an orphan.
        Assert.Equal("old-a", _secrets.Secrets[SecretKeys.ForPrinter("a")]);
        Assert.False(_secrets.Secrets.ContainsKey(SecretKeys.ForPrinter("new")));
        Assert.Empty(_applier.Applied);
    }

    [Fact]
    public async Task FailedRollbackIsReported()
    {
        AppSettings updated = new() { Printers = [LinkPrinter("new", "New")] };
        _settings.SaveFailure = new IOException("disk full");
        _secrets.FailDelete.Add(SecretKeys.ForPrinter("new"));

        SettingsSaveResult result = await Create().SaveAsync(Change(AppSettings.Default, updated, ("new", "pw")));

        Assert.Equal(SettingsSaveStatus.SettingsNotSaved, result.Status);
        Assert.True(result.RollbackIncomplete);
    }

    [Fact]
    public async Task UnreadablePreviousPasswordStopsBeforeAnythingIsWritten()
    {
        AppSettings original = new() { Printers = [LinkPrinter("a", "Alpha")] };
        _secrets.FailGet.Add(SecretKeys.ForPrinter("a"));

        SettingsSaveResult result = await Create().SaveAsync(Change(original, original, ("a", "new-a")));

        // Without the previous value a failed save could not be undone, so nothing is changed.
        Assert.Equal(SettingsSaveStatus.PasswordNotSaved, result.Status);
        Assert.DoesNotContain(_secrets.Operations, operation => operation.StartsWith("set", StringComparison.Ordinal));
        Assert.Null(_settings.Saved);
    }

    [Fact]
    public async Task RemovedPrinterPasswordIsDeletedAfterSettingsAreSaved()
    {
        AppSettings original = new() { Printers = [LinkPrinter("a", "Alpha"), LinkPrinter("b", "Beta")] };
        AppSettings updated = new() { Printers = [LinkPrinter("a", "Alpha")] };
        _secrets.Seed("a", "pa");
        _secrets.Seed("b", "pb");

        SettingsSaveResult result = await Create().SaveAsync(Change(original, updated));

        Assert.Equal(SettingsSaveStatus.Saved, result.Status);
        Assert.Equal(0, result.UndeletedPasswords);
        Assert.False(_secrets.Secrets.ContainsKey(SecretKeys.ForPrinter("b")));
        Assert.Equal("pa", _secrets.Secrets[SecretKeys.ForPrinter("a")]);
        // Deleted only after the old monitoring run ended, so that no poll of the removed printer finds its password missing.
        Assert.Equal(["save settings", "apply", "old run ended", "delete printer/b"], _log);
    }

    [Fact]
    public async Task RemovedPrinterPasswordStaysWhenSettingsWriteFails()
    {
        AppSettings original = new() { Printers = [LinkPrinter("a", "Alpha"), LinkPrinter("b", "Beta")] };
        AppSettings updated = new() { Printers = [LinkPrinter("a", "Alpha")] };
        _secrets.Seed("b", "pb");
        _settings.SaveFailure = new IOException("disk full");

        SettingsSaveResult result = await Create().SaveAsync(Change(original, updated));

        Assert.Equal(SettingsSaveStatus.SettingsNotSaved, result.Status);
        // The old settings still reference printer b, so its password must stay.
        Assert.Equal("pb", _secrets.Secrets[SecretKeys.ForPrinter("b")]);
    }

    [Fact]
    public async Task UndeletablePasswordOfRemovedPrinterIsAWarningOnly()
    {
        AppSettings original = new() { Printers = [LinkPrinter("a", "Alpha"), LinkPrinter("b", "Beta")] };
        AppSettings updated = new() { Printers = [LinkPrinter("a", "Alpha")] };
        _secrets.FailDelete.Add(SecretKeys.ForPrinter("b"));

        SettingsSaveResult result = await Create().SaveAsync(Change(original, updated));

        Assert.Equal(SettingsSaveStatus.Saved, result.Status);
        Assert.Equal(1, result.UndeletedPasswords);
        Assert.Same(updated, _settings.Saved);
        Assert.Equal([updated], _applier.Applied);
    }

    [Fact]
    public async Task RenamedPrinterKeepsItsPasswordUntouched()
    {
        AppSettings original = new() { Printers = [LinkPrinter("a", "Alpha")] };
        AppSettings updated = new() { Printers = [LinkPrinter("a", "Renamed")] };
        _secrets.Seed("a", "pa");

        SettingsSaveResult result = await Create().SaveAsync(Change(original, updated));

        Assert.Equal(SettingsSaveStatus.Saved, result.Status);
        Assert.Empty(_secrets.Operations);
        Assert.Equal("pa", _secrets.Secrets[SecretKeys.ForPrinter("a")]);
    }

    [Fact]
    public async Task RemovedPrinterIdsAreComparedIgnoringCase()
    {
        AppSettings original = new() { Printers = [LinkPrinter("abc", "Alpha")] };
        AppSettings updated = new() { Printers = [LinkPrinter("ABC", "Alpha")] };

        await Create().SaveAsync(Change(original, updated));

        Assert.Empty(_secrets.Operations);
    }

    [Fact]
    public async Task UnsupportedSecretStoreIsNeverTouched()
    {
        AppSettings original = new() { Printers = [LinkPrinter("a", "Alpha"), LinkPrinter("b", "Beta")] };
        AppSettings updated = new() { Printers = [LinkPrinter("a", "Alpha")] };

        SettingsSaveResult result = await Create(PasswordPolicy.Unsupported).SaveAsync(Change(original, updated));

        Assert.Equal(SettingsSaveStatus.Saved, result.Status);
        Assert.Empty(_secrets.Operations);
    }

    [Fact]
    public async Task PasswordsAreRejectedWhenTheyCannotBeStored()
    {
        AppSettings updated = new() { Printers = [LinkPrinter("a", "Alpha")] };

        await Assert.ThrowsAsync<ArgumentException>(() => Create(PasswordPolicy.Unsupported).SaveAsync(Change(AppSettings.Default, updated, ("a", "pw"))));
        await Assert.ThrowsAsync<ArgumentException>(() => Create().SaveAsync(Change(AppSettings.Default, updated, ("unknown", "pw"))));
        Assert.Empty(_secrets.Operations);
    }

    [Fact]
    public async Task FailingApplierDoesNotTurnASaveIntoAFailure()
    {
        SettingsEditorStore store = new(() => _settings, _secrets, Supported, new ThrowingApplier(), _gateway, _logger);

        SettingsSaveResult result = await store.SaveAsync(Change(AppSettings.Default, new AppSettings { PollingInterval = TimeSpan.FromSeconds(9) }));

        Assert.Equal(SettingsSaveStatus.Saved, result.Status);
        Assert.Contains(_logger.Entries, entry => entry.Level == LogLevel.Error);
    }

    [Fact]
    public async Task UnavailableSettingsStoreFailsTheSave()
    {
        SettingsEditorStore store = new(() => throw new InvalidOperationException("no app data folder"), _secrets, Supported, _applier, _gateway, _logger);
        AppSettings updated = new() { Printers = [LinkPrinter("new", "New")] };

        SettingsSaveResult result = await store.SaveAsync(Change(AppSettings.Default, updated, ("new", "pw")));

        Assert.Equal(SettingsSaveStatus.SettingsNotSaved, result.Status);
        Assert.Empty(_secrets.Secrets);
    }

    [Fact]
    public async Task ChangedPasswordForcesAMonitoringRestart()
    {
        AppSettings original = new() { Printers = [LinkPrinter("a", "Alpha")] };
        _secrets.Seed("a", "wrong");

        await Create().SaveAsync(Change(original, original, ("a", "corrected")));
        await Create().SaveAsync(Change(original, original with { Notifications = new NotificationSettings { Error = false } }));

        // Settings alone are unchanged in the first save, but the running client must pick up the new password now.
        Assert.Equal([true, false], _applier.CredentialsChanged);
    }

    [Fact]
    public async Task RemovedPasswordWaitsForTheOldMonitoringRun()
    {
        AppSettings original = new() { Printers = [LinkPrinter("a", "Alpha"), LinkPrinter("b", "Beta")] };
        AppSettings updated = new() { Printers = [LinkPrinter("a", "Alpha")] };
        _secrets.Seed("b", "pb");
        TaskCompletionSource oldRun = new(TaskCreationOptions.RunContinuationsAsynchronously);
        _applier.OldRunEnded = oldRun.Task;

        Task<SettingsSaveResult> save = Create().SaveAsync(Change(original, updated));
        await WaitUntilAsync(() => _log.Contains("apply"));
        await Task.Delay(50, Ct);

        Assert.True(_secrets.Secrets.ContainsKey(SecretKeys.ForPrinter("b")));
        Assert.False(save.IsCompleted);

        oldRun.SetResult();
        Assert.Equal(SettingsSaveStatus.Saved, (await save.WaitAsync(TestTimeouts.Wait, Ct)).Status);
        Assert.False(_secrets.Secrets.ContainsKey(SecretKeys.ForPrinter("b")));
    }

    [Fact]
    public async Task StuckMonitoringRunDelaysTheDeletionOnlyBriefly()
    {
        AppSettings original = new() { Printers = [LinkPrinter("a", "Alpha"), LinkPrinter("b", "Beta")] };
        AppSettings updated = new() { Printers = [LinkPrinter("a", "Alpha")] };
        _secrets.Seed("b", "pb");
        _applier.OldRunEnded = new TaskCompletionSource().Task;
        SettingsEditorStore store = new(() => _settings, _secrets, Supported, _applier, _gateway, _logger, applyWaitTimeout: TimeSpan.FromMilliseconds(50));

        SettingsSaveResult result = await store.SaveAsync(Change(original, updated)).WaitAsync(TestTimeouts.Wait, Ct);

        Assert.Equal(SettingsSaveStatus.Saved, result.Status);
        Assert.False(_secrets.Secrets.ContainsKey(SecretKeys.ForPrinter("b")));
    }

    [Fact]
    public async Task UnexpectedSecretStoreFailureIsRolledBackToo()
    {
        AppSettings original = new() { Printers = [LinkPrinter("a", "Alpha"), LinkPrinter("b", "Beta")] };
        _secrets.Seed("a", "old-a");
        ThrowingOnSetStore secrets = new(_secrets, SecretKeys.ForPrinter("b"));
        SettingsEditorStore store = new(() => _settings, secrets, Supported, _applier, _gateway, _logger);

        SettingsSaveResult result = await store.SaveAsync(Change(original, original, ("a", "new-a"), ("b", "new-b")));

        Assert.Equal(SettingsSaveStatus.PasswordNotSaved, result.Status);
        Assert.Equal("old-a", _secrets.Secrets[SecretKeys.ForPrinter("a")]);
        Assert.Null(_settings.Saved);
    }

    [Fact]
    public async Task PendingSaveCanBeAwaitedWhenQuitting()
    {
        TaskCompletionSource oldRun = new(TaskCreationOptions.RunContinuationsAsynchronously);
        _applier.OldRunEnded = oldRun.Task;
        SettingsEditorStore store = Create();
        Assert.True(await store.WaitForPendingSaveAsync(TimeSpan.Zero));

        Task<SettingsSaveResult> save = store.SaveAsync(Change(AppSettings.Default, new AppSettings { PollingInterval = TimeSpan.FromSeconds(9) }));

        Assert.False(await store.WaitForPendingSaveAsync(TimeSpan.FromMilliseconds(50)));
        oldRun.SetResult();
        Assert.True(await store.WaitForPendingSaveAsync(TestTimeouts.Wait));
        Assert.True(save.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task SaveStopsWebProxiesOfRemovedAndChangedPrinters()
    {
        AppSettings original = new()
        {
            Printers =
            [
                LinkPrinter("pw", "New password"),
                LinkPrinter("addr", "Moved"),
                LinkPrinter("user", "Other user"),
                LinkPrinter("gone", "Removed"),
                LinkPrinter("name", "Renamed"),
                LinkPrinter("same", "Same"),
                LinkPrinter("kind", "Now cloud"),
            ],
        };
        AppSettings updated = new()
        {
            Printers =
            [
                LinkPrinter("pw", "New password"),
                LinkPrinter("ADDR", "Moved", address: "192.168.1.99"),
                LinkPrinter("user", "Other user", userName: "admin"),
                LinkPrinter("name", "Renamed again"),
                LinkPrinter("same", "Same"),
                CloudPrinter("kind", "Now cloud"),
                LinkPrinter("added", "Added"),
            ],
        };

        SettingsSaveResult result = await Create().SaveAsync(Change(original, updated, ("pw", "new-pw"), ("added", "pa")));

        Assert.Equal(SettingsSaveStatus.Saved, result.Status);
        // A rename or an untouched printer keeps its open browser tab working; a new printer has no proxy yet.
        Assert.Equal(["addr", "gone", "kind", "pw", "user"], _gateway.Stopped.Order(StringComparer.OrdinalIgnoreCase));
        Assert.Equal(0, _gateway.StopAllCount);
    }

    [Fact]
    public async Task WebProxyOfRemovedPrinterStopsBeforeItsPasswordIsDeleted()
    {
        AppSettings original = new() { Printers = [LinkPrinter("a", "Alpha"), LinkPrinter("b", "Beta")] };
        AppSettings updated = new() { Printers = [LinkPrinter("a", "Alpha")] };
        FakeWebGateway gateway = new(_log);
        SettingsEditorStore store = new(() => _settings, _secrets, Supported, _applier, gateway, _logger);

        SettingsSaveResult result = await store.SaveAsync(Change(original, updated));

        Assert.Equal(SettingsSaveStatus.Saved, result.Status);
        Assert.True(_log.IndexOf("save settings") < _log.IndexOf("stop web b"));
        Assert.True(_log.IndexOf("stop web b") < _log.IndexOf("delete printer/b"));
    }

    [Fact]
    public async Task FailedSaveStopsNoWebProxy()
    {
        AppSettings original = new() { Printers = [LinkPrinter("a", "Alpha"), LinkPrinter("b", "Beta")] };
        AppSettings updated = new() { Printers = [LinkPrinter("a", "Alpha", address: "192.168.1.99")] };
        _settings.SaveFailure = new IOException("disk full");

        SettingsSaveResult result = await Create().SaveAsync(Change(original, updated, ("a", "new-a")));

        // The old settings and passwords stay in force, so the running proxies are still right.
        Assert.Equal(SettingsSaveStatus.SettingsNotSaved, result.Status);
        Assert.Empty(_gateway.Stopped);
    }

    [Fact]
    public async Task NotificationOnlyChangeStopsNoWebProxy()
    {
        AppSettings original = new() { Printers = [LinkPrinter("a", "Alpha")] };
        AppSettings updated = original with { PollingInterval = TimeSpan.FromSeconds(9) };

        await Create().SaveAsync(Change(original, updated));

        Assert.Empty(_gateway.Stopped);
    }

    [Fact]
    public async Task FailingWebProxyStopDoesNotFailTheSave()
    {
        AppSettings original = new() { Printers = [LinkPrinter("a", "Alpha"), LinkPrinter("b", "Beta")] };
        AppSettings updated = new() { Printers = [LinkPrinter("a", "Alpha")] };
        _secrets.Seed("b", "pb");
        _gateway.StopFailure = new InvalidOperationException("boom");

        SettingsSaveResult result = await Create().SaveAsync(Change(original, updated));

        Assert.Equal(SettingsSaveStatus.Saved, result.Status);
        Assert.False(_secrets.Secrets.ContainsKey(SecretKeys.ForPrinter("b")));
        Assert.Contains(_logger.Entries, static entry => entry.Level == LogLevel.Error && entry.Exception is InvalidOperationException);
    }

    [Fact]
    public async Task HangingWebProxyStopDoesNotBlockTheSave()
    {
        AppSettings original = new() { Printers = [LinkPrinter("a", "Alpha"), LinkPrinter("b", "Beta")] };
        AppSettings updated = new() { Printers = [LinkPrinter("a", "Alpha")] };
        _gateway.StopGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        SettingsEditorStore store = new(() => _settings, _secrets, Supported, _applier, _gateway, _logger, applyWaitTimeout: TimeSpan.FromMilliseconds(50));

        SettingsSaveResult result = await store.SaveAsync(Change(original, updated)).WaitAsync(TestTimeouts.Wait, Ct);

        // The gateway stops the proxy anyway once the wait is cancelled; the save only stops waiting.
        Assert.Equal(SettingsSaveStatus.Saved, result.Status);
        Assert.Equal(["b"], _gateway.Stopped);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        timeout.CancelAfter(TestTimeouts.Wait);
        while (!condition())
        {
            await Task.Delay(5, timeout.Token);
        }
    }

    private SettingsEditorStore Create(PasswordPolicy? policy = null) =>
        new(() => _settings, _secrets, policy ?? Supported, _applier, _gateway, _logger);

    private static SettingsLoadResult Loaded(params PrinterDefinition[] printers) =>
        new() { Settings = new AppSettings { Printers = printers }, Status = SettingsLoadStatus.Loaded };

    private static SettingsChange Change(AppSettings original, AppSettings updated, params (string Id, string Password)[] passwords) =>
        new(original, updated, passwords.ToDictionary(static p => p.Id, static p => p.Password, StringComparer.OrdinalIgnoreCase));

    /// <summary>Fails with an exception the secret store contract does not document.</summary>
    private sealed class ThrowingOnSetStore(ISecretStore inner, string failingKey) : ISecretStore
    {
        public Task<string?> GetAsync(string key, CancellationToken cancellationToken) => inner.GetAsync(key, cancellationToken);

        public Task SetAsync(string key, string secret, CancellationToken cancellationToken) =>
            string.Equals(key, failingKey, StringComparison.OrdinalIgnoreCase)
                ? throw new InvalidOperationException("unexpected")
                : inner.SetAsync(key, secret, cancellationToken);

        public Task<bool> DeleteAsync(string key, CancellationToken cancellationToken) => inner.DeleteAsync(key, cancellationToken);
    }

    private sealed class ThrowingApplier : ISettingsApplier
    {
        public Task ApplyAsync(AppSettings settings, bool credentialsChanged) => throw new InvalidOperationException("boom");
    }
}
