using LayerPeek.Core.Cameras.Overlay;

namespace LayerPeek.App.Settings;

/// <summary>
/// The latest overlay layouts saved by the layout mode of a camera window (T-091) for the settings window's save
/// (<see cref="SettingsChange.LatestOverlayLayouts"/>). Read by the store on its thread at write time.
/// </summary>
internal interface ILatestOverlayLayouts
{
    /// <summary>Printer id (case-insensitive) → the layout saved last; printers without such a layout are missing.</summary>
    IReadOnlyDictionary<string, CameraOverlayLayout> Read();
}

/// <summary>
/// Layouts saved by the layout mode of a camera window that the settings window has not taken over yet (it was loading or
/// saving, or the UI thread has not run the take-over yet). Added on the writer's thread, everything else on the UI thread;
/// thread-safe. Each layout gets a sequence number, so that a save forgets exactly the layouts it read: those are in the
/// saved file (kept or replaced by the window's own change), the ones announced later are newer than the file.
/// </summary>
internal sealed class AnnouncedOverlayLayouts
{
    private readonly Lock _gate = new();
    private readonly Dictionary<string, (CameraOverlayLayout Layout, long Sequence)> _layouts = new(StringComparer.OrdinalIgnoreCase);
    private long _sequence;

    /// <summary>Any thread: <paramref name="layout"/> was saved for <paramref name="printerId"/>; replaces an older one.</summary>
    public void Add(string printerId, CameraOverlayLayout layout)
    {
        lock (_gate)
        {
            _layouts[printerId] = (layout, ++_sequence);
        }
    }

    /// <summary>
    /// Removes <paramref name="layout"/> of <paramref name="printerId"/> when it is still the one waiting (the window takes
    /// it over now); <see langword="false"/> when it was taken over or saved already or a newer one replaced it.
    /// </summary>
    public bool TryTake(string printerId, CameraOverlayLayout layout)
    {
        lock (_gate)
        {
            if (_layouts.TryGetValue(printerId, out (CameraOverlayLayout Layout, long Sequence) waiting) && ReferenceEquals(waiting.Layout, layout))
            {
                _layouts.Remove(printerId);
                return true;
            }

            return false;
        }
    }

    /// <summary>Removes and returns every waiting layout (the window takes them over now).</summary>
    public IReadOnlyList<KeyValuePair<string, CameraOverlayLayout>> TakeAll()
    {
        lock (_gate)
        {
            KeyValuePair<string, CameraOverlayLayout>[] all = [.. _layouts.Select(static entry => KeyValuePair.Create(entry.Key, entry.Value.Layout))];
            _layouts.Clear();
            return all;
        }
    }

    /// <summary>A reader for one save; after it, <see cref="Forget"/> removes what that save read.</summary>
    public Reader CreateReader() => new(this);

    /// <summary>After a successful save: the layouts <paramref name="reader"/> read are in the saved file (or replaced by the window).</summary>
    public void Forget(Reader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        long version = reader.Version;
        lock (_gate)
        {
            foreach (string printerId in _layouts.Where(entry => entry.Value.Sequence <= version).Select(static entry => entry.Key).ToArray())
            {
                _layouts.Remove(printerId);
            }
        }
    }

    /// <summary>The waiting layouts as one save reads them, remembering which ones it read.</summary>
    internal sealed class Reader(AnnouncedOverlayLayouts owner) : ILatestOverlayLayouts
    {
        private long _version;

        /// <summary>The newest sequence number the last <see cref="Read"/> saw; 0 before it.</summary>
        public long Version => Interlocked.Read(ref _version);

        public IReadOnlyDictionary<string, CameraOverlayLayout> Read()
        {
            lock (owner._gate)
            {
                Interlocked.Exchange(ref _version, owner._sequence);
                return owner._layouts.ToDictionary(static entry => entry.Key, static entry => entry.Value.Layout, StringComparer.OrdinalIgnoreCase);
            }
        }
    }
}
