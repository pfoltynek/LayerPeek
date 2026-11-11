using FFmpeg.AutoGen.Abstractions;

using LayerPeek.Core.Cameras;

namespace LayerPeek.Camera.FFmpeg.Tests;

/// <summary>
/// T-189: the grayscale samples of print failure detection come straight from the luma plane, always in full range, so
/// that the detectors' brightness thresholds mean the same as for luma computed from the full-range BGRA frame.
/// </summary>
public sealed unsafe class GrayscaleConverterTests
{
    private const int Width = 64;
    private const int Height = 32;

    /// <summary>A YUV 4:2:0 picture: luma <paramref name="left"/> in the left half, <paramref name="right"/> in the right half, saturated chroma.</summary>
    private static AVFrame* Picture(byte left, byte right, int width = Width, int height = Height)
    {
        FFmpegRuntime.EnsureLoaded();
        AVFrame* frame = ffmpeg.av_frame_alloc();
        frame->width = width;
        frame->height = height;
        frame->format = (int)AVPixelFormat.AV_PIX_FMT_YUV420P;
        Assert.Equal(0, ffmpeg.av_frame_get_buffer(frame, 0));
        for (int y = 0; y < height; y++)
        {
            Span<byte> row = new(frame->data[0] + (y * frame->linesize[0]), width);
            row[..(width / 2)].Fill(left);
            row[(width / 2)..].Fill(right);
        }

        // Strong colour: luma from the Y plane must not depend on it.
        for (int y = 0; y < height / 2; y++)
        {
            new Span<byte>(frame->data[1] + (y * frame->linesize[1]), width / 2).Fill(30);
            new Span<byte>(frame->data[2] + (y * frame->linesize[2]), width / 2).Fill(220);
        }

        return frame;
    }

    private static void Free(AVFrame* frame) => ffmpeg.av_frame_free(&frame);

    private static (byte Left, byte Right) Halves(in CameraGrayscaleFrame gray)
    {
        ReadOnlySpan<byte> pixels = gray.Pixels.Span;
        int row = gray.Height / 2 * gray.Stride;
        // Away from the edge between the halves (area averaging mixes the column at the edge).
        return (pixels[row + 1], pixels[row + gray.Width - 2]);
    }

    [FFmpegTheory]
    [InlineData(32, 16)]
    [InlineData(Width, Height)]
    public void LimitedRangeLumaIsExpandedToFullRange(int width, int height)
    {
        AVFrame* frame = Picture(16, 235);
        using GrayscaleConverter converter = new();
        try
        {
            CameraGrayscaleFrame gray = converter.Convert(frame, AVPixelFormat.AV_PIX_FMT_YUV420P, fullRange: false, width, height);

            Assert.Equal((width, height, Width, Height), (gray.Width, gray.Height, gray.SourceWidth, gray.SourceHeight));
            (byte left, byte right) = Halves(gray);
            Assert.InRange(left, 0, 1);
            Assert.InRange(right, 254, 255);
        }
        finally
        {
            Free(frame);
        }
    }

    /// <summary>
    /// Semi-planar (luma plane alone) and 10-bit (converted as a whole) pictures of odd and equal sizes: limited-range
    /// black and white become 0 and 255 there too.
    /// </summary>
    [FFmpegTheory]
    [InlineData(AVPixelFormat.AV_PIX_FMT_NV12, 1281, 721)]
    [InlineData(AVPixelFormat.AV_PIX_FMT_NV12, 32, 16)]
    [InlineData(AVPixelFormat.AV_PIX_FMT_YUV420P10LE, 1281, 721)]
    [InlineData(AVPixelFormat.AV_PIX_FMT_YUV420P10LE, 32, 16)]
    public void OtherLayoutsAreExpandedToFullRange(AVPixelFormat format, int sourceWidth, int sourceHeight)
    {
        FFmpegRuntime.EnsureLoaded();
        bool wide = format == AVPixelFormat.AV_PIX_FMT_YUV420P10LE;
        using GrayscaleConverter converter = new();
        foreach ((int luma, int expected) in new[] { (16, 0), (235, 255) })
        {
            AVFrame* frame = ffmpeg.av_frame_alloc();
            frame->width = sourceWidth;
            frame->height = sourceHeight;
            frame->format = (int)format;
            Assert.Equal(0, ffmpeg.av_frame_get_buffer(frame, 0));
            try
            {
                for (uint plane = 0; plane < 3 && frame->data[plane] != null; plane++)
                {
                    // Neutral chroma; plane 0 the luma (10-bit: four times the 8-bit level).
                    int rows = plane == 0 ? sourceHeight : (sourceHeight + 1) / 2;
                    int samples = plane == 0 ? sourceWidth : format == AVPixelFormat.AV_PIX_FMT_NV12 ? (sourceWidth + 1) / 2 * 2 : (sourceWidth + 1) / 2;
                    int value = plane == 0 ? luma : 128;
                    for (int y = 0; y < rows; y++)
                    {
                        byte* row = frame->data[plane] + (y * frame->linesize[plane]);
                        if (wide)
                        {
                            new Span<ushort>(row, samples).Fill((ushort)(value * 4));
                        }
                        else
                        {
                            new Span<byte>(row, samples).Fill((byte)value);
                        }
                    }
                }

                CameraGrayscaleFrame gray = converter.Convert(frame, format, fullRange: false, 32, 16);

                for (int y = 0; y < gray.Height; y++)
                {
                    foreach (byte pixel in gray.Pixels.Span.Slice(y * gray.Stride, gray.Width))
                    {
                        Assert.InRange(pixel, Math.Max(expected - 1, 0), Math.Min(expected + 1, 255));
                    }
                }
            }
            finally
            {
                Free(frame);
            }
        }
    }

    [FFmpegFact]
    public void FullRangeLumaIsKept()
    {
        AVFrame* frame = Picture(16, 200);
        using GrayscaleConverter converter = new();
        try
        {
            CameraGrayscaleFrame gray = converter.Convert(frame, AVPixelFormat.AV_PIX_FMT_YUV420P, fullRange: true, 32, 16);

            Assert.Equal(((byte)16, (byte)200), Halves(gray));
        }
        finally
        {
            Free(frame);
        }
    }

    [FFmpegFact]
    public void MidGrayKeepsItsMeaningInBothRanges()
    {
        // Y 126 limited is (126 - 16) * 255 / 219 = 128 full: the same mid gray the BGRA frame shows.
        AVFrame* frame = Picture(126, 126);
        using GrayscaleConverter converter = new();
        try
        {
            CameraGrayscaleFrame gray = converter.Convert(frame, AVPixelFormat.AV_PIX_FMT_YUV420P, fullRange: false, 32, 16);

            Assert.InRange(Halves(gray).Left, 127, 129);
        }
        finally
        {
            Free(frame);
        }
    }

    /// <summary>A stream that changes its range reuses the cached scaler; the range of each picture still applies (scaled and copied).</summary>
    [FFmpegTheory]
    [InlineData(32, 16)]
    [InlineData(Width, Height)]
    public void ARangeChangeIsAppliedToTheReusedScaler(int width, int height)
    {
        AVFrame* frame = Picture(126, 126);
        using GrayscaleConverter converter = new();
        try
        {
            foreach (bool fullRange in new[] { true, false, true, false })
            {
                CameraGrayscaleFrame gray = converter.Convert(frame, AVPixelFormat.AV_PIX_FMT_YUV420P, fullRange, width, height);

                Assert.InRange(Halves(gray).Left, fullRange ? 126 : 127, fullRange ? 126 : 129);
            }
        }
        finally
        {
            Free(frame);
        }
    }

    [FFmpegFact]
    public void AreaAveragingTakesTheMeanOfEachBlock()
    {
        // Columns alternate 0 and 200 (full range): every 2×2 block averages to 100.
        AVFrame* frame = Picture(0, 0);
        for (int y = 0; y < Height; y++)
        {
            byte* row = frame->data[0] + (y * frame->linesize[0]);
            for (int x = 0; x < Width; x++)
            {
                row[x] = (byte)(x % 2 == 0 ? 0 : 200);
            }
        }

        using GrayscaleConverter converter = new();
        try
        {
            CameraGrayscaleFrame gray = converter.Convert(frame, AVPixelFormat.AV_PIX_FMT_YUV420P, fullRange: true, Width / 2, Height / 2);

            Assert.InRange(Halves(gray).Left, 99, 101);
            Assert.InRange(Halves(gray).Right, 99, 101);
        }
        finally
        {
            Free(frame);
        }
    }

    [FFmpegFact]
    public void TheBufferIsReusedAndRowsAreAligned()
    {
        AVFrame* frame = Picture(50, 60);
        using GrayscaleConverter converter = new();
        try
        {
            CameraGrayscaleFrame first = converter.Convert(frame, AVPixelFormat.AV_PIX_FMT_YUV420P, fullRange: true, 20, 16);
            CameraGrayscaleFrame second = converter.Convert(frame, AVPixelFormat.AV_PIX_FMT_YUV420P, fullRange: true, 20, 16);

            Assert.Equal(0, first.Stride % 64);
            Assert.True(first.Pixels.Span == second.Pixels.Span, "The same buffer.");
        }
        finally
        {
            Free(frame);
        }
    }
}
