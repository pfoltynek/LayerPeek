using Avalonia.Controls;

namespace LayerPeek.App.Platform;

/// <summary>
/// Whether the window manager hides a shown window without minimizing it (T-186). Windows: the window is cloaked
/// (<c>DWMWA_CLOAKED</c>), above all while it is on another virtual desktop
/// (<see cref="Windows.WindowsWindowCloaking"/>). A window merely covered by other windows is not reported (Windows cannot
/// tell it reliably; user decision 2026-11-11). Elsewhere: <see cref="NoWindowCloaking"/> (macOS: T-187).
/// </summary>
internal interface IWindowCloaking
{
    /// <summary>
    /// Follows <paramref name="window"/> (shown, UI thread): <paramref name="cloakedChanged"/> gets the current state at
    /// once and every change later, on the UI thread, until the returned watch is disposed. A window without a native
    /// handle (headless) is never reported cloaked.
    /// </summary>
    IDisposable Watch(TopLevel window, Action<bool> cloakedChanged);
}

/// <summary>Windows are never cloaked (other OS, headless windows).</summary>
internal sealed class NoWindowCloaking : IWindowCloaking
{
    public static readonly NoWindowCloaking Instance = new();

    private NoWindowCloaking()
    {
    }

    public IDisposable Watch(TopLevel window, Action<bool> cloakedChanged)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(cloakedChanged);
        cloakedChanged(false);
        return NoWatch.Instance;
    }

    private sealed class NoWatch : IDisposable
    {
        public static readonly NoWatch Instance = new();

        public void Dispose()
        {
        }
    }
}
