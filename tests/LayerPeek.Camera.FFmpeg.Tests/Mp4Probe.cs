using System.Security.Cryptography;

using FFmpeg.AutoGen.Abstractions;

namespace LayerPeek.Camera.FFmpeg.Tests;

/// <summary>What <see cref="Mp4Probe.Read"/> found in a recording.</summary>
internal sealed record Mp4Contents(
    string FormatName,
    int Streams,
    AVCodecID Codec,
    int Width,
    int Height,
    int Packets,
    int KeyFrames,
    bool StartsWithKeyFrame,
    bool TimestampsIncrease,
    int DecodedFrames,
    int DecodeErrors,
    double DurationSeconds,
    long PacketBytes);

/// <summary>One decoded frame: whether the decoder marked it a key frame, and a digest of its pixels (like <c>framemd5</c>).</summary>
internal readonly record struct DecodedFrame(bool KeyFrame, string Digest);

/// <summary>
/// Opens a recording with FFmpeg's demuxer and decodes every frame (T-144): the file is valid, starts with a key frame,
/// its timestamps increase and every frame decodes. Test-only; in the app nothing ever reads recordings back.
/// </summary>
internal static unsafe class Mp4Probe
{
    /// <param name="strict">Any decoding error fails the frame (like <c>ffmpeg -err_detect explode</c>).</param>
    /// <param name="frames">Receives every decoded frame, in output order.</param>
    public static Mp4Contents Read(string path, bool strict = false, List<DecodedFrame>? frames = null)
    {
        FFmpegRuntime.EnsureLoaded();
        AVFormatContext* format = null;
        AVCodecContext* codec = null;
        AVPacket* packet = ffmpeg.av_packet_alloc();
        AVFrame* frame = ffmpeg.av_frame_alloc();
        try
        {
            Check(ffmpeg.avformat_open_input(&format, "file:" + path, null, null), "open");
            Check(ffmpeg.avformat_find_stream_info(format, null), "probe");
            int index = ffmpeg.av_find_best_stream(format, AVMediaType.AVMEDIA_TYPE_VIDEO, -1, -1, null, 0);
            Check(index, "find the video stream");
            AVStream* stream = format->streams[index];
            AVCodecParameters* parameters = stream->codecpar;
            AVCodec* decoder = ffmpeg.avcodec_find_decoder(parameters->codec_id);
            Assert.True(decoder is not null, "No decoder for the recorded codec.");
            codec = ffmpeg.avcodec_alloc_context3(decoder);
            Check(ffmpeg.avcodec_parameters_to_context(codec, parameters), "decoder parameters");
            if (strict)
            {
                codec->err_recognition |= ffmpeg.AV_EF_EXPLODE | ffmpeg.AV_EF_BITSTREAM | ffmpeg.AV_EF_BUFFER;
            }

            Check(ffmpeg.avcodec_open2(codec, decoder, null), "open the decoder");

            int packets = 0;
            int keyFrames = 0;
            int decoded = 0;
            int errors = 0;
            long bytes = 0;
            bool startsWithKey = false;
            bool increasing = true;
            long lastDts = long.MinValue;
            while (ffmpeg.av_read_frame(format, packet) >= 0)
            {
                if (packet->stream_index == index)
                {
                    bool key = (packet->flags & ffmpeg.AV_PKT_FLAG_KEY) != 0;
                    if (packets++ == 0)
                    {
                        startsWithKey = key;
                    }

                    keyFrames += key ? 1 : 0;
                    bytes += packet->size;
                    increasing &= packet->dts != ffmpeg.AV_NOPTS_VALUE && packet->dts > lastDts;
                    lastDts = packet->dts;
                    errors += ffmpeg.avcodec_send_packet(codec, packet) < 0 ? 1 : 0;
                    decoded += Drain(codec, frame, ref errors, frames);
                }

                ffmpeg.av_packet_unref(packet);
            }

            _ = ffmpeg.avcodec_send_packet(codec, null);
            decoded += Drain(codec, frame, ref errors, frames);
            double duration = format->duration > 0 ? format->duration / (double)ffmpeg.AV_TIME_BASE : 0;
            string name = System.Runtime.InteropServices.Marshal.PtrToStringUTF8((nint)format->iformat->name) ?? "";
            return new Mp4Contents(name, (int)format->nb_streams, parameters->codec_id, parameters->width, parameters->height, packets, keyFrames, startsWithKey, increasing, decoded, errors, duration, bytes);
        }
        finally
        {
            ffmpeg.avcodec_free_context(&codec);
            ffmpeg.av_frame_free(&frame);
            ffmpeg.av_packet_free(&packet);
            ffmpeg.avformat_close_input(&format);
        }
    }

    /// <summary>Checks a recording: MP4, one H.264 video stream of the Buddy3D's size, key frame first, increasing timestamps, every frame decodes.</summary>
    public static Mp4Contents AssertValid(string path, int minimumFrames)
    {
        Mp4Contents contents = Read(path);
        Assert.Contains("mp4", contents.FormatName, StringComparison.Ordinal);
        Assert.Equal(1, contents.Streams);
        Assert.Equal(AVCodecID.AV_CODEC_ID_H264, contents.Codec);
        Assert.Equal((TestFixtures.Buddy3DWidth, TestFixtures.Buddy3DHeight), (contents.Width, contents.Height));
        Assert.True(contents.Packets >= minimumFrames, $"{contents.Packets} packets, expected at least {minimumFrames}.");
        Assert.True(contents.StartsWithKeyFrame, "A recording starts with a key frame.");
        Assert.True(contents.TimestampsIncrease, "Timestamps must increase.");
        Assert.Equal(0, contents.DecodeErrors);
        Assert.Equal(contents.Packets, contents.DecodedFrames);
        return contents;
    }

    private static int Drain(AVCodecContext* codec, AVFrame* frame, ref int errors, List<DecodedFrame>? frames)
    {
        int count = 0;
        while (true)
        {
            int result = ffmpeg.avcodec_receive_frame(codec, frame);
            if (result < 0)
            {
                errors += result == ffmpeg.AVERROR(ffmpeg.EAGAIN) || result == ffmpeg.AVERROR_EOF ? 0 : 1;
                return count;
            }

            count++;
            frames?.Add(new DecodedFrame((frame->flags & ffmpeg.AV_FRAME_FLAG_KEY) != 0, Digest(frame)));
        }
    }

    /// <summary>SHA-256 of the visible pixels of a planar 4:2:0 frame (the Buddy3D's format), row by row without padding.</summary>
    private static string Digest(AVFrame* frame)
    {
        AVPixelFormat format = (AVPixelFormat)frame->format;
        Assert.True(format is AVPixelFormat.AV_PIX_FMT_YUV420P or AVPixelFormat.AV_PIX_FMT_YUVJ420P, $"Unexpected pixel format {format}.");
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        for (uint plane = 0; plane < 3; plane++)
        {
            int width = plane == 0 ? frame->width : (frame->width + 1) / 2;
            int height = plane == 0 ? frame->height : (frame->height + 1) / 2;
            for (int row = 0; row < height; row++)
            {
                hash.AppendData(new ReadOnlySpan<byte>(frame->data[plane] + ((long)row * frame->linesize[plane]), width));
            }
        }

        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private static void Check(int result, string stage)
    {
        if (result < 0)
        {
            Assert.Fail($"Reading the recording failed ({stage}): {FFmpegDecoder.Describe(result)}.");
        }
    }
}
