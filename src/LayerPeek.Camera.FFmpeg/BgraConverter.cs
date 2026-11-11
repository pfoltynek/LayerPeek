using FFmpeg.AutoGen.Abstractions;

using LayerPeek.Core.Cameras;

namespace LayerPeek.Camera.FFmpeg;

/// <summary>
/// One swscale conversion of decoded pictures into BGRA of a given size (the camera's resolution, or scaled down for
/// display, T-184) with its reused output buffer. The scalers and buffers are rebuilt only when the source or target
/// changes; a buffer that has to grow grows with headroom, so a window being resized causes a few allocations, not one
/// per size. Not thread-safe: used on the decoder thread only.
/// </summary>
/// <remarks>
/// A scaled conversion runs in two passes: the YUV picture is scaled in YUV 4:2:0 first, then converted to BGRA in its
/// final size, with the same unscaled YUV -> RGB code as a full-resolution frame. Scaling straight into packed RGB in one
/// pass is far slower (measured on Apple Silicon, T-184: 2.0 ms per frame to 960×540, 0.18 ms for the unscaled conversion
/// of 1280×720, 0.53 ms for the two passes to 960×540): swscale has SIMD code for scaling planar YUV and for the unscaled
/// conversion, not for the scaler's packed RGB output. The intermediate picture is 1.5 bytes per pixel of the small size.
/// </remarks>
internal sealed unsafe class BgraConverter : IDisposable
{
    // YUV -> BGRA in the target size, and for a scaled conversion the YUV scaler before it.
    private SwsContext* _converter;
    private SwsContext* _scaler;

    // Current conversion.
    private int _sourceWidth;
    private int _sourceHeight;
    private AVPixelFormat _sourceFormat = AVPixelFormat.AV_PIX_FMT_NONE;
    private bool _fullRange;
    private AVColorSpace _colorSpace;
    private int _width;
    private int _height;
    private int _stride;
    private byte[]? _buffer;
    private byte* _target;
    private ReadOnlyMemory<byte> _pixels;

    // The scaled YUV 4:2:0 picture of a scaled conversion: three planes in one pinned buffer.
    private byte[]? _planesBuffer;
    private byte* _lumaPlane;
    private byte* _bluePlane;
    private byte* _redPlane;
    private int _lumaStride;
    private int _chromaStride;

    // The size the second pass converts: the target rounded up to the block its SIMD code needs (see Configure).
    private int _blockWidth;
    private int _blockHeight;

    /// <summary>
    /// Converts <paramref name="frame"/> (of <paramref name="format"/> and range, already normalized from the "J" formats)
    /// into <paramref name="width"/> × <paramref name="height"/> BGRA (bilinear when scaled, user decision 2026-11-10). The
    /// result borrows this converter's buffer: valid until the next call, or, after a size change replaced the buffer, until
    /// the garbage collector takes the old one (a consumer holding on to it never sees freed memory).
    /// </summary>
    public CameraFrame Convert(AVFrame* frame, AVPixelFormat format, bool fullRange, int width, int height)
    {
        if (_converter is null || frame->width != _sourceWidth || frame->height != _sourceHeight || format != _sourceFormat
            || fullRange != _fullRange || frame->colorspace != _colorSpace || width != _width || height != _height)
        {
            Configure(frame->width, frame->height, format, fullRange, frame->colorspace, width, height);
        }

        // BGRA has one plane; swscale reads up to four entries.
        byte** target = stackalloc byte*[4] { _target, null, null, null };
        int* targetStride = stackalloc int[4] { _stride, 0, 0, 0 };
        int rows;
        if (_scaler is null)
        {
            // AVFrame.data / linesize are inline arrays of 8 pointers / ints. The result is the number of output rows.
            rows = NativeMethods.Scale(_converter, (byte**)&frame->data, (int*)&frame->linesize, 0, frame->height, target, targetStride);
        }
        else
        {
            byte** planes = stackalloc byte*[4] { _lumaPlane, _bluePlane, _redPlane, null };
            int* planeStrides = stackalloc int[4] { _lumaStride, _chromaStride, _chromaStride, 0 };
            rows = NativeMethods.Scale(_scaler, (byte**)&frame->data, (int*)&frame->linesize, 0, frame->height, planes, planeStrides);
            if (rows == height)
            {
                rows = NativeMethods.Scale(_converter, planes, planeStrides, 0, _blockHeight, target, targetStride) == _blockHeight ? height : 0;
            }
        }

        if (rows != height)
        {
            throw new CameraFeedException("Converting the video image failed.");
        }

        return new CameraFrame(width, height, _stride, _pixels, _sourceWidth, _sourceHeight);
    }

    /// <summary>Releases the scalers; the buffers are managed. Idempotent.</summary>
    public void Dispose()
    {
        ffmpeg.sws_freeContext(_converter);
        _converter = null;
        ffmpeg.sws_freeContext(_scaler);
        _scaler = null;
    }

    /// <summary>YUV -> RGB coefficients of the stream (BT.601 unless it says BT.709) and its range; RGB output is full range.</summary>
    private static void SetColorspace(SwsContext* context, AVColorSpace colorSpace, bool fullRange, bool rgbTarget)
    {
        int coefficients = colorSpace == AVColorSpace.AVCOL_SPC_BT709 ? ffmpeg.SWS_CS_ITU709 : ffmpeg.SWS_CS_DEFAULT;
        int4 table = *(int4*)ffmpeg.sws_getCoefficients(coefficients);
        // Returns -1 only for unsupported conversions; the default coefficients are then kept, which is acceptable.
        _ = ffmpeg.sws_setColorspaceDetails(context, in table, fullRange ? 1 : 0, in table, rgbTarget || fullRange ? 1 : 0, 0, 1 << 16, 1 << 16);
    }

    private void Configure(int sourceWidth, int sourceHeight, AVPixelFormat format, bool fullRange, AVColorSpace colorSpace, int width, int height)
    {
        bool scaled = width != sourceWidth || height != sourceHeight;
        if (scaled)
        {
            // Scaled in YUV, keeping the stream's range and colour space for the conversion that follows.
            _scaler = ffmpeg.sws_getCachedContext(
                _scaler, sourceWidth, sourceHeight, format, width, height, AVPixelFormat.AV_PIX_FMT_YUV420P, (int)SwsFlags.SWS_BILINEAR, null, null, null);
            if (_scaler is null)
            {
                throw new CameraFeedException("The video image format is not supported.");
            }

            SetColorspace(_scaler, colorSpace, fullRange, rgbTarget: false);
            // swscale's SIMD YUV -> RGB code takes only widths of whole 16-pixel blocks and even heights (else plain C,
            // measured ~1.5x slower). The second pass therefore converts the target rounded up to that block: the extra
            // columns land in the row padding of the BGRA buffer (its stride is a multiple of 64 bytes = 16 pixels), an
            // extra row in one spare row; neither is part of the frame.
            _blockWidth = (width + 15) & ~15;
            _blockHeight = (height + 1) & ~1;
            _lumaStride = AlignedBuffer.AlignUp(_blockWidth);
            _chromaStride = AlignedBuffer.AlignUp(_blockWidth / 2);
            int lumaLength = checked(_lumaStride * _blockHeight);
            int chromaLength = checked(_chromaStride * (_blockHeight / 2));
            _lumaPlane = AlignedBuffer.Ensure(ref _planesBuffer, checked(lumaLength + (2 * AlignedBuffer.AlignUp(chromaLength))), out _);
            _bluePlane = _lumaPlane + lumaLength;
            _redPlane = _bluePlane + AlignedBuffer.AlignUp(chromaLength);
        }
        else
        {
            ffmpeg.sws_freeContext(_scaler);
            _scaler = null;
            _planesBuffer = null;
        }

        AVPixelFormat converted = scaled ? AVPixelFormat.AV_PIX_FMT_YUV420P : format;
        int convertedWidth = scaled ? _blockWidth : width;
        int convertedHeight = scaled ? _blockHeight : height;
        _converter = ffmpeg.sws_getCachedContext(
            _converter, convertedWidth, convertedHeight, converted, convertedWidth, convertedHeight, AVPixelFormat.AV_PIX_FMT_BGRA, (int)SwsFlags.SWS_BILINEAR, null, null, null);
        if (_converter is null)
        {
            throw new CameraFeedException("The video image format is not supported.");
        }

        SetColorspace(_converter, colorSpace, fullRange, rgbTarget: true);

        int stride = AlignedBuffer.AlignUp(width * CameraFrame.BytesPerPixel);
        int length = checked(stride * height);
        _target = AlignedBuffer.Ensure(ref _buffer, checked(stride * convertedHeight), out int offset);
        _pixels = new ReadOnlyMemory<byte>(_buffer, offset, length);

        _sourceWidth = sourceWidth;
        _sourceHeight = sourceHeight;
        _sourceFormat = format;
        _fullRange = fullRange;
        _colorSpace = colorSpace;
        _width = width;
        _height = height;
        _stride = stride;
    }
}
