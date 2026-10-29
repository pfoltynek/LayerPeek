namespace LayerPeek.App.Platform;

/// <summary>
/// The local network permission of the OS (T-097; macOS 15+, Apple TN3179 "Local Network"): whether a connection that failed
/// with "no route to host" (<see cref="Core.Printers.PrinterProblem.HostUnreachable"/>,
/// <see cref="Core.Cameras.CameraStatus.IsHostUnreachable"/>) may have been blocked by it, so that the UI can say where to
/// allow it instead of a plain "not reachable". Where the OS has no such permission the answer is always no.
/// </summary>
internal interface ILocalNetworkPrivacy
{
    /// <summary>
    /// Whether a "no route to host" failure of a connection to <paramref name="host"/> (host name or IP address, as in a URL)
    /// may be the permission. The first time per <paramref name="subject"/> (e.g. "printer 'mk4'"; never an address or
    /// credentials) a yes is logged with where to allow the access. Thread-safe, never throws.
    /// </summary>
    bool MayBeDenied(string? host, string subject);
}

/// <summary>An OS without a local network permission (Windows, macOS 14): never.</summary>
internal sealed class NoLocalNetworkPrivacy : ILocalNetworkPrivacy
{
    public static NoLocalNetworkPrivacy Instance { get; } = new();

    private NoLocalNetworkPrivacy()
    {
    }

    public bool MayBeDenied(string? host, string subject) => false;
}
