using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using PrusaTrayDiagnostic.App.Platform;
using PrusaTrayDiagnostic.App.Tests.Settings;
using PrusaTrayDiagnostic.App.Tray.Menu;
using PrusaTrayDiagnostic.Core.Secrets;

namespace PrusaTrayDiagnostic.App.Tests.Tray.Menu;

/// <summary>Records opened addresses instead of starting a browser.</summary>
internal sealed class FakeUrlLauncher : IUrlLauncher
{
    private readonly Lock _lock = new();
    private readonly List<Uri> _opened = [];
    private readonly List<(Uri BaseUri, string UserName, string Password)> _openedWithCredentials = [];

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

    /// <summary>Addresses opened with credentials (base address, user name, password), in call order.</summary>
    public IReadOnlyList<(Uri BaseUri, string UserName, string Password)> OpenedWithCredentials
    {
        get
        {
            lock (_lock)
            {
                return [.. _openedWithCredentials];
            }
        }
    }

    public Task<bool> OpenAsync(Uri uri)
    {
        lock (_lock)
        {
            _opened.Add(uri);
        }

        return Task.FromResult(Result);
    }

    public Task<bool> OpenWithCredentialsAsync(Uri baseUri, string userName, string password, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            _openedWithCredentials.Add((baseUri, userName, password));
        }

        return Task.FromResult(Result);
    }

    /// <summary>
    /// Waits until <paramref name="count"/> addresses were opened in total (with or without credentials): the web opener
    /// reads the password off the calling thread, so the launcher is reached asynchronously.
    /// </summary>
    public async Task WaitForOpenedAsync(int count)
    {
        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        while (OpenedCount < count)
        {
            await Task.Delay(5, timeout.Token);
        }
    }

    private int OpenedCount
    {
        get
        {
            lock (_lock)
            {
                return _opened.Count + _openedWithCredentials.Count;
            }
        }
    }
}

/// <summary>Creates the web opener used by the tray menu with in-memory fakes (no OS credential store is touched).</summary>
internal static class TestWebOpener
{
    public static PrinterWebOpener Create(FakeUrlLauncher launcher, ISecretStore? secrets = null, ILogger? logger = null) =>
        new(secrets ?? new FakeSecretStore(), launcher, logger ?? NullLogger.Instance);
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
