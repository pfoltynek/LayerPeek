using System.Text;

using Microsoft.Extensions.Logging;

using LayerPeek.App.SingleInstance;
using LayerPeek.App.Tests.Tray;

namespace LayerPeek.App.Tests.SingleInstance;

public sealed class InstanceCommandServerTests : IAsyncDisposable
{
    private static readonly TimeSpan WaitTimeout = TimeSpan.FromSeconds(10);

    private readonly FakeListener _listener = new();
    private readonly ListLogger<InstanceCommandServer> _logger = new();
    private readonly ReceivedCommands _received = new();
    private readonly InstanceCommandServer _server;

    public InstanceCommandServerTests()
    {
        _server = new InstanceCommandServer(_listener, _received.Add, _logger);
    }

    public ValueTask DisposeAsync() => _server.DisposeAsync();

    [Theory]
    [InlineData("activate\n")]
    [InlineData("activate\r\n")]
    [InlineData("activate")]
    public async Task ValidCommandIsReported(string message)
    {
        _listener.AddClient(Client(message));

        Assert.Equal(InstanceCommand.Activate, await _received.NextAsync(WaitTimeout));
    }

    [Fact]
    public async Task ValidCommandIsAcknowledgedAfterItWasHandedOver()
    {
        FakeClient valid = Client("activate\n");
        FakeClient unknown = Client("exit\n");
        _listener.AddClient(unknown);
        _listener.AddClient(valid);

        await _received.NextAsync(WaitTimeout);
        await UntilAsync(() => valid.Reply.Length > 0);

        Assert.Equal("\n"u8.ToArray(), valid.Reply);
        Assert.Empty(unknown.Reply);
    }

    [Theory]
    [InlineData("")]
    [InlineData("ACTIVATE\n")]
    [InlineData("exit\n")]
    public async Task UnknownCommandIsIgnoredAndTheServerGoesOn(string message)
    {
        _listener.AddClient(Client(message));
        _listener.AddClient(Client("activate\n"));

        Assert.Equal(InstanceCommand.Activate, await _received.NextAsync(WaitTimeout));
        Assert.False(_received.TryNext(out _));
        Assert.Contains(_logger.Entries, entry => entry.Message.Contains("unknown", StringComparison.Ordinal));
    }

    [Fact]
    public async Task OverlongMessageIsIgnored()
    {
        _listener.AddClient(Client(new string('a', 10_000)));
        _listener.AddClient(Client("activate\n"));

        Assert.Equal(InstanceCommand.Activate, await _received.NextAsync(WaitTimeout));
        Assert.False(_received.TryNext(out _));
    }

    [Fact]
    public async Task FailedAcceptIsLoggedAndTheServerGoesOn()
    {
        _listener.AddFailure(new IOException("Pipe broken."));
        _listener.AddClient(Client("activate\n"));

        Assert.Equal(InstanceCommand.Activate, await _received.NextAsync(WaitTimeout));
        Assert.Contains(_logger.Entries, entry => entry.Level == LogLevel.Warning && entry.Exception is IOException);
    }

    [Fact]
    public async Task RefusedPeerIsLoggedWithoutContentAndTheServerGoesOn()
    {
        // T-053: a client of another user is closed by the listener; the server logs it and serves the next one.
        _listener.AddFailure(new InstancePeerRefusedException(peerUserId: 777));
        _listener.AddClient(Client("activate\n"));

        Assert.Equal(InstanceCommand.Activate, await _received.NextAsync(WaitTimeout));
        (LogLevel level, string message, Exception? exception) = Assert.Single(_logger.Entries, entry => entry.Message.Contains("another user", StringComparison.Ordinal));
        Assert.Equal(LogLevel.Warning, level);
        Assert.Contains("uid 777", message, StringComparison.Ordinal);
        Assert.Null(exception);
        Assert.DoesNotContain(_logger.Entries, entry => entry.Message.Contains("Accepting a connection", StringComparison.Ordinal));
    }

    [Fact]
    public async Task FailingReceiverIsLoggedAndTheServerGoesOn()
    {
        int calls = 0;
        FakeListener listener = new();
        await using InstanceCommandServer failing = new(
            listener,
            command =>
            {
                if (Interlocked.Increment(ref calls) == 1)
                {
                    throw new InvalidOperationException("Queue gone.");
                }

                _received.Add(command);
            },
            _logger);

        listener.AddClient(Client("activate\n"));
        listener.AddClient(Client("activate\n"));

        Assert.Equal(InstanceCommand.Activate, await _received.NextAsync(WaitTimeout));
        Assert.Contains(_logger.Entries, entry => entry.Level == LogLevel.Error && entry.Exception is InvalidOperationException);
    }

    [Fact]
    public async Task SilentClientIsDroppedAfterTheReadTimeout()
    {
        _listener.AddClient(new SilentStream());
        _listener.AddClient(Client("activate\n"));

        Assert.Equal(InstanceCommand.Activate, await _received.NextAsync(WaitTimeout));
        Assert.Contains(_logger.Entries, entry => entry.Exception is OperationCanceledException);
    }

    [Fact]
    public async Task DisposingStopsTheServerAndTheListener()
    {
        await _server.DisposeAsync();

        Assert.True(_listener.Disposed);
    }

    private static FakeClient Client(string message) => new(message);

    /// <summary>Waits until <paramref name="condition"/> holds (the reply follows the hand-over on the server's task).</summary>
    private static async Task UntilAsync(Func<bool> condition)
    {
        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(WaitTimeout);
        while (!condition())
        {
            await Task.Delay(10, timeout.Token);
        }
    }

    /// <summary>A client that connects and never writes.</summary>
    private sealed class SilentStream : MemoryStream
    {
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return 0;
        }
    }
}
