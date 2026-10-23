using System.Collections.Concurrent;
using System.Runtime.InteropServices;

namespace PrusaTrayDiagnostic.App.E2E.Tests.Infrastructure;

/// <summary>A shown or moved top-level window, with its window rectangle when the event arrived.</summary>
internal readonly record struct WindowEvent(uint Kind, nint Window, Native.Rect Rect, DateTime ReceivedUtc)
{
    public override string ToString() =>
        $"{ReceivedUtc:HH:mm:ss.fff} {(Kind == WinEventRecorder.EventObjectShow ? "SHOW" : "LOCATIONCHANGE")} 0x{Window:X} {Rect}";
}

/// <summary>
/// Records <c>EVENT_OBJECT_SHOW</c> and <c>EVENT_OBJECT_LOCATIONCHANGE</c> of the windows (not of their parts) of
/// another process with an out-of-context WinEvent hook (T-071: the camera window must not jump after it is shown). The
/// hook lives on its own thread with a message loop; disposing unhooks it and ends the thread.
/// </summary>
internal sealed unsafe partial class WinEventRecorder : IDisposable
{
    public const uint EventObjectShow = 0x8002;
    public const uint EventObjectLocationChange = 0x800B;

    private const uint WinEventOutOfContext = 0x0000;
    private const int ObjIdWindow = 0;
    private const int ChildIdSelf = 0;
    private const uint WmQuit = 0x0012;

    /// <summary>The recorder of the hook thread (callbacks of an out-of-context hook arrive on the thread that set it).</summary>
    [ThreadStatic]
    private static WinEventRecorder? _current;

    private readonly ConcurrentQueue<WindowEvent> _events = new();
    private readonly Thread _thread;
    private uint _threadId;

    private WinEventRecorder(Thread thread) => _thread = thread;

    /// <summary>The events so far, oldest first.</summary>
    public IReadOnlyList<WindowEvent> Events => [.. _events];

    /// <summary>Starts recording the windows of <paramref name="processId"/>.</summary>
    public static WinEventRecorder Start(int processId)
    {
        TaskCompletionSource<uint> started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        WinEventRecorder? recorder = null;
        Thread thread = new(() =>
        {
            _current = recorder;
            nint hook = SetWinEventHook(EventObjectShow, EventObjectLocationChange, 0, &OnEvent, (uint)processId, 0, WinEventOutOfContext);
            if (hook == 0)
            {
                started.SetException(new InvalidOperationException("SetWinEventHook failed."));
                return;
            }

            try
            {
                started.SetResult(GetCurrentThreadId());
                Msg message;
                while (GetMessage(&message, 0, 0, 0) > 0)
                {
                    TranslateMessage(&message);
                    DispatchMessage(&message);
                }
            }
            finally
            {
                UnhookWinEvent(hook);
                _current = null;
            }
        })
        {
            IsBackground = true,
            Name = "E2E WinEvent recorder",
        };
        recorder = new WinEventRecorder(thread);
        thread.Start();
        recorder._threadId = started.Task.WaitAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult();
        return recorder;
    }

    /// <summary>
    /// The location changes of <paramref name="window"/> after it was first shown (empty when it never was): what a user
    /// sees as a jump of the opening window.
    /// </summary>
    public static List<WindowEvent> MovesAfterShow(IReadOnlyList<WindowEvent> events, nint window)
    {
        int shown = -1;
        for (int i = 0; i < events.Count && shown < 0; i++)
        {
            if (events[i].Window == window && events[i].Kind == EventObjectShow)
            {
                shown = i;
            }
        }

        return shown < 0
            ? []
            : [.. events.Skip(shown + 1).Where(e => e.Window == window && e.Kind == EventObjectLocationChange)];
    }

    /// <summary>
    /// The location changes of <paramref name="window"/> after it was first shown whose rectangle differs from the one at
    /// the show. A change notification with the same rectangle (e.g. a frame or topmost refresh through
    /// <c>SetWindowPos</c>) moves nothing on screen. The rectangle is read when the event arrives (out of context), so a
    /// move away and back within one delivery would look the same as no move.
    /// </summary>
    public static List<WindowEvent> JumpsAfterShow(IReadOnlyList<WindowEvent> events, nint window)
    {
        WindowEvent? shown = events.FirstOrDefault(e => e.Window == window && e.Kind == EventObjectShow) is { Window: not 0 } show ? show : null;
        return shown is { } first
            ? [.. MovesAfterShow(events, window).Where(e => !e.Rect.Equals(first.Rect))]
            : [];
    }

    /// <summary>Whether <paramref name="window"/> was shown while recording.</summary>
    public static bool WasShown(IReadOnlyList<WindowEvent> events, nint window) =>
        events.Any(e => e.Window == window && e.Kind == EventObjectShow);

    public void Dispose()
    {
        if (_thread.IsAlive)
        {
            PostThreadMessage(_threadId, WmQuit, 0, 0);
            _thread.Join(TimeSpan.FromSeconds(5));
        }
    }

    [UnmanagedCallersOnly]
    private static void OnEvent(nint hook, uint kind, nint hwnd, int objectId, int childId, uint eventThread, uint eventTime)
    {
        if (_current is { } recorder && hwnd != 0 && objectId == ObjIdWindow && childId == ChildIdSelf)
        {
            Native.GetWindowRect(hwnd, out Native.Rect rect);
            recorder._events.Enqueue(new WindowEvent(kind, hwnd, rect, DateTime.UtcNow));
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
        public Native.Point Point;
        public uint Private;
    }

    [LibraryImport("user32.dll")]
    private static partial nint SetWinEventHook(
        uint eventMin,
        uint eventMax,
        nint module,
        delegate* unmanaged<nint, uint, nint, int, int, uint, uint, void> callback,
        uint processId,
        uint threadId,
        uint flags);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool UnhookWinEvent(nint hook);

    [LibraryImport("user32.dll", EntryPoint = "GetMessageW")]
    private static partial int GetMessage(Msg* message, nint hwnd, uint filterMin, uint filterMax);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool TranslateMessage(Msg* message);

    [LibraryImport("user32.dll", EntryPoint = "DispatchMessageW")]
    private static partial nint DispatchMessage(Msg* message);

    [LibraryImport("user32.dll", EntryPoint = "PostThreadMessageW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool PostThreadMessage(uint threadId, uint message, nint wParam, nint lParam);

    [LibraryImport("kernel32.dll")]
    private static partial uint GetCurrentThreadId();
}
