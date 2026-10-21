using System.Net;

namespace PrusaTrayDiagnostic.Simulator.Camera;

/// <summary>Settings of a <see cref="RtspCamera"/>.</summary>
public sealed record RtspCameraOptions
{
    /// <summary>Frame rate of the Buddy3D recording.</summary>
    public const double DefaultFramesPerSecond = 25;

    /// <summary>The default <see cref="RtpMap"/>: H.264 with the RTP video clock.</summary>
    public const string H264RtpMap = "H264/90000";

    /// <summary>The clip to play: raw H.264, Annex B (see <see cref="H264Clip"/>).</summary>
    public required H264Clip Clip { get; init; }

    /// <summary>
    /// Encoding announced in the session description (<c>a=rtpmap:96 …</c>, encoding name and clock rate). H.264 like the
    /// Buddy3D by default; tests announce another codec (e.g. <c>H265/90000</c>) to check that a client rejects it. The
    /// payload stays the H.264 clip either way.
    /// </summary>
    public string RtpMap
    {
        get;
        init
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(value);
            if (value.AsSpan().ContainsAny('\r', '\n'))
            {
                throw new ArgumentException("The encoding must be a single SDP value.", nameof(value));
            }

            field = value;
        }
    } = H264RtpMap;

    /// <summary>Address to listen on; loopback by default (use <see cref="IPAddress.Any"/> for the local network).</summary>
    public IPAddress Address { get; init; } = IPAddress.Loopback;

    /// <summary>TCP port; 0 lets the OS choose a free one (tests).</summary>
    public int Port { get; init; }

    /// <summary>
    /// Play the clip in an endless loop like a live camera (default). Without looping the clip is played once and the
    /// connection is closed after the last frame (T-014 tests count the frames).
    /// </summary>
    public bool Loop { get; init; } = true;

    /// <summary>
    /// Frames per second when <see cref="RealTime"/> is on; also defines the RTP timestamps (90 kHz clock) in any mode.
    /// </summary>
    public double FramesPerSecond
    {
        get;
        init
        {
            ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(value, 0);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(value, 1000);
            field = value;
        }
    } = DefaultFramesPerSecond;

    /// <summary>Pace the frames at <see cref="FramesPerSecond"/> (default); off sends them as fast as the client reads.</summary>
    public bool RealTime { get; init; } = true;

    /// <summary>Clock for pacing; the system clock unless a test needs another one.</summary>
    public TimeProvider TimeProvider { get; init; } = TimeProvider.System;

    /// <summary>Receives one line per client event (connect, play, disconnect); null for silence.</summary>
    public Action<string>? Log { get; init; }
}
