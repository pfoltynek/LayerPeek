using System.Diagnostics;
using System.Runtime.Versioning;

namespace LayerPeek.App.Platform.Windows;

/// <summary>Opens a page of the Windows Settings app (T-101): a shell execute of an <c>ms-settings:</c> address.</summary>
[SupportedOSPlatform("windows")]
internal static class WindowsSystemSettings
{
    /// <summary>Opens <paramref name="address"/>, which must be an <c>ms-settings:</c> address. Any thread.</summary>
    /// <exception cref="ArgumentException">Another kind of address (nothing else is ever opened here).</exception>
    /// <exception cref="Exception">Windows could not start the Settings app.</exception>
    public static void Open(Uri address)
    {
        ArgumentNullException.ThrowIfNull(address);
        if (!address.IsAbsoluteUri || !string.Equals(address.Scheme, "ms-settings", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Only addresses of the Settings app are opened.", nameof(address));
        }

        // The address alone, through the shell (it is a protocol, not a program); the handle is not needed.
        using Process? started = Process.Start(new ProcessStartInfo(address.OriginalString) { UseShellExecute = true });
    }
}
