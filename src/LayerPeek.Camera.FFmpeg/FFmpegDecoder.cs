using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

using FFmpeg.AutoGen.Abstractions;

using LayerPeek.Core.Cameras;

namespace LayerPeek.Camera.FFmpeg;

/// <summary>
/// Raised inside <see cref="InterruptFlag"/>; FFmpeg checks it during blocking I/O (connect, read) and gives up.
/// </summary>
internal sealed class InterruptFlag
{
    private volatile bool _isSet;

    public bool IsSet => _isSet;

    public void Set() => _isSet = true;
}

/// <summary>
/// The native part of one camera connection: demuxer, H.264 decoder and conversion into BGRA (in the camera's resolution
/// and/or scaled down for display, T-184) and into grayscale (T-189), as the <see cref="CameraStreamDemand"/> wants. Not
/// thread-safe; every member except the counters and <see cref="HoldsNativeResources"/> is called on the feed's decoder
/// thread only. Blocking FFmpeg calls return early once the <see cref="InterruptFlag"/> is set; they then throw
/// <see cref="OperationCanceledException"/> (the caller translates it). Failures throw <see cref="CameraFeedException"/>
/// with a message free of the address.
/// </summary>
/// <remarks>
/// <para>
/// T-203, hardware decoding: only full decoding for a camera window (a display reader, <see cref="CameraStreamDemand.DisplaySize"/>;
/// the camera window is the only one, T-184) uses the device of <see cref="HardwareDecodingPolicy.Current"/>. Key frames
/// decoded on their own (detection, T-153), the snapshot and the camera test stay in software, so without a camera window no
/// device exists. There is one codec context at a time: the hardware one is opened for full decoding and freed (with its
/// device) when full decoding stops; the key frames of detection then get a software context again. Hardware or software
/// is chosen when full decoding starts and again at every key frame of it, where a switch loses nothing (a saved change of
/// the setting applies there, T-204); a running hardware session keeps going without a display reader until full decoding
/// stops (the hub's linger), so closing and reopening a window does not switch twice.
/// </para>
/// <para>
/// The decoder picks the hardware pixel format through FFmpeg's default <c>get_format</c> (with a device attached it takes
/// the device's format when the decoder offers it, otherwise the first software format; FFmpeg calls it again without the
/// hardware format when the hardware initialization fails). Only frames that are converted are mapped into memory
/// (<c>av_hwframe_map</c>, no copy, T-185); when mapping fails they are copied (<c>av_hwframe_transfer_data</c>) for the rest
/// of the session. Any hardware failure (no device, no hardware format, a decoding or transfer error) logs one warning and
/// leaves the connection in software from the next key frame on, without an error of the feed; see
/// <see cref="HardwareDecodingHistory"/> for the rule against switching back and forth.
/// </para>
/// </remarks>
internal sealed unsafe class FFmpegDecoder : IDisposable
{
    /// <summary>Name of FFmpeg's native H.264 decoder, the only one allowed (<c>codec_whitelist</c>).</summary>
    private const string H264Decoder = "h264";

    /// <summary>The stream's codec or container is not one the feed accepts (only H.264).</summary>
    private const string UnsupportedFormat = "The video format of the stream is not supported.";

    /// <summary>Computed once: FFmpeg.AutoGen's <c>ffmpeg.AVERROR(int)</c> allocates on every call.</summary>
    private static readonly int ErrorAgain = ffmpeg.AVERROR(ffmpeg.EAGAIN);

    /// <summary>AVERROR(ENOSYS): the hardware path is not available (not in this build, not offered by the decoder).</summary>
    private static readonly int ErrorNotSupported = ffmpeg.AVERROR(OperatingSystem.IsWindows() ? 40 : OperatingSystem.IsMacOS() ? 78 : 38);

    private const AVHWDeviceType Software = AVHWDeviceType.AV_HWDEVICE_TYPE_NONE;

    private readonly string _url;
    private readonly bool _isNetwork;
    private readonly TimeSpan _ioTimeout;
    private readonly InterruptFlag _interrupt;
    private readonly HardwareDecodingPolicy _policy;
    private readonly HardwareDecodingHistory _history;

    private GCHandle _interruptHandle;
    private AVFormatContext* _format;
    private AVCodec* _decoder;
    private AVCodecContext* _codec;
    private AVPacket* _packet;
    private AVFrame* _frame;
    private int _streamIndex = -1;
    private bool _draining;
    private volatile bool _holdsNativeResources;

    // T-143: packets before decoding, decoding only on demand.
    private CameraStreamInfo? _info;
    private bool _infoDelivered;
    private CameraTimeBase _timeBase;
    private byte[] _packetBuffer = [];

    // _packet holds a packet that was handed out as a packet item and still goes to the decoder.
    private bool _packetPending;

    // T-153: the pending packet is a single key frame to decode on its own (no full decoding).
    private bool _keyFramePending;

    // The decoder gets packets (frame demand); when it was off, packets were skipped and it restarts at a key frame.
    private bool _decoding;
    private bool _needKeyFrame;
    private long _decodedFrames;

    // T-184: the conversion in the camera's resolution and the one scaled down for display, each with its own buffer;
    // T-189: the grayscale samples of print failure detection, from the luma plane.
    private readonly BgraConverter _full = new();
    private readonly BgraConverter _display = new();
    private readonly GrayscaleConverter _grayscale = new();

    // T-195: whether the frame for display is scaled down here or converted in full resolution for the renderer to scale.
    private readonly CameraDisplayScaler _displayScaler = new();

    // T-203: the device of the open codec context (Software: none), the pixel format of its hardware frames
    // (AV_PIX_FMT_NONE while the session decodes in software) and the generation of the choice it was opened with.
    private AVHWDeviceType _codecDevice = Software;
    private AVPixelFormat _hardwareFormat = AVPixelFormat.AV_PIX_FMT_NONE;
    private int _sessionGeneration;
    private long _sessionHardwareFrames;
    private bool _transferFrames;

    // Hardware failed on this connection in this generation of the choice: software to the end of the connection.
    private int _failedGeneration = -1;

    // The mapped (or copied) picture of a hardware frame; reused, released right after its conversion.
    private AVFrame* _hardwarePicture;
    private long _mappedFrames;
    private long _transferredFrames;
    private volatile bool _holdsHardwareDevice;

    public FFmpegDecoder(string url, bool isNetwork, TimeSpan ioTimeout, InterruptFlag interrupt, HardwareDecodingPolicy policy, HardwareDecodingHistory history)
    {
        _url = url;
        _isNetwork = isNetwork;
        _ioTimeout = ioTimeout;
        _interrupt = interrupt;
        _policy = policy;
        _history = history;
    }

    /// <summary>The flag that interrupts the blocking FFmpeg calls of this decoder.</summary>
    public InterruptFlag Interrupt => _interrupt;

    /// <summary>Any native FFmpeg object is allocated. Safe to read from any thread.</summary>
    public bool HoldsNativeResources => _holdsNativeResources;

    /// <summary>Frames decoded so far (for tests: a packet-only reader decodes nothing). Safe to read from any thread.</summary>
    public long DecodedFrames => Interlocked.Read(ref _decodedFrames);

    /// <summary>The H.264 decoder was opened (it is only opened once frames are wanted). Safe to read from any thread.</summary>
    public bool DecoderOpened { get; private set; }

    /// <summary>A hardware device is attached to the open decoder (T-203). Safe to read from any thread.</summary>
    public bool HoldsHardwareDevice => _holdsHardwareDevice;

    /// <summary>Hardware frames mapped into memory for a conversion (T-203, for tests). Safe to read from any thread.</summary>
    public long MappedFrames => Interlocked.Read(ref _mappedFrames);

    /// <summary>Hardware frames copied into memory because mapping failed (T-203, for tests). Safe to read from any thread.</summary>
    public long TransferredFrames => Interlocked.Read(ref _transferredFrames);

    private string OpenStage => _isNetwork ? "Connecting to the camera failed" : "Opening the video failed";

    /// <summary>Connects, probes the stream and prepares the decoder.</summary>
    public void Open()
    {
        if (_format is not null)
        {
            throw new InvalidOperationException("The decoder is already open.");
        }

        _holdsNativeResources = true;
        _interruptHandle = GCHandle.Alloc(_interrupt);
        AVFormatContext* format = ffmpeg.avformat_alloc_context();
        if (format is null)
        {
            throw new CameraFeedException("Out of memory while opening the camera stream.");
        }

        // A function pointer to an [UnmanagedCallersOnly] method: no delegate, no marshalling (Native AOT).
        delegate* unmanaged[Cdecl]<void*, int> interrupt = &OnInterrupt;
        format->interrupt_callback.callback = new AVIOInterruptCB_callback_func { Pointer = (nint)interrupt };
        format->interrupt_callback.opaque = (void*)GCHandle.ToIntPtr(_interruptHandle);

        AVDictionary* options = null;
        try
        {
            // The stream is untrusted (whoever controls the camera or the network controls it): FFmpeg may only use the
            // protocols, demuxers and decoders this feed needs, so that a flaw in any other part of the library is not
            // reachable from the network (security review M1, T-050).
            if (_isNetwork)
            {
                // RTSP is the demuxer (it parses the session description itself, no nested sdp/rtp demuxer); its only
                // I/O protocol is tcp, because rtsp_transport=tcp carries RTP interleaved on the RTSP connection
                // (no rtp/udp). "rtsp" itself is checked when the camera redirects to another rtsp:// address
                // (verified by test: without it the redirect fails). Anything else, e.g. a redirect to rtsps (tls),
                // fails as a connection error.
                ffmpeg.av_dict_set(&options, "protocol_whitelist", "rtsp,tcp", 0);
                ffmpeg.av_dict_set(&options, "format_whitelist", "rtsp", 0);
                // TCP interleaved: no UDP ports to open, works through firewalls, no packet loss.
                ffmpeg.av_dict_set(&options, "rtsp_transport", "tcp", 0);
                // Socket I/O timeout in microseconds; a backstop, callers usually cancel earlier.
                string microseconds = ((long)_ioTimeout.TotalMicroseconds).ToString(CultureInfo.InvariantCulture);
                ffmpeg.av_dict_set(&options, "timeout", microseconds, 0);
                // Low latency (decided in T-014): hand packets over as they arrive instead of buffering them.
                ffmpeg.av_dict_set(&options, "fflags", "nobuffer", 0);
            }
            else
            {
                // Local files: raw H.264 (tests and diagnostics) and the app's own MP4 recordings and timelapses (T-165: the
                // JPEG export reads a timelapse the user picked). "mov" is FFmpeg's MP4 demuxer (registered as
                // "mov,mp4,m4a,3gp,3g2,mj2"; in the macOS build through the RTSP demuxer's dependencies, BtbN has it). Only
                // the file protocol: references to other files or URLs inside an MP4 cannot be followed.
                ffmpeg.av_dict_set(&options, "protocol_whitelist", "file", 0);
                ffmpeg.av_dict_set(&options, "format_whitelist", "h264,mov", 0);
            }

            // Decoders that probing (avformat_find_stream_info) may open.
            ffmpeg.av_dict_set(&options, "codec_whitelist", H264Decoder, 0);

            // Frees the context and sets it to null on failure.
            int result = ffmpeg.avformat_open_input(&format, _url, null, &options);
            _format = format;
            Check(result, OpenStage);
        }
        finally
        {
            ffmpeg.av_dict_free(&options);
        }

        if (!_isNetwork)
        {
            // Files: probe the content (robust, but buffers; irrelevant for a recording).
            Check(ffmpeg.avformat_find_stream_info(_format, null), OpenStage);
        }

        // The RTSP session description (SDP) already fixes the streams and names the codec, and usually carries SPS/PPS
        // as extradata; that is all the decoder needs. Network streams are never probed: avformat_find_stream_info
        // would decode about a second of video first and hand it over as a backlog, which delays the live picture
        // permanently (measured: ~1.2 s), and it cannot find a stream the description does not announce - it would
        // only feed the untrusted payload to other depacketizers and parsers. Without asking for a decoder: the stream
        // is chosen by its type, its codec is checked below.
        int stream = ffmpeg.av_find_best_stream(_format, AVMediaType.AVMEDIA_TYPE_VIDEO, -1, -1, null, 0);

        if (stream == ffmpeg.AVERROR_STREAM_NOT_FOUND)
        {
            ThrowIfInterrupted();
            throw new CameraFeedException("The stream contains no video.");
        }

        Check(stream, OpenStage);
        // Only H.264 (what the Buddy3D sends) is decoded, always by FFmpeg's own decoder; any other codec is refused
        // before a decoder sees a single byte of it.
        AVCodec* decoder = _format->streams[stream]->codecpar->codec_id == AVCodecID.AV_CODEC_ID_H264
            ? ffmpeg.avcodec_find_decoder(AVCodecID.AV_CODEC_ID_H264)
            : null;
        if (decoder is null)
        {
            throw new CameraFeedException(UnsupportedFormat);
        }

        _streamIndex = stream;
        for (int i = 0; i < _format->nb_streams; i++)
        {
            if (i != stream)
            {
                _format->streams[i]->discard = AVDiscard.AVDISCARD_ALL;
            }
        }

        _decoder = decoder;
        _packet = ffmpeg.av_packet_alloc();
        if (_packet is null)
        {
            throw new CameraFeedException("Out of memory while opening the camera stream.");
        }

        // What a recording needs to write the packets without decoding them (T-143, T-144).
        AVStream* video = _format->streams[stream];
        AVCodecParameters* parameters = video->codecpar;
        _timeBase = video->time_base.num > 0 && video->time_base.den > 0
            ? new CameraTimeBase(video->time_base.num, video->time_base.den)
            : CameraTimeBase.Rtp90kHz;
        byte[] extradata = parameters->extradata is not null && parameters->extradata_size > 0
            ? new ReadOnlySpan<byte>(parameters->extradata, parameters->extradata_size).ToArray()
            : [];
        _info = new CameraStreamInfo(CameraVideoCodec.H264, Math.Max(parameters->width, 0), Math.Max(parameters->height, 0), _timeBase, extradata);
    }

    /// <summary>
    /// Opens the H.264 decoder on <paramref name="device"/> (<see cref="Software"/>: none) in place of the open one; only when
    /// frames are wanted (T-143: recording alone decodes nothing). A hardware device that cannot be used is a hardware
    /// failure (logged, counted) and the decoder opens in software. <paramref name="softwareReason"/> (why software was
    /// chosen) and <paramref name="log"/> (full decoding: one line saying which path it takes) are for the log.
    /// </summary>
    private void OpenDecoder(AVHWDeviceType device, string? softwareReason, bool log)
    {
        CloseDecoder();
        if (_frame is null)
        {
            _frame = ffmpeg.av_frame_alloc();
        }

        _codec = ffmpeg.avcodec_alloc_context3(_decoder);
        if (_codec is null || _frame is null)
        {
            throw new CameraFeedException("Out of memory while opening the camera stream.");
        }

        const string DecoderStage = "Starting the video decoder failed";
        Check(ffmpeg.avcodec_parameters_to_context(_codec, _format->streams[_streamIndex]->codecpar), DecoderStage);
        // Every frame leaves the decoder as soon as it is complete: no frame threading (it delays output by one frame
        // per thread), low-delay mode. A 720p Baseline stream decodes easily on one thread.
        _codec->flags |= ffmpeg.AV_CODEC_FLAG_LOW_DELAY;
        _codec->thread_count = 1;

        if (device != Software)
        {
            int attached = AttachDevice(device);
            if (attached < 0)
            {
                softwareReason = "the hardware decoder is not available";
                ReportHardwareFailure(device, "starting", attached);
            }
        }

        AVDictionary* decoderOptions = null;
        int opened;
        try
        {
            ffmpeg.av_dict_set(&decoderOptions, "codec_whitelist", H264Decoder, 0);
            opened = ffmpeg.avcodec_open2(_codec, _decoder, &decoderOptions);
        }
        finally
        {
            ffmpeg.av_dict_free(&decoderOptions);
        }

        if (opened < 0 && _codecDevice != Software)
        {
            // Not expected (the hardware is set up with the first picture), but then software it is.
            ReportHardwareFailure(_codecDevice, "starting", opened);
            OpenDecoder(Software, "the hardware decoder is not available", log);
            return;
        }

        Check(opened, DecoderStage);
        DecoderOpened = true;
        if (log)
        {
            HardwareDecodingLog.Decoding(_policy.Logger, _codecDevice, softwareReason);
        }
    }

    /// <summary>
    /// Attaches a new device of <paramref name="device"/> to the codec context, which owns it from then on
    /// (<c>avcodec_free_context</c> releases it with <c>av_buffer_unref</c>). Returns 0, or the FFmpeg error.
    /// </summary>
    private int AttachDevice(AVHWDeviceType device)
    {
        AVPixelFormat format = HardwareFormat(_decoder, device);
        if (format == AVPixelFormat.AV_PIX_FMT_NONE)
        {
            // Not in this FFmpeg build (or not for H.264).
            return ErrorNotSupported;
        }

        AVBufferRef* context = null;
        int created = _policy.Faults is { FailDeviceCreation: true }
            ? ffmpeg.AVERROR_EXTERNAL
            : ffmpeg.av_hwdevice_ctx_create(&context, device, null, null, 0);
        if (created < 0)
        {
            return created;
        }

        _policy.OnDeviceCreated();
        _codec->hw_device_ctx = context;
        _codecDevice = device;
        _hardwareFormat = format;
        _sessionGeneration = _policy.Current.Generation;
        _sessionHardwareFrames = 0;
        _transferFrames = false;
        _holdsHardwareDevice = true;
        return 0;
    }

    /// <summary>The pixel format of <paramref name="decoder"/>'s frames on a device of <paramref name="device"/>; none when it has no such hardware path.</summary>
    internal static AVPixelFormat HardwareFormat(AVCodec* decoder, AVHWDeviceType device)
    {
        for (int i = 0; ; i++)
        {
            AVCodecHWConfig* config = ffmpeg.avcodec_get_hw_config(decoder, i);
            if (config is null)
            {
                return AVPixelFormat.AV_PIX_FMT_NONE;
            }

            if (config->device_type == device && (config->methods & (int)AvCodecHwConfigMethod.AV_CODEC_HW_CONFIG_METHOD_HW_DEVICE_CTX) != 0)
            {
                return config->pix_fmt;
            }
        }
    }

    /// <summary>
    /// Frees the codec context, and with it a hardware device (T-203); counts a hardware session that decoded frames without
    /// a failure as a success. Idempotent.
    /// </summary>
    private void CloseDecoder()
    {
        if (_codec is null)
        {
            return;
        }

        if (_hardwareFormat != AVPixelFormat.AV_PIX_FMT_NONE && _sessionHardwareFrames > 0)
        {
            _history.ReportSuccess(_sessionGeneration);
        }

        // The pictures may still reference the decoder's (hardware) buffers.
        ffmpeg.av_frame_unref(_frame);
        if (_hardwarePicture is not null)
        {
            ffmpeg.av_frame_unref(_hardwarePicture);
        }

        AVCodecContext* codec = _codec;
        ffmpeg.avcodec_free_context(&codec);
        _codec = null;
        _codecDevice = Software;
        _hardwareFormat = AVPixelFormat.AV_PIX_FMT_NONE;
        _sessionHardwareFrames = 0;
        _holdsHardwareDevice = false;
    }

    /// <summary>
    /// Full decoding starts (T-203): opens the decoder on the device the policy wants for <paramref name="demand"/>, unless
    /// the open one already is; logs which path it takes.
    /// </summary>
    private void StartDecoding(CameraStreamDemand demand)
    {
        AVHWDeviceType device = WantedDevice(demand, out string? reason);
        if (_codec is null || device != _codecDevice)
        {
            OpenDecoder(device, reason, log: true);
        }
        else
        {
            // The software decoder of the key frames goes on (flushed when it decoded the last one).
            HardwareDecodingLog.Decoding(_policy.Logger, Software, reason);
        }

        _decoding = true;
    }

    /// <summary>
    /// The device full decoding should use now (T-203), <see cref="Software"/> with a <paramref name="reason"/> otherwise.
    /// A hardware session is started only for a display reader (the camera window) and then kept until full decoding stops.
    /// </summary>
    private AVHWDeviceType WantedDevice(CameraStreamDemand demand, out string? reason)
    {
        HardwareDecodingChoice choice = _policy.Current;
        if (choice.Device == Software)
        {
            reason = choice.SoftwareReason;
            return Software;
        }

        if (_failedGeneration == choice.Generation)
        {
            reason = "hardware decoding failed on this connection";
            return Software;
        }

        if (_history.IsDisabled(choice.Generation))
        {
            reason = "hardware decoding failed on several connections in a row";
            return Software;
        }

        if (_hardwareFormat == AVPixelFormat.AV_PIX_FMT_NONE && demand.DisplaySize is null)
        {
            reason = "no camera window";
            return Software;
        }

        reason = null;
        return choice.Device;
    }

    /// <summary>
    /// A hardware failure (T-203): one warning with the error, counted in the camera's history; the connection decodes in
    /// software from now on (until the setting changes).
    /// </summary>
    private void ReportHardwareFailure(AVHWDeviceType device, string stage, int error)
    {
        int generation = _codecDevice != Software ? _sessionGeneration : _policy.Current.Generation;
        _failedGeneration = generation;
        int failures = _history.ReportFailure(generation);
        HardwareDecodingLog.HardwareFailed(_policy.Logger, device, stage, error, failures);
        // No success of this session any more.
        _sessionHardwareFrames = 0;
    }

    /// <summary>
    /// The hardware decoder failed while decoding (T-203): it is released at once, and the next key frame opens the decoder
    /// in software (the frames until then are lost, as after any decoding error).
    /// </summary>
    private void AbandonHardware(string stage, int error)
    {
        ReportHardwareFailure(_codecDevice, stage, error);
        CloseDecoder();
        _needKeyFrame = true;
    }

    /// <summary>
    /// Opens the software decoder for a key frame decoded on its own (T-153); detection, the snapshot and the camera test
    /// never use a hardware device.
    /// </summary>
    private void EnsureSoftwareDecoder()
    {
        if (_codec is null || _codecDevice != Software)
        {
            OpenDecoder(Software, softwareReason: null, log: false);
        }
    }

    /// <summary>
    /// The next item by <paramref name="demand"/> (T-143): first the stream description (when
    /// <paramref name="emitInfo"/>), then every video packet while packets are wanted, before it is decoded, and the
    /// decoded frames while frames are wanted, converted in the forms the demand wants (<see cref="CameraStreamItem.Frame"/>,
    /// <see cref="CameraStreamItem.DisplayFrame"/>, <see cref="CameraStreamItem.GrayscaleFrame"/>). Nothing is decoded without
    /// frame demand, except a single key frame when <see cref="CameraStreamDemand.KeyFrameDue"/> (T-153: decoded on its own,
    /// the packets in between are never decoded); when frames are wanted again after packets were skipped, decoding restarts at the next key frame. Returns
    /// <see langword="false"/> at the end of the stream. Packet data (copied into a reused buffer) and frames are valid
    /// until the next call (or, for frames, until the buffer is replaced after a size change; the old buffer is then left
    /// to the garbage collector, so a consumer holding on to it never sees freed memory).
    /// </summary>
    public bool TryRead(CameraStreamDemand demand, bool emitInfo, out CameraStreamItem item)
    {
        if (emitInfo && !_infoDelivered && _info is { } info)
        {
            _infoDelivered = true;
            item = CameraStreamItem.Started(info);
            return true;
        }

        while (true)
        {
            ThrowIfInterrupted();
            bool frames = demand.Frames;
            if (!frames && _decoding)
            {
                // Frames are no longer wanted: the decoder forgets its state and later restarts at a key frame.
                StopDecoding();
            }

            // Without a codec context after a hardware failure until the next key frame opens the software one.
            if (_decoding && _codec is not null)
            {
                int received = ffmpeg.avcodec_receive_frame(_codec, _frame);
                if (received == 0)
                {
                    Interlocked.Increment(ref _decodedFrames);
                    if (_hardwareFormat != AVPixelFormat.AV_PIX_FMT_NONE && !AcceptHardwareFrame())
                    {
                        // The hardware decoder failed (simulated by a test); the frame is dropped.
                        continue;
                    }

                    item = Convert(demand);
                    return true;
                }

                if (received == ffmpeg.AVERROR_EOF)
                {
                    item = default;
                    return false;
                }

                // A damaged frame (INVALIDDATA) is skipped like a missing one: live streams recover with the next key frame.
                if (received != ErrorAgain && received != ffmpeg.AVERROR_INVALIDDATA)
                {
                    if (_hardwareFormat != AVPixelFormat.AV_PIX_FMT_NONE)
                    {
                        ThrowIfInterrupted();
                        AbandonHardware("decoding", received);
                        continue;
                    }

                    Check(received, "Decoding the video failed");
                }
            }

            if (_draining)
            {
                item = default;
                return false;
            }

            if (_packetPending)
            {
                // Handed out as a packet item before; now it is decoded.
                _packetPending = false;
                if (_keyFramePending)
                {
                    _keyFramePending = false;
                    if (TryDecodeKeyFrame(demand, out item))
                    {
                        return true;
                    }

                    continue;
                }

                SendPacket(demand);
                continue;
            }

            int read = ffmpeg.av_read_frame(_format, _packet);
            if (read == ffmpeg.AVERROR_EOF)
            {
                ThrowIfInterrupted();
                if (!_decoding || _codec is null)
                {
                    item = default;
                    return false;
                }

                // Flush: the decoder returns its buffered frames, then AVERROR_EOF.
                _draining = true;
                Check(ffmpeg.avcodec_send_packet(_codec, null), "Decoding the video failed");
                continue;
            }

            Check(read, "Reading the camera stream failed");
            if (_packet->stream_index != _streamIndex)
            {
                ffmpeg.av_packet_unref(_packet);
                continue;
            }

            if (frames && !_decoding)
            {
                StartDecoding(demand);
            }

            // Checked only for key frames (one clock read every few seconds), and only without full decoding.
            bool keyFrame = !_decoding && (_packet->flags & ffmpeg.AV_PKT_FLAG_KEY) != 0 && demand.KeyFrameDue;
            if (keyFrame)
            {
                EnsureSoftwareDecoder();
            }

            if (demand.Packets)
            {
                item = CameraStreamItem.FromPacket(CopyPacket());
                if (_decoding || keyFrame)
                {
                    _packetPending = true;
                    _keyFramePending = keyFrame;
                }
                else
                {
                    Skip();
                }

                return true;
            }

            if (_decoding)
            {
                SendPacket(demand);
            }
            else if (keyFrame)
            {
                if (TryDecodeKeyFrame(demand, out item))
                {
                    return true;
                }
            }
            else
            {
                Skip();
            }
        }
    }

    /// <summary>
    /// T-153: decodes the key frame in <c>_packet</c> on its own and releases the packet. The decoder starts clean, gets
    /// the key frame and is drained, so its picture comes out now (without waiting for later packets, also with a reorder
    /// delay), and is flushed again afterwards; full decoding later restarts at a key frame. A damaged key frame gives no
    /// frame (<see langword="false"/>), like in full decoding.
    /// </summary>
    private bool TryDecodeKeyFrame(CameraStreamDemand demand, out CameraStreamItem item)
    {
        item = default;
        ffmpeg.avcodec_flush_buffers(_codec);
        _needKeyFrame = true;
        int sent = ffmpeg.avcodec_send_packet(_codec, _packet);
        ffmpeg.av_packet_unref(_packet);
        if (sent == ffmpeg.AVERROR_INVALIDDATA)
        {
            return false;
        }

        Check(sent, "Decoding the video failed");
        Check(ffmpeg.avcodec_send_packet(_codec, null), "Decoding the video failed");
        int received = ffmpeg.avcodec_receive_frame(_codec, _frame);
        try
        {
            if (received == 0)
            {
                Interlocked.Increment(ref _decodedFrames);
                // Converted into the reused buffer before the flush below releases the decoder's picture.
                item = Convert(demand);
                return true;
            }

            if (received != ErrorAgain && received != ffmpeg.AVERROR_EOF && received != ffmpeg.AVERROR_INVALIDDATA)
            {
                Check(received, "Decoding the video failed");
            }

            return false;
        }
        finally
        {
            // Leaves the drained state (the decoder takes packets again) and releases the picture until the next sample.
            ffmpeg.av_frame_unref(_frame);
            ffmpeg.avcodec_flush_buffers(_codec);
        }
    }

    /// <summary>A packet that the decoder does not get: it must restart at a key frame later.</summary>
    private void Skip()
    {
        ffmpeg.av_packet_unref(_packet);
        _needKeyFrame = true;
    }

    private void StopDecoding()
    {
        if (_packetPending)
        {
            ffmpeg.av_packet_unref(_packet);
            _packetPending = false;
            _keyFramePending = false;
        }

        if (_codecDevice != Software)
        {
            // T-203: the hardware session (and the system decoder behind it) exists only during full decoding.
            CloseDecoder();
        }
        else if (_codec is not null)
        {
            ffmpeg.avcodec_flush_buffers(_codec);
        }

        _decoding = false;
        _needKeyFrame = true;
    }

    /// <summary>
    /// Sends <c>_packet</c> to the decoder (from a key frame on, after skipped packets) and releases it. At a key frame the
    /// decoder switches between hardware and software when the policy wants the other one (T-203): nothing is lost there.
    /// </summary>
    private void SendPacket(CameraStreamDemand demand)
    {
        bool key = (_packet->flags & ffmpeg.AV_PKT_FLAG_KEY) != 0;
        if (_needKeyFrame || _codec is null)
        {
            if (!key)
            {
                ffmpeg.av_packet_unref(_packet);
                return;
            }

            _needKeyFrame = false;
        }

        if (key)
        {
            AVHWDeviceType device = WantedDevice(demand, out string? reason);
            if (_codec is null || device != _codecDevice)
            {
                OpenDecoder(device, reason, log: true);
            }
            else if (_hardwareFormat != AVPixelFormat.AV_PIX_FMT_NONE)
            {
                // The hardware session goes on under the current choice (the setting may have changed and changed back
                // since it was opened): a later failure counts in that generation and ends hardware for this connection.
                _sessionGeneration = _policy.Current.Generation;
            }
        }

        int sent = ffmpeg.avcodec_send_packet(_codec, _packet);
        ffmpeg.av_packet_unref(_packet);
        if (sent == ffmpeg.AVERROR_INVALIDDATA)
        {
            return;
        }

        if (sent < 0 && _hardwareFormat != AVPixelFormat.AV_PIX_FMT_NONE)
        {
            ThrowIfInterrupted();
            AbandonHardware("decoding", sent);
            return;
        }

        Check(sent, "Decoding the video failed");
    }

    /// <summary>
    /// Checks a frame of a hardware session (T-203). A software frame means FFmpeg could not set the hardware up and chose a
    /// software format (<c>get_format</c>): a hardware failure, but the frame is good and the session goes on in software
    /// until the next key frame reopens the decoder. Returns <see langword="false"/> when the frame is to be dropped.
    /// </summary>
    private bool AcceptHardwareFrame()
    {
        if (_frame->format != (int)_hardwareFormat)
        {
            AVHWDeviceType device = _codecDevice;
            ReportHardwareFailure(device, "starting", ErrorNotSupported);
            _hardwareFormat = AVPixelFormat.AV_PIX_FMT_NONE;
            return true;
        }

        _sessionHardwareFrames++;
        if (_policy.Faults is { FailDecodingAfterFrames: { } failAfter } && _sessionHardwareFrames > failAfter)
        {
            AbandonHardware("decoding", ffmpeg.AVERROR_EXTERNAL);
            return false;
        }

        return true;
    }

    /// <summary>
    /// The picture of the hardware frame in <c>_frame</c> in memory (T-203): mapped without a copy, or copied when mapping
    /// failed before in this session; <see langword="null"/> when that failed too (the hardware is then abandoned). The
    /// caller releases it after the conversion.
    /// </summary>
    private AVFrame* MapHardwareFrame()
    {
        if (_hardwarePicture is null)
        {
            _hardwarePicture = ffmpeg.av_frame_alloc();
            if (_hardwarePicture is null)
            {
                throw new CameraFeedException("Out of memory while decoding the camera stream.");
            }
        }

        if (!_transferFrames)
        {
            int mapped = _policy.Faults is { FailMapping: true }
                ? ErrorNotSupported
                : ffmpeg.av_hwframe_map(_hardwarePicture, _frame, (int)AvHwframeMap.AV_HWFRAME_MAP_READ);
            if (mapped >= 0)
            {
                Interlocked.Increment(ref _mappedFrames);
                return _hardwarePicture;
            }

            ffmpeg.av_frame_unref(_hardwarePicture);
            _transferFrames = true;
            HardwareDecodingLog.MappingFailed(_policy.Logger, _codecDevice, mapped);
        }

        int transferred = ffmpeg.av_hwframe_transfer_data(_hardwarePicture, _frame, 0);
        if (transferred >= 0)
        {
            // The copy carries only the pixels; the colour properties come from the decoded frame.
            transferred = ffmpeg.av_frame_copy_props(_hardwarePicture, _frame);
        }

        if (transferred < 0)
        {
            ffmpeg.av_frame_unref(_hardwarePicture);
            ThrowIfInterrupted();
            AbandonHardware("copying a frame", transferred);
            return null;
        }

        Interlocked.Increment(ref _transferredFrames);
        return _hardwarePicture;
    }

    /// <summary>The current packet as a borrowed <see cref="CameraPacket"/> over a reused managed buffer.</summary>
    private CameraPacket CopyPacket()
    {
        int size = Math.Max(_packet->size, 0);
        if (_packetBuffer.Length < size)
        {
            // Grows to the largest packet (a key frame) once; no allocation per packet afterwards.
            _packetBuffer = GC.AllocateUninitializedArray<byte>(Math.Max(size, _packetBuffer.Length * 2));
        }

        if (size > 0)
        {
            new ReadOnlySpan<byte>(_packet->data, size).CopyTo(_packetBuffer);
        }

        long? pts = _packet->pts == ffmpeg.AV_NOPTS_VALUE ? null : _packet->pts;
        long? dts = _packet->dts == ffmpeg.AV_NOPTS_VALUE ? null : _packet->dts;
        return new CameraPacket(
            new ReadOnlyMemory<byte>(_packetBuffer, 0, size),
            pts,
            dts,
            Math.Max(_packet->duration, 0),
            (_packet->flags & ffmpeg.AV_PKT_FLAG_KEY) != 0,
            _timeBase);
    }

    /// <summary>Releases every native object; idempotent, also after a failed <see cref="Open"/>.</summary>
    public void Dispose()
    {
        _full.Dispose();
        _display.Dispose();
        _grayscale.Dispose();

        // Also releases a hardware device (T-203).
        CloseDecoder();

        AVFrame* frame = _frame;
        ffmpeg.av_frame_free(&frame);
        _frame = null;

        AVFrame* hardwarePicture = _hardwarePicture;
        ffmpeg.av_frame_free(&hardwarePicture);
        _hardwarePicture = null;

        AVPacket* packet = _packet;
        ffmpeg.av_packet_free(&packet);
        _packet = null;

        if (_format is not null)
        {
            // May still call the interrupt callback, so the handle is released afterwards.
            AVFormatContext* format = _format;
            ffmpeg.avformat_close_input(&format);
            _format = null;
        }

        if (_interruptHandle.IsAllocated)
        {
            _interruptHandle.Free();
        }

        _holdsNativeResources = false;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int OnInterrupt(void* opaque)
    {
        // Must never throw into native code.
        return opaque is not null && GCHandle.FromIntPtr((nint)opaque).Target is InterruptFlag { IsSet: true } ? 1 : 0;
    }

    internal static string Describe(int error)
    {
        if (OperatingSystem.IsWindows() && DescribeWindowsNetworkError(-error) is { } network)
        {
            return network;
        }

        const int Size = 256;
        byte* text = stackalloc byte[Size];
        return ffmpeg.av_strerror(error, text, Size) == 0
            ? Marshal.PtrToStringUTF8((nint)text) ?? string.Create(CultureInfo.InvariantCulture, $"error {error}")
            : string.Create(CultureInfo.InvariantCulture, $"error {error}");
    }

    /// <summary>
    /// Network errors of the Windows FFmpeg build, which <c>av_strerror</c> does not know: FFmpeg reports them as
    /// AVERROR(errno) with either the C runtime's POSIX numbers (e.g. 138 for ETIMEDOUT) or Winsock numbers
    /// (WSAE*, libavformat/network.h maps some errno names to them).
    /// </summary>
    private static string? DescribeWindowsNetworkError(int code) => code switch
    {
        106 or 10053 => "Connection aborted",
        107 or 10061 => "Connection refused",
        108 or 10054 => "Connection reset by the camera",
        110 or 10065 => "Host unreachable",
        116 or 10050 => "Network is down",
        118 or 10051 => "Network unreachable",
        138 or 10060 => "Connection timed out",
        10064 => "Host is down",
        _ => null,
    };

    private void ThrowIfInterrupted()
    {
        if (_interrupt.IsSet)
        {
            throw new OperationCanceledException();
        }
    }

    /// <summary>
    /// Throws for a negative FFmpeg result: <see cref="OperationCanceledException"/> when the call was interrupted,
    /// otherwise <see cref="CameraFeedException"/> with FFmpeg's error text (which never contains the address).
    /// </summary>
    private void Check(int result, string stage)
    {
        if (result >= 0)
        {
            return;
        }

        ThrowIfInterrupted();
        throw new CameraFeedException($"{stage}: {Describe(result)}.") { IsHostUnreachable = IsHostUnreachable(result) };
    }

    /// <summary>
    /// Whether an FFmpeg result is AVERROR(EHOSTUNREACH), "no route to host", with the errno of the running OS (T-097: on
    /// macOS 15+ also what a denied Local Network permission looks like). Windows builds report the C runtime's number or
    /// the Winsock one (see <see cref="DescribeWindowsNetworkError"/>).
    /// </summary>
    internal static bool IsHostUnreachable(int result) => -result switch
    {
        65 when OperatingSystem.IsMacOS() => true,
        113 when OperatingSystem.IsLinux() => true,
        110 or 10065 when OperatingSystem.IsWindows() => true,
        _ => false,
    };

    /// <summary>
    /// Converts the decoded picture in <c>_frame</c> as <paramref name="demand"/> wants it, also a key frame decoded on its
    /// own (T-153): scaled down to fit its display size while that is due (T-184, T-185), in full resolution while that is
    /// due, and into grayscale while that is due (T-189: print failure detection gets no BGRA frame). An image that already
    /// fits the display size, or would get only a little smaller (T-195, <see cref="CameraDisplayScaler"/>), is converted
    /// once, in full resolution, for both. With nothing wanted (a lingering connection, a frame the mini preview skips)
    /// nothing is converted and the item carries no pixels; a hardware frame (T-203) is then not even mapped into memory.
    /// </summary>
    private CameraStreamItem Convert(CameraStreamDemand demand)
    {
        int width = _frame->width;
        int height = _frame->height;
        if (width <= 0 || height <= 0 || width > CameraFrameSize.MaxSide || height > CameraFrameSize.MaxSide)
        {
            throw new CameraFeedException("The camera sent an image of an unsupported size.");
        }

        // What is due, decided once per frame before any pixel is touched.
        bool full = demand.FullResolutionDue;
        CameraGrayscaleSize? grayscaleSize = demand.GrayscaleDue ? demand.GrayscaleSize : null;
        CameraFrameSize? displayFit = null;
        // T-185: a display reader with a frame rate limit (the mini preview) wants only some frames; the others are decoded
        // (the next ones depend on them) but not converted. A frame converted in full resolution anyway is converted for
        // display too, so that the display reader never gets the full frame in place of its own.
        if (demand.DisplaySize is { } displaySize && (full || demand.DisplayDue))
        {
            // From the decoded YUV picture, not from the full BGRA frame: the source is 1.5 instead of 4 bytes per pixel, the
            // full conversion is skipped whenever nobody needs it, and the colour handling is the same (see BgraConverter).
            // T-195: only when it is clearly smaller; otherwise the full conversion is cheaper and the renderer scales it.
            if (_displayScaler.Target(displaySize, width, height) is { } fit)
            {
                displayFit = fit;
            }
            else
            {
                full = true;
            }
        }

        if (!full && grayscaleSize is null && displayFit is null)
        {
            return CameraStreamItem.FromFrames(default, default, default);
        }

        AVFrame* source = _frame;
        if (_hardwareFormat != AVPixelFormat.AV_PIX_FMT_NONE && _frame->format == (int)_hardwareFormat)
        {
            // T-203: NV12 in memory shared with the hardware decoder; it goes through the same conversions as software frames.
            source = MapHardwareFrame();
            if (source is null)
            {
                return CameraStreamItem.FromFrames(default, default, default);
            }
        }

        try
        {
            return Convert(source, full, grayscaleSize, displayFit);
        }
        finally
        {
            if (source != _frame)
            {
                // Unlocks the mapped picture (or drops the copy) right away; the conversions copied what they need.
                ffmpeg.av_frame_unref(source);
            }
        }
    }

    private CameraStreamItem Convert(AVFrame* source, bool full, CameraGrayscaleSize? grayscaleSize, CameraFrameSize? displayFit)
    {
        AVPixelFormat format = (AVPixelFormat)source->format;
        bool fullRange = source->color_range == AVColorRange.AVCOL_RANGE_JPEG;
        // The deprecated "J" formats mean full range; swscale wants the plain format plus the range.
        switch (format)
        {
            case AVPixelFormat.AV_PIX_FMT_YUVJ420P:
                format = AVPixelFormat.AV_PIX_FMT_YUV420P;
                fullRange = true;
                break;
            case AVPixelFormat.AV_PIX_FMT_YUVJ422P:
                format = AVPixelFormat.AV_PIX_FMT_YUV422P;
                fullRange = true;
                break;
            case AVPixelFormat.AV_PIX_FMT_YUVJ444P:
                format = AVPixelFormat.AV_PIX_FMT_YUV444P;
                fullRange = true;
                break;
            case AVPixelFormat.AV_PIX_FMT_YUVJ440P:
                format = AVPixelFormat.AV_PIX_FMT_YUV440P;
                fullRange = true;
                break;
        }

        CameraGrayscaleFrame grayscaleFrame = grayscaleSize is { } gray
            // From the luma plane of the decoded picture, scaled straight to the detectors' size (see GrayscaleConverter).
            ? _grayscale.Convert(source, format, fullRange, gray.Width, gray.Height)
            : default;
        CameraFrame displayFrame = displayFit is { } fit ? _display.Convert(source, format, fullRange, fit.Width, fit.Height) : default;
        CameraFrame fullFrame = full ? _full.Convert(source, format, fullRange, source->width, source->height) : default;
        return CameraStreamItem.FromFrames(fullFrame, displayFrame, grayscaleFrame);
    }
}
