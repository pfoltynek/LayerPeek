using System.Runtime.Versioning;

using Avalonia.Threading;

using LayerPeek.App.Platform.MacOS;
using LayerPeek.App.Tests.Platform.MacOS;
using LayerPeek.App.UiTests.Infrastructure;

namespace LayerPeek.App.UiTests.Platform;

/// <summary>
/// A Keychain call that waits for a macOS dialog (unlock, allow access) must not freeze the windows or the tray menu
/// (decided 2026-10-29): called on the Avalonia UI thread, the store returns at once and the UI thread keeps processing
/// its jobs while the Keychain call is blocked.
/// </summary>
[SupportedOSPlatform("macos")]
public sealed class KeychainSecretStoreUiThreadTests(HeadlessSession session)
{
    [Fact]
    public Task BlockedKeychainCallDoesNotBlockTheUiThread()
    {
        Assert.SkipUnless(OperatingSystem.IsMacOS(), "The Keychain store exists only on macOS.");
        return session.RunAsync(static async _ =>
        {
            using BlockingKeychainItems items = new();
            KeychainSecretStore store = new("LayerPeek.Tests/ui-thread", items);
            int uiThread = Environment.CurrentManagedThreadId;

            Task<string?> get = BlockingKeychainItems.FromCaller(() => store.GetAsync("printer/mk4", CancellationToken.None));
            Assert.True(await items.WaitUntilBlockedAsync(TimeSpan.FromSeconds(10)));

            // The UI thread is free: a job posted to it runs while the Keychain call is still blocked.
            bool ran = await Dispatcher.UIThread.InvokeAsync(static () => true, DispatcherPriority.Input).GetTask().WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            Assert.True(ran);
            Assert.False(get.IsCompleted);

            items.Release();
            Assert.Null(await get.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));
            // The UI thread is dedicated (the pool never runs on it), so its id is a valid check here.
            Assert.DoesNotContain(uiThread, items.CallThreads);
            Assert.Equal(0, items.InlineCalls);
        });
    }
}
