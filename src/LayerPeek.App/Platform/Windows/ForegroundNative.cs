using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace LayerPeek.App.Platform.Windows;

/// <summary>
/// The window manager functions of <see cref="ForegroundRaiser"/> (T-119) in <c>user32</c>. Source-generated marshalling
/// only (Native AOT).
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed partial class ForegroundNative : IForegroundWindowApi
{
    public static readonly ForegroundNative Instance = new();

    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoActivate = 0x0010;

    /// <summary>HWND_TOP: the top of the window's band (topmost windows stay topmost, the others stay below them).</summary>
    private const nint HwndTop = 0;

    private ForegroundNative()
    {
    }

    nint IForegroundWindowApi.GetForegroundWindow() => GetForegroundWindow();

    public uint GetWindowThreadId(nint hwnd) => GetWindowThreadProcessId(hwnd, out _);

    public bool IsHung(nint hwnd) => IsHungAppWindow(hwnd);

    bool IForegroundWindowApi.AttachThreadInput(uint thread, uint toThread, bool attach) => AttachThreadInput(thread, toThread, attach);

    bool IForegroundWindowApi.BringWindowToTop(nint hwnd) => BringWindowToTop(hwnd);

    bool IForegroundWindowApi.SetForegroundWindow(nint hwnd) => SetForegroundWindow(hwnd);

    public bool RaiseWithoutActivation(nint hwnd) => SetWindowPos(hwnd, HwndTop, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate);

    [LibraryImport("user32.dll")]
    private static partial nint GetForegroundWindow();

    [LibraryImport("user32.dll")]
    private static partial uint GetWindowThreadProcessId(nint hwnd, out uint processId);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsHungAppWindow(nint hwnd);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AttachThreadInput(uint attach, uint attachTo, [MarshalAs(UnmanagedType.Bool)] bool doAttach);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool BringWindowToTop(nint hwnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetForegroundWindow(nint hwnd);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetWindowPos(nint hwnd, nint insertAfter, int x, int y, int width, int height, uint flags);
}
