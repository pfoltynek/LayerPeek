using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;

using LayerPeek.App.Cameras;
using LayerPeek.App.Localization;
using LayerPeek.App.UiTests.Infrastructure;
using LayerPeek.App.Windows;

namespace LayerPeek.App.UiTests.Cameras;

/// <summary>
/// The macOS shortcuts of the camera window (T-100, user decision 2026-10-29): ⌃⌘F toggles full screen next to F11, ⌘. is
/// Esc, ⌘W closes the window; the texts name ⌃⌘F. Forced through <see cref="CameraViewModel.UsesMacShortcuts"/> on every
/// OS; the Windows variant (F11 only, no ⌘ shortcuts) is checked the same way.
/// </summary>
public sealed class CameraWindowMacShortcutsUiTests(HeadlessSession session)
{
    private const RawInputModifiers Command = RawInputModifiers.Meta;

    private static CameraViewModel ViewModel(CameraWindow window) => (CameraViewModel)window.DataContext!;

    private static void Press(CameraWindow window, Key key, PhysicalKey physical, RawInputModifiers modifiers, string? symbol = null)
    {
        window.KeyPress(key, modifiers, physical, symbol);
        window.KeyRelease(key, modifiers, physical, symbol);
        Dispatcher.UIThread.RunJobs();
        window.Settle();
    }

    private static void PressControlCommandF(CameraWindow window) => Press(window, Key.F, PhysicalKey.F, RawInputModifiers.Control | Command, "f");

    private static void PressCommandPeriod(CameraWindow window) => Press(window, Key.OemPeriod, PhysicalKey.Period, Command, ".");

    /// <summary>⌘W; only pressed when it closes the window (a closed headless window takes no more input).</summary>
    private static void PressCommandW(CameraWindow window, bool closes = true)
    {
        if (!closes)
        {
            Press(window, Key.W, PhysicalKey.W, Command, "w");
            return;
        }

        window.KeyPress(Key.W, Command, PhysicalKey.W, "w");
        Dispatcher.UIThread.RunJobs();
    }

    private static CameraWindow OpenNormal(CameraHarness harness)
    {
        CameraWindow window = harness.Open();
        window.Width = 960;
        window.Height = 540;
        window.Settle();
        Assert.True(ViewModel(window).IsNormal);
        return window;
    }

    [Fact]
    public Task ControlCommandFTogglesFullScreenWhileF11AndEscStillWork() =>
        session.RunAsync(static async scope =>
        {
            await using CameraHarness harness = new(macShortcuts: true);
            CameraWindow window = OpenNormal(harness);
            CameraViewModel viewModel = ViewModel(window);

            PressControlCommandF(window);
            Assert.True(viewModel.IsFullScreen);
            Assert.Equal(WindowState.FullScreen, window.WindowState);

            PressControlCommandF(window);
            Assert.True(viewModel.IsNormal);
            Assert.Equal(WindowState.Normal, window.WindowState);

            // F11 stays as a fallback (fn+F11 on a Mac keyboard), Esc leaves full screen.
            Press(window, Key.F11, PhysicalKey.F11, RawInputModifiers.None);
            Assert.True(viewModel.IsFullScreen);
            Press(window, Key.Escape, PhysicalKey.Escape, RawInputModifiers.None);
            Assert.True(viewModel.IsNormal);

            // Only exactly ⌃⌘F: ⌘F and ⌃F do nothing.
            Press(window, Key.F, PhysicalKey.F, Command, "f");
            Press(window, Key.F, PhysicalKey.F, RawInputModifiers.Control, "f");
            Assert.True(viewModel.IsNormal);
        });

    [Fact]
    public Task CommandPeriodIsEscInFullScreenAndTheMiniPreview() =>
        session.RunAsync(static async scope =>
        {
            await using CameraHarness harness = new(macShortcuts: true);
            CameraWindow window = OpenNormal(harness);
            CameraViewModel viewModel = ViewModel(window);

            viewModel.ShowMiniCommand.Execute(null);
            window.Settle();
            Assert.True(viewModel.IsMini);
            PressControlCommandF(window);
            Assert.True(viewModel.IsFullScreen);

            // Like Esc: back to the mode full screen was entered from, then from the mini preview to the normal window.
            PressCommandPeriod(window);
            Assert.True(viewModel.IsMini);
            PressCommandPeriod(window);
            Assert.True(viewModel.IsNormal);

            // In the normal window ⌘. does nothing, like Esc; the window stays open.
            PressCommandPeriod(window);
            Assert.True(viewModel.IsNormal);
            Assert.True(window.IsVisible);
        });

    [Fact]
    public Task CommandPeriodDiscardsTheLayoutModeLikeEsc() =>
        session.RunAsync(static async scope =>
        {
            await using CameraHarness harness = new(macShortcuts: true);
            CameraWindow window = OpenNormal(harness);
            CameraViewModel viewModel = ViewModel(window);
            window.Click(window.Find<Button>("CameraLayoutButton"));
            Dispatcher.UIThread.RunJobs();
            window.Settle();
            Assert.True(viewModel.IsLayoutModeActive);

            PressCommandPeriod(window);

            Assert.False(viewModel.IsLayoutModeActive);
            Assert.True(viewModel.IsNormal);
            Assert.True(window.IsVisible);
        });

    [Fact]
    public Task CommandWClosesTheWindowInEveryMode() =>
        session.RunAsync(static async scope =>
        {
            await using CameraHarness harness = new(macShortcuts: true);
            CameraWindow normal = OpenNormal(harness);
            PressCommandW(normal);
            Assert.False(normal.IsVisible);
            Assert.False(harness.Windows.OpenWindows.ContainsKey("a"));

            CameraWindow full = harness.Open("b");
            PressControlCommandF(full);
            Assert.True(ViewModel(full).IsFullScreen);
            PressCommandW(full);
            Assert.False(full.IsVisible);

            CameraWindow mini = OpenNormal(harness);
            ViewModel(mini).ShowMiniCommand.Execute(null);
            mini.Settle();
            PressCommandW(mini);
            Assert.False(mini.IsVisible);
            Assert.Empty(harness.Windows.OpenWindows);
        });

    [Fact]
    public Task WindowsVariantKeepsF11AndIgnoresTheCommandShortcuts() =>
        session.RunAsync(static async scope =>
        {
            await using CameraHarness harness = new(macShortcuts: false);
            CameraWindow window = OpenNormal(harness);
            CameraViewModel viewModel = ViewModel(window);
            Assert.Equal("Full screen (F11)", ToolTip.GetTip(window.Find<Button>("CameraFullScreenButton")));
            Assert.Equal("Camera image. Double-click switches full screen; F11 full screen, Esc back.", viewModel.ImageHint);

            PressControlCommandF(window);
            Assert.True(viewModel.IsNormal);
            PressCommandW(window, closes: false);
            Assert.True(window.IsVisible);

            Press(window, Key.F11, PhysicalKey.F11, RawInputModifiers.None);
            Assert.True(viewModel.IsFullScreen);
            PressCommandPeriod(window);
            Assert.True(viewModel.IsFullScreen);
            Press(window, Key.Escape, PhysicalKey.Escape, RawInputModifiers.None);
            Assert.True(viewModel.IsNormal);
        });

    [Fact]
    public Task SwitchingTheVariantUpdatesTheTexts() =>
        session.RunAsync(static async scope =>
        {
            await using CameraHarness harness = new(macShortcuts: false);
            CameraWindow window = OpenNormal(harness);
            Button fullScreen = window.Find<Button>("CameraFullScreenButton");

            ViewModel(window).UsesMacShortcuts = true;
            window.Settle();

            Assert.Equal("Full screen (⌃⌘F)", ToolTip.GetTip(fullScreen));
            Assert.Equal("Full screen (⌃⌘F)", AutomationProperties.GetName(fullScreen));
        });

    public static TheoryData<string> Languages => ["en", "cs", "de", "es", "fr", "it", "pl", "sk"];

    [Theory]
    [MemberData(nameof(Languages))]
    public Task MacTextsNameControlCommandFAndTheTipFitsInEveryLanguage(string language) =>
        session.RunAsync(async scope =>
        {
            await using CameraHarness harness = new(macShortcuts: true);
            new UiLanguage().Apply(language);
            CameraWindow window = OpenNormal(harness);
            Button fullScreen = window.Find<Button>("CameraFullScreenButton");

            string tip = Assert.IsType<string>(ToolTip.GetTip(fullScreen));
            Assert.Equal(LocalizedStrings.Instance["CameraFullScreenMac"], tip);
            Assert.EndsWith("(⌃⌘F)", tip, StringComparison.Ordinal);
            Assert.Equal(tip, AutomationProperties.GetName(fullScreen));
            string hint = ViewModel(window).ImageHint;
            Assert.Equal(LocalizedStrings.Instance["CameraImageHintMac"], hint);
            Assert.Contains("⌃⌘F", hint, StringComparison.Ordinal);
            Assert.DoesNotContain("F11", hint, StringComparison.Ordinal);
            Assert.NotNull(Ui.Find<Control>(window, control => AutomationProperties.GetName(control) == hint));

            // The tip as the user sees it: on one line, nothing cut.
            ToolTip.SetIsOpen(fullScreen, true);
            Dispatcher.UIThread.RunJobs();
            window.Settle();
            try
            {
                TextBlock text = TipText(fullScreen, tip);
                text.Measure(Size.Infinity);
                Assert.Single(text.TextLayout.TextLines);
                Assert.False(text.TextLayout.TextLines.Any(static line => line.HasCollapsed), $"{language}: '{tip}' is cut.");
                Assert.True(text.Bounds.Width + 0.5 >= text.DesiredSize.Width, $"{language}: '{tip}' {text.Bounds.Width} < {text.DesiredSize.Width}.");
            }
            finally
            {
                ToolTip.SetIsOpen(fullScreen, false);
                Dispatcher.UIThread.RunJobs();
            }
        });

    /// <summary>
    /// The text block of the open tip of <paramref name="control"/>: the tip is a popup of its own, not in the window; its
    /// <see cref="ToolTip"/> is kept in an attached property of the control (found in Avalonia's property registry).
    /// </summary>
    private static TextBlock TipText(Control control, string tip)
    {
        AvaloniaProperty property = AvaloniaPropertyRegistry.Instance.GetRegisteredAttached(control.GetType())
            .Single(static p => p.OwnerType == typeof(ToolTip) && p.PropertyType == typeof(ToolTip));
        ToolTip toolTip = Assert.IsType<ToolTip>(control.GetValue(property));
        Assert.True(toolTip.IsEffectivelyVisible);
        return toolTip.GetVisualDescendants().OfType<TextBlock>().Single(block => block.Text == tip);
    }
}
