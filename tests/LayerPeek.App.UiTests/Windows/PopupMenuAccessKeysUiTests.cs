using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;

using LayerPeek.App.UiTests.Infrastructure;

namespace LayerPeek.App.UiTests.Windows;

/// <summary>
/// The letter alone in any menu that Avalonia shows in a popup of its own (T-121): a <see cref="ContextMenu"/> and a
/// <see cref="MenuFlyout"/> (the printer image menu of the settings), like the tray menu (T-069 Q2) and classic Windows menus.
/// Several items with the same letter take turns in focus without running, as in Windows menus; other popups (a flyout with
/// a button) keep the usual rule that an access key needs Alt. Not on macOS, whose menus have no access keys. The app's
/// registration in <c>App.Initialize</c> is what makes it work, so nothing here registers it.
/// </summary>
public sealed class PopupMenuAccessKeysUiTests(HeadlessSession session)
{
    /// <summary>Whether the letter alone runs a menu item on this platform (not on macOS, T-121).</summary>
    private static readonly bool LetterAlone = !OperatingSystem.IsMacOS();

    [Fact]
    public Task LetterAloneRunsTheItemOfAContextMenu() =>
        session.RunAsync(static scope =>
        {
            List<string> clicked = [];
            ContextMenu menu = new() { ItemsSource = new[] { Item("_Open", clicked), Item("_Close", clicked) } };
            Border target = new() { Background = Avalonia.Media.Brushes.Gray, ContextMenu = menu };
            Window window = Show(scope, target);

            target.RaiseEvent(new ContextRequestedEventArgs());
            Settle(window);
            Assert.True(menu.IsOpen);
            Press(PopupOf(menu), 'C', RawInputModifiers.None);

            Assert.Equal(LetterAlone ? ["_Close"] : [], clicked);
            Assert.Equal(!LetterAlone, menu.IsOpen);
            menu.Close();
            return Task.CompletedTask;
        });

    [Fact]
    public Task SameLetterTakesTurnsInFocusWithoutRunningAnItem() =>
        session.RunAsync(static scope =>
        {
            List<string> clicked = [];
            MenuItem alpha = Item("_Alpha", clicked);
            MenuItem apple = Item("_Apple", clicked);
            ContextMenu menu = new() { ItemsSource = new[] { alpha, Item("_Beta", clicked), apple } };
            Border target = new() { Background = Avalonia.Media.Brushes.Gray, ContextMenu = menu };
            Window window = Show(scope, target);
            target.RaiseEvent(new ContextRequestedEventArgs());
            Settle(window);
            TopLevel popup = PopupOf(menu);

            Press(popup, 'A', RawInputModifiers.None);
            Assert.Equal(LetterAlone, alpha.IsFocused);
            Press(popup, 'A', RawInputModifiers.None);
            Assert.Equal(LetterAlone, apple.IsFocused);
            Press(popup, 'A', RawInputModifiers.Alt);
            Assert.True(alpha.IsFocused);

            Assert.Empty(clicked);
            Assert.True(menu.IsOpen);
            menu.Close();
            return Task.CompletedTask;
        });

    [Fact]
    public Task LetterInASubmenuIsLeftToTheSubmenu() =>
        session.RunAsync(static scope =>
        {
            List<string> clicked = [];
            MenuItem more = Item("_More", clicked);
            MenuItem other = Item("_Other", clicked);
            more.ItemsSource = new[] { other };
            ContextMenu menu = new() { ItemsSource = new[] { Item("_Open", clicked), more } };
            Border target = new() { Background = Avalonia.Media.Brushes.Gray, ContextMenu = menu };
            Window window = Show(scope, target);
            target.RaiseEvent(new ContextRequestedEventArgs());
            Settle(window);

            Press(PopupOf(menu), 'M', RawInputModifiers.Alt);
            Assert.True(more.IsSubMenuOpen);
            TopLevel submenu = TopLevel.GetTopLevel(other)!;
            Assert.NotSame(PopupOf(menu), submenu);
            // A letter typed in the submenu that bubbles up to the menu does not run "Open" of the menu.
            other.Focus();
            Settle(window);
            Press(submenu, 'O', RawInputModifiers.None);

            Assert.DoesNotContain("_Open", clicked);
            menu.Close();
            return Task.CompletedTask;
        });

    [Fact]
    public Task LetterAloneRunsTheItemOfAMenuFlyout() =>
        session.RunAsync(static scope =>
        {
            List<string> clicked = [];
            MenuFlyout flyout = new() { ItemsSource = new[] { Item("_Automatic", clicked), Item("_Custom", clicked) } };
            Button button = new() { Content = "Image", Flyout = flyout };
            Window window = Show(scope, button);

            flyout.ShowAt(button);
            Settle(window);
            Assert.True(flyout.IsOpen);
            TopLevel popup = TopLevel.GetTopLevel((Control)((MenuItem)flyout.Items[0]!).Parent!)!;
            Press(popup, 'C', RawInputModifiers.None);

            Assert.Equal(LetterAlone ? ["_Custom"] : [], clicked);
            Assert.Equal(!LetterAlone, flyout.IsOpen);
            flyout.Hide();
            return Task.CompletedTask;
        });

    [Fact]
    public Task LetterOfNoMenuItemDoesNotRunAnAccessKeyOfTheWindow() =>
        session.RunAsync(static scope =>
        {
            // Like the printer image menu of the settings: its items have no access keys, the window's buttons have. The key
            // raised again as Alt+letter on the popup bubbles on to the window, which must not run its own access key.
            int saves = 0;
            Button save = new() { Content = "_Save" };
            save.Click += (_, _) => saves++;
            List<string> clicked = [];
            MenuFlyout flyout = new() { ItemsSource = new[] { Item("Automatic", clicked), Item("Custom", clicked) } };
            Button button = new() { Content = "Image", Flyout = flyout };
            Window window = Show(scope, new StackPanel { Children = { save, button } });

            flyout.ShowAt(button);
            Settle(window);
            MenuItem first = (MenuItem)flyout.Items[0]!;
            first.Focus();
            Settle(window);
            TopLevel popup = TopLevel.GetTopLevel(first)!;
            Assert.NotSame(window, popup);
            Press(popup, 'S', RawInputModifiers.None);

            Assert.Equal(0, saves);
            Assert.Empty(clicked);
            Assert.True(flyout.IsOpen);
            flyout.Hide();
            return Task.CompletedTask;
        });

    [Fact]
    public Task LetterAloneDoesNothingInAPopupThatIsNoMenu() =>
        session.RunAsync(static scope =>
        {
            int clicks = 0;
            Button save = new() { Content = "_Save" };
            save.Click += (_, _) => clicks++;
            Flyout flyout = new() { Content = save };
            Button button = new() { Content = "More", Flyout = flyout };
            Window window = Show(scope, button);

            flyout.ShowAt(button);
            Settle(window);
            save.Focus();
            Settle(window);
            TopLevel popup = TopLevel.GetTopLevel(save)!;
            Assert.NotSame(window, popup);
            Press(popup, 'S', RawInputModifiers.None);
            Assert.Equal(0, clicks);

            Press(popup, 'S', RawInputModifiers.Alt);
            Assert.Equal(1, clicks);
            flyout.Hide();
            return Task.CompletedTask;
        });

    private static MenuItem Item(string header, List<string> clicked)
    {
        MenuItem item = new() { Header = header };
        item.Click += (_, _) => clicked.Add(header);
        return item;
    }

    private static Window Show(UiTestScope scope, Control content)
    {
        Window window = scope.Track(new Window { Width = 300, Height = 200, Content = content });
        window.Show();
        Settle(window);
        return window;
    }

    private static TopLevel PopupOf(ContextMenu menu)
    {
        TopLevel popup = TopLevel.GetTopLevel(menu)!;
        Assert.IsNotType<Window>(popup, exactMatch: false);
        return popup;
    }

    private static void Settle(TopLevel topLevel)
    {
        Dispatcher.UIThread.RunJobs();
        topLevel.Settle();
    }

    /// <summary>Presses and releases the key of <paramref name="letter"/> (A–Z) like Windows reports it (lower-case symbol without Shift).</summary>
    private static void Press(TopLevel topLevel, char letter, RawInputModifiers modifiers)
    {
        Key key = Enum.Parse<Key>(letter.ToString());
        PhysicalKey physical = Enum.Parse<PhysicalKey>(letter.ToString());
        string symbol = char.ToLowerInvariant(letter).ToString();
        topLevel.KeyPress(key, modifiers, physical, symbol);
        Dispatcher.UIThread.RunJobs();
        if (topLevel.PlatformImpl is not null)
        {
            // Not when the item ran and closed the menu (its popup) on the key press: the release goes nowhere.
            topLevel.KeyRelease(key, modifiers, physical, symbol);
            Settle(topLevel);
        }
    }
}
