using LayerPeek.Core.Cameras;

namespace LayerPeek.App.Settings;

/// <summary>An "always on top" option of one printer's camera window in one mode (T-117).</summary>
/// <param name="PrinterId">Compared case-insensitively.</param>
/// <param name="Mode"><see cref="CameraWindowMode.Normal"/> or <see cref="CameraWindowMode.Mini"/>.</param>
internal sealed record CameraTopmostChoice(string PrinterId, CameraWindowMode Mode, bool Topmost);

/// <summary>
/// The latest "always on top" options saved by the pin of a camera window (T-117) for the settings window's save
/// (<see cref="SettingsChange.LatestCameraTopmost"/>), like <see cref="ILatestOverlayLayouts"/>. Read by the store on its
/// thread at write time.
/// </summary>
internal interface ILatestCameraTopmost
{
    /// <summary>The option saved last per printer and mode; printers and modes without one are missing.</summary>
    IReadOnlyList<CameraTopmostChoice> Read();
}

/// <summary>
/// Options saved by the pin of a camera window that the settings window has not taken over yet (it was loading or saving,
/// or the UI thread has not run the take-over yet), like <see cref="AnnouncedOverlayLayouts"/>. Added on the writer's
/// thread, everything else on the UI thread; thread-safe. Each option gets a sequence number, so that a save forgets exactly
/// the options it read.
/// </summary>
internal sealed class AnnouncedCameraTopmost
{
    private readonly Lock _gate = new();
    private readonly List<(CameraTopmostChoice Choice, long Sequence)> _choices = [];
    private long _sequence;

    /// <summary>Any thread: <paramref name="choice"/> was saved; replaces an older one of the same printer and mode.</summary>
    public void Add(CameraTopmostChoice choice)
    {
        ArgumentNullException.ThrowIfNull(choice);
        lock (_gate)
        {
            _choices.RemoveAll(waiting => SameTarget(waiting.Choice, choice));
            _choices.Add((choice, ++_sequence));
        }
    }

    /// <summary>
    /// Removes <paramref name="choice"/> when it is still the one waiting (the window takes it over now);
    /// <see langword="false"/> when it was taken over or saved already or a newer one replaced it.
    /// </summary>
    public bool TryTake(CameraTopmostChoice choice)
    {
        lock (_gate)
        {
            return _choices.RemoveAll(waiting => ReferenceEquals(waiting.Choice, choice)) > 0;
        }
    }

    /// <summary>Removes and returns every waiting option (the window takes them over now).</summary>
    public IReadOnlyList<CameraTopmostChoice> TakeAll()
    {
        lock (_gate)
        {
            CameraTopmostChoice[] all = [.. _choices.Select(static waiting => waiting.Choice)];
            _choices.Clear();
            return all;
        }
    }

    /// <summary>A reader for one save; after it, <see cref="Forget"/> removes what that save read.</summary>
    public Reader CreateReader() => new(this);

    /// <summary>After a successful save: the options <paramref name="reader"/> read are in the saved file (or replaced by the window).</summary>
    public void Forget(Reader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        long version = reader.Version;
        lock (_gate)
        {
            _choices.RemoveAll(waiting => waiting.Sequence <= version);
        }
    }

    private static bool SameTarget(CameraTopmostChoice a, CameraTopmostChoice b) =>
        a.Mode == b.Mode && string.Equals(a.PrinterId, b.PrinterId, StringComparison.OrdinalIgnoreCase);

    /// <summary>The waiting options as one save reads them, remembering which ones it read.</summary>
    internal sealed class Reader(AnnouncedCameraTopmost owner) : ILatestCameraTopmost
    {
        private long _version;

        /// <summary>The newest sequence number the last <see cref="Read"/> saw; 0 before it.</summary>
        public long Version => Interlocked.Read(ref _version);

        public IReadOnlyList<CameraTopmostChoice> Read()
        {
            lock (owner._gate)
            {
                Interlocked.Exchange(ref _version, owner._sequence);
                return [.. owner._choices.Select(static waiting => waiting.Choice)];
            }
        }
    }
}
