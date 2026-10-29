using System.Collections.Concurrent;

using Microsoft.Extensions.Logging;

namespace LayerPeek.App.Platform.MacOS;

/// <summary>
/// <see cref="ILocalNetworkPrivacy"/> on macOS 15 and later (T-097): since macOS 15 every outgoing connection to an address in
/// the local network needs the "Local Network" permission (Apple TN3179). Until the user allowed it (while the system asks,
/// and after "Don't Allow") BSD sockets fail at once with EHOSTUNREACH, "no route to host"; .NET reports it as
/// <see cref="System.Net.Sockets.SocketError.HostUnreachable"/>, FFmpeg as AVERROR(EHOSTUNREACH). The same error also has
/// ordinary causes, so the answer is only "may". No native calls: macOS offers no API to read the permission.
/// Opening System Settings goes through <paramref name="open"/> (in the app <see cref="MacSystemSettings"/>; in the test
/// mode and in tests a stand-in, so System Settings never opens there), like <see cref="MacNotificationSettings"/> (T-020).
/// </summary>
/// <param name="open">Opens an address of System Settings; may throw (logged).</param>
internal sealed partial class MacLocalNetworkPrivacy(ILogger<MacLocalNetworkPrivacy> logger, Action<Uri> open) : ILocalNetworkPrivacy
{
    private readonly ILogger<MacLocalNetworkPrivacy> _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    private readonly Action<Uri> _open = open ?? throw new ArgumentNullException(nameof(open));
    private readonly ConcurrentDictionary<string, byte> _logged = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// System Settings &gt; Privacy &amp; Security &gt; Local Network: the identifier of the Privacy &amp; Security extension
    /// (<c>CFBundleIdentifier</c> of <c>SecurityPrivacyExtension.appex</c>, which allows the <c>x-apple.systempreferences:</c>
    /// scheme; its legacy identifier is <c>com.apple.preference.security</c>) with the anchor of the Local Network list.
    /// Unverified (T-097): no list of anchors names <c>Privacy_LocalNetwork</c>; System Settings shows the Privacy &amp;
    /// Security pane for an anchor it does not know.
    /// </summary>
    public static Uri SettingsAddress { get; } = new("x-apple.systempreferences:com.apple.settings.PrivacySecurity.extension?Privacy_LocalNetwork");

    /// <summary>System Settings &gt; Privacy &amp; Security, opened when macOS refuses <see cref="SettingsAddress"/>.</summary>
    public static Uri FallbackAddress { get; } = new("x-apple.systempreferences:com.apple.settings.PrivacySecurity.extension");

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

    public void OpenSettings()
    {
        try
        {
            _open(SettingsAddress);
            return;
        }
        catch (Exception ex)
        {
            LogLocalNetworkPaneFailed(ex);
        }

        try
        {
            _open(FallbackAddress);
        }
        catch (Exception ex)
        {
            LogOpenFailed(ex);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "The connection to {Subject} in the local network failed with \"no route to host\": Local Network access for LayerPeek may be off (System Settings > Privacy & Security > Local Network).")]
    private partial void LogMayBeDenied(string subject);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Opening System Settings > Privacy & Security > Local Network failed; opening Privacy & Security instead.")]
    private partial void LogLocalNetworkPaneFailed(Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Opening System Settings > Privacy & Security failed.")]
    private partial void LogOpenFailed(Exception exception);
}
