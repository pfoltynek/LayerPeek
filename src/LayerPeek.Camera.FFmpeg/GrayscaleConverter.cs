using FFmpeg.AutoGen.Abstractions;

using LayerPeek.Core.Cameras;

namespace LayerPeek.Camera.FFmpeg;

/// <summary>
/// One swscale conversion of decoded pictures into 8-bit luma of a given size (T-189: the samples of print failure
/// detection) with its reused output buffer. Rebuilt only when the source or target changes. Not thread-safe: used on the
/// decoder thread only.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>Only the Y plane is read: for the usual 8-bit pictures it is handed to swscale as a GRAY8 image of its own, so no
/// chroma is touched and no RGB is computed (a picture of another layout is converted as a whole).</item>
/// <item>Area averaging, like the managed <see cref="CameraFrameExchange"/> scaler from BGRA it replaces and like the
/// detector fixtures (<c>scale=320:180:flags=area</c>).</item>
/// <item>Always full range (0–255): a limited-range picture (Y 16–235, most cameras) is expanded, so the luma means the
/// same as luma computed from the full-range BGRA frame and the detectors' brightness thresholds keep their meaning. A
/// full-range picture (the Buddy3D: <c>yuvj420p</c>, BT.709) is scaled as it is. swscale expands the range while it
/// scales; a luma plane already of the target size is only copied by swscale (tested: the range is then ignored), so it
/// is expanded afterwards by a table.</item>
/// <item>The luma of the stream's colour space: for BT.709 slightly different from BT.601 luma of the BGRA frame in
/// saturated colours (equal for grays); measured on the Buddy3D fixture: ~1.3 levels on average (T-189 notes).</item>
/// </list>
/// </remarks>
internal sealed unsafe class GrayscaleConverter : IDisposable
{
    /// <summary>Limited-range luma (16–235) -> full range (0–255), rounded and clamped.</summary>
    private static readonly byte[] LimitedToFull = CreateLimitedToFull();

    private SwsContext* _scaler;

    // Current conversion.
    private int _sourceWidth;
    private int _sourceHeight;
    private AVPixelFormat _sourceFormat = AVPixelFormat.AV_PIX_FMT_NONE;
    private bool _fullRange;
    private bool _lumaPlaneOnly;
    private bool _expandAfterwards;
    private int _width;
    private int _height;
    private int _stride;
    private byte[]? _buffer;
    private byte* _target;
    private ReadOnlyMemory<byte> _pixels;

    /// <summary>
    /// Scales the luma of <paramref name="frame"/> (of <paramref name="format"/> and range, already normalized from the "J"
    /// formats) into <paramref name="width"/> × <paramref name="height"/> full-range GRAY8. The result borrows this
    /// converter's buffer: valid until the next call, or, after a size change replaced the buffer, until the garbage
    /// collector takes the old one.
    /// </summary>
    public CameraGrayscaleFrame Convert(AVFrame* frame, AVPixelFormat format, bool fullRange, int width, int height)
    {
        if (_scaler is null || frame->width != _sourceWidth || frame->height != _sourceHeight || format != _sourceFormat
            || fullRange != _fullRange || width != _width || height != _height)
        {
            Configure(frame->width, frame->height, format, fullRange, width, height);
        }

        byte** target = stackalloc byte*[4] { _target, null, null, null };
        int* targetStride = stackalloc int[4] { _stride, 0, 0, 0 };
        int rows;
        if (_lumaPlaneOnly)
        {
            byte** plane = stackalloc byte*[4] { frame->data[0], null, null, null };
            int* planeStride = stackalloc int[4] { frame->linesize[0], 0, 0, 0 };
            rows = NativeMethods.Scale(_scaler, plane, planeStride, 0, frame->height, target, targetStride);
        }
        else
        {
            // AVFrame.data / linesize are inline arrays of 8 pointers / ints.
            rows = NativeMethods.Scale(_scaler, (byte**)&frame->data, (int*)&frame->linesize, 0, frame->height, target, targetStride);
        }

        if (rows != height)
        {
            throw new CameraFeedException("Converting the video image failed.");
        }

        if (_expandAfterwards)
        {
            ExpandRange();
        }

        return new CameraGrayscaleFrame(width, height, _stride, _pixels, _sourceWidth, _sourceHeight);
    }

    /// <summary>Releases the scaler; the buffer is managed. Idempotent.</summary>
    public void Dispose()
    {
        ffmpeg.sws_freeContext(_scaler);
        _scaler = null;
    }

    private static byte[] CreateLimitedToFull()
    {
        byte[] table = new byte[256];
        for (int y = 0; y < table.Length; y++)
        {
            table[y] = (byte)Math.Clamp((((y - 16) * 255) + 109) / 219, 0, 255);
        }

        return table;
    }

    /// <summary>Maps the copied limited-range luma in the buffer to full range, row by row.</summary>
    private void ExpandRange()
    {
        ReadOnlySpan<byte> table = LimitedToFull;
        for (int y = 0; y < _height; y++)
        {
            Span<byte> row = new(_target + ((long)y * _stride), _width);
            for (int x = 0; x < row.Length; x++)
            {
                row[x] = table[row[x]];
            }
        }
    }

    /// <summary>Whether plane 0 of <paramref name="format"/> is the luma alone, one byte per pixel (all 8-bit planar and semi-planar YUV).</summary>
    private static bool HasLumaPlane(AVPixelFormat format)
    {
        AVPixFmtDescriptor* descriptor = ffmpeg.av_pix_fmt_desc_get(format);
        if (descriptor is null || descriptor->nb_components == 0
            || (descriptor->flags & (ffmpeg.AV_PIX_FMT_FLAG_RGB | ffmpeg.AV_PIX_FMT_FLAG_PAL | ffmpeg.AV_PIX_FMT_FLAG_BITSTREAM | ffmpeg.AV_PIX_FMT_FLAG_BE)) != 0)
        {
            return false;
        }

        AVComponentDescriptor luma = descriptor->comp[0];
        return luma is { plane: 0, step: 1, offset: 0, shift: 0, depth: 8 };
    }

    private void Configure(int sourceWidth, int sourceHeight, AVPixelFormat format, bool fullRange, int width, int height)
    {
        _lumaPlaneOnly = HasLumaPlane(format);
        AVPixelFormat source = _lumaPlaneOnly ? AVPixelFormat.AV_PIX_FMT_GRAY8 : format;
        _scaler = ffmpeg.sws_getCachedContext(
            _scaler, sourceWidth, sourceHeight, source, width, height, AVPixelFormat.AV_PIX_FMT_GRAY8, (int)SwsFlags.SWS_AREA, null, null, null);
        if (_scaler is null)
        {
            throw new CameraFeedException("The video image format is not supported.");
        }

        // Only the ranges matter for luma (the coefficients are for chroma); the result is full range. The result code is
        // about the YUV -> RGB tables a gray target does not have; the ranges are applied while scaling (tested).
        int4 table = *(int4*)ffmpeg.sws_getCoefficients(ffmpeg.SWS_CS_DEFAULT);
        _ = ffmpeg.sws_setColorspaceDetails(_scaler, in table, fullRange ? 1 : 0, in table, 1, 0, 1 << 16, 1 << 16);
        // Only the plain copy of a luma plane is known to skip the range (see the type remarks).
        _expandAfterwards = _lumaPlaneOnly && !fullRange && width == sourceWidth && height == sourceHeight;

        int stride = AlignedBuffer.AlignUp(width);
        int length = checked(stride * height);
        _target = AlignedBuffer.Ensure(ref _buffer, length, out int offset);
        _pixels = new ReadOnlyMemory<byte>(_buffer, offset, length);

        _sourceWidth = sourceWidth;
        _sourceHeight = sourceHeight;
        _sourceFormat = format;
        _fullRange = fullRange;
        _width = width;
        _height = height;
        _stride = stride;
    }
}
