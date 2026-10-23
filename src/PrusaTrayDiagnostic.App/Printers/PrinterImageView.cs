using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace PrusaTrayDiagnostic.App.Printers;

/// <summary>
/// Draws a <see cref="PrinterPicture"/> (T-039) letterboxed into its square bounds: an illustration from the pre-rendered
/// size that matches the physical pixels at the current scaling (so it is sharp at 100–200 % instead of a scaled
/// bitmap), or the custom picture, decoded once and scaled smoothly. Give it a fixed <c>Width</c> and <c>Height</c>.
/// Decorative by default (left out of the UI Automation control view); set <c>AutomationProperties.Name</c> where it
/// carries meaning and it is reported as an image with that name (<see cref="PrinterImageViewAutomationPeer"/>).
/// </summary>
public sealed class PrinterImageView : Control
{
    public static readonly StyledProperty<PrinterPicture?> PictureProperty =
        AvaloniaProperty.Register<PrinterImageView, PrinterPicture?>(nameof(Picture));

    // The decoded custom picture of Picture (owned; replaced when the bytes change, released when detached).
    private byte[]? _customSource;
    private Bitmap? _custom;

    static PrinterImageView()
    {
        AffectsRender<PrinterImageView>(PictureProperty);
    }

    public PrinterPicture? Picture
    {
        get => GetValue(PictureProperty);
        set => SetValue(PictureProperty, value);
    }

    /// <summary>Pixel size of the illustration drawn last (0 for a custom picture or nothing); for tests.</summary>
    internal int LastIllustrationPixelSize { get; private set; }

    /// <summary>Id of the illustration drawn last (<see langword="null"/> for a custom picture or nothing); for tests.</summary>
    internal string? LastIllustrationId { get; private set; }

    public override void Render(DrawingContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        base.Render(context);
        LastIllustrationPixelSize = 0;
        LastIllustrationId = null;
        Rect bounds = new(Bounds.Size);
        if (Picture is not { } picture || bounds.Width <= 0 || bounds.Height <= 0)
        {
            return;
        }

        double side = Math.Min(bounds.Width, bounds.Height);
        Bitmap? bitmap = picture.CustomPng is { } png ? Custom(png) : null;
        if (bitmap is null)
        {
            double scaling = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1;
            int pixelSize = PrinterIllustrations.SelectPixelSize(side, scaling);
            bitmap = PrinterBitmaps.Illustration(picture.FallbackIllustrationId, pixelSize);
            LastIllustrationPixelSize = pixelSize;
            LastIllustrationId = picture.FallbackIllustrationId;
        }

        Rect destination = PrinterBitmaps.Fit(bitmap.Size, bounds);
        using (context.PushRenderOptions(new RenderOptions { BitmapInterpolationMode = BitmapInterpolationMode.HighQuality }))
        {
            context.DrawImage(bitmap, new Rect(bitmap.Size), destination);
        }
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new PrinterImageViewAutomationPeer(this);

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        ArgumentNullException.ThrowIfNull(change);
        base.OnPropertyChanged(change);
        if (change.Property == PictureProperty && !ReferenceEquals(Picture?.CustomPng, _customSource))
        {
            ReleaseCustom();
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        ReleaseCustom();
    }

    private Bitmap? Custom(byte[] png)
    {
        if (!ReferenceEquals(png, _customSource))
        {
            ReleaseCustom();
            _customSource = png;
            _custom = PrinterBitmaps.DecodeCustom(png);
        }

        return _custom;
    }

    private void ReleaseCustom()
    {
        _custom?.Dispose();
        _custom = null;
        _customSource = null;
    }
}
