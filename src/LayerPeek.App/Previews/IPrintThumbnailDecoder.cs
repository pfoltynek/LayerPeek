using Avalonia.Media;

using LayerPeek.Core.Previews;

namespace LayerPeek.App.Previews;

/// <summary>Turns a G-code preview (PNG) into an image for the screen (T-087); behind an interface so that tests need no renderer.</summary>
internal interface IPrintThumbnailDecoder
{
    /// <summary>
    /// Decodes <paramref name="preview"/> with its longer side at most <paramref name="maxSide"/> pixels, keeping the
    /// aspect ratio (never enlarged). The result is owned by the caller (disposed when it is <see cref="IDisposable"/>).
    /// Any thread; may be slow (call it off the UI thread).
    /// </summary>
    /// <exception cref="Exception">The PNG cannot be decoded (corrupt, truncated, unsupported); the caller treats it as no preview.</exception>
    IImage Decode(PrintPreview preview, int maxSide);
}
