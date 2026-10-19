namespace PrusaTrayDiagnostic.Core.Cameras;

/// <summary>
/// An owned, immutable copy of a <see cref="CameraFrame"/> (BGRA, rows tightly packed: <see cref="Stride"/> = width * 4).
/// Unlike a frame it stays valid indefinitely; create one with <see cref="CameraFrame.ToImage"/>.
/// </summary>
public sealed class CameraImage
{
    private readonly byte[] _pixels;

    internal CameraImage(int width, int height, byte[] pixels)
    {
        Width = width;
        Height = height;
        _pixels = pixels;
    }

    public int Width { get; }

    public int Height { get; }

    public int Stride => Width * CameraFrame.BytesPerPixel;

    public ReadOnlyMemory<byte> Pixels => _pixels;
}
