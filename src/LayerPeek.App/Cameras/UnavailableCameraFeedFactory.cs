using LayerPeek.Core.Cameras;

namespace LayerPeek.App.Cameras;

/// <summary>
/// <see cref="ICameraFeedFactory"/> for places without a video decoder (the default of a settings window created without
/// a camera tester, e.g. in tests). Every stream fails with <see cref="CameraFeedException"/>.
/// </summary>
internal sealed class UnavailableCameraFeedFactory : ICameraFeedFactory
{
    public const string Message = "No video decoder is available.";

    public static UnavailableCameraFeedFactory Instance { get; } = new();

    private UnavailableCameraFeedFactory()
    {
    }

    public ICameraFeed Create(Uri streamUri)
    {
        ArgumentNullException.ThrowIfNull(streamUri);
        throw new CameraFeedException(Message);
    }
}
