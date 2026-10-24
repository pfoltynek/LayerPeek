using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

using LayerPeek.App.Printers;
using LayerPeek.App.UiTests.Infrastructure;
using LayerPeek.Core.Printers;

namespace LayerPeek.App.UiTests.Printers;

/// <summary>
/// The bundled printer illustrations (T-039) and <see cref="PrinterImageView"/>: every illustration exists in every size,
/// the view draws the size that matches the physical pixels, letterboxes a custom picture and draws nothing without one.
/// </summary>
public sealed class PrinterImageViewTests(HeadlessSession session)
{
    private const uint Red = 0xFFFF0000;

    [Fact]
    public Task EveryIllustrationExistsInEverySizeWithTransparentBackground() =>
        session.RunAsync(static _ =>
        {
            foreach (string id in PrinterIllustrations.Ids)
            {
                foreach (int size in PrinterIllustrations.PixelSizes)
                {
                    using Stream stream = AssetLoader.Open(PrinterIllustrations.AssetUri(id, size));
                    byte[] png = new byte[stream.Length];
                    stream.ReadExactly(png);
                    (int width, int height, uint[] pixels) = UiTestImages.Read(png);

                    Assert.Equal((size, size), (width, height));
                    Assert.Contains(pixels, static p => p >> 24 == 0);
                    Assert.Contains(pixels, static p => p >> 24 == 0xFF);
                }
            }

            return Task.CompletedTask;
        });

    [Fact]
    public Task EveryModelOfTheManualChoiceHasAnIllustration() =>
        session.RunAsync(static _ =>
        {
            foreach (PrinterModel model in (PrinterModel[])[.. PrinterModels.Selectable, PrinterModel.Unknown])
            {
                Bitmap bitmap = PrinterBitmaps.Illustration(PrinterIllustrations.IdFor(model), 24);
                Assert.Equal(new PixelSize(24, 24), bitmap.PixelSize);
            }

            return Task.CompletedTask;
        });

    [Theory]
    [InlineData(24, 1.0, 24)]
    [InlineData(24, 1.5, 48)]
    [InlineData(24, 2.0, 48)]
    [InlineData(48, 1.0, 48)]
    [InlineData(48, 2.0, 96)]
    [InlineData(56, 1.0, 56)]
    [InlineData(56, 2.0, 112)]
    public Task IllustrationMatchesThePhysicalPixels(double size, double scaling, int expectedPixels) =>
        session.RunAsync(scope =>
        {
            (Window window, PrinterImageView view) = Show(scope, size, PrinterPicture.FromImage(new PrinterImage(PrinterImageKind.Model, PrinterModel.CoreOne)));

            window.SetRenderScaling(scaling);
            window.Settle();

            Assert.Equal(expectedPixels, view.LastIllustrationPixelSize);
            Assert.Equal(new Size(size, size), view.Bounds.Size);
            return Task.CompletedTask;
        });

    [Fact]
    public Task CustomPictureIsLetterboxedIntoTheSquare() =>
        session.RunAsync(static scope =>
        {
            // 64 × 32 red picture in a 48 × 48 view: 48 × 24, centred vertically, transparent bands above and below.
            byte[] png = UiTestImages.Png(64, 32, Red);
            PrinterPicture picture = new(new PrinterImage(PrinterImageKind.Custom, PrinterModel.Unknown), png);
            (Window window, PrinterImageView view) = Show(scope, 48, picture);

            using WriteableBitmap frame = Capture(window);
            Point origin = view.TranslatePoint(default, window)!.Value;

            Assert.Equal(0, view.LastIllustrationPixelSize);
            Assert.Equal((255, 0, 0), Ui.Pixel(frame, (int)origin.X + 24, (int)origin.Y + 24));
            Assert.Equal((255, 255, 255), Ui.Pixel(frame, (int)origin.X + 24, (int)origin.Y + 4));
            Assert.Equal((255, 255, 255), Ui.Pixel(frame, (int)origin.X + 24, (int)origin.Y + 44));
            return Task.CompletedTask;
        });

    [Fact]
    public Task DamagedCustomPictureShowsTheAutomaticIllustration() =>
        session.RunAsync(static scope =>
        {
            // The printer would show the detected MK4S without its custom picture.
            PrinterPicture picture = PrinterPicture.For(Core.Printers.PrinterImageChoice.Custom, ConnectionType.PrusaLink, new PrinterModelCode(1, 4, 1), [0x89, 0x50, 0x4E, 0x47, 1, 2, 3]);
            Assert.True(picture.IsCustom);

            (_, PrinterImageView view) = Show(scope, 24, picture);

            Assert.Equal(24, view.LastIllustrationPixelSize);
            Assert.Equal("mk4", view.LastIllustrationId);
            return Task.CompletedTask;
        });

    [Fact]
    public Task NoPictureDrawsNothing() =>
        session.RunAsync(static scope =>
        {
            (Window window, PrinterImageView view) = Show(scope, 24, null);

            using WriteableBitmap frame = Capture(window);
            Point origin = view.TranslatePoint(default, window)!.Value;

            Assert.Equal(0, view.LastIllustrationPixelSize);
            Assert.Equal((255, 255, 255), Ui.Pixel(frame, (int)origin.X + 12, (int)origin.Y + 12));
            return Task.CompletedTask;
        });

    [Fact]
    public Task NamedImageIsAnImageForScreenReaders() =>
        session.RunAsync(static scope =>
        {
            // Through the automation peer, i.e. what UI Automation really reports (T-069 Q3: the drawing control had no peer
            // of its own and was left out of the control view although the XAML named it).
            (_, PrinterImageView view) = Show(scope, 48, PrinterPicture.FromImage(new PrinterImage(PrinterImageKind.Model, PrinterModel.Mk4S)));
            AutomationProperties.SetName(view, "Printer image");

            AutomationPeer peer = ControlAutomationPeer.CreatePeerForElement(view);

            Assert.Equal(AutomationControlType.Image, peer.GetAutomationControlType());
            Assert.True(peer.IsControlElement());
            Assert.True(peer.IsContentElement());
            Assert.Equal("Printer image", peer.GetName());
            Assert.Equal("Image", peer.GetClassName());
            return Task.CompletedTask;
        });

    [Fact]
    public Task UnnamedImageStaysDecorative() =>
        session.RunAsync(static scope =>
        {
            // The list row and the image button name the printer themselves; their image adds nothing for a screen reader.
            (_, PrinterImageView view) = Show(scope, 24, PrinterPicture.FromImage(new PrinterImage(PrinterImageKind.Model, PrinterModel.Mk4S)));

            AutomationPeer peer = ControlAutomationPeer.CreatePeerForElement(view);

            Assert.Equal(AutomationControlType.Image, peer.GetAutomationControlType());
            Assert.False(peer.IsControlElement());
            Assert.False(peer.IsContentElement());

            // Named later (a binding that resolves after the peer exists): from then on an image for screen readers.
            AutomationProperties.SetName(view, "Printer image");
            Assert.True(peer.IsControlElement());

            // An explicit accessibility view still wins.
            AutomationProperties.SetAccessibilityView(view, AccessibilityView.Raw);
            Assert.False(peer.IsControlElement());
            return Task.CompletedTask;
        });

    private static (Window Window, PrinterImageView View) Show(UiTestScope scope, double size, PrinterPicture? picture)
    {
        PrinterImageView view = new() { Width = size, Height = size, Picture = picture };
        Window window = scope.Track(new Window
        {
            Width = 200,
            Height = 200,
            Background = Avalonia.Media.Brushes.White,
            Content = new Border { Padding = new Thickness(10), Child = view, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top },
        });
        window.Show();
        window.SetRenderScaling(1.0);
        window.Settle();
        return (window, view);
    }

    private static WriteableBitmap Capture(Window window)
    {
        window.Settle();
        return window.CaptureRenderedFrame() ?? throw new InvalidOperationException("No frame was rendered.");
    }
}
