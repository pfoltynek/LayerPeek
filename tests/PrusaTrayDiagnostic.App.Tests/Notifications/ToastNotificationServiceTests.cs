using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;

using Microsoft.Extensions.Logging;

using PrusaTrayDiagnostic.App.Notifications;
using PrusaTrayDiagnostic.App.Tray;
using PrusaTrayDiagnostic.App.Tests.Tray;
using PrusaTrayDiagnostic.Core.Events;
using PrusaTrayDiagnostic.Core.Notifications;
using PrusaTrayDiagnostic.Core.Printers;
using PrusaTrayDiagnostic.Core.Settings;

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

    private ToastNotificationService Service => _service ??= Create(static () => CultureInfo.GetCultureInfo("en-US"));

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
    public async Task TextsUseTheLanguageOfTheTextCulture()
    {
        _service = Create(static () => CultureInfo.GetCultureInfo("cs-CZ"));

        _service.Show(Notification(PrintEventKind.PrintFinished));

        Assert.Contains("Tisk dokončen", Assert.Single(await _platform.Session.WaitForShownAsync(1)), StringComparison.Ordinal);
    }

    [Fact]
    public async Task LanguageChangedAfterConstructionReachesTheNextToast()
    {
        CultureInfo language = CultureInfo.GetCultureInfo("en-US");
        _service = Create(() => Volatile.Read(ref language));

        _service.Show(Notification(PrintEventKind.PrintFinished));
        await _platform.Session.WaitForShownAsync(1);
        Volatile.Write(ref language, CultureInfo.GetCultureInfo("pl-PL"));
        _service.Show(Notification(PrintEventKind.PrintFinished));
        // The language is read when the worker builds the toast, so switch only after it did.
        await _platform.Session.WaitForShownAsync(2);
        Volatile.Write(ref language, CultureInfo.GetCultureInfo("it-IT"));
        _service.Show(Notification(PrintEventKind.PrintPaused));

        IReadOnlyList<string> shown = await _platform.Session.WaitForShownAsync(3);
        Assert.Contains("Print finished", shown[0], StringComparison.Ordinal);
        Assert.Contains("Zakończono drukowanie", shown[1], StringComparison.Ordinal);
        Assert.Contains("Stampa in pausa", shown[2], StringComparison.Ordinal);
    }

    [Fact]
    public async Task FailingTextCultureIsLoggedAndTheNextToastIsStillShown()
    {
        int calls = 0;
        _service = Create(() => Interlocked.Increment(ref calls) == 1 ? throw new InvalidOperationException("no language") : CultureInfo.GetCultureInfo("en-US"));

        _service.Show(Notification(PrintEventKind.PrintStarted));
        _service.Show(Notification(PrintEventKind.PrintFinished));

        Assert.Contains("Print finished", Assert.Single(await _platform.Session.WaitForShownAsync(1)), StringComparison.Ordinal);
        await Waiting.ForAsync(() => _logger.Entries.Any(e => e.Level == LogLevel.Error && e.Exception?.Message == "no language"));
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
        await Waiting.ForAsync(() => _logger.Entries.Any(e => e.Level == LogLevel.Error && e.Exception?.Message == "WinRT failed"));
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
        await Waiting.ForAsync(() => _logger.Entries.Any(e => e.Exception?.Message == "No WinRT"));
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

        await Waiting.ForAsync(() => _platform.OpenCount > 0);
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
        await Waiting.ForAsync(() => _platform.Session.Disposed);
        // The queued toast is not shown after dispose.
        Assert.Single(_platform.Session.Shown);
        Assert.Equal(_platform.OpenThreadId, _platform.Session.DisposeThreadId);
    }

    [Fact]
    public async Task ClickIsForwarded()
    {
        Service.Start();
        await Waiting.ForAsync(() => _platform.Activated is not null);

        _platform.Activated!("printer=mk4");

        Assert.Equal("printer=mk4", Assert.Single(_activations));
    }

    [Fact]
    public async Task ClickAfterDisposeIsIgnored()
    {
        Service.Start();
        await Waiting.ForAsync(() => _platform.Activated is not null);
        Service.Dispose();

        _platform.Activated!("printer=mk4");

        Assert.Empty(_activations);
    }

    [Fact]
    public async Task FailingClickHandlerIsLoggedNotThrown()
    {
        _service = new ToastNotificationService(_platform, _ => throw new InvalidOperationException("boom"), static () => CultureInfo.InvariantCulture, _logger);
        _service.Start();
        await Waiting.ForAsync(() => _platform.Activated is not null);

        _platform.Activated!("printer=mk4");

        Assert.Contains(_logger.Entries, e => e.Level == LogLevel.Error && e.Exception?.Message == "boom");
    }

    [Fact]
    public async Task SavedSettingsReachTheNextToast()
    {
        // Settings window (T-015): a notification-only change arrives without restart (UpdateSettings), a rename restarts
        // the run (ApplySettings); both flow through the notifier into the next toast. With several printers (T-013) every
        // toast is titled with its own printer.
        FakePrinterMonitor monitor = new();
        using PrintEventNotifier notifier = new(monitor, Service, new ListLogger<PrintEventNotifier>());
        IMonitoringObserver observer = new PrintNotificationObserver(notifier);
        PrinterDefinition mk4 = Definition("mk4", "MK4");
        PrinterDefinition xl = Definition("xl", "XL");
        observer.ApplySettings(new AppSettings { Printers = [mk4, xl], Notifications = new NotificationSettings { Sound = NotificationSound.Off } });
        monitor.Raise(Snapshot(PrinterState.Idle, printerId: "xl"));
        monitor.Raise(Snapshot(PrinterState.Printing, Job(10), printerId: "xl") with { Timestamp = Now.AddSeconds(5) });

        observer.UpdateSettings(new AppSettings { Printers = [mk4, xl], Notifications = new NotificationSettings { Sound = NotificationSound.All } });
        monitor.Raise(Snapshot(PrinterState.Paused, Job(10), printerId: "xl") with { Timestamp = Now.AddSeconds(10) });

        observer.ApplySettings(new AppSettings
        {
            Printers = [mk4, Definition("xl", "XL <left>")],
            Notifications = new NotificationSettings { Sound = NotificationSound.All },
        });
        monitor.Raise(Snapshot(PrinterState.Printing, Job(10), printerId: "xl") with { Timestamp = Now.AddSeconds(15) });

        IReadOnlyList<string> shown = await _platform.Session.WaitForShownAsync(3);
        Assert.Contains("<text>XL</text><text>Print started</text>", shown[0], StringComparison.Ordinal);
        Assert.Contains("silent=\"true\"", shown[0], StringComparison.Ordinal);
        Assert.Contains("<text>XL</text><text>Print paused</text>", shown[1], StringComparison.Ordinal);
        Assert.DoesNotContain("<audio", shown[1], StringComparison.Ordinal);
        // The rename keeps the printer's state (same id and address): the resume is a transition, not a new baseline.
        Assert.Contains("<text>XL &lt;left&gt;</text><text>Print resumed</text>", shown[2], StringComparison.Ordinal);
        Assert.Contains("launch=\"printer=xl\"", shown[2], StringComparison.Ordinal);
    }

    [Fact]
    public void NullArgumentsAreRejected()
    {
        Func<CultureInfo> culture = static () => CultureInfo.InvariantCulture;
        Assert.Throws<ArgumentNullException>(() => new ToastNotificationService(null!, _activations.Enqueue, culture, _logger));
        Assert.Throws<ArgumentNullException>(() => new ToastNotificationService(_platform, null!, culture, _logger));
        Assert.Throws<ArgumentNullException>(() => new ToastNotificationService(_platform, _activations.Enqueue, null!, _logger));
        Assert.Throws<ArgumentNullException>(() => new ToastNotificationService(_platform, _activations.Enqueue, culture, null!));
        Assert.Throws<ArgumentNullException>(() => Service.Show(null!));
    }

    private ToastNotificationService Create(Func<CultureInfo> textCulture) => new(_platform, _activations.Enqueue, textCulture, _logger);

    private static PrintNotification Notification(PrintEventKind kind) => new()
    {
        Printer = Definition(),
        Kind = kind,
        Timestamp = Now,
    };
}
