#if DEBUG
using Microsoft.Extensions.Logging;

namespace LayerPeek.App.Platform;

/// <summary>
/// Debug test mode only (T-099, <c>LAYERPEEK_TEST_NO_DISPLAY_MS</c>): reports no active display for a while after the start,
/// then the real count, so that the end-to-end test exercises the wait of T-096/T-099 without putting the display to sleep.
/// Does not exist in Release builds.
/// </summary>
internal sealed partial class PretendedNoDisplay : IActiveDisplays
{
    private readonly IActiveDisplays _real;
    private readonly TimeProvider _clock;
    private readonly TimeSpan _duration;
    private readonly ILogger _logger;
    private readonly long _started;
    private bool _logged;

    public PretendedNoDisplay(IActiveDisplays real, TimeSpan duration, TimeProvider clock, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(real);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(logger);
        _real = real;
        _duration = duration;
        _clock = clock;
        _logger = logger;
        _started = clock.GetTimestamp();
    }

    public bool TryCount(out uint count, out int error)
    {
        if (_clock.GetElapsedTime(_started) < _duration)
        {
            if (!_logged)
            {
                _logged = true;
                LogPretending(_duration);
            }

            count = 0;
            error = 0;
            return true;
        }

        return _real.TryCount(out count, out error);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Test mode: no active display is reported for {Duration}.")]
    private partial void LogPretending(TimeSpan duration);
}
#endif
