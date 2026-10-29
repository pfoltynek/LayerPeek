using Microsoft.Extensions.Logging;

using LayerPeek.App.SingleInstance;
using LayerPeek.App.Tests.Tray;
using LayerPeek.App.Tray;

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

    /// <summary>T-099: while the services run before the UI, a provisional handler handles what it can.</summary>
    [Fact]
    public void AProvisionalHandlerGetsTheWaitingCommandsUntilTheAppReplacesIt()
    {
        List<InstanceCommand> provisional = [];
        Assert.True(InstanceCommand.TryForToast("printer=xl", out InstanceCommand toast));
        _queue.Post(toast);

        _queue.AttachProvisional(Handled(provisional), _dispatcher, _logger);
        _dispatcher.RunAll();
        Assert.Equal([toast], provisional);

        _queue.Post(toast);
        // Replaced before the posted command ran: the app's handler gets it, never both.
        _queue.Attach(_handled.Add, _dispatcher, _logger);
        _dispatcher.RunAll();

        Assert.Equal([toast], provisional);
        Assert.Equal([toast], _handled);
        Assert.Throws<InvalidOperationException>(() => _queue.Attach(_handled.Add, _dispatcher, _logger));
    }

    /// <summary>User decision 2026-10-29: a later start during the wait is honoured once the UI runs.</summary>
    [Fact]
    public void AnActivationDuringTheWaitReachesTheAppOnce()
    {
        List<InstanceCommand> provisional = [];
        _queue.AttachProvisional(Kept(provisional), _dispatcher, _logger);

        _queue.Post(InstanceCommand.Activate);
        _dispatcher.RunAll();

        Assert.Equal([InstanceCommand.Activate], provisional);
        Assert.Equal(1, _queue.PendingCount);
        Assert.Empty(_handled);

        _queue.Attach(_handled.Add, _dispatcher, _logger);
        _dispatcher.RunAll();
        _dispatcher.RunAll();

        Assert.Equal([InstanceCommand.Activate], _handled);
        Assert.Equal(0, _queue.PendingCount);
    }

    [Fact]
    public void SeveralActivationsDuringTheWaitCoalesce()
    {
        _queue.Post(InstanceCommand.Activate);
        _queue.AttachProvisional(Kept([]), _dispatcher, _logger);
        for (int i = 0; i < 5; i++)
        {
            _queue.Post(InstanceCommand.Activate);
            _dispatcher.RunAll();
        }

        Assert.Equal(1, _queue.PendingCount);
        _queue.Attach(_handled.Add, _dispatcher, _logger);
        _dispatcher.RunAll();

        Assert.Equal([InstanceCommand.Activate], _handled);
    }

    /// <summary>
    /// The hand-over (the wait is over, the app's handler not attached yet): an activation posted then, and one still
    /// queued at the dispatcher when the app attaches, both reach the app; each exactly once.
    /// </summary>
    [Fact]
    public void ActivationsOfTheHandOverAreNeitherDroppedNorDuplicated()
    {
        StartupUiDispatcher startup = new();
        List<InstanceCommand> provisional = [];
        _queue.AttachProvisional(Kept(provisional), startup, _logger);

        // After the wait: posted to the startup dispatcher, run when Avalonia's takes over.
        _queue.Post(InstanceCommand.Activate);
        startup.AttachTo(_dispatcher);
        Assert.Equal(1, _queue.PendingCount);

        // Before the app's handler: kept again, coalesced with the one waiting.
        _queue.Post(InstanceCommand.Activate);
        _dispatcher.RunAll();
        Assert.Equal(1, _queue.PendingCount);

        // Another command the provisional handler leaves (e.g. the test quit after the wait): kept for the UI too.
        Assert.True(InstanceCommand.TryForToast("printer=xl", out InstanceCommand other));
        _queue.Post(other);
        _dispatcher.RunAll();

        _queue.Attach(_handled.Add, _dispatcher, _logger);
        _dispatcher.RunAll();

        Assert.Equal([InstanceCommand.Activate, other], _handled);
        Assert.Equal(0, _queue.PendingCount);
        Assert.Equal(3, provisional.Count);
    }

    [Fact]
    public void ClosingDropsWhatTheProvisionalHandlerKept()
    {
        _queue.AttachProvisional(Kept([]), _dispatcher, _logger);
        _queue.Post(InstanceCommand.Activate);
        _dispatcher.RunAll();

        _queue.Close();

        Assert.Equal(0, _queue.PendingCount);
    }

    [Fact]
    public void AProvisionalHandlerNeverReplacesAnother()
    {
        _queue.AttachProvisional(Kept([]), _dispatcher, _logger);

        Assert.Throws<InvalidOperationException>(() => _queue.AttachProvisional(Kept([]), _dispatcher, _logger));
    }

    [Fact]
    public void AProvisionalHandlerNeverReplacesTheApp()
    {
        _queue.Attach(_handled.Add, _dispatcher, _logger);

        Assert.Throws<InvalidOperationException>(() => _queue.AttachProvisional(Kept([]), _dispatcher, _logger));
    }

    private static Func<InstanceCommand, bool> Handled(List<InstanceCommand> seen) => command =>
    {
        seen.Add(command);
        return true;
    };

    private static Func<InstanceCommand, bool> Kept(List<InstanceCommand> seen) => command =>
    {
        seen.Add(command);
        return false;
    };
}
