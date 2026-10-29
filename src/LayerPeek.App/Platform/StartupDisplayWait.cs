using System.Diagnostics;

using Microsoft.Extensions.Logging;

namespace LayerPeek.App.Platform;

/// <summary>How <see cref="StartupDisplayWait.WaitForDisplay"/> ended.</summary>
internal enum StartupDisplayWaitResult
{
    /// <summary>At least one display is active: the UI can start.</summary>
    Ready,

    /// <summary>The displays could not be counted: the UI starts anyway (the wait never blocks on a failing check).</summary>
    Unknown,

    /// <summary>The process was asked to quit while it waited: the UI must not start.</summary>
    Cancelled,
}

/// <summary>
/// Waits before the UI starts until a display is active (T-096, macOS). Avalonia.Native creates its render timer, a
/// <c>CVDisplayLink</c> for the active displays, once while the platform initializes; with every display asleep (lid
/// closed, the display slept, a login item started meanwhile) that fails with <c>kCVReturnInvalidDisplay</c> (-6661),
/// Avalonia throws and the process ends; the platform cannot be initialized a second time in the same process. So the
/// app checks first and polls cheaply until a display wakes; then the menu bar item appears. Synchronous on purpose:
/// it runs on the main thread before AppKit starts, which must stay the thread Avalonia runs on.
/// </summary>
/// <param name="displays">The active displays.</param>
/// <param name="pause">Waits the given time; returns <see langword="true"/> when the token was cancelled meanwhile.</param>
/// <param name="logger">The startup log.</param>
/// <param name="pollInterval">Time between two checks while no display is active; <see cref="DefaultPollInterval"/> when null.</param>
internal sealed partial class StartupDisplayWait(IActiveDisplays displays, Func<TimeSpan, CancellationToken, bool> pause, ILogger logger, TimeSpan? pollInterval = null)
{
    /// <summary>One check per second: a call to the window server, cheap; the menu bar item appears at most a second after the display wakes.</summary>
    public static readonly TimeSpan DefaultPollInterval = TimeSpan.FromSeconds(1);

    private readonly IActiveDisplays _displays = displays ?? throw new ArgumentNullException(nameof(displays));
    private readonly Func<TimeSpan, CancellationToken, bool> _pause = pause ?? throw new ArgumentNullException(nameof(pause));
    private readonly ILogger _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    private readonly TimeSpan _pollInterval = pollInterval ?? DefaultPollInterval;

    /// <summary>The real pause: blocks the calling thread, returns early (<see langword="true"/>) on cancellation.</summary>
    public static bool BlockingPause(TimeSpan interval, CancellationToken cancellationToken) =>
        cancellationToken.WaitHandle.WaitOne(interval);

    /// <summary>Returns once a display is active, the check fails, or <paramref name="cancellationToken"/> is cancelled. Never throws.</summary>
    public StartupDisplayWaitResult WaitForDisplay(CancellationToken cancellationToken)
    {
        long started = Stopwatch.GetTimestamp();
        bool waiting = false;
        while (true)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                TimeSpan cancelledAfter = Stopwatch.GetElapsedTime(started);
                LogCancelled(cancelledAfter);
                return StartupDisplayWaitResult.Cancelled;
            }

            uint count;
            int error;
            bool known;
            try
            {
                known = _displays.TryCount(out count, out error);
            }
            catch (Exception ex)
            {
                LogCheckThrew(ex);
                return StartupDisplayWaitResult.Unknown;
            }

            if (!known)
            {
                LogCheckFailed(error);
                return StartupDisplayWaitResult.Unknown;
            }

            if (count > 0)
            {
                if (waiting)
                {
                    TimeSpan waited = Stopwatch.GetElapsedTime(started);
                    LogDisplayAppeared(count, waited);
                }

                return StartupDisplayWaitResult.Ready;
            }

            if (!waiting)
            {
                waiting = true;
                LogWaiting();
            }

            _ = _pause(_pollInterval, cancellationToken);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "No active display (all displays asleep or none connected): the menu bar item starts once a display is active.")]
    private partial void LogWaiting();

    [LoggerMessage(Level = LogLevel.Information, Message = "{Count} active display(s) after {Elapsed}; starting the UI.")]
    private partial void LogDisplayAppeared(uint count, TimeSpan elapsed);

    [LoggerMessage(Level = LogLevel.Information, Message = "Asked to quit after {Elapsed} while waiting for an active display; the UI was not started.")]
    private partial void LogCancelled(TimeSpan elapsed);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Counting the active displays failed with error {Error}; starting the UI without the check.")]
    private partial void LogCheckFailed(int error);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Counting the active displays failed; starting the UI without the check.")]
    private partial void LogCheckThrew(Exception exception);
}
