using Microsoft.Extensions.Logging;

using LayerPeek.App.SingleInstance;
using LayerPeek.App.Tests.Tray;

namespace LayerPeek.App.Tests.SingleInstance;

public sealed class InstanceCommandQueueTests
{
    private readonly InstanceCommandQueue _queue = new();
    private readonly ManualDispatcher _dispatcher = new();
    private readonly ListLogger<InstanceCommandQueue> _logger = new();
    private readonly List<InstanceCommand> _handled = [];

    [Fact]
    public void CommandsWaitUntilTheAppIsReady()
    {
        _queue.Post(InstanceCommand.Activate);

        Assert.Equal(1, _queue.PendingCount);
        Assert.Equal(0, _dispatcher.PostCount);

        _queue.Attach(_handled.Add, _dispatcher, _logger);
        Assert.Empty(_handled);
        _dispatcher.RunAll();

        Assert.Equal([InstanceCommand.Activate], _handled);
        Assert.Equal(0, _queue.PendingCount);
    }

    [Fact]
    public void TheSameCommandWaitsOnlyOnce()
    {
        _queue.Post(InstanceCommand.Activate);
        _queue.Post(InstanceCommand.Activate);

        _queue.Attach(_handled.Add, _dispatcher, _logger);
        _dispatcher.RunAll();

        Assert.Equal([InstanceCommand.Activate], _handled);
    }

    [Fact]
    public void CommandsAfterAttachGoThroughTheDispatcher()
    {
        _queue.Attach(_handled.Add, _dispatcher, _logger);

        _queue.Post(InstanceCommand.Activate);
        _queue.Post(InstanceCommand.Activate);

        Assert.Empty(_handled);
        Assert.Equal(2, _dispatcher.Pending);
        _dispatcher.RunAll();
        Assert.Equal([InstanceCommand.Activate, InstanceCommand.Activate], _handled);
    }

    [Fact]
    public void CommandsAfterCloseAreDropped()
    {
        _queue.Post(InstanceCommand.Activate);
        _queue.Close();
        _queue.Attach(_handled.Add, _dispatcher, _logger);
        _queue.Post(InstanceCommand.Activate);

        _dispatcher.RunAll();

        Assert.Empty(_handled);
        Assert.Equal(0, _queue.PendingCount);
    }

    [Fact]
    public void CommandsPostedBeforeCloseButNotYetRunAreDropped()
    {
        _queue.Attach(_handled.Add, _dispatcher, _logger);
        _queue.Post(InstanceCommand.Activate);

        _queue.Close();
        _dispatcher.RunAll();

        Assert.Empty(_handled);
    }

    [Fact]
    public void HandlerFailureIsLoggedAndDoesNotEscape()
    {
        _queue.Attach(_ => throw new InvalidOperationException("Window creation failed."), _dispatcher, _logger);
        _queue.Post(InstanceCommand.Activate);

        _dispatcher.RunAll();

        Assert.Contains(_logger.Entries, entry => entry.Level == LogLevel.Error && entry.Exception is InvalidOperationException);
    }

    [Fact]
    public void AttachingTwiceIsRefused()
    {
        _queue.Attach(_handled.Add, _dispatcher, _logger);

        Assert.Throws<InvalidOperationException>(() => _queue.Attach(_handled.Add, _dispatcher, _logger));
    }

    [Fact]
    public async Task PostingFromManyThreadsLosesNothing()
    {
        _queue.Attach(_handled.Add, _dispatcher, _logger);

        await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(() =>
        {
            for (int i = 0; i < 50; i++)
            {
                _queue.Post(InstanceCommand.Activate);
            }
        })));
        _dispatcher.RunAll();

        Assert.Equal(400, _handled.Count);
    }

    /// <summary>T-099: while the services run before the UI, a provisional handler takes the commands.</summary>
    [Fact]
    public void AProvisionalHandlerGetsTheWaitingCommandsUntilTheAppReplacesIt()
    {
        List<InstanceCommand> provisional = [];
        Assert.True(InstanceCommand.TryForToast("printer=xl", out InstanceCommand toast));
        _queue.Post(toast);

        _queue.AttachProvisional(provisional.Add, _dispatcher, _logger);
        _dispatcher.RunAll();
        Assert.Equal([toast], provisional);

        _queue.Post(InstanceCommand.Activate);
        // Replaced before the posted command ran: the app's handler gets it, never both.
        _queue.Attach(_handled.Add, _dispatcher, _logger);
        _dispatcher.RunAll();

        Assert.Equal([toast], provisional);
        Assert.Equal([InstanceCommand.Activate], _handled);
        Assert.Throws<InvalidOperationException>(() => _queue.Attach(_handled.Add, _dispatcher, _logger));
    }

    [Fact]
    public void AProvisionalHandlerNeverReplacesAnother()
    {
        _queue.AttachProvisional(_handled.Add, _dispatcher, _logger);

        Assert.Throws<InvalidOperationException>(() => _queue.AttachProvisional(_handled.Add, _dispatcher, _logger));
    }

    [Fact]
    public void AProvisionalHandlerNeverReplacesTheApp()
    {
        _queue.Attach(_handled.Add, _dispatcher, _logger);

        Assert.Throws<InvalidOperationException>(() => _queue.AttachProvisional(_handled.Add, _dispatcher, _logger));
    }
}
