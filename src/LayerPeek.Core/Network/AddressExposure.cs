using System.Net;
using System.Net.Sockets;

namespace LayerPeek.Core.Network;

/// <summary>Where the traffic to an address may travel, as far as the address itself tells (T-182).</summary>
public enum AddressExposure
{
    /// <summary>The scheme encrypts the connection (<c>https</c>); the host does not matter.</summary>
    Encrypted,

    /// <summary>Unencrypted, but the host is in the local network (or the computer itself).</summary>
    LocalNetwork,

    /// <summary>Unencrypted to a host that may be outside the local network: the settings show a warning.</summary>
    UntrustedNetwork,
}

/// <summary>
/// Classifies a printer or camera address by its text only (T-182, user decisions 2026-11-11): no DNS query and no other
/// network access, so it is cheap enough for every keystroke. Local are loopback (<c>127.0.0.0/8</c>, <c>::1</c>,
/// <c>localhost</c>), RFC 1918 (<c>10/8</c>, <c>172.16/12</c>, <c>192.168/16</c>), link-local (<c>169.254/16</c>,
/// <c>fe80::/10</c>), IPv6 ULA (<c>fc00::/7</c>), IPv4 mapped into IPv6 as its IPv4 address, single-label names
/// (<c>prusa-mk4</c>) and the local suffixes <c>.local</c>, <c>.lan</c> and <c>.home.arpa</c>. Everything else is
/// <see cref="AddressExposure.UntrustedNetwork"/>, CGNAT <c>100.64.0.0/10</c> included (user decision 2026-11-11, Q4).
/// </summary>
/// <remarks>
/// Not shared with the App's <c>LocalNetworkAddress</c> (T-097, macOS Local Network permission): its rules differ on
/// purpose (loopback is not local there, multicast and broadcast are, <c>.lan</c> and <c>.home.arpa</c> are not).
/// </remarks>
public static class AddressExposureClassifier
{
    private static readonly string[] LocalSuffixes = [".local", ".lan", ".home.arpa"];

    /// <summary>Classifies an absolute address; only the scheme and the host are read.</summary>
    public static AddressExposure Classify(Uri address)
    {
        ArgumentNullException.ThrowIfNull(address);
        if (!address.IsAbsoluteUri)
        {
            throw new ArgumentException("The address must be absolute.", nameof(address));
        }

        if (string.Equals(address.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            return AddressExposure.Encrypted;
        }

        bool local = address.HostNameType switch
        {
            // DnsSafeHost: without the brackets of IPv6 (with its zone, which IPAddress accepts).
            UriHostNameType.IPv4 or UriHostNameType.IPv6 => IPAddress.TryParse(address.DnsSafeHost, out IPAddress? ip) && IsLocal(ip),
            UriHostNameType.Dns or UriHostNameType.Basic => IsLocalName(address.Host),
            _ => false,
        };
        return local ? AddressExposure.LocalNetwork : AddressExposure.UntrustedNetwork;
    }

    /// <summary>Loopback, RFC 1918, link-local or ULA (IPv4 mapped into IPv6 counts as its IPv4 address).</summary>
    public static bool IsLocal(IPAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);
        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        if (IPAddress.IsLoopback(address))
        {
            return true;
        }

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            Span<byte> b = stackalloc byte[4];
            _ = address.TryWriteBytes(b, out _);
            return b[0] == 10
                || (b[0] == 172 && b[1] >= 16 && b[1] <= 31)
                || (b[0] == 192 && b[1] == 168)
                || (b[0] == 169 && b[1] == 254);
        }

        return address.AddressFamily == AddressFamily.InterNetworkV6 && (address.IsIPv6LinkLocal || address.IsIPv6UniqueLocal);
    }

    /// <summary><c>localhost</c>, a single label or a name with a local suffix (case-insensitive, trailing dot ignored).</summary>
    public static bool IsLocalName(string host)
    {
        ArgumentNullException.ThrowIfNull(host);
        ReadOnlySpan<char> name = host.AsSpan().Trim().TrimEnd('.');
        if (name.IsEmpty)
        {
            return false;
        }

        // A single label (localhost included) is resolved only by the local network or the computer itself.
        if (!name.Contains('.'))
        {
            return true;
        }

        foreach (string suffix in LocalSuffixes)
        {
            if (name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
