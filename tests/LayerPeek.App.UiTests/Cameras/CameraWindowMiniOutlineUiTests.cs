using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;

using LayerPeek.App.Cameras;
using LayerPeek.App.Tests.Cameras;
using LayerPeek.App.UiTests.Infrastructure;
using LayerPeek.App.Windows;
using LayerPeek.Core.Cameras;

namespace LayerPeek.App.UiTests.Cameras;

/// <summary>
/// The thin outline of the borderless mini preview (T-118, design "Návrh vzhledu"): a 1 DIP neutral border over the image
/// and the strip, only in the mini preview, brighter while the pointer is over the window (also over a resize grip). It is
/// no pointer target and changes no layout: the grips still take presses at the edge, the image, the strip and the size of
/// the mini preview stay as they were. Its look over a real desktop (light and dark) and the crispness at 125/150 % on a real
/// screen are manual checks (T-114, T-115).
/// </summary>
public sealed class CameraWindowMiniOutlineUiTests(HeadlessSession session)
{
    private static readonly CameraWindowDefaults MiniFirst = CameraWindowDefaults.Default with { StartMode = CameraWindowStartMode.Mini };

    private static readonly Color Normal = Color.Parse("#73FFFFFF");

    private static readonly Color Hover = Color.Parse("#BFFFFFFF");

    private static CameraViewModel ViewModel(CameraWindow window) => (CameraViewModel)window.DataContext!;

    private static Border Outline(CameraWindow window) => Named<Border>(window, "MiniOutline");

    private static Color? ColourOf(Border outline) => (outline.BorderBrush as ISolidColorBrush)?.Color;

    /// <summary>A part of the window by its x:Name (the outline, the image and the strip have no automation id).</summary>
    private static T Named<T>(CameraWindow window, string name)
        where T : Control =>
        window.GetVisualDescendants().OfType<T>().Single(control => control.Name == name);

    private static Rect BoundsIn(Control control, Visual root) =>
        new(control.TranslatePoint(default, root) ?? throw new InvalidOperationException("Not in the window."), control.Bounds.Size);

    private static void Run(CameraWindow window, Action action)
    {
        action();
        Dispatcher.UIThread.RunJobs();
        window.Settle();
    }

    private static void MouseAway(CameraWindow window) => Run(window, () => window.MouseMove(new Point(-10, -10)));

    /// <summary>The mini preview at its minimum size (240 × 168: the 16:9 image 135 and the strip 33), pointer away, no fades.</summary>
    private static CameraWindow OpenSmallestMini(CameraHarness harness)
    {
        CameraWindow window = harness.Open();
        window.Settle();
        Assert.True(ViewModel(window).IsMini);
        CameraHarness.SkipWindowButtonFade(window);
        CameraHarness.ResizeMiniWithGrip(window, window.MinWidth, window.MinHeight);
        Assert.Equal(new Size(240, 168), window.ClientSize);
        MouseAway(window);
        return window;
    }

    [Fact]
    public Task OutlineIsShownOnlyInTheMiniPreviewAlsoAfterSwitchingModes() =>
        session.RunAsync(async scope =>
        {
            await using CameraHarness harness = new(screens: FakeScreenCatalog.ThreeMonitors(), window: CameraWindowDefaults.Default with { StartMode = CameraWindowStartMode.Normal });
            CameraWindow window = harness.Open();
            window.Settle();
            CameraViewModel viewModel = ViewModel(window);
            Border outline = Outline(window);

            Assert.False(viewModel.IsMini);
            Assert.False(outline.IsEffectivelyVisible);

            Run(window, () => viewModel.ShowMiniCommand.Execute(null));
            Assert.True(outline.IsEffectivelyVisible);

            // Full screen entered from the mini preview: no outline; Esc returns to the mini preview and it is back.
            Run(window, () => viewModel.ToggleFullScreenCommand.Execute(null));
            Assert.True(viewModel.IsFullScreen);
            Assert.False(outline.IsEffectivelyVisible);
            Run(window, () => window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null));
            Assert.True(viewModel.IsMini);
            Assert.True(outline.IsEffectivelyVisible);

            Run(window, () => viewModel.ShowNormalCommand.Execute(null));
            Assert.False(viewModel.IsMini);
            Assert.False(outline.IsEffectivelyVisible);

            // Full screen entered from the normal window: none either.
            Run(window, () => viewModel.ToggleFullScreenCommand.Execute(null));
            Assert.True(viewModel.IsFullScreen);
            Assert.False(outline.IsEffectivelyVisible);
        });

    [Fact]
    public Task OutlineIsADecorationOverTheWholeMiniPreview() =>
        session.RunAsync(async scope =>
        {
            await using CameraHarness harness = new(screens: FakeScreenCatalog.ThreeMonitors(), window: MiniFirst);
            CameraWindow window = OpenSmallestMini(harness);
            Border outline = Outline(window);

            Assert.Equal(new Thickness(1), outline.BorderThickness);
            Assert.Null(outline.Background);
            Assert.False(outline.IsHitTestVisible);
            Assert.Equal(new CornerRadius(0), outline.CornerRadius);
            Assert.Equal(0, outline.BoxShadow.Count);
            // Over the image and the strip (both rows), above the strip (ZIndex 1).
            Assert.Equal(2, Grid.GetRowSpan(outline));
            Assert.Equal(2, outline.ZIndex);
            Assert.Equal(new Rect(0, 0, 240, 168), BoundsIn(outline, window));
        });

    [Fact]
    public Task OutlineLeavesTheImageTheStripAndTheSizeOfTheMiniPreviewAsTheyWere() =>
        session.RunAsync(async scope =>
        {
            await using CameraHarness harness = new(screens: FakeScreenCatalog.ThreeMonitors(), window: MiniFirst);
            CameraWindow window = OpenSmallestMini(harness);

            // The image keeps the whole 16:9 area and the strip the 33 DIP below it (T-089): the outline only overdraws.
            Assert.Equal(new Rect(0, 0, 240, 135), BoundsIn(Named<Border>(window, "Surface"), window));
            Assert.Equal(new Rect(0, 135, 240, 33), BoundsIn(Named<Control>(window, "MiniStrip"), window));
            Assert.Equal(new Size(240, 168), window.ClientSize);

            // Hovering changes only the colour, never a size (no jumping).
            Run(window, () => window.MouseMove(new Point(120, 60)));
            window.Settle();
            Assert.Equal(new Thickness(1), Outline(window).BorderThickness);
            Assert.Equal(new Rect(0, 0, 240, 135), BoundsIn(Named<Border>(window, "Surface"), window));
            Assert.Equal(new Size(240, 168), window.ClientSize);
        });

    [Fact]
    public Task OutlineIsBrighterWhileThePointerIsOverTheImageTheStripOrAGrip() =>
        session.RunAsync(async scope =>
        {
            await using CameraHarness harness = new(screens: FakeScreenCatalog.ThreeMonitors(), window: MiniFirst);
            CameraWindow window = harness.Open();
            window.Settle();
            CameraHarness.ResizeMiniWithGrip(window, window.MinWidth, window.MinHeight);
            Border outline = Outline(window);

            // The fade on the brush takes 0.15 s like that of the window buttons (T-117); it runs on real time, so it is skipped.
            BrushTransition transition = Assert.IsType<BrushTransition>(Assert.Single(outline.Transitions!));
            Assert.Equal(Border.BorderBrushProperty, transition.Property);
            Assert.Equal(TimeSpan.FromSeconds(0.15), transition.Duration);
            CameraHarness.SkipWindowButtonFade(window);
            Assert.Null(outline.Transitions);

            MouseAway(window);
            Assert.Equal(Normal, ColourOf(outline));

            // Over the image.
            Run(window, () => window.MouseMove(new Point(120, 60)));
            Assert.Equal(Hover, ColourOf(outline));
            MouseAway(window);
            Assert.Equal(Normal, ColourOf(outline));

            // Over the strip.
            Run(window, () => window.MouseMove(new Point(60, 150)));
            Assert.Equal(Hover, ColourOf(outline));

            // Over the left grip and the bottom right corner grip.
            Run(window, () => window.MouseMove(new Point(2, 70)));
            Assert.Equal(Hover, ColourOf(outline));
            Run(window, () => window.MouseMove(new Point(238, 166)));
            Assert.Equal(Hover, ColourOf(outline));

            MouseAway(window);
            Assert.Equal(Normal, ColourOf(outline));
        });

    [Theory]
    [InlineData(0.5, 70, "West")]
    [InlineData(2, 2, "NorthWest")]
    [InlineData(120, 0.5, "North")]
    [InlineData(239.5, 70, "East")]
    [InlineData(239.5, 167.5, "SouthEast")]
    [InlineData(120, 167.5, "South")]
    [InlineData(2, 160, "West")]
    public Task GripsStillTakePressesUnderTheOutline(double x, double y, string edge) =>
        session.RunAsync(async scope =>
        {
            await using CameraHarness harness = new(screens: FakeScreenCatalog.ThreeMonitors(), window: MiniFirst);
            CameraWindow window = OpenSmallestMini(harness);
            Assert.True(Outline(window).IsEffectivelyVisible);

            Border grip = Assert.IsType<Border>(window.InputHitTest(new Point(x, y)));
            Assert.Contains("grip", grip.Classes);
            Assert.Equal(Enum.Parse<WindowEdge>(edge), grip.Tag);
        });

    [Fact]
    public Task GripResizeStillWorksWithTheOutline() =>
        session.RunAsync(async scope =>
        {
            await using CameraHarness harness = new(screens: FakeScreenCatalog.ThreeMonitors(), window: MiniFirst);
            CameraWindow window = OpenSmallestMini(harness);

            CameraHarness.ResizeMiniWithGrip(window, 320, 400);

            // The height follows the width (16:9 image + strip), as without the outline (T-089); the outline follows the window.
            Assert.Equal(320, window.ClientSize.Width);
            Assert.Equal(new Rect(0, 0, 320, 180), BoundsIn(Named<Border>(window, "Surface"), window));
            Assert.Equal(new Rect(window.ClientSize), BoundsIn(Outline(window), window));
        });

    [Fact]
    public Task LayoutModeIsNotAffectedByTheOutline() =>
        session.RunAsync(async scope =>
        {
            await using CameraHarness harness = new(screens: FakeScreenCatalog.ThreeMonitors(), window: CameraWindowDefaults.Default with { StartMode = CameraWindowStartMode.Normal });
            CameraWindow window = harness.Open();
            window.Settle();
            CameraViewModel viewModel = ViewModel(window);

            Run(window, () => viewModel.EditLayoutCommand.Execute(null));
            Assert.True(viewModel.IsLayoutModeActive);
            Assert.False(Outline(window).IsEffectivelyVisible);
            // The edges of the image belong to the layout mode, not to an outline or a grip.
            Visual? hit = window.InputHitTest(new Point(1, 100)) as Visual;
            Assert.NotNull(hit);
            Control overlay = Named<Control>(window, "LayoutOverlay");
            Assert.True(ReferenceEquals(hit, overlay) || hit.GetVisualAncestors().Contains(overlay), $"hit: {hit}");
        });

    /// <summary>
    /// The 1 DIP outline is rounded to whole device pixels by the layout rounding of <see cref="Border"/> (design 2): one
    /// pixel at 100 and 125 %, two at 150–200 %, every one fully drawn (no blurred half pixel), on all four edges.
    /// </summary>
    [Theory]
    [InlineData(1.0, 1)]
    [InlineData(1.25, 1)]
    [InlineData(1.5, 2)]
    [InlineData(1.75, 2)]
    [InlineData(2.0, 2)]
    public Task OutlineIsCrispWholeDevicePixelsAtEveryScaling(double scaling, int pixels) =>
        session.RunAsync(async scope =>
        {
            await using CameraHarness harness = new(screens: FakeScreenCatalog.ThreeMonitors(), window: MiniFirst);
            CameraWindow window = OpenSmallestMini(harness);
            window.SetRenderScaling(scaling);
            window.Settle();

            using WriteableBitmap frame = window.CaptureRenderedFrame()!;
            int width = frame.PixelSize.Width;
            int height = frame.PixelSize.Height;
            Assert.Equal((int)Math.Round(240 * scaling), width);
            Assert.Equal((int)Math.Round(168 * scaling), height);

            // White at 45 % over the black content: #737373. Away from the corners, the image is black at the edges
            // without a frame, the strip is #141416 (its line of state is at the top of the strip, not at the edges).
            int midY = (int)(60 * scaling);
            int midX = (int)(60 * scaling);
            for (int i = 0; i < pixels; i++)
            {
                AssertOutline(Ui.Pixel(frame, i, midY), $"left {i}");
                AssertOutline(Ui.Pixel(frame, width - 1 - i, midY), $"right {i}");
                AssertOutline(Ui.Pixel(frame, midX, i), $"top {i}");
            }

            Assert.Equal((0, 0, 0), Ui.Pixel(frame, pixels, midY));
            Assert.Equal((0, 0, 0), Ui.Pixel(frame, width - 1 - pixels, midY));
            Assert.Equal((0, 0, 0), Ui.Pixel(frame, midX, pixels));

            // Bottom edge over the strip: the outline, then the strip's own background.
            for (int i = 0; i < pixels; i++)
            {
                (byte r, byte g, byte b) bottom = Ui.Pixel(frame, midX, height - 1 - i);
                Assert.True(bottom.r > 0x73 - 4 && bottom.r < 0x73 + 0x14, $"bottom {i}: {bottom}");
                Assert.Equal(bottom.r, bottom.g);
            }

            Assert.True(Ui.Pixel(frame, midX, height - 1 - pixels).R < 0x30, "the strip below the outline");
        });

    private static void AssertOutline((byte R, byte G, byte B) pixel, string where)
    {
        Assert.True(Math.Abs(pixel.R - 0x73) <= 2 && Math.Abs(pixel.G - 0x73) <= 2 && Math.Abs(pixel.B - 0x73) <= 2, $"{where}: {pixel}");
    }

    [Fact]
    public Task SnapshotOfTheOutlineOfTheSmallestMiniPreview() =>
        session.RunAsync(async scope =>
        {
            await using CameraHarness harness = new(screens: FakeScreenCatalog.ThreeMonitors(), window: MiniFirst);
            (CameraWindow window, _) = await harness.OpenStreamingAsync(320, 180, 0xFF406080);
            CameraHarness.SkipWindowButtonFade(window);
            CameraHarness.ResizeMiniWithGrip(window, window.MinWidth, window.MinHeight);
            MouseAway(window);

            Assert.Equal(Normal, ColourOf(Outline(window)));
            SnapshotAssert.Matches(window, "camera-mini-outline");
        });

    [Fact]
    public Task SnapshotOfTheOutlineWhileThePointerIsOverAGrip() =>
        session.RunAsync(async scope =>
        {
            await using CameraHarness harness = new(screens: FakeScreenCatalog.ThreeMonitors(), window: MiniFirst);
            (CameraWindow window, _) = await harness.OpenStreamingAsync(320, 180, 0xFF406080);
            CameraHarness.SkipWindowButtonFade(window);
            CameraHarness.ResizeMiniWithGrip(window, 320, 400);
            Run(window, () => window.MouseMove(new Point(2, 90)));

            Assert.Equal(Hover, ColourOf(Outline(window)));
            SnapshotAssert.Matches(window, "camera-mini-outline-hover");
        });
}
