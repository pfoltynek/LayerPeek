using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

using FFmpeg.AutoGen.Abstractions;

using PrusaTrayDiagnostic.Core.Cameras;

namespace PrusaTrayDiagnostic.Camera.FFmpeg;

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
/// The native part of one camera connection: demuxer, H.264 decoder and BGRA conversion. Not thread-safe; every
/// member except <see cref="HoldsNativeResources"/> is called on the feed's decoder thread only. Blocking FFmpeg
/// calls return early once the <see cref="InterruptFlag"/> is set; they then throw <see cref="OperationCanceledException"/>
/// (the caller translates it). Failures throw <see cref="CameraFeedException"/> with a message free of the address.
/// </summary>
internal sealed unsafe class FFmpegDecoder : IDisposable
{
    /// <summary>Row and buffer alignment for the converted image (SIMD friendly for swscale).</summary>
    private const int Alignment = 64;

    /// <summary>Computed once: FFmpeg.AutoGen's <c>ffmpeg.AVERROR(int)</c> allocates on every call.</summary>
    private static readonly int ErrorAgain = ffmpeg.AVERROR(ffmpeg.EAGAIN);

    private readonly string _url;
    private readonly bool _isNetwork;
    private readonly TimeSpan _ioTimeout;
    private readonly InterruptFlag _interrupt;

    private GCHandle _interruptHandle;
    private AVFormatContext* _format;
    private AVCodecContext* _codec;
    private AVPacket* _packet;
    private AVFrame* _frame;
    private SwsContext* _scaler;
    private int _streamIndex = -1;
    private bool _draining;
    private volatile bool _holdsNativeResources;

    // Current conversion; the scaler and buffer are rebuilt only when one of these changes.
    private int _width;
    private int _height;
    private AVPixelFormat _sourceFormat = AVPixelFormat.AV_PIX_FMT_NONE;
    private bool _fullRange;
    private AVColorSpace _colorSpace;
    private int _stride;
    private byte[]? _buffer;
    private byte* _target;
    private ReadOnlyMemory<byte> _pixels;

    public FFmpegDecoder(string url, bool isNetwork, TimeSpan ioTimeout, InterruptFlag interrupt)
    {
        _url = url;
        _isNetwork = isNetwork;
        _ioTimeout = ioTimeout;
        _interrupt = interrupt;
    }

    /// <summary>Any native FFmpeg object is allocated. Safe to read from any thread.</summary>
    public bool HoldsNativeResources => _holdsNativeResources;

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
            if (_isNetwork)
            {
                // TCP interleaved: no UDP ports to open, works through firewalls, no packet loss.
                ffmpeg.av_dict_set(&options, "rtsp_transport", "tcp", 0);
                // Socket I/O timeout in microseconds; a backstop, callers usually cancel earlier.
                string microseconds = ((long)_ioTimeout.TotalMicroseconds).ToString(CultureInfo.InvariantCulture);
                ffmpeg.av_dict_set(&options, "timeout", microseconds, 0);
                // Low latency (decided in T-014): hand packets over as they arrive instead of buffering them.
                ffmpeg.av_dict_set(&options, "fflags", "nobuffer", 0);
            }

            // Frees the context and sets it to null on failure.
            int result = ffmpeg.avformat_open_input(&format, _url, null, &options);
            _format = format;
            Check(result, OpenStage);
        }
        finally
        {
            ffmpeg.av_dict_free(&options);
        }

        AVCodec* decoder = null;
        int stream = ffmpeg.AVERROR_STREAM_NOT_FOUND;
        if (_isNetwork)
        {
            // The RTSP session description (SDP) already names the codec and usually carries SPS/PPS as extradata;
            // that is all the decoder needs. avformat_find_stream_info would decode about a second of video first
            // and then hand it over as a backlog, which delays the live picture permanently (measured: ~1.2 s).
            stream = ffmpeg.av_find_best_stream(_format, AVMediaType.AVMEDIA_TYPE_VIDEO, -1, -1, &decoder, 0);
        }

        if (stream < 0)
        {
            // Files, and streams whose description is not enough: probe the content (robust, but buffers).
            Check(ffmpeg.avformat_find_stream_info(_format, null), OpenStage);
            stream = ffmpeg.av_find_best_stream(_format, AVMediaType.AVMEDIA_TYPE_VIDEO, -1, -1, &decoder, 0);
        }

        if (stream == ffmpeg.AVERROR_STREAM_NOT_FOUND)
        {
            ThrowIfInterrupted();
            throw new CameraFeedException("The stream contains no video.");
        }

        if (stream == ffmpeg.AVERROR_DECODER_NOT_FOUND)
        {
            throw new CameraFeedException("The video format of the stream is not supported.");
        }

        Check(stream, OpenStage);
        _streamIndex = stream;
        for (int i = 0; i < _format->nb_streams; i++)
        {
            if (i != stream)
            {
                _format->streams[i]->discard = AVDiscard.AVDISCARD_ALL;
            }
        }

        _codec = ffmpeg.avcodec_alloc_context3(decoder);
        _packet = ffmpeg.av_packet_alloc();
        _frame = ffmpeg.av_frame_alloc();
        if (_codec is null || _packet is null || _frame is null)
        {
            throw new CameraFeedException("Out of memory while opening the camera stream.");
        }

        const string DecoderStage = "Starting the video decoder failed";
        Check(ffmpeg.avcodec_parameters_to_context(_codec, _format->streams[stream]->codecpar), DecoderStage);
        // Every frame leaves the decoder as soon as it is complete: no frame threading (it delays output by one frame
        // per thread), low-delay mode. A 720p Baseline stream decodes easily on one thread.
        _codec->flags |= ffmpeg.AV_CODEC_FLAG_LOW_DELAY;
        _codec->thread_count = 1;

        Check(ffmpeg.avcodec_open2(_codec, decoder, null), DecoderStage);
    }

    /// <summary>
    /// Decodes the next frame into the reused buffer. Returns <see langword="false"/> at the end of the stream. The
    /// returned frame stays valid until the next call (or until the buffer is replaced after a size change; the old
    /// buffer is then left to the garbage collector, so a consumer holding on to it never sees freed memory).
    /// </summary>
    public bool TryReadFrame(out CameraFrame frame)
    {
        while (true)
        {
            ThrowIfInterrupted();
            int received = ffmpeg.avcodec_receive_frame(_codec, _frame);
            if (received == 0)
            {
                frame = Convert();
                return true;
            }

            if (received == ffmpeg.AVERROR_EOF)
            {
                frame = default;
                return false;
            }

            // A damaged frame (INVALIDDATA) is skipped like a missing one: live streams recover with the next key frame.
            if (received != ErrorAgain && received != ffmpeg.AVERROR_INVALIDDATA)
            {
                Check(received, "Decoding the video failed");
            }

            if (_draining)
            {
                frame = default;
                return false;
            }

            int read = ffmpeg.av_read_frame(_format, _packet);
            if (read == ffmpeg.AVERROR_EOF)
            {
                ThrowIfInterrupted();
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

            int sent = ffmpeg.avcodec_send_packet(_codec, _packet);
            ffmpeg.av_packet_unref(_packet);
            if (sent != ffmpeg.AVERROR_INVALIDDATA)
            {
                Check(sent, "Decoding the video failed");
            }
        }
    }

    /// <summary>Releases every native object; idempotent, also after a failed <see cref="Open"/>.</summary>
    public void Dispose()
    {
        if (_scaler is not null)
        {
            ffmpeg.sws_freeContext(_scaler);
            _scaler = null;
        }

        AVFrame* frame = _frame;
        ffmpeg.av_frame_free(&frame);
        _frame = null;

        AVPacket* packet = _packet;
        ffmpeg.av_packet_free(&packet);
        _packet = null;

        AVCodecContext* codec = _codec;
        ffmpeg.avcodec_free_context(&codec);
        _codec = null;

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

    private static string Describe(int error)
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

    private static int AlignUp(int value) => (value + (Alignment - 1)) & ~(Alignment - 1);

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
        throw new CameraFeedException($"{stage}: {Describe(result)}.");
    }

    private CameraFrame Convert()
    {
        int width = _frame->width;
        int height = _frame->height;
        AVPixelFormat format = (AVPixelFormat)_frame->format;
        bool fullRange = _frame->color_range == AVColorRange.AVCOL_RANGE_JPEG;
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

        if (_scaler is null || width != _width || height != _height || format != _sourceFormat
            || fullRange != _fullRange || _frame->colorspace != _colorSpace)
        {
            Configure(width, height, format, fullRange, _frame->colorspace);
        }

        // BGRA has one plane; swscale reads up to four entries.
        byte** target = stackalloc byte*[4] { _target, null, null, null };
        int* targetStride = stackalloc int[4] { _stride, 0, 0, 0 };
        // AVFrame.data / linesize are inline arrays of 8 pointers / ints.
        int rows = NativeMethods.Scale(_scaler, (byte**)&_frame->data, (int*)&_frame->linesize, 0, height, target, targetStride);
        if (rows != height)
        {
            throw new CameraFeedException("Converting the video image failed.");
        }

        return new CameraFrame(width, height, _stride, _pixels);
    }

    private void Configure(int width, int height, AVPixelFormat format, bool fullRange, AVColorSpace colorSpace)
    {
        if (width <= 0 || height <= 0 || width > 16384 || height > 16384)
        {
            throw new CameraFeedException("The camera sent an image of an unsupported size.");
        }

        _scaler = ffmpeg.sws_getCachedContext(
            _scaler, width, height, format, width, height, AVPixelFormat.AV_PIX_FMT_BGRA, (int)SwsFlags.SWS_BILINEAR, null, null, null);
        if (_scaler is null)
        {
            throw new CameraFeedException("The video image format is not supported.");
        }

        // YUV -> RGB coefficients of the stream (BT.601 unless it says BT.709) and its range; RGB output is full range.
        int coefficients = colorSpace == AVColorSpace.AVCOL_SPC_BT709 ? ffmpeg.SWS_CS_ITU709 : ffmpeg.SWS_CS_DEFAULT;
        int4 table = *(int4*)ffmpeg.sws_getCoefficients(coefficients);
        // Returns -1 only for unsupported conversions; the default coefficients are then kept, which is acceptable.
        _ = ffmpeg.sws_setColorspaceDetails(_scaler, in table, fullRange ? 1 : 0, in table, 1, 0, 1 << 16, 1 << 16);

        int stride = AlignUp(width * CameraFrame.BytesPerPixel);
        int length = checked(stride * height);
        if (_buffer is null || _buffer.Length < length + Alignment)
        {
            // Pinned (POH): FFmpeg writes through a raw pointer and the address must never move.
            _buffer = GC.AllocateUninitializedArray<byte>(length + Alignment, pinned: true);
        }

        byte* start = (byte*)Unsafe.AsPointer(ref MemoryMarshal.GetArrayDataReference(_buffer));
        int offset = (int)((Alignment - ((nuint)start % Alignment)) % Alignment);
        _target = start + offset;
        _pixels = new ReadOnlyMemory<byte>(_buffer, offset, length);

        _width = width;
        _height = height;
        _sourceFormat = format;
        _fullRange = fullRange;
        _colorSpace = colorSpace;
        _stride = stride;
    }
}
