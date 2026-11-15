using System.Collections.Concurrent;

using FFmpeg.AutoGen.Abstractions;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using LayerPeek.Core.Cameras;

namespace LayerPeek.Camera.FFmpeg;

/// <summary>
/// What hardware decoding of one <see cref="FFmpegCameraFeedFactory"/> currently means (T-203): the device the setting
/// selects on this OS (<see cref="HardwareDecoderSelection"/>), why it is software otherwise, and a generation that changes
/// with every change of the setting. Decoders read <see cref="Current"/> when full decoding starts and at every key frame
/// of it (one volatile read), so a saved change applies from the next key frame on (T-204, question 2). Also keeps the
/// failure history of every camera (<see cref="HistoryOf"/>). Thread-safe.
/// </summary>
internal sealed class HardwareDecodingPolicy
{
    private readonly bool _isMacOS;
    private readonly ConcurrentDictionary<string, HardwareDecodingHistory> _histories = new(StringComparer.Ordinal);
    private readonly Lock _gate = new();

    // Guarded by _gate; Current is replaced as a whole (readers never see a half-updated choice).
    private CameraHardwareDecoding _mode;
    private bool _hasOverride;
    private AVHWDeviceType _override;
    private volatile HardwareDecodingChoice _current;

    private long _devicesCreated;

    public HardwareDecodingPolicy(CameraHardwareDecoding mode, ILogger? logger = null)
        : this(mode, OperatingSystem.IsMacOS(), logger)
    {
    }

    /// <param name="isMacOS">The OS of the rule (tests check both).</param>
    internal HardwareDecodingPolicy(CameraHardwareDecoding mode, bool isMacOS, ILogger? logger = null)
    {
        _isMacOS = isMacOS;
        _mode = mode;
        Logger = logger ?? NullLogger.Instance;
        _current = Choose(generation: 0);
    }

    /// <summary>The log of the decoders (one line per start of full decoding and per fallback, never per frame).</summary>
    public ILogger Logger { get; }

    /// <summary>The current choice; replaced by a change of <see cref="Mode"/> or of the test override.</summary>
    public HardwareDecodingChoice Current => _current;

    /// <summary>The user's setting. A change starts a new generation: failures of the old one no longer count.</summary>
    public CameraHardwareDecoding Mode
    {
        get
        {
            lock (_gate)
            {
                return _mode;
            }
        }

        set
        {
            if (value is not (CameraHardwareDecoding.Automatic or CameraHardwareDecoding.Off))
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "Unknown hardware decoding setting.");
            }

            lock (_gate)
            {
                if (_mode == value)
                {
                    return;
                }

                _mode = value;
                _current = Choose(_current.Generation + 1);
            }
        }
    }

    /// <summary>
    /// Hardware devices created so far (for tests: no device without a camera window; also counts devices that a fault
    /// injection refused).
    /// </summary>
    internal long DevicesCreated => Interlocked.Read(ref _devicesCreated);

    /// <summary>Faults injected by tests; <see langword="null"/> in the application.</summary>
    internal HardwareDecodingFaults? Faults { get; set; }

    /// <summary>
    /// The device the decoders use whatever the setting says (<c>AV_HWDEVICE_TYPE_NONE</c>: software), or
    /// <see langword="null"/> for the rule. The Debug test switch (<c>LAYERPEEK_TEST_HW_DECODER</c>)
    /// and tests set it; a change starts a new generation like a change of the setting.
    /// </summary>
    internal void SetOverride(AVHWDeviceType? device)
    {
        lock (_gate)
        {
            _hasOverride = device.HasValue;
            _override = device.GetValueOrDefault();
            _current = Choose(_current.Generation + 1);
        }
    }

    /// <summary>The failure history of the camera <paramref name="camera"/> (its address without credentials).</summary>
    public HardwareDecodingHistory HistoryOf(string camera) => _histories.GetOrAdd(camera, static _ => new HardwareDecodingHistory());

    internal void OnDeviceCreated() => Interlocked.Increment(ref _devicesCreated);

    // Under _gate.
    private HardwareDecodingChoice Choose(int generation)
    {
        if (_hasOverride)
        {
            return new HardwareDecodingChoice(_override, _override == AVHWDeviceType.AV_HWDEVICE_TYPE_NONE ? "test switch" : null, generation);
        }

        AVHWDeviceType device = HardwareDecoderSelection.Select(_mode, _isMacOS);
        string? reason = device != AVHWDeviceType.AV_HWDEVICE_TYPE_NONE ? null
            : _mode == CameraHardwareDecoding.Off ? "turned off in the settings"
            : "not used on this OS yet";
        return new HardwareDecodingChoice(device, reason, generation);
    }
}

/// <summary>
/// One state of <see cref="HardwareDecodingPolicy"/>: the <paramref name="Device"/> to use (<c>AV_HWDEVICE_TYPE_NONE</c>:
/// software, then <paramref name="SoftwareReason"/> says why) in <paramref name="Generation"/>.
/// </summary>
internal sealed record HardwareDecodingChoice(AVHWDeviceType Device, string? SoftwareReason, int Generation);

/// <summary>
/// Against switching back and forth (T-203): consecutive failed hardware sessions of one camera. A hardware failure ends
/// hardware decoding for the rest of its connection (the decoder's own flag); the next connection tries again; after
/// <see cref="MaxConsecutiveFailures"/> failed sessions in a row the camera stays in software until the process ends or
/// the setting changes (a new generation of <see cref="HardwareDecodingPolicy"/>). A hardware session that decoded frames
/// and ended without a failure starts the count again. Thread-safe; called a few times per connection and at key frames.
/// </summary>
internal sealed class HardwareDecodingHistory
{
    public const int MaxConsecutiveFailures = 2;

    private readonly Lock _gate = new();
    private int _generation;
    private int _failures;

    /// <summary>Consecutive failures in <paramref name="generation"/>.</summary>
    public int FailuresIn(int generation)
    {
        lock (_gate)
        {
            return _generation == generation ? _failures : 0;
        }
    }

    /// <summary>Whether the camera stays in software in <paramref name="generation"/>.</summary>
    public bool IsDisabled(int generation) => FailuresIn(generation) >= MaxConsecutiveFailures;

    /// <summary>A hardware session failed; returns the consecutive failures of <paramref name="generation"/>.</summary>
    public int ReportFailure(int generation)
    {
        lock (_gate)
        {
            Enter(generation);
            return ++_failures;
        }
    }

    /// <summary>A hardware session decoded frames and ended without a failure.</summary>
    public void ReportSuccess(int generation)
    {
        lock (_gate)
        {
            Enter(generation);
            // Once disabled, the camera stays in software (nothing can succeed any more in this generation).
            if (_failures < MaxConsecutiveFailures)
            {
                _failures = 0;
            }
        }
    }

    // Under _gate: a newer generation forgets the failures of the older one; an older one (a decoder that read its
    // choice before the change) counts in the newer one, which is the stricter of both.
    private void Enter(int generation)
    {
        if (generation > _generation)
        {
            _generation = generation;
            _failures = 0;
        }
    }
}

/// <summary>Faults of the hardware path injected by tests (<see cref="HardwareDecodingPolicy.Faults"/>).</summary>
internal sealed class HardwareDecodingFaults
{
    /// <summary><c>av_hwdevice_ctx_create</c> fails (the device cannot be created).</summary>
    public bool FailDeviceCreation { get; init; }

    /// <summary><c>av_hwframe_map</c> fails: the frames are copied (<c>av_hwframe_transfer_data</c>) instead.</summary>
    public bool FailMapping { get; init; }

    /// <summary>Decoding fails after this many hardware frames of a hardware session (like a lost device).</summary>
    public int? FailDecodingAfterFrames { get; init; }
}
