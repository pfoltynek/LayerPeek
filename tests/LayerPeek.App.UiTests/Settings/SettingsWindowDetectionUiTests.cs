using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;

using LayerPeek.App.Localization;
using LayerPeek.App.Resources;
using LayerPeek.App.Settings;
using LayerPeek.App.Tests.Settings;
using LayerPeek.App.UiTests.Infrastructure;
using LayerPeek.Core.Cameras;
using LayerPeek.Core.Detection;
using LayerPeek.Core.Printers;
using LayerPeek.Core.Settings;

using static LayerPeek.App.Tests.Settings.SettingsTestData;

namespace LayerPeek.App.UiTests.Settings;

/// <summary>
/// The Detection sub-tab (T-154) in a real window: the bed region editor over the camera frame (drawn, moved and resized
/// with the mouse and the keyboard, coordinates converted with the bands of another aspect ratio), "Whole frame" and, for a
/// Core One only, "Default for Core One"; the region and the detectors saved with the printer; the last saved frame while
/// the camera has not been tested in this window. Headless Avalonia, no camera.
/// </summary>
public sealed class SettingsWindowDetectionUiTests(HeadlessSession session)
{
    private static readonly CameraSettings Camera = new(enabled: true, new Uri("rtsp://192.168.0.227/live"));

    private static PrinterDefinition CoreOne(string id = "core") =>
        LinkPrinter(id, "Core One") with { Camera = Camera, Image = PrinterImageChoice.ForModel(PrinterModel.CoreOne) };

    private static async Task<(SettingsWindowHarness Ui, DetectionRegionEditor Editor)> OpenAsync(UiTestScope scope, PrinterDefinition printer, ICameraTester? cameraTester = null, ILastCameraFrames? lastFrames = null, FakeEditorStore? store = null)
    {
        SettingsWindowHarness ui = await SettingsWindowHarness.OpenAsync(scope, store ?? SettingsWindowHarness.StoreWith(printer), cameraTester: cameraTester, lastFrames: lastFrames);
        ui.ViewModel.SelectedPrinterTabIndex = SettingsViewModel.DetectionPrinterTab;
        ui.Window.Settle();
        return (ui, ui.Find<DetectionRegionEditor>("PrinterDetectionRegion"));
    }

    /// <summary>A point of the frame (0–1) in window coordinates.</summary>
    private static Point At(DetectionRegionEditor editor, Window window, double x, double y)
    {
        ViewRect image = editor.ImageRect;
        return editor.TranslatePoint(new Point(image.X + (x * image.Width), image.Y + (y * image.Height)), window)!.Value;
    }

    private static void Drag(Window window, Point from, Point to)
    {
        window.MouseMove(from);
        window.MouseDown(from, MouseButton.Left);
        // Several moves like a real drag; the region is written back only at the end.
        window.MouseMove(new Point((from.X + to.X) / 2, (from.Y + to.Y) / 2), RawInputModifiers.LeftMouseButton);
        window.MouseMove(to, RawInputModifiers.LeftMouseButton);
        window.MouseUp(to, MouseButton.Left);
        window.Settle();
    }

    private static void Press(Window window, PhysicalKey key, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        window.KeyPressQwerty(key, modifiers);
        window.KeyReleaseQwerty(key, modifiers);
        window.Settle();
    }

    private static void AssertRegion(DetectionRegion actual, double x, double y, double width, double height, double tolerance = 0.006)
    {
        Assert.Equal(x, actual.X, tolerance);
        Assert.Equal(y, actual.Y, tolerance);
        Assert.Equal(width, actual.Width, tolerance);
        Assert.Equal(height, actual.Height, tolerance);
    }

    [Fact]
    public Task DrawingMovingAndResizingWithTheMouseEditTheRegion() =>
        session.RunAsync(static async scope =>
        {
            (SettingsWindowHarness ui, DetectionRegionEditor editor) = await OpenAsync(scope, CoreOne());
            PrinterEditorViewModel printer = ui.ViewModel.SelectedPrinter!;
            Assert.Equal(DetectionRegion.CoreOneBed, editor.Region);

            // Outside the region: a new one.
            Drag(ui.Window, At(editor, ui.Window, 0.1, 0.1), At(editor, ui.Window, 0.5, 0.4));
            AssertRegion(printer.DetectionRegion, 0.1, 0.1, 0.4, 0.3);
            Assert.True(ui.ViewModel.IsDirty);

            // Inside it: moved, the size kept.
            Drag(ui.Window, At(editor, ui.Window, 0.3, 0.25), At(editor, ui.Window, 0.5, 0.45));
            AssertRegion(printer.DetectionRegion, 0.3, 0.3, 0.4, 0.3);

            // The bottom right corner: resized, the top left stays; beyond the frame it stops at the edge.
            Drag(ui.Window, At(editor, ui.Window, 0.7, 0.6), At(editor, ui.Window, 1.3, 0.8));
            AssertRegion(printer.DetectionRegion, 0.3, 0.3, 0.7, 0.5);
            Assert.False(editor.IsDragging);
        });

    [Fact]
    public Task WithA4By3FrameTheBandsAreNotPartOfTheFrame() =>
        session.RunAsync(static async scope =>
        {
            // 4:3 in the 16:9 editor: bands left and right. A drag from the left band to the middle starts at the left edge.
            FakeLastCameraFrames frames = new() { Frame = static _ => new SolidImage(new Size(640, 480)) };
            (SettingsWindowHarness ui, DetectionRegionEditor editor) = await OpenAsync(scope, CoreOne(), lastFrames: frames);
            await Ui.UntilAsync(() => editor.Source is not null, "the last frame is shown");
            ui.Window.Settle();
            ViewRect image = editor.ImageRect;
            // 4:3 in 256 × 144: 192 wide, 32 on each side.
            Assert.Equal(192, image.Width, 0.01);
            Assert.Equal(32, image.X, 0.01);

            Point band = editor.TranslatePoint(new Point(5, image.Height * 0.2), ui.Window)!.Value;
            Drag(ui.Window, band, At(editor, ui.Window, 0.5, 0.5));

            AssertRegion(ui.ViewModel.SelectedPrinter!.DetectionRegion, 0, 0.2, 0.5, 0.3);
        });

    [Fact]
    public Task TheKeyboardMovesAndResizesTheRegion() =>
        session.RunAsync(static async scope =>
        {
            (SettingsWindowHarness ui, DetectionRegionEditor editor) = await OpenAsync(scope, CoreOne());
            PrinterEditorViewModel printer = ui.ViewModel.SelectedPrinter!;
            Assert.True(editor.Focus());

            Press(ui.Window, PhysicalKey.ArrowLeft);
            Press(ui.Window, PhysicalKey.ArrowUp);
            AssertRegion(printer.DetectionRegion, 0.14, 0.54, 0.70, 0.45, 1e-9);
            Press(ui.Window, PhysicalKey.ArrowRight, RawInputModifiers.Shift);
            Press(ui.Window, PhysicalKey.ArrowUp, RawInputModifiers.Shift);
            AssertRegion(printer.DetectionRegion, 0.14, 0.54, 0.71, 0.44, 1e-9);

            // A large step (Ctrl, ⌘ on macOS) stops at the edge, and the arrow stays in the editor (no sub-tab switch).
            RawInputModifiers command = OperatingSystem.IsMacOS() ? RawInputModifiers.Meta : RawInputModifiers.Control;
            Press(ui.Window, PhysicalKey.ArrowDown, command);
            AssertRegion(printer.DetectionRegion, 0.14, 0.56, 0.71, 0.44, 1e-9);
            Assert.True(ui.Find<TabItem>("PrinterTabDetection").IsSelected);
            Assert.True(editor.IsFocused);

            // The name for screen readers follows, in whole percent.
            Assert.Equal(
                Strings.SettingsDetectionRegionName.Replace("{0}", "14", StringComparison.Ordinal).Replace("{1}", "56", StringComparison.Ordinal).Replace("{2}", "71", StringComparison.Ordinal).Replace("{3}", "44", StringComparison.Ordinal),
                AutomationProperties.GetName(editor));
            Assert.Equal(Strings.SettingsDetectionRegionHint, AutomationProperties.GetHelpText(editor));
        });

    [Fact]
    public Task EscapeCancelsADrag() =>
        session.RunAsync(static async scope =>
        {
            (SettingsWindowHarness ui, DetectionRegionEditor editor) = await OpenAsync(scope, CoreOne());
            Point from = At(editor, ui.Window, 0.05, 0.05);
            ui.Window.MouseMove(from);
            ui.Window.MouseDown(from, MouseButton.Left);
            ui.Window.MouseMove(At(editor, ui.Window, 0.4, 0.4), RawInputModifiers.LeftMouseButton);
            Assert.True(editor.IsDragging);

            Press(ui.Window, PhysicalKey.Escape);
            ui.Window.MouseUp(At(editor, ui.Window, 0.4, 0.4), MouseButton.Left);
            ui.Window.Settle();

            Assert.Equal(DetectionRegion.CoreOneBed, ui.ViewModel.SelectedPrinter!.DetectionRegion);
            Assert.False(ui.ViewModel.IsDirty);
        });

    [Fact]
    public Task AClickOutsideTheRegionFocusesTheEditorAndKeepsTheRegion() =>
        session.RunAsync(static async scope =>
        {
            (SettingsWindowHarness ui, DetectionRegionEditor editor) = await OpenAsync(scope, CoreOne());
            Point point = At(editor, ui.Window, 0.05, 0.05);
            Point jitter = point + new Point(DetectionRegionEditor.DrawThreshold / 2, DetectionRegionEditor.DrawThreshold / 2);

            // A click (with a jitter below the threshold) draws nothing: it only focuses the editor for the keyboard.
            ui.Window.MouseMove(point);
            ui.Window.MouseDown(point, MouseButton.Left);
            ui.Window.MouseMove(jitter, RawInputModifiers.LeftMouseButton);
            ui.Window.MouseUp(jitter, MouseButton.Left);
            ui.Window.Settle();

            Assert.True(editor.IsFocused);
            Assert.False(editor.IsDragging);
            Assert.Equal(DetectionRegion.CoreOneBed, ui.ViewModel.SelectedPrinter!.DetectionRegion);
            Assert.False(ui.ViewModel.IsDirty);
        });

    [Fact]
    public Task TheButtonsSetTheWholeFrameOrTheCoreOneRegionAndTheSettingsAreSaved() =>
        session.RunAsync(static async scope =>
        {
            FakeEditorStore store = SettingsWindowHarness.StoreWith(CoreOne());
            (SettingsWindowHarness ui, DetectionRegionEditor editor) = await OpenAsync(scope, CoreOne(), store: store);

            ui.Click("PrinterDetectionFullFrame");
            Assert.Equal(DetectionRegion.Full, editor.Region);
            ui.Click("PrinterDetectionCoreOne");
            Assert.Equal(DetectionRegion.CoreOneBed, editor.Region);
            ui.Click("PrinterDetectionEnabled");
            ui.Click("PrinterDetector-bed-change");
            await ui.SaveAsync();

            PrinterDetectionSettings saved = store.Saves[^1].Updated.Printers[0].Detection;
            Assert.True(saved.Enabled);
            Assert.Equal(DetectionRegion.CoreOneBed, saved.Region);
            Assert.False(saved.For(DetectionFakes.BedChange.Id).Enabled);
        });

    [Fact]
    public Task DefaultForCoreOneIsOfferedOnlyForACoreOne() =>
        session.RunAsync(static async scope =>
        {
            (SettingsWindowHarness ui, _) = await OpenAsync(scope, LinkPrinter("mk4", "MK4") with { Camera = Camera });

            Assert.False(ui.Window.IsShown<Button>("PrinterDetectionCoreOne"));
            Assert.True(ui.Window.IsShown<Button>("PrinterDetectionFullFrame"));
            Assert.True(ui.Window.IsShown<TextBlock>("PrinterDetectionNoFrame"));
            Assert.True(ui.Window.IsShown<TextBlock>("PrinterDetectionGloballyOff"));
        });

    [Fact]
    public Task WithTheCameraOffTheFieldsAreDisabledWithTheReason() =>
        session.RunAsync(static async scope =>
        {
            (SettingsWindowHarness ui, DetectionRegionEditor editor) = await OpenAsync(scope, CoreOne() with { Camera = CameraSettings.Disabled });

            Assert.True(ui.Window.IsShown<TextBlock>("PrinterDetectionCameraOff"));
            Assert.False(ui.Find<CheckBox>("PrinterDetectionEnabled").IsEffectivelyEnabled);
            Assert.False(editor.IsEffectivelyEnabled);
            Assert.False(ui.Find<Slider>("PrinterDetectorSensitivity-bed-change").IsEffectivelyEnabled);
        });

    [Fact]
    public Task TheLastFrameIsShownUntilLoadFrameTakesANewOne() =>
        session.RunAsync(static async scope =>
        {
            FakeLastCameraFrames frames = new() { Frame = static _ => new SolidImage(new Size(1280, 720)) };
            CameraImage image = TestCameraImages.Quadrants(320, 180, 0xFFE05A1E, 0xFF2E7D32, 0xFF1565C0, 0xFFF9A825);
            FakeCameraTester tester = new() { Immediate = new CameraTestResult(CameraTestOutcome.Success, 320, 180, Image: image) };
            (SettingsWindowHarness ui, DetectionRegionEditor editor) = await OpenAsync(scope, CoreOne(), tester, frames);
            await Ui.UntilAsync(() => editor.Source is SolidImage, "the last frame is shown");
            ui.Window.Settle();
            Assert.True(ui.Window.IsShown<TextBlock>("PrinterDetectionLastFrame"));
            Assert.False(ui.Window.IsShown<TextBlock>("PrinterDetectionNoFrame"));

            ui.Click("PrinterDetectionLoadFrame");
            await Ui.UntilAsync(() => editor.Source is not SolidImage and not null, "the new frame is shown");
            ui.Window.Settle();

            Assert.False(ui.Window.IsShown<TextBlock>("PrinterDetectionLastFrame"));
            Assert.Equal([("core", 320, 180)], frames.Saved);
            Assert.Single(tester.Calls);
        });

    [Fact]
    public Task TheGlobalSwitchOnTheRecordingTabChangesTheHintOfThePrinter() =>
        session.RunAsync(static async scope =>
        {
            (SettingsWindowHarness ui, _) = await OpenAsync(scope, CoreOne());
            Assert.True(ui.Window.IsShown<TextBlock>("PrinterDetectionGloballyOff"));

            ui.Click("SettingsTabRecording");
            Assert.False(ui.Find<CheckBox>("SettingsDetectionEnabled").IsChecked);
            Assert.True(ui.Find<CheckBox>("SettingsDetectionSaveSnapshots").IsChecked);
            ui.Click("SettingsDetectionEnabled");
            ui.Click("SettingsTabPrinters");

            Assert.True(ui.Window.IsShown<TextBlock>("PrinterDetectionRunsHint"));
            Assert.False(ui.Window.IsShown<TextBlock>("PrinterDetectionGloballyOff"));
        });

    [Fact]
    public Task AnIntervalOutsideTheRangeShowsItsErrorAndFocusesTheFieldOnSave() =>
        session.RunAsync(static async scope =>
        {
            FakeEditorStore store = SettingsWindowHarness.StoreWith(CoreOne());
            (SettingsWindowHarness ui, _) = await OpenAsync(scope, CoreOne(), store: store);
            ui.ViewModel.DetectionIntervalSeconds = 6;
            ui.ViewModel.SelectedTabIndex = SettingsViewModel.GeneralTab;

            await ui.ViewModel.SaveCommand.ExecuteAsync(null);
            ui.Window.Settle();

            Assert.Empty(store.Saves);
            Assert.True(ui.Find<TabItem>("SettingsTabRecording").IsSelected);
            Assert.True(ui.Window.IsShown<TextBlock>("SettingsDetectionIntervalError"));
            NumericUpDown interval = ui.Find<NumericUpDown>("SettingsDetectionInterval");
            Assert.True(interval.IsKeyboardFocusWithin);
            Assert.Contains("invalid", interval.Classes);
            Assert.Equal(ui.ViewModel.DetectionIntervalError, AutomationProperties.GetHelpText(interval));
            Assert.Equal(SettingsViewModel.DetectionIntervalHint, ToolTip.GetTip(interval));
            Assert.Equal(Strings.SettingsDetectionEnabledHint, ToolTip.GetTip(ui.Find<CheckBox>("SettingsDetectionEnabled")));
        });

    /// <summary>T-163: the experimental dark filament mode: off by default, with the badge and its hint, saved per printer.</summary>
    [Fact]
    public Task TheDarkFilamentModeIsOffByDefaultAndSaved() =>
        session.RunAsync(static async scope =>
        {
            FakeEditorStore store = SettingsWindowHarness.StoreWith(CoreOne());
            (SettingsWindowHarness ui, _) = await OpenAsync(scope, CoreOne(), store: store);
            CheckBox darkFilament = ui.Find<CheckBox>("PrinterDetectionDarkFilament");
            Assert.True(ui.Window.IsShown<CheckBox>("PrinterDetectionDarkFilament"));
            Assert.True(ui.Window.IsShown<Border>("PrinterDetectionDarkFilamentExperimental"));
            Assert.False(darkFilament.IsChecked);
            Assert.Equal(Strings.SettingsDetectionDarkFilament, darkFilament.Content);
            Assert.Equal(Strings.SettingsDetectionDarkFilamentHint, ToolTip.GetTip(darkFilament));
            Assert.Equal(Strings.SettingsDetectionDarkFilamentHint, AutomationProperties.GetHelpText(darkFilament));

            ui.Click("PrinterDetectionDarkFilament");
            Assert.True(ui.ViewModel.SelectedPrinter!.DetectionDarkFilament);
            Assert.True(ui.ViewModel.IsDirty);
            await ui.ViewModel.SaveCommand.ExecuteAsync(null);

            PrinterDetectionSettings saved = Assert.Single(store.Saves).Updated.Printers[0].Detection;
            Assert.True(saved.DarkFilament);
            Assert.False(saved.Enabled);
        });

    [Fact]
    public Task TheDetectorTextsFollowALanguageSwitch() =>
        session.RunAsync(static async scope =>
        {
            (SettingsWindowHarness ui, _) = await OpenAsync(scope, CoreOne());
            CheckBox detector = ui.Find<CheckBox>("PrinterDetector-bed-change");
            Assert.Equal(Strings.DetectorBedChange, detector.Content);

            Assert.True(new UiLanguage().Apply("cs"));
            ui.ViewModel.RefreshTexts();
            ui.Window.Settle();

            Assert.Equal("Změna na podložce", detector.Content);
            Assert.True(Ui.ShowsText(ui.Window, "Známé omezení: u tmavého filamentu na černém plátu je méně spolehlivý."));
        });

    public static TheoryData<string> Languages => [.. AppLanguages.Supported];

    /// <summary>
    /// The detection is experimental (user decision 2026-11-04, the badge of the timelapse, T-161): right of the switch of
    /// the printer and next to the heading of the section on the Recording tab, with its own hint, in every language.
    /// </summary>
    [Theory]
    [MemberData(nameof(Languages))]
    public Task TheDetectionHasTheExperimentalBadge(string language) =>
        session.RunAsync(async scope =>
        {
            Assert.True(new UiLanguage().Apply(language));
            try
            {
                (SettingsWindowHarness ui, _) = await OpenAsync(scope, CoreOne());

                CheckBox enabled = ui.Find<CheckBox>("PrinterDetectionEnabled");
                Border printerBadge = ui.Find<Border>("PrinterDetectionExperimental");
                AssertBadge(printerBadge, ui.Window);
                Rect box = Bounds(enabled, ui.Window);
                Rect badge = Bounds(printerBadge, ui.Window);
                Assert.True(badge.Left >= box.Right, $"{language}: the badge is not right of the check box.");
                Assert.True(badge.Top >= box.Top && badge.Bottom <= box.Bottom, $"{language}: the badge is not on the row of the check box.");

                ui.Click("SettingsTabRecording");
                ui.Window.Settle();
                AssertBadge(ui.Find<Border>("SettingsDetectionExperimental"), ui.Window);
            }
            finally
            {
                Assert.True(new UiLanguage().Apply("en"));
            }
        });

    private static void AssertBadge(Border badge, Window window)
    {
        Assert.True(badge.IsEffectivelyVisible);
        TextBlock text = Assert.IsType<TextBlock>(badge.Child);
        Assert.Equal(Strings.SettingsExperimental, text.Text);
        Assert.Equal(Strings.SettingsDetectionExperimentalHint, ToolTip.GetTip(badge));
        // The tip is mouse-only (the badge takes no focus): screen readers get its meaning as the help text.
        Assert.Equal(Strings.SettingsDetectionExperimentalHint, AutomationProperties.GetHelpText(badge));
        Assert.True(Bounds(badge, window).Right <= window.ClientSize.Width);
    }

    private static Rect Bounds(Control control, Window window) =>
        control.TranslatePoint(default, window) is { } origin ? new Rect(origin, control.Bounds.Size) : throw new InvalidOperationException("Not in the window.");

    /// <summary>A plain image of a given size (the last saved frame of a test).</summary>
    private sealed class SolidImage(Size size) : IImage
    {
        public Size Size { get; } = size;

        public void Draw(DrawingContext context, Rect sourceRect, Rect destRect) => context.FillRectangle(Brushes.SteelBlue, destRect);
    }
}
