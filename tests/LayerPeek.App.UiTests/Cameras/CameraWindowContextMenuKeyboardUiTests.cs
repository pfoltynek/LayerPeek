using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;

using LayerPeek.App.Cameras;
using LayerPeek.App.Localization;
using LayerPeek.App.Resources;
using LayerPeek.App.Tests.Cameras;
using LayerPeek.App.UiTests.Infrastructure;
using LayerPeek.App.Windows;
using LayerPeek.Core.Cameras;

namespace LayerPeek.App.UiTests.Cameras;

/// <summary>
/// Access keys of the camera window's context menu (T-121): like in the tray menu (T-069 Q2) and in classic Windows menus,
/// the letter alone runs the item and closes the menu, with or without a preceding Alt press, in the normal window ("Edit
/// layout", "Always on top") and in the mini preview ("Always on top" only); Alt+letter keeps working; a letter of no
/// visible, enabled item does nothing. macOS has no access keys in menus (the tray menu is a native NSMenu there), so the
/// letter alone stays without effect on macOS; Alt+letter is Avalonia's own behaviour on every platform. The app's
/// registration in <c>App.Initialize</c> is what makes it work, so nothing here registers it.
/// </summary>
public sealed class CameraWindowContextMenuKeyboardUiTests(HeadlessSession session)
{
    /// <summary>Whether the letter alone runs a menu item on this platform (not on macOS, T-121).</summary>
    private static readonly bool LetterAlone = !OperatingSystem.IsMacOS();

    private static CameraViewModel ViewModel(CameraWindow window) => (CameraViewModel)window.DataContext!;

    private static CameraHarness Harness() =>
        new(screens: FakeScreenCatalog.ThreeMonitors(), window: CameraWindowDefaults.Default with { TopmostNormal = false, TopmostMini = false });

    private static CameraWindow Open(CameraHarness harness)
    {
        CameraWindow window = harness.Open();
        window.Width = 960;
        window.Height = 540;
        window.Settle();
        CameraHarness.SkipWindowButtonFade(window);
        Assert.True(ViewModel(window).IsNormal);
        return window;
    }

    [Theory]
    [InlineData("en")]
    [InlineData("cs")]
    public Task LetterAloneRunsTheItemsOfTheNormalWindowAndClosesTheMenu(string language) =>
        session.RunAsync(async scope =>
        {
            Assert.True(new UiLanguage().Apply(language));
            await using CameraHarness harness = Harness();
            CameraWindow window = Open(harness);
            ContextMenu menu = window.ContextMenu!;

            RightClick(window);
            Assert.True(menu.IsOpen);
            Press(window, AccessKeyOf(Strings.CameraTopmostMenu), RawInputModifiers.None);
            Assert.Equal(LetterAlone, window.Topmost);
            Assert.Equal(!LetterAlone, menu.IsOpen);
            Close(window);

            RightClick(window);
            Press(window, AccessKeyOf(Strings.CameraLayoutEditMenu), RawInputModifiers.None);
            Assert.Equal(LetterAlone, ViewModel(window).LayoutMode.IsActive);
            Assert.Equal(!LetterAlone, menu.IsOpen);
            Close(window);
        });

    [Fact]
    public Task LetterAfterAltRunsTheItemOfTheMenuOpenedByShiftF10() =>
        session.RunAsync(static async scope =>
        {
            await using CameraHarness harness = Harness();
            CameraWindow window = Open(harness);
            ContextMenu menu = window.ContextMenu!;

            OpenByKeyboard(window);
            Assert.True(menu.IsOpen);
            // Alt pressed and released alone shows the underlines and keeps the menu open, then the letter alone.
            TopLevel popup = KeyTarget(window);
            popup.KeyPress(Key.LeftAlt, RawInputModifiers.Alt, PhysicalKey.AltLeft, null);
            popup.KeyRelease(Key.LeftAlt, RawInputModifiers.None, PhysicalKey.AltLeft, null);
            Settle(window);
            Assert.True(menu.IsOpen, "Alt closed the context menu.");
            Press(window, AccessKeyOf(Strings.CameraTopmostMenu), RawInputModifiers.None);

            Assert.Equal(LetterAlone, window.Topmost);
            Assert.Equal(!LetterAlone, menu.IsOpen);
            Close(window);
        });

    [Fact]
    public Task LetterAloneRunsTheItemOfTheMiniPreview() =>
        session.RunAsync(static async scope =>
        {
            await using CameraHarness harness = Harness();
            CameraWindow window = Open(harness);
            ContextMenu menu = window.ContextMenu!;
            Run(window, () => ViewModel(window).ShowMiniCommand.Execute(null));

            OpenByKeyboard(window);
            Assert.True(menu.IsOpen);
            Press(window, AccessKeyOf(Strings.CameraTopmostMenu), RawInputModifiers.None);

            Assert.Equal(LetterAlone, window.Topmost);
            Assert.Equal(!LetterAlone, menu.IsOpen);
            Assert.True(ViewModel(window).IsMini);
            Close(window);
        });

    [Fact]
    public Task AltWithTheLetterStillRunsTheItemInBothModes() =>
        session.RunAsync(static async scope =>
        {
            await using CameraHarness harness = Harness();
            CameraWindow window = Open(harness);
            ContextMenu menu = window.ContextMenu!;

            RightClick(window);
            Press(window, AccessKeyOf(Strings.CameraTopmostMenu), RawInputModifiers.Alt);
            Assert.True(window.Topmost);
            Assert.False(menu.IsOpen);

            Run(window, () => ViewModel(window).ShowMiniCommand.Execute(null));
            Assert.False(window.Topmost);
            RightClick(window);
            Press(window, AccessKeyOf(Strings.CameraTopmostMenu), RawInputModifiers.Alt);
            Assert.True(window.Topmost);
            Assert.False(menu.IsOpen);

            Run(window, () => ViewModel(window).ShowNormalCommand.Execute(null));
            RightClick(window);
            Press(window, AccessKeyOf(Strings.CameraLayoutEditMenu), RawInputModifiers.Alt);
            Assert.True(ViewModel(window).LayoutMode.IsActive);
            Assert.False(menu.IsOpen);
        });

    [Fact]
    public Task LetterOfNoItemOfAHiddenItemOrOfADisabledItemDoesNothing() =>
        session.RunAsync(static async scope =>
        {
            await using CameraHarness harness = Harness();
            CameraWindow window = Open(harness);
            ContextMenu menu = window.ContextMenu!;
            char layout = AccessKeyOf(Strings.CameraLayoutEditMenu);

            // No item has Z, with or without Alt.
            RightClick(window);
            Press(window, 'Z', RawInputModifiers.None);
            Press(window, 'Z', RawInputModifiers.Alt);
            Assert.True(menu.IsOpen);
            Assert.False(window.Topmost);
            Assert.False(ViewModel(window).LayoutMode.IsActive);
            Close(window);

            // "Edit layout" is disabled in a small window.
            window.Width = 400;
            window.Height = 250;
            window.Settle();
            RightClick(window);
            Press(window, layout, RawInputModifiers.None);
            Assert.True(menu.IsOpen);
            Assert.False(ViewModel(window).LayoutMode.IsActive);
            Close(window);

            // ... and hidden in the mini preview.
            Run(window, () => ViewModel(window).ShowMiniCommand.Execute(null));
            RightClick(window);
            Press(window, layout, RawInputModifiers.None);
            Assert.True(menu.IsOpen);
            Assert.True(ViewModel(window).IsMini);
            Assert.False(window.Topmost);
            Close(window);
        });

    [Fact]
    public Task ControlWithTheLetterIsNoAccessKey() =>
        session.RunAsync(static async scope =>
        {
            await using CameraHarness harness = Harness();
            CameraWindow window = Open(harness);
            ContextMenu menu = window.ContextMenu!;

            RightClick(window);
            Press(window, AccessKeyOf(Strings.CameraTopmostMenu), RawInputModifiers.Control);
            Press(window, AccessKeyOf(Strings.CameraTopmostMenu), RawInputModifiers.Control | RawInputModifiers.Alt);

            Assert.True(menu.IsOpen);
            Assert.False(window.Topmost);
            Close(window);
        });

    /// <summary>Opens the context menu with the right button over the image.</summary>
    private static void RightClick(Window window)
    {
        window.Settle();
        Point point = new(100, 100);
        window.MouseDown(point, MouseButton.Right);
        window.MouseUp(point, MouseButton.Right);
        Settle(window);
    }

    /// <summary>
    /// Opens the context menu from the keyboard: the context request without a pointer that Avalonia raises for the Menu key
    /// and Shift+F10 (the headless platform maps no key to it; the real keys: E2E <c>CameraPinTests</c>).
    /// </summary>
    private static void OpenByKeyboard(Window window)
    {
        window.RaiseEvent(new ContextRequestedEventArgs());
        Settle(window);
    }

    /// <summary>Where the keyboard goes: the top level of the open context menu (its own popup, as on Windows), else the window.</summary>
    private static TopLevel KeyTarget(Window window) =>
        window.ContextMenu is { IsOpen: true } menu && TopLevel.GetTopLevel(menu) is { } popup ? popup : window;

    private static void Close(Window window)
    {
        window.ContextMenu!.Close();
        Settle(window);
    }

    private static void Run(Window window, Action action)
    {
        action();
        Settle(window);
    }

    private static void Settle(Window window)
    {
        Dispatcher.UIThread.RunJobs();
        window.Settle();
    }

    /// <summary>The access key letter of a resource text (the character after the first "_").</summary>
    private static char AccessKeyOf(string text)
    {
        int marker = text.IndexOf('_', StringComparison.Ordinal);
        Assert.True(marker >= 0 && marker < text.Length - 1, $"'{text}' has no access key.");
        return text[marker + 1];
    }

    /// <summary>Presses and releases the key of <paramref name="letter"/> (A–Z) like Windows reports it (lower-case symbol without Shift).</summary>
    private static void Press(Window window, char letter, RawInputModifiers modifiers)
    {
        char upper = char.ToUpperInvariant(letter.ToString().Normalize(System.Text.NormalizationForm.FormD)[0]);
        Key key = Enum.Parse<Key>(upper.ToString());
        PhysicalKey physical = Enum.Parse<PhysicalKey>(upper.ToString());
        string symbol = (modifiers & RawInputModifiers.Shift) != 0 ? upper.ToString() : char.ToLowerInvariant(upper).ToString();
        TopLevel target = KeyTarget(window);
        target.KeyPress(key, modifiers, physical, symbol);
        Dispatcher.UIThread.RunJobs();
        if (target.PlatformImpl is not null)
        {
            // Not when the item ran and closed the menu (its popup) on the key press: the release goes nowhere.
            target.KeyRelease(key, modifiers, physical, symbol);
        }

        Settle(window);
    }
}
