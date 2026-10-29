using System.Net;
using System.Net.Sockets;

namespace LayerPeek.App.Platform;

/// <summary>
/// Whether a host is in the local network as far as the address itself tells (T-097, pure): the private and link-local
/// ranges, multicast and broadcast (Apple TN3179: all multicast addresses and 255.255.255.255 are local), and host names
/// that only a local network resolves (mDNS <c>.local</c>, single labels like <c>prusa-mk4</c>). Loopback is not (the
/// simulator and the tests), nor are other addresses and names (they may be routed).
/// </summary>
internal static class LocalNetworkAddress
{
    public static bool IsLocal(string? host)
    {
        string value = (host ?? string.Empty).Trim();
        if (value.Length > 1 && value[0] == '[' && value[^1] == ']')
        {
            value = value[1..^1];
        }

        if (value.Length == 0)
        {
            return false;
        }

        if (IPAddress.TryParse(value, out IPAddress? address))
        {
            return IsLocal(address);
        }

        string name = value.TrimEnd('.');
        if (name.Length == 0 || string.Equals(name, "localhost", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return !name.Contains('.', StringComparison.Ordinal) || name.EndsWith(".local", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsLocal(IPAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);
        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        if (IPAddress.IsLoopback(address))
        {
            return false;
        }

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            byte[] b = address.GetAddressBytes();
            return b[0] == 10
                || (b[0] == 172 && b[1] >= 16 && b[1] <= 31)
                || (b[0] == 192 && b[1] == 168)
                || (b[0] == 169 && b[1] == 254)
                || (b[0] >= 224 && b[0] <= 239)
                || address.Equals(IPAddress.Broadcast);
        }

        return address.AddressFamily == AddressFamily.InterNetworkV6
            && (address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || address.IsIPv6UniqueLocal || address.IsIPv6Multicast);
    }
}
