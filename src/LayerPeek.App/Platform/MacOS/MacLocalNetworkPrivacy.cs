using System.Collections.Concurrent;

using Microsoft.Extensions.Logging;

namespace LayerPeek.App.Platform.MacOS;

/// <summary>
/// <see cref="ILocalNetworkPrivacy"/> on macOS 15 and later (T-097): since macOS 15 every outgoing connection to an address in
/// the local network needs the "Local Network" permission (Apple TN3179). Until the user allowed it (while the system asks,
/// and after "Don't Allow") BSD sockets fail at once with EHOSTUNREACH, "no route to host"; .NET reports it as
/// <see cref="System.Net.Sockets.SocketError.HostUnreachable"/>, FFmpeg as AVERROR(EHOSTUNREACH). The same error also has
/// ordinary causes, so the answer is only "may". No native calls: macOS offers no API to read the permission.
/// </summary>
internal sealed partial class MacLocalNetworkPrivacy(ILogger<MacLocalNetworkPrivacy> logger) : ILocalNetworkPrivacy
{
    private readonly ILogger<MacLocalNetworkPrivacy> _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    private readonly ConcurrentDictionary<string, byte> _logged = new(StringComparer.OrdinalIgnoreCase);

    public bool MayBeDenied(string? host, string subject)
    {
        if (!LocalNetworkAddress.IsLocal(host))
        {
            return false;
        }

        if (_logged.TryAdd(subject ?? string.Empty, 0))
        {
            LogMayBeDenied(subject ?? string.Empty);
        }

        return true;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "The connection to {Subject} in the local network failed with \"no route to host\": Local Network access for LayerPeek may be off (System Settings > Privacy & Security > Local Network).")]
    private partial void LogMayBeDenied(string subject);
}
