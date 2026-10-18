using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using PrusaTrayDiagnostic.App.Platform;
using PrusaTrayDiagnostic.App.Tray.Menu;
using PrusaTrayDiagnostic.Core.Web;

namespace PrusaTrayDiagnostic.App.Tests.Tray.Menu;

/// <summary>Records opened addresses instead of starting a browser.</summary>
internal sealed class FakeUrlLauncher : IUrlLauncher
{
    private static readonly TimeSpan WaitTimeout = TimeSpan.FromSeconds(10);

    private readonly Lock _lock = new();
    private readonly List<Uri> _opened = [];
    private readonly List<Uri> _openedSecret = [];
    private readonly List<(int Count, TaskCompletionSource Reached)> _waiters = [];

    public bool Result { get; set; } = true;

    public IReadOnlyList<Uri> Opened
    {
        get
        {
            lock (_lock)
            {
                return [.. _opened];
            }
        }
    }

    /// <summary>Addresses that carry a secret (proxy addresses with their token, T-028), in call order.</summary>
    public IReadOnlyList<Uri> OpenedSecret
    {
        get
        {
            lock (_lock)
            {
                return [.. _openedSecret];
            }
        }
    }

    public Task<bool> OpenAsync(Uri uri)
    {
        lock (_lock)
        {
            _opened.Add(uri);
            ReleaseWaitersLocked();
        }

        return Task.FromResult(Result);
    }

    public Task<bool> OpenSecretAsync(Uri uri, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromResult(false);
        }

        lock (_lock)
        {
            _openedSecret.Add(uri);
            ReleaseWaitersLocked();
        }

        return Task.FromResult(Result);
    }

    /// <summary>
    /// Completes when <paramref name="count"/> addresses were opened in total (plain or secret): the web opener asks the
    /// gateway off the calling thread, so the launcher is reached asynchronously. No polling; fails after a real-time guard.
    /// </summary>
    public async Task WaitForOpenedAsync(int count)
    {
        (int Count, TaskCompletionSource Reached) waiter;
        lock (_lock)
        {
            if (OpenedCountLocked >= count)
            {
                return;
            }

            waiter = (count, new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));
            _waiters.Add(waiter);
        }

        try
        {
            await waiter.Reached.Task.WaitAsync(WaitTimeout, TestContext.Current.CancellationToken);
        }
        finally
        {
            // A timed-out or cancelled wait leaves no waiter behind (a released one is already gone).
            lock (_lock)
            {
                _waiters.Remove(waiter);
            }
        }
    }

    private int OpenedCountLocked => _opened.Count + _openedSecret.Count;

    private void ReleaseWaitersLocked()
    {
        for (int i = _waiters.Count - 1; i >= 0; i--)
        {
            if (OpenedCountLocked >= _waiters[i].Count)
            {
                _waiters[i].Reached.TrySetResult();
                _waiters.RemoveAt(i);
            }
        }
    }
}

/// <summary>
/// Creates the web opener used by the tray menu with in-memory fakes (no proxy, no OS credential store). Without a gateway
/// it behaves like a printer without a stored password: the plain address is opened.
/// </summary>
internal static class TestWebOpener
{
    public static PrinterWebOpener Create(FakeUrlLauncher launcher, IPrinterWebGateway? gateway = null, ILogger? logger = null) =>
        new(gateway ?? FakeWebGateway.WithoutPassword(), launcher, logger ?? NullLogger.Instance);
}

/// <summary>Keeps the menu as a list of entries and logs every operation, like the NativeMenu adapter would apply them.</summary>
internal sealed class FakeMenuTarget : ITrayMenuTarget
{
    public event EventHandler<TrayMenuEntry>? ItemClicked;

    public List<TrayMenuEntry> Fixed { get; } = [];

    public List<TrayMenuEntry> Printers { get; } = [];

    public List<string> Operations { get; } = [];

    public int ClickSubscriberCount => ItemClicked?.GetInvocationList().Length ?? 0;

    public void SetFixedItems(IReadOnlyList<TrayMenuEntry> items)
    {
        Operations.Add("fixed");
        Fixed.Clear();
        Fixed.AddRange(items);
        // Like the real target: the whole menu is replaced, printer items are inserted again.
        Printers.Clear();
    }

    public void InsertPrinterItem(int index, TrayMenuEntry entry)
    {
        Operations.Add($"insert {index}");
        Printers.Insert(index, entry);
    }

    public void UpdatePrinterItem(int index, TrayMenuEntry entry)
    {
        Operations.Add($"update {index}");
        Printers[index] = entry;
    }

    public void RemovePrinterItem(int index)
    {
        Operations.Add($"remove {index}");
        Printers.RemoveAt(index);
    }

    public void ClearPrinterItems()
    {
        Operations.Add("clear");
        Printers.Clear();
    }

    public void Click(TrayMenuEntry entry) => ItemClicked?.Invoke(this, entry);
}

internal sealed class FakeAppCommands : ITrayAppCommands
{
    public int AboutCount { get; private set; }

    public int QuitCount { get; private set; }

    public int DiagnosticsCount { get; private set; }

    public Exception? ThrowOnAbout { get; set; }

    public void ShowAbout()
    {
        AboutCount++;
        if (ThrowOnAbout is not null)
        {
            throw ThrowOnAbout;
        }
    }

    /// <summary>Printer ids of diagnostics requests in order; <see langword="null"/> = no preselection.</summary>
    public List<string?> DiagnosticsRequests { get; } = [];

    public void ShowDiagnostics(string? printerId)
    {
        DiagnosticsCount++;
        DiagnosticsRequests.Add(printerId);
    }

    public int SettingsCount { get; private set; }

    public void ShowSettings() => SettingsCount++;

    public void Quit() => QuitCount++;
}
