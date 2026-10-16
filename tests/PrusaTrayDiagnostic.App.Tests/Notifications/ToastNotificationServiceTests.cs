using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;

using Microsoft.Extensions.Logging;

using PrusaTrayDiagnostic.App.Notifications;
using PrusaTrayDiagnostic.App.Tests.Tray;
using PrusaTrayDiagnostic.Core.Events;
using PrusaTrayDiagnostic.Core.Notifications;

using static PrusaTrayDiagnostic.App.Tests.Tray.TestPrinters;

namespace PrusaTrayDiagnostic.App.Tests.Notifications;

public sealed class ToastNotificationServiceTests : IDisposable
{
    private readonly FakeToastPlatform _platform = new();
    private readonly ConcurrentQueue<string?> _activations = new();
    private readonly ListLogger<ToastNotificationService> _logger = new();
    private ToastNotificationService? _service;

    public void Dispose()
    {
        _service?.Dispose();
        // Unblocks a worker a test left waiting, so no thread outlives the test.
        _platform.Session.Release();
    }

    private ToastNotificationService Service => _service ??= Create(CultureInfo.GetCultureInfo("en-US"));

    [Fact]
    public void ConstructionStartsNothing()
    {
        _ = Service;

        Assert.Equal(0, _platform.OpenCount);
    }

    [Fact]
    public async Task ToastIsShownOnTheNotificationThread()
    {
        Service.Show(Notification(PrintEventKind.PrintFinished));

        string xml = Assert.Single(await _platform.Session.WaitForShownAsync(1));
        Assert.Contains("Print finished", xml, StringComparison.Ordinal);
        Assert.Equal(1, _platform.OpenCount);
        Assert.NotEqual(Environment.CurrentManagedThreadId, _platform.OpenThreadId);
        Assert.Equal(_platform.OpenThreadId, _platform.Session.ShowThreadId);
    }

    [Fact]
    public async Task TextsUseTheCultureGivenAtConstruction()
    {
        _service = Create(CultureInfo.GetCultureInfo("cs-CZ"));

        _service.Show(Notification(PrintEventKind.PrintFinished));

        Assert.Contains("Tisk dokončen", Assert.Single(await _platform.Session.WaitForShownAsync(1)), StringComparison.Ordinal);
    }

    [Fact]
    public async Task SoundFlagReachesTheToast()
    {
        Service.Show(Notification(PrintEventKind.PrintFinished) with { PlaySound = false });
        Service.Show(Notification(PrintEventKind.Error) with { PlaySound = true });

        IReadOnlyList<string> shown = await _platform.Session.WaitForShownAsync(2);
        Assert.Contains("silent=\"true\"", shown[0], StringComparison.Ordinal);
        Assert.DoesNotContain("<audio", shown[1], StringComparison.Ordinal);
    }

    [Fact]
    public async Task ShowFromManyThreadsDeliversEveryNotification()
    {
        const int Threads = 8;
        const int PerThread = ToastNotificationService.MaxPending / Threads;
        using Barrier start = new(Threads);

        Thread[] threads = [.. Enumerable.Range(0, Threads).Select(t => new Thread(() =>
        {
            start.SignalAndWait();
            for (int i = 0; i < PerThread; i++)
            {
                Service.Show(Notification(PrintEventKind.PrintStarted) with { FileName = $"t{t}-{i}.bgcode" });
            }
        }))];
        _ = Service;
        foreach (Thread thread in threads)
        {
            thread.Start();
        }

        foreach (Thread thread in threads)
        {
            thread.Join(TestTimeouts.Wait);
        }

        IReadOnlyList<string> shown = await _platform.Session.WaitForShownAsync(Threads * PerThread);
        Assert.Equal(Threads * PerThread, shown.Distinct().Count());
        Assert.Equal(1, _platform.OpenCount);
    }

    [Fact]
    public async Task ShowDoesNotWaitForASlowToastAndDropsWhatDoesNotFitTheQueue()
    {
        _platform.Session.Block();
        Service.Show(Notification(PrintEventKind.PrintStarted) with { FileName = "first" });
        await _platform.Session.WaitUntilShowEnteredAsync();

        Stopwatch elapsed = Stopwatch.StartNew();
        for (int i = 0; i < ToastNotificationService.MaxPending + 3; i++)
        {
            Service.Show(Notification(PrintEventKind.PrintStarted) with { FileName = $"queued {i}" });
        }

        elapsed.Stop();
        Assert.True(elapsed.Elapsed < TimeSpan.FromSeconds(2), $"Show blocked for {elapsed.Elapsed}.");

        _platform.Session.Release();
        IReadOnlyList<string> shown = await _platform.Session.WaitForShownAsync(ToastNotificationService.MaxPending + 1);
        await Task.Delay(100, TestContext.Current.CancellationToken);
        Assert.Equal(ToastNotificationService.MaxPending + 1, _platform.Session.Shown.Count);
        Assert.Contains("queued 0", shown[1], StringComparison.Ordinal);
        Assert.Contains(_logger.Entries, e => e.Level == LogLevel.Warning);
    }

    [Fact]
    public async Task FailingToastIsLoggedAndTheNextOneIsStillShown()
    {
        _platform.Session.FailNext = new InvalidOperationException("WinRT failed");

        Service.Show(Notification(PrintEventKind.PrintStarted));
        Service.Show(Notification(PrintEventKind.PrintFinished));

        string xml = Assert.Single(await _platform.Session.WaitForShownAsync(1));
        Assert.Contains("Print finished", xml, StringComparison.Ordinal);
        await WaitForAsync(() => _logger.Entries.Any(e => e.Level == LogLevel.Error && e.Exception?.Message == "WinRT failed"));
    }

    [Fact]
    public async Task TooLongPrinterIdIsShownWithoutClickAndLogged()
    {
        Service.Show(Notification(PrintEventKind.PrintFinished) with { Printer = Definition(id: new string('x', 2000)) });

        string xml = Assert.Single(await _platform.Session.WaitForShownAsync(1));
        Assert.DoesNotContain("launch=", xml, StringComparison.Ordinal);
        Assert.Contains(_logger.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains("click", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task FailedInitializationIsLoggedOnceAndNotificationsAreDiscarded()
    {
        _platform.OpenFailure = new InvalidOperationException("No WinRT");

        Service.Show(Notification(PrintEventKind.PrintStarted));
        await WaitForAsync(() => _logger.Entries.Any(e => e.Exception?.Message == "No WinRT"));
        Service.Show(Notification(PrintEventKind.PrintFinished));
        Service.Start();
        await Task.Delay(200, TestContext.Current.CancellationToken);

        Assert.Equal(1, _platform.OpenCount);
        Assert.Single(_logger.Entries, e => e.Exception?.Message == "No WinRT");
    }

    [Fact]
    public async Task StartOpensThePlatformOnceWithoutShowingAnything()
    {
        Service.Start();
        Service.Start();

        await WaitForAsync(() => _platform.OpenCount > 0);
        await Task.Delay(100, TestContext.Current.CancellationToken);
        Assert.Equal(1, _platform.OpenCount);
        Assert.Empty(_platform.Session.Shown);
    }

    [Fact]
    public async Task ShowAfterDisposeIsIgnored()
    {
        Service.Dispose();

        Service.Show(Notification(PrintEventKind.PrintStarted));
        Service.Start();
        await Task.Delay(100, TestContext.Current.CancellationToken);

        Assert.Equal(0, _platform.OpenCount);
        Service.Dispose();
    }

    [Fact]
    public async Task DisposeDoesNotWaitForABlockedToastAndReleasesTheSessionAfterwards()
    {
        _platform.Session.Block();
        Service.Show(Notification(PrintEventKind.PrintStarted));
        Service.Show(Notification(PrintEventKind.PrintFinished));
        await _platform.Session.WaitUntilShowEnteredAsync();

        Stopwatch elapsed = Stopwatch.StartNew();
        Service.Dispose();
        elapsed.Stop();

        Assert.True(elapsed.Elapsed < TimeSpan.FromSeconds(2), $"Dispose blocked for {elapsed.Elapsed}.");
        Assert.False(_platform.Session.Disposed);
        _platform.Session.Release();
        await WaitForAsync(() => _platform.Session.Disposed);
        // The queued toast is not shown after dispose.
        Assert.Single(_platform.Session.Shown);
        Assert.Equal(_platform.OpenThreadId, _platform.Session.DisposeThreadId);
    }

    [Fact]
    public async Task ClickIsForwarded()
    {
        Service.Start();
        await WaitForAsync(() => _platform.Activated is not null);

        _platform.Activated!("printer=mk4");

        Assert.Equal("printer=mk4", Assert.Single(_activations));
    }

    [Fact]
    public async Task ClickAfterDisposeIsIgnored()
    {
        Service.Start();
        await WaitForAsync(() => _platform.Activated is not null);
        Service.Dispose();

        _platform.Activated!("printer=mk4");

        Assert.Empty(_activations);
    }

    [Fact]
    public async Task FailingClickHandlerIsLoggedNotThrown()
    {
        _service = new ToastNotificationService(_platform, _ => throw new InvalidOperationException("boom"), CultureInfo.InvariantCulture, _logger);
        _service.Start();
        await WaitForAsync(() => _platform.Activated is not null);

        _platform.Activated!("printer=mk4");

        Assert.Contains(_logger.Entries, e => e.Level == LogLevel.Error && e.Exception?.Message == "boom");
    }

    [Fact]
    public void NullArgumentsAreRejected()
    {
        CultureInfo culture = CultureInfo.InvariantCulture;
        Assert.Throws<ArgumentNullException>(() => new ToastNotificationService(null!, _activations.Enqueue, culture, _logger));
        Assert.Throws<ArgumentNullException>(() => new ToastNotificationService(_platform, null!, culture, _logger));
        Assert.Throws<ArgumentNullException>(() => new ToastNotificationService(_platform, _activations.Enqueue, null!, _logger));
        Assert.Throws<ArgumentNullException>(() => new ToastNotificationService(_platform, _activations.Enqueue, culture, null!));
        Assert.Throws<ArgumentNullException>(() => Service.Show(null!));
    }

    private ToastNotificationService Create(CultureInfo culture) => new(_platform, _activations.Enqueue, culture, _logger);

    private static PrintNotification Notification(PrintEventKind kind) => new()
    {
        Printer = Definition(),
        Kind = kind,
        Timestamp = Now,
    };

    private static async Task WaitForAsync(Func<bool> condition)
    {
        Stopwatch elapsed = Stopwatch.StartNew();
        while (!condition())
        {
            Assert.True(elapsed.Elapsed < TestTimeouts.Wait, "Condition not met in time.");
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }
    }

    private sealed class FakeToastPlatform : IToastPlatform
    {
        private int _openCount;

        public FakeToastSession Session { get; } = new();

        public Exception? OpenFailure { get; set; }

        public int OpenCount => Volatile.Read(ref _openCount);

        public int OpenThreadId { get; private set; }

        public Action<string?>? Activated { get; private set; }

        public IToastSession Open(Action<string?> activated)
        {
            Interlocked.Increment(ref _openCount);
            OpenThreadId = Environment.CurrentManagedThreadId;
            if (OpenFailure is not null)
            {
                throw OpenFailure;
            }

            Activated = activated;
            return Session;
        }
    }

    private sealed class FakeToastSession : IToastSession
    {
        private readonly Lock _lock = new();
        private readonly List<string> _shown = [];
        private readonly ManualResetEventSlim _gate = new(initialState: true);
        private readonly TaskCompletionSource _entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private volatile bool _disposed;

        public Exception? FailNext { get; set; }

        public int ShowThreadId { get; private set; }

        public int DisposeThreadId { get; private set; }

        public bool Disposed => _disposed;

        public IReadOnlyList<string> Shown
        {
            get
            {
                lock (_lock)
                {
                    return [.. _shown];
                }
            }
        }

        public void Block() => _gate.Reset();

        public void Release() => _gate.Set();

        public Task WaitUntilShowEnteredAsync() => _entered.Task.WaitAsync(TestTimeouts.Wait, TestContext.Current.CancellationToken);

        public async Task<IReadOnlyList<string>> WaitForShownAsync(int count)
        {
            await WaitForAsync(() => Shown.Count >= count);
            return Shown;
        }

        public void Show(string toastXml)
        {
            ShowThreadId = Environment.CurrentManagedThreadId;
            _entered.TrySetResult();
            _gate.Wait(TestTimeouts.Wait);
            if (FailNext is { } failure)
            {
                FailNext = null;
                throw failure;
            }

            lock (_lock)
            {
                _shown.Add(toastXml);
            }
        }

        public void Dispose()
        {
            DisposeThreadId = Environment.CurrentManagedThreadId;
            _disposed = true;
        }
    }
}
