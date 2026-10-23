using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;

using PrusaTrayDiagnostic.App.Localization;
using PrusaTrayDiagnostic.App.Resources;
using PrusaTrayDiagnostic.App.Tray.Menu;
using PrusaTrayDiagnostic.App.UiTests.Infrastructure;

namespace PrusaTrayDiagnostic.App.UiTests.Tray;

/// <summary>
/// Access keys of the tray menu that Avalonia draws itself on Windows (T-042 point 2, T-069 Q2): the letter alone runs the
/// item, like in classic Windows menus and in Avalonia's own submenus, with or without a preceding Alt press. The window
/// stands in for Avalonia.Win32's internal tray popup root: a borderless window whose content is a menu flyout presenter
/// that closes the window (TrayIconImpl.TrayIconMenuFlyoutPresenter). The app's registration in <c>App.Initialize</c> is
/// what makes it work, so nothing here registers it.
/// </summary>
public sealed class TrayMenuKeyboardUiTests(HeadlessSession session)
{
    [Theory]
    [InlineData("en")]
    [InlineData("cs")]
    public Task LetterAloneRunsTheItemOfTheMainMenu(string language) =>
        session.RunAsync(scope =>
        {
            Assert.True(new UiLanguage().Apply(language));
            TrayRoot menu = TrayRoot.Show(scope);

            Press(menu.Window, AccessKeyOf(Strings.MenuSettings), RawInputModifiers.None);

            Assert.Equal([Strings.MenuSettings], menu.Clicked);
            Assert.False(menu.Window.IsVisible, "The menu stays open after its item ran.");
            return Task.CompletedTask;
        });

    [Fact]
    public Task LetterAfterAltRunsTheItem() =>
        session.RunAsync(static scope =>
        {
            TrayRoot menu = TrayRoot.Show(scope);

            // Alt pressed and released alone shows the underlines, then the letter alone.
            menu.Window.KeyPress(Key.LeftAlt, RawInputModifiers.Alt, PhysicalKey.AltLeft, null);
            menu.Window.KeyRelease(Key.LeftAlt, RawInputModifiers.None, PhysicalKey.AltLeft, null);
            menu.Window.Settle();
            Assert.True(menu.Window.IsVisible);
            Press(menu.Window, AccessKeyOf(Strings.MenuAbout), RawInputModifiers.None);

            Assert.Equal([Strings.MenuAbout], menu.Clicked);
            Assert.False(menu.Window.IsVisible);
            return Task.CompletedTask;
        });

    [Fact]
    public Task AltWithTheLetterStillRunsTheItem() =>
        session.RunAsync(static scope =>
        {
            TrayRoot menu = TrayRoot.Show(scope);

            Press(menu.Window, AccessKeyOf(Strings.MenuQuit), RawInputModifiers.Alt);

            Assert.Equal([Strings.MenuQuit], menu.Clicked);
            return Task.CompletedTask;
        });

    [Fact]
    public Task ShiftedLetterRunsTheItemToo() =>
        session.RunAsync(static scope =>
        {
            TrayRoot menu = TrayRoot.Show(scope);

            Press(menu.Window, AccessKeyOf(Strings.MenuSettings), RawInputModifiers.Shift);

            Assert.Equal([Strings.MenuSettings], menu.Clicked);
            return Task.CompletedTask;
        });

    [Fact]
    public Task LetterOfADisabledItemOrOfNoItemDoesNothing() =>
        session.RunAsync(static scope =>
        {
            TrayRoot menu = TrayRoot.Show(scope);

            Press(menu.Window, AccessKeyOf(Strings.MenuDiagnostics), RawInputModifiers.None);
            Press(menu.Window, 'Z', RawInputModifiers.None);

            Assert.Empty(menu.Clicked);
            Assert.True(menu.Window.IsVisible);
            return Task.CompletedTask;
        });

    [Fact]
    public Task ControlWithTheLetterIsNoAccessKey() =>
        session.RunAsync(static scope =>
        {
            TrayRoot menu = TrayRoot.Show(scope);

            Press(menu.Window, AccessKeyOf(Strings.MenuSettings), RawInputModifiers.Control);
            Press(menu.Window, AccessKeyOf(Strings.MenuSettings), RawInputModifiers.Control | RawInputModifiers.Alt);

            Assert.Empty(menu.Clicked);
            Assert.True(menu.Window.IsVisible);
            return Task.CompletedTask;
        });

    [Fact]
    public Task LetterAloneDoesNothingInOrdinaryWindows() =>
        session.RunAsync(static scope =>
        {
            // Other windows keep the usual rule: an access key needs Alt (a letter typed into a text box is just a letter).
            int clicks = 0;
            Button save = new() { Content = "_Save" };
            save.Click += (_, _) => clicks++;
            TextBox box = new();
            Window window = scope.Track(new Window { Width = 300, Height = 200, Content = new StackPanel { Children = { box, save } } });
            window.Show();
            window.Settle();

            Press(window, 'S', RawInputModifiers.None);
            Assert.Equal(0, clicks);
            box.Focus();
            window.Settle();
            Press(window, 'S', RawInputModifiers.None);
            Assert.Equal(0, clicks);

            Press(window, 'S', RawInputModifiers.Alt);
            Assert.Equal(1, clicks);
            return Task.CompletedTask;
        });

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
        char upper = char.ToUpperInvariant(RemoveDiacritics(letter));
        Key key = Enum.Parse<Key>(upper.ToString());
        PhysicalKey physical = Enum.Parse<PhysicalKey>(upper.ToString());
        string symbol = (modifiers & RawInputModifiers.Shift) != 0 ? upper.ToString() : char.ToLowerInvariant(upper).ToString();
        window.KeyPress(key, modifiers, physical, symbol);
        Dispatcher.UIThread.RunJobs();
        if (window.PlatformImpl is null)
        {
            // The item ran and closed the menu (its window) on the key press; the release goes nowhere.
            return;
        }

        window.KeyRelease(key, modifiers, physical, symbol);
        window.Settle();
    }

    private static char RemoveDiacritics(char letter) =>
        letter.ToString().Normalize(System.Text.NormalizationForm.FormD)[0];

    /// <summary>The stand-in for Avalonia.Win32's tray popup root and the items of the app's main menu.</summary>
    private sealed class TrayRoot
    {
        private TrayRoot(Window window) => Window = window;

        public Window Window { get; }

        /// <summary>Headers of the clicked items in order.</summary>
        public List<string> Clicked { get; } = [];

        public static TrayRoot Show(UiTestScope scope)
        {
            Window window = new()
            {
                WindowDecorations = WindowDecorations.None,
                SizeToContent = SizeToContent.WidthAndHeight,
                ShowInTaskbar = false,
                Topmost = true,
            };
            TrayRoot root = new(window);
            MenuItem printer = new() { Header = NativeMenuText.EscapeAccessKeys("MK4 workshop: Ready") };
            printer.Items.Add(root.Item(Strings.MenuOpenPrusaLink, enabled: true));
            window.Content = new TrayRootPresenter
            {
                ItemsSource = new Control[]
                {
                    printer,
                    new Separator(),
                    root.Item(Strings.MenuDiagnostics, enabled: false),
                    root.Item(Strings.MenuSettings, enabled: true),
                    new Separator(),
                    root.Item(Strings.MenuAbout, enabled: true),
                    root.Item(Strings.MenuQuit, enabled: true),
                },
            };
            scope.Track(window).Show();
            window.Settle();
            Dispatcher.UIThread.RunJobs();
            return root;
        }

        private MenuItem Item(string header, bool enabled)
        {
            MenuItem item = new() { Header = header, IsEnabled = enabled };
            item.Click += (_, _) => Clicked.Add(header);
            return item;
        }
    }

    /// <summary>Like Avalonia.Win32's TrayIconMenuFlyoutPresenter: closing the menu closes its window.</summary>
    private sealed class TrayRootPresenter : MenuFlyoutPresenter
    {
        protected override Type StyleKeyOverride => typeof(MenuFlyoutPresenter);

        public override void Close()
        {
            SelectedIndex = -1;
            (TopLevel.GetTopLevel(this) as Window)?.Close();
        }
    }
}
