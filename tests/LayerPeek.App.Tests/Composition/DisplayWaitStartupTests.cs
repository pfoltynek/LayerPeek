using System.Globalization;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using LayerPeek.App.Composition;
using LayerPeek.App.Notifications;
using LayerPeek.App.Platform;
using LayerPeek.App.Tests.Notifications;
using LayerPeek.App.Tests.Tray;
using LayerPeek.App.Tray;
using LayerPeek.Core.Events;
using LayerPeek.Core.Notifications;

using static LayerPeek.App.Tests.Tray.TestPrinters;

namespace LayerPeek.App.Tests.Composition;

/// <summary>
/// T-099 (user decision 2026-10-29): while macOS has no active display the services run (monitoring with its UI-thread work,
/// notifications) on the waiting main thread; once a display wakes the UI attaches to the same instance; quitting meanwhile
/// stops them; with an active display nothing starts before the UI (the normal start). Fake display, fake monitoring and a
/// fake notification center under the real notification service: runs on every OS, nothing reaches the system.
/// </summary>
public sealed class DisplayWaitStartupTests : IDisposable
{
    private static readonly TimeSpan Poll = TimeSpan.FromMilliseconds(10);

    private readonly FakeUserNotificationCenter _center = new();
    private readonly UserNotificationService _notifications;
    private readonly StartupLoop _loop = new(new ManagedStartupRunLoop());
    private readonly List<FakeMonitoring> _started = [];
    private readonly List<FakeMonitoring> _stopped = [];
    private int _continuedOn;

    public DisplayWaitStartupTests() =>
        _notifications = new UserNotificationService(_center, static () => CultureInfo.GetCultureInfo("en-US"), new ListLogger<UserNotificationService>());

    public void Dispose()
    {
        foreach (FakeMonitoring monitoring in _started)
        {
            monitoring.Dispose();
        }

        _notifications.Dispose();
        _loop.Dispose();
    }

    [Fact]
    public async Task MonitoringAndNotificationsRunWhileWaitingAndTheUiAttachesToTheSameInstance()
    {
        // The display wakes only after the monitoring updated the tray and notifications reached the notification center.
        FakeMonitoring? monitoring = null;
        ConditionDisplays displays = new(() => monitoring is { UiUpdates: >= 3 } && _center.Posted.Count >= 3);
        DisplayWaitStartup<FakeMonitoring> startup = Create(displays, dispatcher => StartAsync(dispatcher, started => monitoring = started));
        int waitingThread = Environment.CurrentManagedThreadId;
        SynchronizationContext? before = SynchronizationContext.Current;

        DisplayWaitOutcome<FakeMonitoring> outcome = startup.Run(CancellationToken.None);

        Assert.True(outcome.Result == StartupDisplayWaitResult.Ready, $"{outcome.Result}: {monitoring?.UiUpdates} UI updates, {_center.Posted.Count} notifications, {_started.Count} started, {_loop.Dispatcher.PendingCount} pending, {_center.AuthorizationRequests} auth.");
        Assert.True(displays.Polls > 1, "The app waited for the display.");
        Assert.Same(before, SynchronizationContext.Current);
        Assert.NotNull(outcome.Services);
        Assert.True(outcome.Services.IsCompletedSuccessfully);
        FakeMonitoring started = Assert.Single(_started);
        Assert.Same(started, await outcome.Services);
        // The services were started on the waiting (main) thread and their awaits came back to it.
        Assert.Equal(waitingThread, _continuedOn);
        Assert.All(started.UiThreads, thread => Assert.Equal(waitingThread, thread));
        Assert.True(_center.Posted.Count >= 3, "Notifications were delivered while waiting.");

        // The UI attaches: later UI-thread work of the same monitoring goes to Avalonia's dispatcher (here a manual one).
        ManualDispatcher avalonia = new();
        _loop.Dispatcher.AttachTo(avalonia);
        int updatesBefore = started.UiUpdates;
        await Waiting.ForAsync(() => avalonia.Pending > 0);
        avalonia.RunAll();
        Assert.True(started.UiUpdates > updatesBefore);
        Assert.Single(_started);
        Assert.Empty(_stopped);
    }

    [Fact]
    public void WithAnActiveDisplayNothingStartsBeforeTheUi()
    {
        ConditionDisplays displays = new(static () => true);
        DisplayWaitStartup<FakeMonitoring> startup = Create(displays, static _ => throw new InvalidOperationException("Never started before the UI."));
        SynchronizationContext? before = SynchronizationContext.Current;

        DisplayWaitOutcome<FakeMonitoring> outcome = startup.Run(CancellationToken.None);

        Assert.Equal(StartupDisplayWaitResult.Ready, outcome.Result);
        Assert.Null(outcome.Services);
        Assert.Equal(1, displays.Polls);
        Assert.Same(before, SynchronizationContext.Current);
        Assert.Equal(0, _loop.Dispatcher.PendingCount);
        Assert.Empty(_center.Posted);
    }

    [Fact]
    public void QuittingWhileWaitingStopsTheRunningServices()
    {
        using CancellationTokenSource quit = new();
        ConditionDisplays displays = new(static () => false);
        DisplayWaitStartup<FakeMonitoring> startup = Create(displays, dispatcher => StartAsync(dispatcher, started => started.OnUiUpdate = count =>
        {
            if (count == 2)
            {
                // Like SIGTERM while the display sleeps.
                quit.Cancel();
            }
        }));

        DisplayWaitOutcome<FakeMonitoring> outcome = startup.Run(quit.Token);

        Assert.Equal(StartupDisplayWaitResult.Cancelled, outcome.Result);
        Assert.Null(outcome.Services);
        Assert.Same(Assert.Single(_started), Assert.Single(_stopped));
    }

    [Fact]
    public void QuittingWaitsForTheServicesOnlyUpToTheLimit()
    {
        using CancellationTokenSource quit = new();
        ListLoggerFactory loggers = new();
        ConditionDisplays displays = new(() =>
        {
            quit.Cancel();
            return false;
        });
        DisplayWaitStartup<FakeMonitoring> startup = new(
            displays,
            _loop,
            dispatcher => StartAsync(dispatcher, static _ => { }),
            static _ => new TaskCompletionSource().Task,
            loggers,
            Poll,
            TimeSpan.FromMilliseconds(100));

        DisplayWaitOutcome<FakeMonitoring> outcome = startup.Run(quit.Token);

        Assert.Equal(StartupDisplayWaitResult.Cancelled, outcome.Result);
        Assert.Contains(loggers.Messages, static m => m.Contains("did not finish", StringComparison.Ordinal));
    }

    [Fact]
    public void AFailedStartOfTheServicesEndsTheWaitWithItsException()
    {
        ConditionDisplays displays = new(static () => false);
        DisplayWaitStartup<FakeMonitoring> startup = Create(displays, static _ => Task.FromException<FakeMonitoring>(new InvalidOperationException("No services.")));
        SynchronizationContext? before = SynchronizationContext.Current;

        InvalidOperationException failure = Assert.Throws<InvalidOperationException>(() => startup.Run(CancellationToken.None));

        Assert.Equal("No services.", failure.Message);
        Assert.Same(before, SynchronizationContext.Current);
    }

    /// <summary>Review finding 3: a failed start and a quit in the same pause end with the quit (exit code 0), the failure logged.</summary>
    [Fact]
    public void AQuitWinsOverAFailedStartOfTheServices()
    {
        using CancellationTokenSource quit = new();
        ListLoggerFactory loggers = new();
        ConditionDisplays displays = new(static () => false);
        DisplayWaitStartup<FakeMonitoring> startup = new(
            displays,
            _loop,
            _ =>
            {
                quit.Cancel();
                return Task.FromException<FakeMonitoring>(new InvalidOperationException("No services."));
            },
            static _ => throw new InvalidOperationException("Never stopped: they never started."),
            loggers,
            Poll);

        DisplayWaitOutcome<FakeMonitoring> outcome = startup.Run(quit.Token);

        Assert.Equal(StartupDisplayWaitResult.Cancelled, outcome.Result);
        Assert.Null(outcome.Services);
        Assert.Contains(loggers.Messages, static m => m.Contains("Starting the monitoring failed while the app was asked to quit", StringComparison.Ordinal));
    }

    private DisplayWaitStartup<FakeMonitoring> Create(IActiveDisplays displays, Func<IUiDispatcher, Task<FakeMonitoring>> start) =>
        new(displays, _loop, start, StopAsync, NullLoggerFactory.Instance, Poll);

    /// <summary>Like <c>Program.StartServicesAsync</c>: loading happens off the main thread, monitoring starts after the await.</summary>
    private async Task<FakeMonitoring> StartAsync(IUiDispatcher dispatcher, Action<FakeMonitoring> configure)
    {
        await Task.Run(static () => Thread.Sleep(5), TestContext.Current.CancellationToken).ConfigureAwait(true);
        _continuedOn = Environment.CurrentManagedThreadId;
        FakeMonitoring monitoring = new(dispatcher, _notifications);
        configure(monitoring);
        _started.Add(monitoring);
        monitoring.Start();
        return monitoring;
    }

    private Task StopAsync(FakeMonitoring monitoring)
    {
        monitoring.Stop();
        _stopped.Add(monitoring);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Monitoring in the background like the real session: each poll updates the tray (through the UI thread's dispatcher) and
    /// shows a notification (from the background thread, like <c>PrintEventNotifier</c>).
    /// </summary>
    private sealed class FakeMonitoring(IUiDispatcher dispatcher, INotificationService notifications) : IDisposable
    {
        private readonly CancellationTokenSource _stop = new();
        private readonly Lock _lock = new();
        private readonly List<int> _uiThreads = [];
        private int _uiUpdates;
        private bool _disposed;

        public int UiUpdates => Volatile.Read(ref _uiUpdates);

        public IReadOnlyList<int> UiThreads
        {
            get
            {
                lock (_lock)
                {
                    return [.. _uiThreads];
                }
            }
        }

        public Action<int>? OnUiUpdate { get; set; }

        public void Start()
        {
            CancellationToken stop = _stop.Token;
            _ = Task.Run(
                async () =>
                {
                    while (!stop.IsCancellationRequested)
                    {
                        dispatcher.Post(() =>
                        {
                            lock (_lock)
                            {
                                _uiThreads.Add(Environment.CurrentManagedThreadId);
                            }

                            int count = Interlocked.Increment(ref _uiUpdates);
                            OnUiUpdate?.Invoke(count);
                        });
                        notifications.Show(new PrintNotification { Printer = Definition(), Kind = PrintEventKind.PrintFinished, Timestamp = Now });
                        try
                        {
                            await Task.Delay(5, stop);
                        }
                        catch (OperationCanceledException)
                        {
                            return;
                        }
                    }
                },
                CancellationToken.None);
        }

        public void Stop()
        {
            lock (_lock)
            {
                if (!_disposed)
                {
                    _stop.Cancel();
                }
            }
        }

        public void Dispose()
        {
            Stop();
            lock (_lock)
            {
                _disposed = true;
            }

            _stop.Dispose();
        }
    }

    /// <summary>No active display until <paramref name="awake"/> says so; gives up (a failed check) after many polls.</summary>
    private sealed class ConditionDisplays(Func<bool> awake) : IActiveDisplays
    {
        private const int MaxPolls = 1000;

        public int Polls { get; private set; }

        public bool TryCount(out uint count, out int error)
        {
            Polls++;
            if (Polls > MaxPolls)
            {
                count = 0;
                error = -1;
                return false;
            }

            count = awake() ? 1u : 0u;
            error = 0;
            return true;
        }
    }

    private sealed class ListLoggerFactory : ILoggerFactory
    {
        private readonly Lock _lock = new();
        private readonly List<string> _messages = [];

        public IReadOnlyList<string> Messages
        {
            get
            {
                lock (_lock)
                {
                    return [.. _messages];
                }
            }
        }

        public void AddProvider(ILoggerProvider provider)
        {
        }

        public ILogger CreateLogger(string categoryName) => new Logger(this);

        public void Dispose()
        {
        }

        private sealed class Logger(ListLoggerFactory owner) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                lock (owner._lock)
                {
                    owner._messages.Add(formatter(state, exception));
                }
            }
        }
    }
}
