using LayerPeek.App.Platform.Windows;

namespace LayerPeek.App.Tests.Platform.Windows;

/// <summary>
/// A window manager for <see cref="ForegroundRaiser"/> (T-119) that records every call: windows have the threads of
/// <see cref="Threads"/>, the foreground is <see cref="Foreground"/>, and <c>SetForegroundWindow</c> succeeds only while
/// <see cref="Grants"/> says so (the foreground lock). Linked into the UI tests.
/// </summary>
internal sealed class FakeForegroundWindowApi : IForegroundWindowApi
{
    public List<string> Calls { get; } = [];

    public nint Foreground { get; set; }

    public Dictionary<nint, uint> Threads { get; } = [];

    public HashSet<nint> Hung { get; } = [];

    /// <summary>Whether <c>SetForegroundWindow</c> passes, by whether the input is attached at that moment.</summary>
    public Func<bool, bool> Grants { get; set; } = static attached => attached;

    /// <summary>Thrown by <c>SetForegroundWindow</c> when set (e.g. a missing entry point).</summary>
    public Exception? SetForegroundFailure { get; set; }

    public bool IsAttached { get; private set; }

    /// <summary>Whether <c>AttachThreadInput</c> with attach succeeds (2026-10-31: denied for the thread of VMware Workstation).</summary>
    public bool AttachAllowed { get; set; } = true;

    public nint GetForegroundWindow()
    {
        Calls.Add("foreground?");
        return Foreground;
    }

    public uint GetWindowThreadId(nint hwnd) => Threads.GetValueOrDefault(hwnd);

    public bool IsHung(nint hwnd) => Hung.Contains(hwnd);

    public bool AttachThreadInput(uint thread, uint toThread, bool attach)
    {
        Calls.Add($"{(attach ? "attach" : "detach")} {thread}->{toThread}");
        if (attach && !AttachAllowed)
        {
            return false;
        }

        IsAttached = attach;
        return true;
    }

    public bool BringWindowToTop(nint hwnd)
    {
        Calls.Add($"top {hwnd}");
        return true;
    }

    public bool SetForegroundWindow(nint hwnd)
    {
        Calls.Add($"set foreground {hwnd}");
        if (SetForegroundFailure is { } failure)
        {
            throw failure;
        }

        if (!Grants(IsAttached))
        {
            return false;
        }

        Foreground = hwnd;
        return true;
    }

    public bool RaiseWithoutActivation(nint hwnd)
    {
        Calls.Add($"raise {hwnd}");
        return true;
    }
}
