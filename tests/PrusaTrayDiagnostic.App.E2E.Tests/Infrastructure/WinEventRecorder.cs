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
/// hook lives on its own <see cref="MessageLoopThread{T}"/>; disposing unhooks it and ends the thread.
/// </summary>
internal sealed partial class WinEventRecorder : IDisposable
{
    public const uint EventObjectShow = 0x8002;
    public const uint EventObjectLocationChange = 0x800B;

    private const uint WinEventOutOfContext = 0x0000;
    private const int ObjIdWindow = 0;
    private const int ChildIdSelf = 0;

    /// <summary>The recorder of the hook thread (callbacks of an out-of-context hook arrive on the thread that set it).</summary>
    [ThreadStatic]
    private static WinEventRecorder? _current;

    private readonly ConcurrentQueue<WindowEvent> _events = new();
    private MessageLoopThread<nint>? _loop;

    private WinEventRecorder()
    {
    }

    /// <summary>The events so far, oldest first.</summary>
    public IReadOnlyList<WindowEvent> Events => [.. _events];

    /// <summary>Starts recording the windows of <paramref name="processId"/>.</summary>
    public static async Task<WinEventRecorder> StartAsync(int processId)
    {
        WinEventRecorder recorder = new();
        recorder._loop = await MessageLoopThread<nint>.StartAsync(
            "E2E WinEvent recorder",
            () =>
            {
                _current = recorder;
                return Hook(processId);
            },
            static hook =>
            {
                UnhookWinEvent(hook);
                _current = null;
            });
        return recorder;
    }

    /// <summary>
    /// The location changes of <paramref name="window"/> after it was first shown (empty when it never was): what a user
    /// may see as a jump of the opening window.
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
    /// What a user may see as a jump of the opening <paramref name="window"/>, or <see langword="null"/> when it kept still
    /// (T-071 Q1, decided 2026-10-23: strict). Every location change after the first show counts, also one that reports
    /// the rectangle of the show: a <c>SetWindowPos</c> that moves nothing still placed the window again after it was
    /// shown. The rectangle is read when the event arrives (out of context), so it only describes the event.
    /// </summary>
    public static string? JumpProblem(IReadOnlyList<WindowEvent> events, nint window)
    {
        if (!WasShown(events, window))
        {
            return "the WinEvent hook saw no EVENT_OBJECT_SHOW of the window";
        }

        Native.Rect shown = events.First(e => e.Window == window && e.Kind == EventObjectShow).Rect;
        List<WindowEvent> moves = MovesAfterShow(events, window);
        return moves.Count == 0
            ? null
            : $"{moves.Count} location change(s) after the window was shown at {shown}: {string.Join(", ", moves.Select(move => move.Rect.Equals(shown) ? $"{move} (same rectangle)" : move.ToString()))}";
    }

    /// <summary>Whether <paramref name="window"/> was shown while recording.</summary>
    public static bool WasShown(IReadOnlyList<WindowEvent> events, nint window) =>
        events.Any(e => e.Window == window && e.Kind == EventObjectShow);

    public void Dispose() => _loop?.Dispose();

    /// <summary>Sets the hook on the calling thread (the callbacks arrive on it).</summary>
    private static unsafe nint Hook(int processId)
    {
        nint hook = SetWinEventHook(EventObjectShow, EventObjectLocationChange, 0, &OnEvent, (uint)processId, 0, WinEventOutOfContext);
        return hook != 0 ? hook : throw new InvalidOperationException("SetWinEventHook failed.");
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

    [LibraryImport("user32.dll")]
    private static unsafe partial nint SetWinEventHook(
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
}
