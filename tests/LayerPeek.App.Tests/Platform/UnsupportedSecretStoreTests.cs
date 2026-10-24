using LayerPeek.App.Platform;
using LayerPeek.Core.Secrets;

namespace LayerPeek.App.Tests.Platform;

public sealed class UnsupportedSecretStoreTests
{
    private readonly UnsupportedSecretStore _store = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task EveryOperationReportsUnsupported()
    {
        await Assert.ThrowsAsync<SecretStoreException>(() => _store.GetAsync("printer/a", Ct));
        await Assert.ThrowsAsync<SecretStoreException>(() => _store.SetAsync("printer/a", "secret", Ct));
        await Assert.ThrowsAsync<SecretStoreException>(() => _store.DeleteAsync("printer/a", Ct));
    }

    [Fact]
    public void InvalidArgumentsAreRejectedImmediately()
    {
        // Synchronous throw (not a faulted task), like the Windows store.
        Assert.Throws<ArgumentException>(() => { _ = _store.GetAsync(" ", Ct); });
        Assert.Throws<ArgumentNullException>(() => { _ = _store.SetAsync("printer/a", null!, Ct); });
        Assert.Throws<ArgumentNullException>(() => { _ = _store.DeleteAsync(null!, Ct); });
    }

    [Fact]
    public async Task CancellationWins()
    {
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _store.GetAsync("printer/a", new CancellationToken(canceled: true)));
    }

    [Fact]
    public async Task MessageExplainsWhy()
    {
        SecretStoreException ex = await Assert.ThrowsAsync<SecretStoreException>(() => _store.GetAsync("printer/a", Ct));

        Assert.Contains("not supported", ex.Message, StringComparison.OrdinalIgnoreCase);
    }
}
