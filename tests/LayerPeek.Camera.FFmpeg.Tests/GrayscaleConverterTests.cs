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
