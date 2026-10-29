using LayerPeek.App.Platform;

namespace LayerPeek.App.Tests.Platform;

/// <summary>
/// Answers "may be denied" for the local addresses (like macOS 15+) and records every question and every request to open
/// the settings (T-097); opens nothing.
/// </summary>
internal sealed class FakeLocalNetworkPrivacy : ILocalNetworkPrivacy
{
    private readonly Lock _lock = new();
    private readonly List<(string? Host, string Subject)> _calls = [];
    private int _opened;

    /// <summary>How many times <see cref="OpenSettings"/> was called.</summary>
    public int Opened => Volatile.Read(ref _opened);

    public IReadOnlyList<(string? Host, string Subject)> Calls
    {
        get
        {
            lock (_lock)
            {
                return [.. _calls];
            }
        }
    }

    public bool MayBeDenied(string? host, string subject)
    {
        lock (_lock)
        {
            _calls.Add((host, subject));
        }

        return LocalNetworkAddress.IsLocal(host);
    }

    public void OpenSettings() => Interlocked.Increment(ref _opened);
}
