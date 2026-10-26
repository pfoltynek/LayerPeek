using System.Runtime.InteropServices;

using Avalonia.Media;
using Avalonia.Media.Imaging;

using LayerPeek.Core.Previews;

namespace LayerPeek.App.Previews;

/// <summary>
/// Decodes G-code previews with Avalonia (Skia): the 380 × 285 PNG of a stock Prusa profile becomes e.g. a 40 × 30
/// <see cref="Bitmap"/> for the M card at 100 %, scaled down smoothly once while decoding (T-087).
/// </summary>
internal sealed class AvaloniaPrintThumbnailDecoder : IPrintThumbnailDecoder
{
    public IImage Decode(PrintPreview preview, int maxSide)
    {
        ArgumentNullException.ThrowIfNull(preview);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxSide, 1);
        using MemoryStream stream = Open(preview.Png);
        if (preview.Width <= maxSide && preview.Height <= maxSide)
        {
            return new Bitmap(stream);
        }

        return preview.Width >= preview.Height
            ? Bitmap.DecodeToWidth(stream, maxSide, BitmapInterpolationMode.HighQuality)
            : Bitmap.DecodeToHeight(stream, maxSide, BitmapInterpolationMode.HighQuality);
    }

    private static MemoryStream Open(ReadOnlyMemory<byte> png) =>
        MemoryMarshal.TryGetArray(png, out ArraySegment<byte> segment)
            ? new MemoryStream(segment.Array!, segment.Offset, segment.Count, writable: false)
            : new MemoryStream(png.ToArray(), writable: false);
}
