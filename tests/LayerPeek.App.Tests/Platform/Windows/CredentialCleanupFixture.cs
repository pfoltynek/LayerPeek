using LayerPeek.App.Tests.Platform.MacOS;
using LayerPeek.App.Tests.Platform.Windows;
using LayerPeek.Tests.Shared;

[assembly: AssemblyFixture(typeof(CredentialCleanupFixture))]

namespace LayerPeek.App.Tests.Platform.Windows;

/// <summary>
/// Removes all test credentials (Windows Credential Manager, macOS Keychain: <see cref="TestKeychain"/>), test shortcut files (<see cref="TestShortcuts"/>) and test registry keys (<see cref="TestRegistry"/>) before the first test (leftovers of a crashed run) and after the last one.
/// Per-test cleanup still happens in the tests; this is the safety net. Concurrent runs of this assembly
/// are serialized by <see cref="TestRunLock"/>.
/// </summary>
public sealed class CredentialCleanupFixture : IAsyncLifetime
{
    private TestRunLock? _runLock;

    public async ValueTask InitializeAsync()
    {
        _runLock = await TestRunLock.AcquireAsync("LayerPeek.App.Tests", TimeSpan.FromMinutes(5));
        try
        {
            if (OperatingSystem.IsMacOS())
            {
                // Before the sweep: deleting a leftover must not be able to show a dialog either.
                TestKeychain.DisallowInteraction();
            }

            await SweepAsync();
            if (OperatingSystem.IsMacOS())
            {
                await TestKeychain.ProbeAsync();
            }
        }
        catch
        {
            // Do not keep other runs waiting when this one cannot start.
            _runLock.Dispose();
            _runLock = null;
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            await SweepAsync();
        }
        finally
        {
            if (OperatingSystem.IsMacOS())
            {
                TestKeychain.AllowInteraction();
            }

            _runLock?.Dispose();
        }
    }

    private static async Task SweepAsync()
    {
        await TestRunLock.RetryAsync("delete test credentials", static () =>
        {
            if (OperatingSystem.IsWindows())
            {
                TestCredentials.DeleteAll();
            }
            else if (OperatingSystem.IsMacOS())
            {
                TestKeychain.DeleteAll();
            }
        });
        await TestRunLock.RetryAsync("delete test shortcuts", TestShortcuts.DeleteAll);
        await TestRunLock.RetryAsync("delete test registry keys", TestRegistry.DeleteAll);
    }
}
