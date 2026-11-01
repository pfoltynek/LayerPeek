using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

using LayerPeek.App.Platform.Windows;

namespace LayerPeek.App.Tests.Platform.Windows;

/// <summary>
/// The real foreground lock of Windows (T-119), on demand only (<c>LAYERPEEK_E2E=1</c> on Windows; a normal
/// <c>dotnet test</c> never takes the foreground from the user): a visible window of this test process, shown without
/// activation, while the foreground window belongs to another process (whatever the user works in, never changed: it
/// only gets the foreground back at the end). First what Avalonia's <c>Window.Activate</c> does on Windows, a plain
/// <c>SetForegroundWindow</c>, must be refused – the bug; when Windows lets it pass (the user did not touch the
/// computer for longer than the foreground lock timeout, a debugger is attached…) there is nothing to reproduce and the
/// test is skipped. Then <see cref="ForegroundRaiser"/> must either bring the window into the foreground above the
/// other app's window, or – only when Windows also denied attaching to the foreground thread (2026-10-31: VMware
/// Workstation, <c>ERROR_ACCESS_DENIED</c>) – leave it in the accepted fallback (user decision 2026-10-31): directly below
/// the foreground window, above all other non-topmost windows. Never topmost. The output names the case. The window lives on its own thread with a message loop and is destroyed in
/// <c>finally</c> (it dies with the test process at the latest).
/// </summary>
[SupportedOSPlatform("windows")]
public sealed partial class ForegroundRaiserE2ETests
{
    /// <summary>Set to 1 to run the end-to-end tests (as for the Windows E2E tests).</summary>
    public const string RunVariable = "LAYERPEEK_E2E";

    private const uint WsOverlappedWindow = 0x00CF_0000;
    private const int WsExTopmost = 0x0000_0008;
    private const int GwlExStyle = -20;
    private const uint GwHwndNext = 2;
    private const uint GwHwndPrev = 3;
    private const int SwShowNoActivate = 4;
    private const uint PmRemove = 0x0001;

    [Fact]
    public async Task RaisesAWindowRefusedByThePlainForegroundCallAboveAnotherProcess()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "The foreground lock exists only on Windows.");
        Assert.SkipUnless(Environment.GetEnvironmentVariable(RunVariable) == "1", $"On demand only: set {RunVariable}=1.");
        nint other = GetForegroundWindow();
        _ = GetWindowThreadProcessId(other, out uint otherProcess);
        Assert.SkipWhen(other == 0 || otherProcess == (uint)Environment.ProcessId, "No window of another process is in the foreground.");

        TaskCompletionSource<Outcome> done = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Thread thread = new(() =>
        {
            try
            {
                done.SetResult(Run(other));
            }
            catch (Exception ex)
            {
                done.SetException(ex);
            }
        })
        {
            IsBackground = true,
            Name = "T-119 foreground window",
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Outcome outcome = await done.Task.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
        string otherName = ProcessName(otherProcess);
        TestContext.Current.TestOutputHelper?.WriteLine(
            $"Other process '{otherName}': plain SetForegroundWindow refused {outcome.PlainRefused}; raiser {outcome.Result}, foreground after {outcome.Foreground}, above {outcome.Above}, topmost {outcome.Topmost}; calls: {outcome.Trace}.");

        Assert.SkipUnless(outcome.PlainRefused, $"Windows let the plain SetForegroundWindow pass (the foreground of '{otherName}' was not locked); nothing to reproduce.");
        Assert.False(outcome.Topmost, "The raiser made the window topmost.");
        if (outcome.Result == ForegroundRaiseResult.Foreground)
        {
            TestContext.Current.TestOutputHelper?.WriteLine("Case: the raiser brought the window into the foreground.");
            Assert.True(outcome.Foreground, "The window is not the foreground window after the raiser.");
            Assert.True(outcome.Above, $"The window is not above the window of '{otherName}' in the z-order.");
            return;
        }

        // Accepted last resort (user decision 2026-10-31), only when Windows also denied attaching the input.
        Assert.True(outcome.AttachDenied, $"The raiser ended with {outcome.Result} although attaching the input was not denied.");
        TestContext.Current.TestOutputHelper?.WriteLine($"Case: Windows denied attaching to '{otherName}'; accepted fallback (directly below the foreground window).");
        Assert.Equal(ForegroundRaiseResult.RaisedOnly, outcome.Result);
        Assert.True(outcome.BelowForegroundOnly, $"The window is not directly below the foreground window of '{otherName}' (above all other non-topmost windows).");
    }

    /// <summary>On the window's own thread (like the UI thread of the app, which owns its windows).</summary>
    private static Outcome Run(nint other)
    {
        nint hwnd = CreateWindowExW(0, "STATIC", "LayerPeek T-119 test window", WsOverlappedWindow, 80, 80, 360, 200, 0, 0, 0, 0);
        if (hwnd == 0)
        {
            throw new InvalidOperationException($"CreateWindowEx failed: {Marshal.GetLastPInvokeError()}.");
        }

        try
        {
            _ = ShowWindow(hwnd, SwShowNoActivate);
            Pump();

            // What Avalonia's Window.Activate does on Windows.
            _ = SetForegroundWindow(hwnd);
            Pump();
            bool plainRefused = GetForegroundWindow() != hwnd;
            if (!plainRefused)
            {
                return new Outcome(false, ForegroundRaiseResult.AlreadyForeground, true, IsAbove(hwnd, other), IsTopmost(hwnd), false, true, string.Empty);
            }

            TracingApi api = new(ForegroundNative.Instance);
            ForegroundRaiseResult result = ForegroundRaiser.Raise(api, hwnd);
            Pump();
            return new Outcome(
                true,
                result,
                GetForegroundWindow() == hwnd,
                IsAbove(hwnd, other),
                IsTopmost(hwnd),
                api.AttachDenied,
                IsDirectlyBelow(hwnd, other),
                $"{string.Join("; ", api.Trace)}; visible windows above: {VisibleAbove(hwnd)}");
        }
        finally
        {
            // The user's window gets the foreground back (allowed: this process has it now).
            if (GetForegroundWindow() == hwnd)
            {
                _ = SetForegroundWindow(other);
            }

            _ = DestroyWindow(hwnd);
            Pump();
        }
    }

    /// <summary>Handles the messages of the window for a moment (activation, painting).</summary>
    private static unsafe void Pump()
    {
        Stopwatch watch = Stopwatch.StartNew();
        Msg message;
        while (watch.ElapsedMilliseconds < 300)
        {
            while (PeekMessageW(&message, 0, 0, 0, PmRemove))
            {
                _ = TranslateMessage(&message);
                _ = DispatchMessageW(&message);
            }

            Thread.Sleep(10);
        }
    }

    private static bool IsTopmost(nint hwnd) => (GetWindowLongPtrW(hwnd, GwlExStyle) & WsExTopmost) != 0;

    /// <summary>Whether <paramref name="lower"/> comes after <paramref name="upper"/> in the z-order of the top-level windows.</summary>
    private static bool IsAbove(nint upper, nint lower)
    {
        for (nint next = GetWindow(upper, GwHwndNext); next != 0; next = GetWindow(next, GwHwndNext))
        {
            if (next == lower)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The processes of the visible top-level windows above <paramref name="hwnd"/>, topmost ones marked (no titles).</summary>
    private static string VisibleAbove(nint hwnd)
    {
        List<string> above = [];
        for (nint previous = GetWindow(hwnd, GwHwndPrev); previous != 0; previous = GetWindow(previous, GwHwndPrev))
        {
            if (IsWindowVisible(previous))
            {
                _ = GetWindowThreadProcessId(previous, out uint process);
                above.Add($"0x{previous:X} {ProcessName(process)}{(IsTopmost(previous) ? " (topmost)" : string.Empty)}");
            }
        }

        return above.Count == 0 ? "none" : string.Join(", ", above);
    }

    /// <summary>Whether the only non-topmost visible top-level window above <paramref name="hwnd"/> is <paramref name="foreground"/>.</summary>
    private static bool IsDirectlyBelow(nint hwnd, nint foreground)
    {
        bool foregroundAbove = false;
        for (nint previous = GetWindow(hwnd, GwHwndPrev); previous != 0; previous = GetWindow(previous, GwHwndPrev))
        {
            if (previous == foreground)
            {
                foregroundAbove = true;
            }
            else if (IsWindowVisible(previous) && !IsTopmost(previous))
            {
                return false;
            }
        }

        return foregroundAbove;
    }

    private static string ProcessName(uint processId)
    {
        try
        {
            using Process process = Process.GetProcessById((int)processId);
            return process.ProcessName;
        }
        catch (ArgumentException)
        {
            return "?";
        }
    }

    /// <param name="BelowForegroundOnly">The only non-topmost visible window above it is the foreground window.</param>
    private readonly record struct Outcome(bool PlainRefused, ForegroundRaiseResult Result, bool Foreground, bool Above, bool Topmost, bool AttachDenied, bool BelowForegroundOnly, string Trace);

    /// <summary>
    /// The real window manager, with what each call returned for the test output, and its last error for the calls
    /// imported with <c>SetLastError</c> (for the others it would be stale).
    /// </summary>
    private sealed class TracingApi(IForegroundWindowApi inner) : IForegroundWindowApi
    {
        public List<string> Trace { get; } = [];

        public nint GetForegroundWindow() => inner.GetForegroundWindow();

        public uint GetWindowThreadId(nint hwnd) => Traced($"thread of 0x{hwnd:X}", inner.GetWindowThreadId(hwnd), lastError: false);

        public bool IsHung(nint hwnd) => Traced($"hung 0x{hwnd:X}", inner.IsHung(hwnd), lastError: false);

        /// <summary>Windows refused attaching the input to the foreground thread.</summary>
        public bool AttachDenied { get; private set; }

        public bool AttachThreadInput(uint thread, uint toThread, bool attach)
        {
            bool result = Traced($"{(attach ? "attach" : "detach")} {thread}->{toThread}", inner.AttachThreadInput(thread, toThread, attach));
            AttachDenied |= attach && !result;
            return result;
        }

        public bool BringWindowToTop(nint hwnd) => Traced("BringWindowToTop", inner.BringWindowToTop(hwnd));

        public bool SetForegroundWindow(nint hwnd) => Traced("SetForegroundWindow", inner.SetForegroundWindow(hwnd), lastError: false);

        public bool RaiseWithoutActivation(nint hwnd) => Traced("SetWindowPos(HWND_TOP)", inner.RaiseWithoutActivation(hwnd));

        private T Traced<T>(string call, T result, bool lastError = true)
        {
            Trace.Add(lastError ? $"{call} = {result} (error {Marshal.GetLastPInvokeError()})" : $"{call} = {result}");
            return result;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Msg
    {
        public nint Window;
        public uint Message;
        public nint WParam;
        public nint LParam;
        public uint Time;
        public int X;
        public int Y;
        public uint Private;
    }

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    private static partial nint CreateWindowExW(uint extendedStyle, string className, string windowName, uint style, int x, int y, int width, int height, nint parent, nint menu, nint instance, nint parameter);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DestroyWindow(nint hwnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ShowWindow(nint hwnd, int command);

    [LibraryImport("user32.dll")]
    private static partial nint GetForegroundWindow();

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetForegroundWindow(nint hwnd);

    [LibraryImport("user32.dll")]
    private static partial uint GetWindowThreadProcessId(nint hwnd, out uint processId);

    [LibraryImport("user32.dll")]
    private static partial nint GetWindowLongPtrW(nint hwnd, int index);

    [LibraryImport("user32.dll")]
    private static partial nint GetWindow(nint hwnd, uint command);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsWindowVisible(nint hwnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static unsafe partial bool PeekMessageW(Msg* message, nint hwnd, uint filterMin, uint filterMax, uint remove);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static unsafe partial bool TranslateMessage(Msg* message);

    [LibraryImport("user32.dll")]
    private static unsafe partial nint DispatchMessageW(Msg* message);
}
