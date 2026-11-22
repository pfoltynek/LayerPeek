using System.Globalization;

using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;

using LayerPeek.App.Cameras;
using LayerPeek.App.Localization;
using LayerPeek.App.Tests.Cameras;
using LayerPeek.App.Tests.Cameras.Recording;
using LayerPeek.App.UiTests.Infrastructure;
using LayerPeek.App.Windows;
using LayerPeek.Core.Cameras;
using LayerPeek.Core.Cameras.Recording;

namespace LayerPeek.App.UiTests.Cameras;

/// <summary>
/// T-251 (design <c>docs/design/mini-preview-fps-badge.md</c>, user decisions 2026-11-22): the frame rate badge in the top
/// left corner of the mini preview's picture. Only while the picture streams in the mini preview; clear of the resize grips
/// and of the window buttons in every language down to the smallest mini preview, otherwise faded out (decision A) until
/// there is room again; no pointer target, so dragging over it still moves the window; one line whose width does not jump
/// between one and two digits; a name for screen readers, no live region.
/// </summary>
public sealed class CameraWindowMiniFrameRateBadgeUiTests(HeadlessSession session)
{
    /// <summary>The design: 8 DIP from the left edge (clear of the 6 DIP grips), 8 DIP to the buttons.</summary>
    private const double Edge = 8;

    private const double Gap = 8;

    private static readonly CameraWindowDefaults MiniFirst = CameraWindowDefaults.Default with { StartMode = CameraWindowStartMode.Mini };

    /// <summary>The region (numbers) that belongs to each app language.</summary>
    private static readonly Dictionary<string, string> Regions = new(StringComparer.Ordinal)
    {
        ["en"] = "en-US", ["cs"] = "cs-CZ", ["sk"] = "sk-SK", ["de"] = "de-DE", ["es"] = "es-ES", ["fr"] = "fr-FR", ["it"] = "it-IT", ["pl"] = "pl-PL",
    };

    public static TheoryData<string, bool> LanguagesWithAndWithoutLimit()
    {
        TheoryData<string, bool> data = [];
        foreach (string language in Regions.Keys)
        {
            data.Add(language, true);
            data.Add(language, false);
        }

        return data;
    }

    private static CameraViewModel ViewModel(CameraWindow window) => (CameraViewModel)window.DataContext!;

    private static Border Badge(CameraWindow window) => window.Find<Border>("CameraMiniFrameRateBadge");

    private static TextBlock BadgeText(CameraWindow window) => Ui.Find<TextBlock>(Badge(window), static _ => true);

    private static Rect BoundsIn(Control control, Visual root) =>
        new(control.TranslatePoint(default, root) ?? throw new InvalidOperationException("Not in the window."), control.Bounds.Size);

    private static void Settle(CameraWindow window)
    {
        Dispatcher.UIThread.RunJobs();
        window.Settle();
    }

    private static void MouseAway(CameraWindow window)
    {
        window.MouseMove(new Point(-10, -10), RawInputModifiers.None);
        Settle(window);
    }

    private static void UseLanguage(CameraHarness harness, string language)
    {
        CultureInfo culture = CultureInfo.GetCultureInfo(Regions[language]);
        // The body's culture and the default of the thread (work posted to the UI thread); the session restores both.
        CultureInfo.CurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentCulture = culture;
        Assert.True(new UiLanguage().Apply(language));
        harness.Windows.RefreshTexts();
    }

    /// <summary>
    /// The smallest mini preview (240 × 168: the 16:9 picture 135 and the strip 33) with its picture streaming, the pointer
    /// away and no fades.
    /// </summary>
    private static async Task<CameraWindow> OpenSmallestMiniAsync(CameraHarness harness)
    {
        (CameraWindow window, _) = await harness.OpenStreamingAsync(1280, 720, 0xFF406080);
        Assert.True(ViewModel(window).IsMini);
        CameraHarness.SkipWindowButtonFade(window);
        CameraHarness.ResizeMiniWithGrip(window, window.MinWidth, window.MinHeight);
        Assert.Equal(new Size(240, 168), window.ClientSize);
        MouseAway(window);
        return window;
    }

    /// <summary>The left edge of the leftmost window button that is drawn without the pointer, or the right edge less 6 DIP.</summary>
    private static double LeftOfButtonsShownAtRest(CameraWindow window)
    {
        double left = window.ClientSize.Width - 6;
        foreach (Control button in window.Find<StackPanel>("CameraWindowButtons").Children)
        {
            if (button.IsVisible && button.Opacity > 0)
            {
                left = Math.Min(left, BoundsIn(button, window).Left);
            }
        }

        return left;
    }

    private static double LeftOfAllButtons(CameraWindow window) =>
        window.Find<StackPanel>("CameraWindowButtons").Children.Where(static b => b.IsVisible).Min(b => BoundsIn(b, window).Left);

    private static bool IsShown(CameraWindow window) => Badge(window) is { IsEffectivelyVisible: true, Opacity: > 0 };

    [Theory]
    [MemberData(nameof(LanguagesWithAndWithoutLimit))]
    public Task TheBadgeFitsTheSmallestMiniPreviewInEveryLanguage(string language, bool limit) =>
        session.RunAsync(async scope =>
        {
            await using CameraHarness harness = new(screens: FakeScreenCatalog.ThreeMonitors(), window: MiniFirst);
            harness.SaveMiniFrameRate(limit ? MiniPreviewFrameRate.Default : null);
            CameraWindow window = await OpenSmallestMiniAsync(harness);
            UseLanguage(harness, language);
            Settle(window);

            Border badge = Badge(window);
            TextBlock text = BadgeText(window);
            Assert.True(IsShown(window), $"{language}: hidden");
            Assert.Equal(ViewModel(window).FrameRateBadgeText, text.Text);
            Rect bounds = BoundsIn(badge, window);
            Assert.True(bounds.Left >= Edge && bounds.Top >= 6, $"{language}: {bounds} in the grips");
            // In the picture (above the strip), and clear of the pinned button at rest.
            Assert.True(bounds.Bottom <= 135, $"{language}: {bounds} below the picture");
            Assert.True(bounds.Right + Gap <= LeftOfButtonsShownAtRest(window), $"{language}: {bounds} on the buttons");
            // One line, never cut.
            Assert.Equal(TextWrapping.NoWrap, text.TextWrapping);
            Assert.True(text.Bounds.Height < text.FontSize * 2, $"{language}: {text.Bounds.Height} DIP high");
            Assert.True(text.Bounds.Width >= text.DesiredSize.Width - text.Margin.Left - text.Margin.Right - 0.5, $"{language}: cut");
            // The screen reader's name, no live region.
            Assert.Equal(ViewModel(window).FrameRateBadgeName, AutomationProperties.GetName(badge));
            Assert.Equal(AutomationLiveSetting.Off, AutomationProperties.GetLiveSetting(badge));
            // The padded text itself is only drawn.
            Assert.Equal(AccessibilityView.Raw, AutomationProperties.GetAccessibilityView(text));
        });

    [Fact]
    public Task TheBadgeFadesOutWhereTheButtonsNeedTheRoomAndComesBack() =>
        session.RunAsync(static async scope =>
        {
            FakeCameraRecordings recordings = new();
            recordings.SetActivity("a", CameraRecordingActivity.Recording, raise: false);
            recordings.SetTimelapse("a", CameraRecordingActivity.Recording, raise: false);
            await using CameraHarness harness = new(screens: FakeScreenCatalog.ThreeMonitors(), window: MiniFirst, recordings: recordings);
            CameraWindow window = await OpenSmallestMiniAsync(harness);

            // At rest TL, REC and the pin leave room for "– / 15 fps".
            Assert.True(IsShown(window));
            Assert.True(BoundsIn(Badge(window), window).Right + Gap <= LeftOfButtonsShownAtRest(window));

            // The pointer over the picture shows every button: no room any more.
            window.MouseMove(new Point(120, 100), RawInputModifiers.None);
            Settle(window);
            Assert.True(BoundsIn(Badge(window), window).Right + Gap > LeftOfAllButtons(window));
            Assert.False(IsShown(window));
            Assert.True(window.IsFrameRateBadgeCramped);

            MouseAway(window);
            Assert.True(IsShown(window));

            // Keyboard focus in the buttons shows them too.
            window.Find<Button>("CameraRecordButton").Focus(NavigationMethod.Tab);
            Settle(window);
            Assert.False(IsShown(window));
            window.GetVisualDescendants().OfType<Border>().Single(static b => b.Name == "Surface").Focus();
            Settle(window);
            Assert.True(IsShown(window));
        });

    [Fact]
    public Task WithoutRecordingButtonsTheBadgeStaysWhileThePointerIsOverTheWindow() =>
        session.RunAsync(static async scope =>
        {
            await using CameraHarness harness = new(screens: FakeScreenCatalog.ThreeMonitors(), window: MiniFirst);
            CameraWindow window = await OpenSmallestMiniAsync(harness);

            window.MouseMove(new Point(120, 100), RawInputModifiers.None);
            Settle(window);

            Assert.True(IsShown(window));
            Assert.True(BoundsIn(Badge(window), window).Right + Gap <= LeftOfAllButtons(window));
        });

    [Fact]
    public Task TheBadgeIsNoPointerTarget() =>
        session.RunAsync(static async scope =>
        {
            await using CameraHarness harness = new(screens: FakeScreenCatalog.ThreeMonitors(), window: MiniFirst);
            CameraWindow window = await OpenSmallestMiniAsync(harness);
            Border badge = Badge(window);

            // A press on the badge reaches the picture, which moves the window.
            IInputElement? hit = window.InputHitTest(Ui.CentreOf(badge, window));

            Assert.False(badge.IsHitTestVisible);
            Assert.NotNull(hit);
            Assert.False(hit is Visual visual && (visual == badge || badge.IsVisualAncestorOf(visual)), $"{hit} takes the press");
            Assert.False(badge.Focusable);
        });

    [Fact]
    public Task TheWidthDoesNotJumpBetweenOneAndTwoDigits() =>
        session.RunAsync(static async scope =>
        {
            await using CameraHarness harness = new(screens: FakeScreenCatalog.ThreeMonitors(), window: MiniFirst);
            CameraWindow window = await OpenSmallestMiniAsync(harness);
            TextBlock text = BadgeText(window);

            List<double> widths = [];
            foreach (int? actual in new int?[] { null, 0, 9, 10, 15 })
            {
                text.Text = CameraViewModel.FormatFrameRateBadge(actual, MiniPreviewFrameRate.Default);
                Settle(window);
                widths.Add(Badge(window).Bounds.Width);
            }

            Assert.All(widths, width => Assert.Equal(widths[0], width, precision: 1));
        });

    [Fact]
    public Task TheBadgeIsOnlyInTheStreamingMiniPreview() =>
        session.RunAsync(static async scope =>
        {
            await using CameraHarness harness = new(screens: FakeScreenCatalog.ThreeMonitors(), window: MiniFirst);

            // Connecting: none.
            CameraWindow window = harness.Open();
            Settle(window);
            Assert.True(ViewModel(window).IsStatusVisible);
            Assert.False(Badge(window).IsEffectivelyVisible);

            FakeCameraFeed feed = await harness.Factory.WaitForFeedAsync(1);
            feed.SendColor(1280, 720, 0xFF406080);
            await Ui.UntilAsync(() => ViewModel(window).HasFrame && !ViewModel(window).IsStatusVisible, "frame shown");
            Settle(window);
            Assert.True(Badge(window).IsEffectivelyVisible);

            // The normal window and full screen: none.
            ViewModel(window).ShowNormalCommand.Execute(null);
            Settle(window);
            Assert.False(Badge(window).IsEffectivelyVisible);
            ViewModel(window).ShowFullScreenCommand.Execute(null);
            Settle(window);
            Assert.False(Badge(window).IsEffectivelyVisible);
            ViewModel(window).ShowMiniCommand.Execute(null);
            Settle(window);
            Assert.True(Badge(window).IsEffectivelyVisible);

            // Switched off in the settings: gone at once, and back.
            harness.SaveMiniFrameRate(MiniPreviewFrameRate.Default, badge: false);
            Settle(window);
            Assert.False(Badge(window).IsEffectivelyVisible);
            harness.SaveMiniFrameRate(MiniPreviewFrameRate.Default, badge: true);
            Settle(window);
            Assert.True(Badge(window).IsEffectivelyVisible);
        });

    [Fact]
    public Task SnapshotOfTheSmallestMiniPreviewWithTheBadge() =>
        session.RunAsync(static async scope =>
        {
            await using CameraHarness harness = new(screens: FakeScreenCatalog.ThreeMonitors(), window: MiniFirst);
            CameraWindow window = await OpenSmallestMiniAsync(harness);

            SnapshotAssert.Matches(window, "camera-mini-fps-badge");
        });
}
