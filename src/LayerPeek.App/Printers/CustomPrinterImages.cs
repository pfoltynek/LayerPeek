using LayerPeek.Core.Images;
using LayerPeek.Core.Printers;

namespace LayerPeek.App.Printers;

/// <summary>
/// The stored custom printer images (T-039) in memory, for the tray menu and the diagnostics window: loaded once at
/// startup (<see cref="PrinterImageStartup"/>) and updated by every saved change of the settings window, so that nothing
/// reads files while drawing. Thread-safe.
/// </summary>
internal sealed class CustomPrinterImages
{
    private readonly Lock _gate = new();

    // Guarded by _gate; replaced, never changed, so readers need no copy.
    private Dictionary<string, byte[]> _images = new(StringComparer.OrdinalIgnoreCase);

    // Guarded by _gate: ids set or removed since the last load started (a load must not undo them).
    private readonly HashSet<string> _changedSinceLoad = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>An image was added, replaced or removed; raised on the thread of the change. Handlers must be fast.</summary>
    public event EventHandler? Changed;

    /// <summary>The stored image of <paramref name="printerId"/> (PNG), or <see langword="null"/>.</summary>
    public byte[]? Get(string printerId)
    {
        ArgumentNullException.ThrowIfNull(printerId);
        lock (_gate)
        {
            return _images.GetValueOrDefault(printerId);
        }
    }

    /// <summary>The picture of <paramref name="printer"/> with its stored custom image, if it uses one.</summary>
    public PrinterPicture PictureOf(PrinterDefinition printer)
    {
        ArgumentNullException.ThrowIfNull(printer);
        return PrinterPicture.For(printer, printer.Image.Mode == PrinterImageMode.Custom ? Get(printer.Id) : null);
    }

    /// <summary>Remembers <paramref name="png"/> as the image of <paramref name="printerId"/> (no copy is made: never change it).</summary>
    public void Set(string printerId, byte[] png)
    {
        ArgumentNullException.ThrowIfNull(printerId);
        ArgumentNullException.ThrowIfNull(png);
        lock (_gate)
        {
            _images = new Dictionary<string, byte[]>(_images, StringComparer.OrdinalIgnoreCase) { [printerId] = png };
            _changedSinceLoad.Add(printerId);
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Remove(string printerId)
    {
        ArgumentNullException.ThrowIfNull(printerId);
        lock (_gate)
        {
            _changedSinceLoad.Add(printerId);
            if (!_images.ContainsKey(printerId))
            {
                return;
            }

            Dictionary<string, byte[]> images = new(_images, StringComparer.OrdinalIgnoreCase);
            images.Remove(printerId);
            _images = images;
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Reads the images of the <paramref name="printers"/> that use a custom one. A file that is missing, damaged or
    /// unreadable is skipped (the printer shows its automatic image). An image set or removed while this runs wins over
    /// the file read here. Never throws except for cancellation.
    /// </summary>
    /// <returns>How many images were loaded.</returns>
    public async Task<int> LoadAsync(IPrinterImageStore store, IReadOnlyList<PrinterDefinition> printers, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(printers);
        lock (_gate)
        {
            _changedSinceLoad.Clear();
        }

        Dictionary<string, byte[]> loaded = new(StringComparer.OrdinalIgnoreCase);
        foreach (PrinterDefinition printer in printers)
        {
            if (printer.Image.Mode != PrinterImageMode.Custom)
            {
                continue;
            }

            try
            {
                if (await store.ReadAsync(printer.Id, cancellationToken).ConfigureAwait(false) is { } png)
                {
                    loaded[printer.Id] = png;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Shown as the automatic image; the next start tries again.
            }
        }

        int added = 0;
        lock (_gate)
        {
            Dictionary<string, byte[]> images = new(_images, StringComparer.OrdinalIgnoreCase);
            foreach ((string id, byte[] png) in loaded)
            {
                if (!_changedSinceLoad.Contains(id))
                {
                    images[id] = png;
                    added++;
                }
            }

            _images = images;
        }

        if (added > 0)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }

        return added;
    }
}
