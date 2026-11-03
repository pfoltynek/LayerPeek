using FFmpeg.AutoGen.Abstractions;

using LayerPeek.Core.Cameras;
using LayerPeek.Core.Cameras.Recording;

namespace LayerPeek.Camera.FFmpeg;

/// <summary>
/// Recording files written by FFmpeg (T-144): fragmented MP4 (<c>movflags=frag_keyframe+empty_moov</c>, user decision
/// 2026-11-03), video only, the H.264 packets as they come from the camera (remux, no encoder is ever opened).
/// </summary>
/// <remarks>
/// Fragmented MP4 is readable after a crash up to the last complete fragment (one key frame interval, ~2.3 s on the
/// Buddy3D): the header (<c>moov</c>) is written first, every key frame closes a fragment, and every fragment is flushed
/// to the operating system at once (<c>flush_packets</c>). The macOS build contains this muxer since T-147.
/// </remarks>
public sealed class FFmpegCameraRecordingWriterFactory : ICameraRecordingWriterFactory
{
    internal const string Mp4Muxer = "mp4";

    private readonly string _muxer;
    private readonly Lazy<CameraRecorderAvailability> _availability;

    public FFmpegCameraRecordingWriterFactory()
        : this(Mp4Muxer)
    {
    }

    /// <param name="muxer">The muxer's short name; tests name a missing one to simulate a build without it.</param>
    internal FFmpegCameraRecordingWriterFactory(string muxer)
    {
        ArgumentException.ThrowIfNullOrEmpty(muxer);
        _muxer = muxer;
        _availability = new(CheckAvailability, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    public CameraRecorderAvailability Availability => _availability.Value;

    public ICameraRecordingWriter Create(string path, CameraStreamInfo info, ReadOnlySpan<byte> firstKeyFrame)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentNullException.ThrowIfNull(info);
        if (Availability is { IsAvailable: false, Reason: var reason })
        {
            throw new CameraRecordingException(CameraRecordingFailure.Unavailable, reason!);
        }

        return FFmpegRecordingWriter.Open(path, info, firstKeyFrame, _muxer);
    }

    private CameraRecorderAvailability CheckAvailability()
    {
        try
        {
            return FFmpegRuntime.HasMuxer(_muxer)
                ? CameraRecorderAvailability.Available
                : CameraRecorderAvailability.Unavailable("The video library (FFmpeg) has no MP4 muxer.");
        }
        catch (CameraFeedException)
        {
            return CameraRecorderAvailability.Unavailable("The video library (FFmpeg) is not available.");
        }
    }
}

/// <summary>One open recording file (see <see cref="FFmpegCameraRecordingWriterFactory"/>). One thread at a time.</summary>
internal sealed unsafe class FFmpegRecordingWriter : ICameraRecordingWriter
{
    /// <summary>ENOSPC; the same number on macOS, Linux and in the Windows C runtime.</summary>
    private const int NoSpaceError = 28;

    private const string NalStartCode = "\0\0\0\u0001";

    private AVFormatContext* _format;
    private AVPacket* _packet;
    private AVRational _inputTimeBase;
    private AVRational _outputTimeBase;
    private long _lastDts = long.MinValue;
    private long _bytes;
    private bool _completed;

    private FFmpegRecordingWriter()
    {
    }

    public long BytesWritten => Volatile.Read(ref _bytes);

    /// <summary>Whether a native FFmpeg object is still allocated (for tests).</summary>
    internal bool HoldsNativeResources => _format is not null || _packet is not null;

    /// <exception cref="CameraRecordingException">See <see cref="ICameraRecordingWriterFactory.Create"/>.</exception>
    public static FFmpegRecordingWriter Open(string path, CameraStreamInfo info, ReadOnlySpan<byte> firstKeyFrame, string muxer)
    {
        FFmpegRecordingWriter writer = new();
        try
        {
            writer.OpenCore(path, info, firstKeyFrame, muxer);
            return writer;
        }
        catch
        {
            writer.Dispose();
            throw;
        }
    }

    public void Write(ReadOnlySpan<byte> data, long pts, long dts, long duration, bool isKeyFrame)
    {
        ObjectDisposedException.ThrowIf(_format is null || _completed, this);
        long outDts = Rescale(dts);
        // Rounding into the muxer's time base must never make two packets equal (the muxer rejects that).
        if (outDts <= _lastDts)
        {
            outDts = _lastDts + 1;
        }

        _lastDts = outDts;
        long outPts = Math.Max(Rescale(pts), outDts);
        int result;
        fixed (byte* bytes = data)
        {
            // Not reference counted: av_write_frame neither copies nor keeps it (the MP4 muxer copies into its fragment).
            _packet->data = bytes;
            _packet->size = data.Length;
            _packet->pts = outPts;
            _packet->dts = outDts;
            _packet->duration = duration > 0 ? Rescale(duration) : 0;
            _packet->flags = isKeyFrame ? ffmpeg.AV_PKT_FLAG_KEY : 0;
            _packet->stream_index = 0;
            _packet->pos = -1;
            result = ffmpeg.av_write_frame(_format, _packet);
            _packet->data = null;
            _packet->size = 0;
        }

        Check(result, "Writing the recording failed");
        UpdateBytes();
    }

    public void Complete()
    {
        ObjectDisposedException.ThrowIf(_format is null, this);
        if (_completed)
        {
            return;
        }

        _completed = true;
        int trailer = ffmpeg.av_write_trailer(_format);
        UpdateBytes();
        // Closing flushes what is left; its result counts too (e.g. the disk filled up with the last fragment).
        int closed = ffmpeg.avio_closep(&_format->pb);
        Check(trailer, "Completing the recording failed");
        Check(closed, "Completing the recording failed");
    }

    public void Dispose()
    {
        if (_format is not null)
        {
            if (_format->pb is not null)
            {
                _ = ffmpeg.avio_closep(&_format->pb);
            }

            ffmpeg.avformat_free_context(_format);
            _format = null;
        }

        AVPacket* packet = _packet;
        ffmpeg.av_packet_free(&packet);
        _packet = null;
    }

    /// <summary>
    /// The SPS and PPS NAL units of an Annex B access unit, as Annex B (the camera sends them in-band before every key
    /// frame when the session description has none, like the simulator); empty when there are none.
    /// </summary>
    internal static byte[] ExtractParameterSets(ReadOnlySpan<byte> annexB)
    {
        List<byte>? sets = null;
        int position = 0;
        while (NextNal(annexB, ref position, out ReadOnlySpan<byte> nal))
        {
            int type = nal.Length > 0 ? nal[0] & 0x1F : 0;
            // 7: sequence parameter set, 8: picture parameter set.
            if (type is 7 or 8)
            {
                sets ??= [];
                foreach (char c in NalStartCode)
                {
                    sets.Add((byte)c);
                }

                sets.AddRange(nal);
            }
        }

        return sets is null ? [] : [.. sets];
    }

    /// <summary>The next NAL unit after a 3- or 4-byte start code from <paramref name="position"/>, without the start code.</summary>
    private static bool NextNal(ReadOnlySpan<byte> data, ref int position, out ReadOnlySpan<byte> nal)
    {
        int start = FindStartCode(data, position, out int codeLength);
        if (start < 0)
        {
            nal = default;
            return false;
        }

        int begin = start + codeLength;
        int next = FindStartCode(data, begin, out _);
        int end = next < 0 ? data.Length : next;
        // A 4-byte start code of the next unit leaves its leading zero here; trailing zeros are not part of a NAL unit.
        while (end > begin && data[end - 1] == 0)
        {
            end--;
        }

        nal = data[begin..end];
        position = next < 0 ? data.Length : next;
        return true;
    }

    private static int FindStartCode(ReadOnlySpan<byte> data, int from, out int length)
    {
        for (int i = from; i + 2 < data.Length; i++)
        {
            if (data[i] == 0 && data[i + 1] == 0)
            {
                if (data[i + 2] == 1)
                {
                    length = 3;
                    return i;
                }

                if (data[i + 2] == 0 && i + 3 < data.Length && data[i + 3] == 1)
                {
                    length = 4;
                    return i;
                }
            }
        }

        length = 0;
        return -1;
    }

    private void OpenCore(string path, CameraStreamInfo info, ReadOnlySpan<byte> firstKeyFrame, string muxer)
    {
        try
        {
            FFmpegRuntime.EnsureLoaded();
        }
        catch (CameraFeedException ex)
        {
            throw new CameraRecordingException(CameraRecordingFailure.Unavailable, "The video library (FFmpeg) is not available.", ex);
        }

        if (info.Codec != CameraVideoCodec.H264)
        {
            throw new CameraRecordingException(CameraRecordingFailure.StreamFailed, "The video format of the stream cannot be recorded.");
        }

        AVFormatContext* format = null;
        if (ffmpeg.avformat_alloc_output_context2(&format, null, muxer, null) < 0 || format is null)
        {
            throw new CameraRecordingException(CameraRecordingFailure.Unavailable, "The video library (FFmpeg) has no MP4 muxer.");
        }

        _format = format;
        _packet = ffmpeg.av_packet_alloc();
        AVStream* stream = ffmpeg.avformat_new_stream(format, null);
        if (_packet is null || stream is null)
        {
            throw new CameraRecordingException("Out of memory while starting the recording.");
        }

        // The SPS/PPS of the session description; without them (the simulator, cameras that send them in-band only) the
        // ones in front of the first key frame. The MP4 muxer turns Annex B into its avcC box and length prefixes itself.
        ReadOnlySpan<byte> extradata = info.Extradata.Length > 0 ? info.Extradata.Span : ExtractParameterSets(firstKeyFrame);
        if (extradata.IsEmpty)
        {
            throw new CameraRecordingException(CameraRecordingFailure.StreamFailed, "The camera stream has no H.264 parameter sets (SPS/PPS) to record.");
        }

        AVCodecParameters* parameters = stream->codecpar;
        parameters->codec_type = AVMediaType.AVMEDIA_TYPE_VIDEO;
        parameters->codec_id = AVCodecID.AV_CODEC_ID_H264;
        parameters->extradata = (byte*)ffmpeg.av_mallocz((ulong)(extradata.Length + ffmpeg.AV_INPUT_BUFFER_PADDING_SIZE));
        if (parameters->extradata is null)
        {
            throw new CameraRecordingException("Out of memory while starting the recording.");
        }

        extradata.CopyTo(new Span<byte>(parameters->extradata, extradata.Length));
        parameters->extradata_size = extradata.Length;
        (int width, int height) = info is { Width: > 0, Height: > 0 } ? (info.Width, info.Height) : ParseSize(extradata, firstKeyFrame);
        parameters->width = width;
        parameters->height = height;
        _inputTimeBase = new AVRational { num = info.TimeBase.Numerator, den = info.TimeBase.Denominator };
        stream->time_base = _inputTimeBase;

        // Explicit protocol: no part of the path is ever taken for another protocol (e.g. a drive letter).
        Check(ffmpeg.avio_open(&format->pb, "file:" + path, ffmpeg.AVIO_FLAG_WRITE), "Creating the recording file failed");

        AVDictionary* options = null;
        try
        {
            // User decision 2026-11-03 (T-144): fragmented MP4, readable after a crash up to the last fragment.
            ffmpeg.av_dict_set(&options, "movflags", "frag_keyframe+empty_moov", 0);
            // Every completed fragment goes to the operating system at once instead of waiting in FFmpeg's buffer.
            ffmpeg.av_dict_set(&options, "flush_packets", "1", 0);
            Check(ffmpeg.avformat_write_header(format, &options), "Starting the recording file failed");
        }
        finally
        {
            ffmpeg.av_dict_free(&options);
        }

        // The muxer may have chosen another time base for the stream.
        _outputTimeBase = stream->time_base;
        UpdateBytes();
    }

    /// <summary>Image size from the SPS by FFmpeg's H.264 parser (the RTSP session description usually does not state it).</summary>
    private static (int Width, int Height) ParseSize(ReadOnlySpan<byte> extradata, ReadOnlySpan<byte> keyFrame)
    {
        AVCodecParserContext* parser = ffmpeg.av_parser_init(AVCodecID.AV_CODEC_ID_H264);
        AVCodecContext* context = ffmpeg.avcodec_alloc_context3(null);
        byte* buffer = (byte*)ffmpeg.av_mallocz((ulong)(keyFrame.Length + ffmpeg.AV_INPUT_BUFFER_PADDING_SIZE));
        try
        {
            if (parser is null || context is null || buffer is null)
            {
                return (0, 0);
            }

            // The parser reads the codec parameters from the context first (Annex B or avcC), then the access unit.
            context->extradata = (byte*)ffmpeg.av_mallocz((ulong)(extradata.Length + ffmpeg.AV_INPUT_BUFFER_PADDING_SIZE));
            if (context->extradata is not null)
            {
                extradata.CopyTo(new Span<byte>(context->extradata, extradata.Length));
                context->extradata_size = extradata.Length;
            }

            keyFrame.CopyTo(new Span<byte>(buffer, keyFrame.Length));
            parser->flags |= ffmpeg.PARSER_FLAG_COMPLETE_FRAMES;
            byte* output = null;
            int outputSize = 0;
            _ = ffmpeg.av_parser_parse2(parser, context, &output, &outputSize, buffer, keyFrame.Length, ffmpeg.AV_NOPTS_VALUE, ffmpeg.AV_NOPTS_VALUE, 0);
            return parser->width > 0 && parser->height > 0 ? (parser->width, parser->height) : (0, 0);
        }
        finally
        {
            if (parser is not null)
            {
                ffmpeg.av_parser_close(parser);
            }

            // Frees the extradata too.
            ffmpeg.avcodec_free_context(&context);
            ffmpeg.av_free(buffer);
        }
    }

    private long Rescale(long value) =>
        ffmpeg.av_rescale_q_rnd(value, _inputTimeBase, _outputTimeBase, AVRounding.AV_ROUND_NEAR_INF | AVRounding.AV_ROUND_PASS_MINMAX);

    private void UpdateBytes()
    {
        if (_format is not null && _format->pb is not null)
        {
            Volatile.Write(ref _bytes, ffmpeg.avio_tell(_format->pb));
        }
    }

    private static void Check(int result, string stage)
    {
        if (result >= 0)
        {
            return;
        }

        throw new CameraRecordingException(
            result == -NoSpaceError ? CameraRecordingFailure.DiskFull : CameraRecordingFailure.WriteFailed,
            $"{stage}: {FFmpegDecoder.Describe(result)}.");
    }
}
