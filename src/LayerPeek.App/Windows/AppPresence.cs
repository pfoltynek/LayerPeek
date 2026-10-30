using LayerPeek.App.Platform;

namespace LayerPeek.App.Windows;

/// <summary>
/// Counts the open auxiliary windows that need a presence in the app switchers (T-102, user decision 2026-10-29): every
/// window except one that stays above other windows (the mini preview by default, a normal camera window with its
/// "always on top" option on; full screen never is). The first counted window shows the app (macOS: Dock icon and ⌘Tab);
/// closing the last one, or putting it on top, hides it again. Every auxiliary window reports here through
/// <see cref="AuxiliaryWindowTracker"/>. Free of Avalonia so that it is testable; UI thread only.
/// </summary>
internal sealed class AppPresence
{
    private readonly IAppPresence _platform;

    // Every open window and whether it counts (not on top).
    private readonly Dictionary<object, bool> _windows = new(ReferenceEqualityComparer.Instance);
    private int _counted;

    public AppPresence(IAppPresence platform)
    {
        ArgumentNullException.ThrowIfNull(platform);
        _platform = platform;
    }

    /// <summary>Whether the app is shown: at least one counted window is open.</summary>
    public bool IsVisible => _counted > 0;

    /// <summary>Open windows that count (not on top).</summary>
    public int Count => _counted;

    /// <summary>A window opened, or its "on top" changed: <paramref name="topmost"/> windows are open but do not count.</summary>
    public void Set(object window, bool topmost)
    {
        ArgumentNullException.ThrowIfNull(window);
        bool counts = !topmost;
        bool known = _windows.TryGetValue(window, out bool counted);
        if (known && counted == counts)
        {
            return;
        }

        _windows[window] = counts;
        Change(counts ? 1 : counted ? -1 : 0);
    }

    /// <summary>A window closed; unknown windows are ignored.</summary>
    public void Remove(object window)
    {
        ArgumentNullException.ThrowIfNull(window);
        if (_windows.Remove(window, out bool counted) && counted)
        {
            Change(-1);
        }
    }

    private void Change(int delta)
    {
        bool wasVisible = IsVisible;
        _counted += delta;
        if (IsVisible != wasVisible)
        {
            // Hidden while windows on top stay open: they keep the app active.
            _platform.SetVisible(IsVisible, keepActive: !IsVisible && _windows.Count > 0);
        }
    }
}
