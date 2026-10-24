using System.Runtime.InteropServices;

namespace LayerPeek.App.E2E.Tests.Infrastructure;

/// <summary>
/// A background thread of the test process with a Win32 message loop, for what needs one (T-071): an out-of-context
/// WinEvent hook delivers its callbacks there, a window of the test process is served there. <c>setUp</c> runs first
/// on the thread (its message queue already exists) and returns the state the caller needs; <c>tearDown</c> runs on the
/// thread after the loop ended (e.g. unhook, destroy the window: both must happen on the thread that created them).
/// Disposing ends the loop and waits for the thread.
/// </summary>
internal sealed class MessageLoopThread<T> : IDisposable
{
    private static readonly TimeSpan StartTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(5);

    private readonly Thread _thread;
    private readonly uint _threadId;

    private MessageLoopThread(Thread thread, uint threadId, T state)
    {
        _thread = thread;
        _threadId = threadId;
        State = state;
    }

    /// <summary>What <c>setUp</c> returned (e.g. a window handle).</summary>
    public T State { get; }

    /// <summary>Starts the thread, runs <paramref name="setUp"/> on it, then the message loop until disposed.</summary>
    public static async Task<MessageLoopThread<T>> StartAsync(string name, Func<T> setUp, Action<T> tearDown, ApartmentState apartment = ApartmentState.MTA)
    {
        TaskCompletionSource<(uint ThreadId, T State)> started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Thread thread = new(() => Run(setUp, tearDown, started))
        {
            IsBackground = true,
            Name = name,
        };
        thread.SetApartmentState(apartment);
        thread.Start();
        (uint threadId, T state) = await started.Task.WaitAsync(StartTimeout);
        return new MessageLoopThread<T>(thread, threadId, state);
    }

    public void Dispose()
    {
        // The queue exists before setUp ran, so the quit message cannot get lost.
        if (_thread.IsAlive && MessageLoopNative.PostQuit(_threadId))
        {
            _thread.Join(StopTimeout);
        }
    }

    private static void Run(Func<T> setUp, Action<T> tearDown, TaskCompletionSource<(uint, T)> started)
    {
        T state;
        try
        {
            MessageLoopNative.CreateQueue();
            state = setUp();
        }
        catch (Exception ex)
        {
            started.SetException(ex);
            return;
        }

        try
        {
            started.SetResult((MessageLoopNative.CurrentThreadId(), state));
            MessageLoopNative.Pump();
        }
        finally
        {
            tearDown(state);
        }
    }
}

/// <summary>The Win32 calls of <see cref="MessageLoopThread{T}"/> (source-generated interop cannot live in a generic type).</summary>
internal static unsafe partial class MessageLoopNative
{
    private const uint WmQuit = 0x0012;
    private const uint PmNoRemove = 0x0000;

    /// <summary>Creates the message queue of the calling thread.</summary>
    public static void CreateQueue()
    {
        Msg message;
        PeekMessage(&message, 0, 0, 0, PmNoRemove);
    }

    /// <summary>Dispatches the messages of the calling thread until <c>WM_QUIT</c>.</summary>
    public static void Pump()
    {
        Msg message;
        while (GetMessage(&message, 0, 0, 0) > 0)
        {
            TranslateMessage(&message);
            DispatchMessage(&message);
        }
    }

    public static bool PostQuit(uint threadId) => PostThreadMessage(threadId, WmQuit, 0, 0);

    public static uint CurrentThreadId() => GetCurrentThreadId();

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

    [LibraryImport("user32.dll", EntryPoint = "PeekMessageW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool PeekMessage(Msg* message, nint hwnd, uint filterMin, uint filterMax, uint remove);

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
