using PrusaTrayDiagnostic.App.Platform;
using PrusaTrayDiagnostic.App.Tray.Menu;

namespace PrusaTrayDiagnostic.App.Tests.Tray.Menu;

/// <summary>Records opened addresses instead of starting a browser.</summary>
internal sealed class FakeUrlLauncher : IUrlLauncher
{
    private readonly Lock _lock = new();
    private readonly List<Uri> _opened = [];

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

    public Task<bool> OpenAsync(Uri uri)
    {
        lock (_lock)
        {
            _opened.Add(uri);
        }

        return Task.FromResult(Result);
    }
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

    public void ShowDiagnostics() => DiagnosticsCount++;

    public void Quit() => QuitCount++;
}
