#if DEBUG
using System.Runtime.Versioning;

using Microsoft.Win32;

using LayerPeek.App.Composition;
using LayerPeek.App.Notifications;
using LayerPeek.App.Platform.Windows;
using LayerPeek.App.Tests.Tray;

namespace LayerPeek.App.Tests.Platform.Windows;

/// <summary>
/// What a test app with toasts leaves in Windows is removed (T-067): the toasts of the test app id and its own Start menu
/// shortcut, never the production ones. A fake history and in-memory or temp-folder shortcuts stand in for Windows; the
/// real Start menu and the notification center of the test app are only touched by the end-to-end tests.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class TestToastCleanupTests
{
    private static readonly string StartMenu = @"C:\Users\test\AppData\Roaming\Microsoft\Windows\Start Menu\Programs";
    private static readonly string TestShortcut = Path.Combine(StartMenu, "LayerPeek E2E.lnk");
    private static readonly string ProductionShortcut = Path.Combine(StartMenu, "LayerPeek.lnk");

    private readonly FakeToastHistory _history = new();
    private readonly InMemoryShortcutStore _shortcuts = new();
    private readonly InMemoryComServerRegistry _activators = new();
    private readonly ListLogger<TestToastCleanup> _logger = new();

    public TestToastCleanupTests()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Toasts and shortcuts exist only on Windows.");
    }

    [Fact]
    public void ClearsTheToastsOfTheTestAppAndDeletesItsShortcutOnly()
    {
        AppShortcut production = new(@"C:\Apps\LayerPeek.exe", "LayerPeek");
        _shortcuts.Shortcuts[ProductionShortcut] = production;
        _shortcuts.Shortcuts[TestShortcut] = new AppShortcut(@"C:\src\bin\Debug\LayerPeek.exe", "LayerPeek.E2E");

        IReadOnlyList<string> failures = new TestToastCleanup(_history, _shortcuts, TestShortcut, _activators, _logger).Run();

        Assert.Empty(failures);
        Assert.Equal(["LayerPeek.E2E"], _history.Cleared);
        Assert.Equal([TestShortcut], _shortcuts.Deleted);
        Assert.Equal(production, Assert.Single(_shortcuts.Shortcuts).Value);
        Assert.Equal(ProductionShortcut, Assert.Single(_shortcuts.Shortcuts).Key);
    }

    [Fact]
    public void WithoutAShortcutOnlyTheHistoryIsCleared()
    {
        IReadOnlyList<string> failures = new TestToastCleanup(_history, _shortcuts, TestShortcut, _activators, _logger).Run();

        Assert.Empty(failures);
        Assert.Equal(["LayerPeek.E2E"], _history.Cleared);
        Assert.Empty(_shortcuts.Shortcuts);
    }

    [Fact]
    public void AFailingHistoryStillDeletesTheShortcutAndReportsTheFailure()
    {
        _history.Failure = new InvalidOperationException("history gone");
        _shortcuts.Shortcuts[TestShortcut] = new AppShortcut(@"C:\x.exe", "LayerPeek.E2E");

        IReadOnlyList<string> failures = new TestToastCleanup(_history, _shortcuts, TestShortcut, _activators, _logger).Run();

        Assert.Contains("history gone", Assert.Single(failures), StringComparison.Ordinal);
        Assert.Empty(_shortcuts.Shortcuts);
        Assert.Contains(_logger.Entries, entry => entry.Exception is InvalidOperationException);
    }

    [Fact]
    public void AFailingDeleteIsReportedAfterTheHistoryWasCleared()
    {
        _shortcuts.DeleteFailure = new UnauthorizedAccessException("denied");

        IReadOnlyList<string> failures = new TestToastCleanup(_history, _shortcuts, TestShortcut, _activators, _logger).Run();

        Assert.Contains("denied", Assert.Single(failures), StringComparison.Ordinal);
        Assert.Equal(["LayerPeek.E2E"], _history.Cleared);
    }

    [Fact]
    public void TheToastActivatorOfTheTestAppGoesAndTheProductionOneStays()
    {
        // T-068: a click on a test toast after the test app ended would otherwise start the (Debug) exe.
        Guid test = new(AppEnvironment.TestToastActivatorClsid);
        Guid production = new(AppEnvironment.ProductionToastActivatorClsid);
        _activators.Servers[test] = "\"C:\\src\\bin\\Debug\\LayerPeek.exe\" -ToastActivated";
        _activators.Servers[production] = "\"C:\\Apps\\LayerPeek.exe\" -ToastActivated";

        Assert.Empty(new TestToastCleanup(_history, _shortcuts, TestShortcut, _activators, _logger).Run());

        Assert.Equal([test], _activators.Deleted);
        Assert.Equal(production, Assert.Single(_activators.Servers).Key);
    }

    [Fact]
    public void AFailingActivatorRemovalIsReportedAfterEverythingElse()
    {
        _activators.DeleteFailure = new UnauthorizedAccessException("registry denied");
        _shortcuts.Shortcuts[TestShortcut] = new AppShortcut(@"C:\x.exe", AppEnvironment.TestAppUserModelId);

        IReadOnlyList<string> failures = new TestToastCleanup(_history, _shortcuts, TestShortcut, _activators, _logger).Run();

        Assert.Contains("registry denied", Assert.Single(failures), StringComparison.Ordinal);
        Assert.Equal([AppEnvironment.TestAppUserModelId], _history.Cleared);
        Assert.Empty(_shortcuts.Shortcuts);
    }

    [Fact]
    public void TheTestActivatorIsNeitherTheProductionOneNorEmpty()
    {
        Assert.NotEqual(new Guid(AppEnvironment.ProductionToastActivatorClsid), new Guid(AppEnvironment.TestToastActivatorClsid));
        Assert.NotEqual(Guid.Empty, new Guid(AppEnvironment.TestToastActivatorClsid));
        // Fixed for good: Windows and the installer (T-017) know the production activator by it.
        Assert.Equal("E8389DCA-851B-498C-8082-69B1AB263C14", AppEnvironment.ProductionToastActivatorClsid);
    }

    [Theory]
    [InlineData(@"C:\Users\test\AppData\Roaming\Microsoft\Windows\Start Menu\Programs\LayerPeek.lnk")]
    [InlineData(@"C:\Users\test\Desktop\Other.lnk")]
    [InlineData(@"C:\Users\test\LayerPeek E2E.lnk.bak")]
    public void RefusesAnyShortcutButTheTestOne(string path)
    {
        Assert.Throws<ArgumentException>(() => new TestToastCleanup(_history, _shortcuts, path, _activators, _logger));
    }

    [Fact]
    public void TheTestShortcutLiesInTheStartMenuNextToTheProductionOneUnderItsOwnName()
    {
        string programs = Environment.GetFolderPath(Environment.SpecialFolder.Programs);

        Assert.Equal(Path.Combine(programs, "LayerPeek E2E.lnk"), TestToastCleanup.DefaultShortcutPath());
        Assert.NotEqual(AppShortcutRegistration.DefaultShortcutPath(), TestToastCleanup.DefaultShortcutPath(), StringComparer.OrdinalIgnoreCase);
        Assert.NotEqual(AppEnvironment.ProductionAppUserModelId, AppEnvironment.TestAppUserModelId);
    }

    /// <summary>Real shortcut files, in the reserved test folder only: the test one goes, the production one stays byte for byte.</summary>
    [Fact]
    public void RealShortcutFilesInTheTestFolderLoseOnlyTheTestShortcut()
    {
        string folder = TestShortcuts.NewDirectory();
        try
        {
            WindowsShortcutStore store = new();
            string test = Path.Combine(folder, TestToastCleanup.ShortcutFileName);
            string production = Path.Combine(folder, AppShortcutRegistration.ShortcutFileName);
            store.Write(test, new AppShortcut(Environment.ProcessPath!, AppEnvironment.TestAppUserModelId));
            store.Write(production, new AppShortcut(Environment.ProcessPath!, AppEnvironment.ProductionAppUserModelId));
            byte[] productionBefore = File.ReadAllBytes(production);

            Assert.Empty(new TestToastCleanup(_history, store, test, _activators, _logger).Run());

            Assert.False(File.Exists(test));
            Assert.Equal(productionBefore, File.ReadAllBytes(production));
            // A second cleanup finds nothing and still succeeds.
            Assert.Empty(new TestToastCleanup(_history, store, test, _activators, _logger).Run());
            Assert.False(store.Delete(test));
        }
        finally
        {
            TestShortcuts.Delete(folder);
        }
    }

    /// <summary>
    /// The real WinRT history call, for an app id that never showed a toast (not the test app id: an end-to-end run of
    /// another worktree may be using it): Windows accepts it and registers nothing for it.
    /// </summary>
    [Fact]
    public void RealHistoryOfAnAppWithoutToastsClearsWithoutRegisteringIt()
    {
        const string appId = "LayerPeek.Tests.NeverShown";
        const string settings = @"Software\Microsoft\Windows\CurrentVersion\Notifications\Settings\" + appId;
        Assert.SkipWhen(Registry.CurrentUser.OpenSubKey(settings) is not null, "Windows already knows the app id of this test.");

        Exception? failure = null;
        Thread thread = new(() =>
        {
            try
            {
                new WinRtToastHistory().Clear(appId);
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });
        thread.Start();
        thread.Join();

        Assert.Null(failure);
        using RegistryKey? registered = Registry.CurrentUser.OpenSubKey(settings);
        Assert.Null(registered);
    }

    /// <summary>
    /// Quitting cleans up only once the toast thread has ended: a registration still in progress would otherwise write the
    /// test shortcut again right after it was deleted.
    /// </summary>
    [Fact]
    public async Task QuittingCleansUpOnlyAfterTheToastThreadEnded()
    {
        _shortcuts.Shortcuts[TestShortcut] = new AppShortcut(@"C:\x.exe", AppEnvironment.TestAppUserModelId);
        TaskCompletionSource threadEnded = new(TaskCreationOptions.RunContinuationsAsynchronously);

        Task quitting = TrayHost.RunTestToastCleanupAsync(new TestToastCleanup(_history, _shortcuts, TestShortcut, _activators, _logger), threadEnded.Task, _logger);
        await Task.Delay(200, TestContext.Current.CancellationToken);

        Assert.False(quitting.IsCompleted);
        Assert.Empty(_history.Cleared);
        Assert.Single(_shortcuts.Shortcuts);

        threadEnded.SetResult();
        await quitting.WaitAsync(TestTimeouts.Wait, TestContext.Current.CancellationToken);

        Assert.Equal([AppEnvironment.TestAppUserModelId], _history.Cleared);
        Assert.Empty(_shortcuts.Shortcuts);
    }

    [Fact]
    public async Task AHungToastThreadDelaysTheCleanupOnlyUpToItsLimit()
    {
        _shortcuts.Shortcuts[TestShortcut] = new AppShortcut(@"C:\x.exe", AppEnvironment.TestAppUserModelId);
        TaskCompletionSource neverEnds = new(TaskCreationOptions.RunContinuationsAsynchronously);

        await TrayHost.RunTestToastCleanupAsync(new TestToastCleanup(_history, _shortcuts, TestShortcut, _activators, _logger), neverEnds.Task, _logger)
            .WaitAsync(TestTimeouts.Wait, TestContext.Current.CancellationToken);

        Assert.Equal([AppEnvironment.TestAppUserModelId], _history.Cleared);
        Assert.Empty(_shortcuts.Shortcuts);
        Assert.Contains(_logger.Entries, entry => entry.Exception is TimeoutException);
    }

    [Fact]
    public async Task WithoutTestToastsQuittingCleansNothingAndDoesNotWait()
    {
        TaskCompletionSource neverEnds = new(TaskCreationOptions.RunContinuationsAsynchronously);

        Task quitting = TrayHost.RunTestToastCleanupAsync(null, neverEnds.Task, _logger);

        Assert.True(quitting.IsCompletedSuccessfully);
        await quitting;
        Assert.Empty(_history.Cleared);
    }

    private sealed class FakeToastHistory : IToastHistory
    {
        public List<string> Cleared { get; } = [];

        public Exception? Failure { get; set; }

        public void Clear(string appUserModelId)
        {
            if (Failure is not null)
            {
                throw Failure;
            }

            Cleared.Add(appUserModelId);
        }
    }
}
#endif
