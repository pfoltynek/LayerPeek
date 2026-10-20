using System.Text;

using Microsoft.Extensions.Logging;

using PrusaTrayDiagnostic.App.SingleInstance;
using PrusaTrayDiagnostic.App.Tests.Tray;

namespace PrusaTrayDiagnostic.App.Tests.SingleInstance;

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

    [Theory]
    [InlineData("")]
    [InlineData("ACTIVATE\n")]
    [InlineData("quit\n")]
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

    private static MemoryStream Client(string message) => new(Encoding.ASCII.GetBytes(message));

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
