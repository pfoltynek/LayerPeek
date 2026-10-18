namespace PrusaTrayDiagnostic.App.Cameras;

/// <summary>
/// Printers whose camera address lost its credentials when the settings were loaded at startup (T-014 decision: the
/// file is saved right away, so that the password disappears from it). The settings file no longer tells, so the settings
/// window learns it from here and shows the notice until the user saves the settings. In memory only; thread-safe.
/// </summary>
internal sealed class RemovedCameraCredentials
{
    private readonly Lock _gate = new();
    private readonly HashSet<string> _printerIds = new(StringComparer.OrdinalIgnoreCase);

    public void Add(IEnumerable<string> printerIds)
    {
        ArgumentNullException.ThrowIfNull(printerIds);
        lock (_gate)
        {
            foreach (string id in printerIds)
            {
                _printerIds.Add(id);
            }
        }
    }

    /// <summary>A copy (case-insensitive).</summary>
    public IReadOnlySet<string> PrinterIds
    {
        get
        {
            lock (_gate)
            {
                return new HashSet<string>(_printerIds, StringComparer.OrdinalIgnoreCase);
            }
        }
    }

    /// <summary>The user saved the settings (and so reviewed them): the notice is no longer shown.</summary>
    public void Clear()
    {
        lock (_gate)
        {
            _printerIds.Clear();
        }
    }
}
