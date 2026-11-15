using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Threading;

using LayerPeek.App.Localization;
using LayerPeek.App.Resources;
using LayerPeek.App.UiTests.Infrastructure;
using LayerPeek.App.UiTests.Settings;

using static LayerPeek.App.Tests.Settings.SettingsTestData;

namespace LayerPeek.App.UiTests.Windows;

/// <summary>
/// The app's own context menu of text fields (T-242, App.axaml): Cut, Copy, Paste and Select All in the app's language,
/// enabled as the field allows, acting on the field it opened on; a password field offers no Cut and no Copy; the keys of
/// the text box keep working. Every text box of the app gets it (the style is the app's, no field sets it).
/// </summary>
public sealed class TextBoxContextMenuUiTests(HeadlessSession session)
{
    private const string Cut = "TextBoxCutMenuItem";
    private const string Copy = "TextBoxCopyMenuItem";
    private const string Paste = "TextBoxPasteMenuItem";
    private const string SelectAll = "TextBoxSelectAllMenuItem";

    private static string PasteText => OperatingSystem.IsMacOS() ? Strings.TextBoxPasteMac : Strings.TextBoxPaste;

    [Fact]
    public Task MenuOffersTheFourItemsInTheAppLanguageAndFollowsASwitch() =>
        session.RunAsync(static scope =>
        {
            TextBox box = new() { Text = "Alpha" };
            Window window = Show(scope, box);
            UiLanguage language = new();
            Assert.True(language.Apply("de"));

            MenuFlyout menu = Open(window, box);
            Assert.Equal([Cut, Copy, Paste, SelectAll], VisibleItems(menu).Select(Id));
            Assert.Equal([Strings.TextBoxCut, Strings.TextBoxCopy, PasteText, Strings.TextBoxSelectAll], VisibleItems(menu).Select(static item => item.Header));
            Assert.Equal("_Ausschneiden", Strings.TextBoxCut);
            menu.Hide();

            Assert.True(language.Apply("cs"));
            menu = Open(window, box);
            Assert.Equal([Strings.TextBoxCut, Strings.TextBoxCopy, PasteText, Strings.TextBoxSelectAll], VisibleItems(menu).Select(static item => item.Header));
            Assert.Equal("Vyj_mout", Strings.TextBoxCut);
            menu.Hide();
            return Task.CompletedTask;
        });

    [Fact]
    public Task ItemsAreEnabledAsTheFieldAllowsAndActOnIt() =>
        session.RunAsync(static async scope =>
        {
            TextBox box = new() { Text = "Alpha" };
            Window window = Show(scope, box);

            // No selection: nothing to cut or copy.
            MenuFlyout menu = Open(window, box);
            Assert.False(Item(menu, Cut).IsEnabled);
            Assert.False(Item(menu, Copy).IsEnabled);
            Assert.True(Item(menu, Paste).IsEnabled);
            Assert.True(Item(menu, SelectAll).IsEnabled);

            ClickItem(window, menu, SelectAll);
            Assert.False(menu.IsOpen);
            Assert.Equal("Alpha", box.SelectedText);

            menu = Open(window, box);
            Assert.True(Item(menu, Cut).IsEnabled);
            Assert.True(Item(menu, Copy).IsEnabled);
            ClickItem(window, menu, Copy);
            Assert.Equal("Alpha", await window.Clipboard!.TryGetTextAsync());
            Assert.Equal("Alpha", box.Text);

            menu = Open(window, box);
            ClickItem(window, menu, Cut);
            Assert.Equal(string.Empty, box.Text);

            menu = Open(window, box);
            Assert.False(Item(menu, SelectAll).IsEnabled);
            ClickItem(window, menu, Paste);
            await Ui.UntilAsync(() => box.Text == "Alpha", "the pasted text");
        });

    [Fact]
    public Task ReadOnlyFieldOffersCopyAndSelectAllOnly() =>
        session.RunAsync(static scope =>
        {
            TextBox box = new() { Text = "Licence", IsReadOnly = true };
            Window window = Show(scope, box);
            box.SelectAll();

            MenuFlyout menu = Open(window, box);
            Assert.False(Item(menu, Cut).IsEnabled);
            Assert.True(Item(menu, Copy).IsEnabled);
            Assert.False(Item(menu, Paste).IsEnabled);
            Assert.True(Item(menu, SelectAll).IsEnabled);
            menu.Hide();
            return Task.CompletedTask;
        });

    [Fact]
    public Task PasswordFieldOffersNoCutAndNoCopy() =>
        session.RunAsync(static scope =>
        {
            TextBox password = new() { Text = "secret", PasswordChar = '●' };
            TextBox plain = new() { Text = "plain" };
            Window window = Show(scope, new StackPanel { Children = { password, plain } });
            password.SelectAll();

            MenuFlyout menu = Open(window, password);
            Assert.Equal([Paste, SelectAll], VisibleItems(menu).Select(Id));
            menu.Hide();

            // The menu is shared: on a plain field it offers everything again.
            menu = Open(window, plain);
            Assert.Equal([Cut, Copy, Paste, SelectAll], VisibleItems(menu).Select(Id));
            menu.Hide();
            return Task.CompletedTask;
        });

    [Fact]
    public Task KeysOfTheTextBoxKeepWorking() =>
        session.RunAsync(static async scope =>
        {
            TextBox box = new() { Text = "Alpha" };
            Window window = Show(scope, box);
            Assert.True(box.Focus());
            PlatformHotkeyConfiguration keys = Application.Current!.PlatformSettings!.HotkeyConfiguration;

            Press(window, keys.SelectAll[0]);
            Assert.Equal("Alpha", box.SelectedText);
            Press(window, keys.Copy[0]);
            Assert.Equal("Alpha", await window.Clipboard!.TryGetTextAsync());

            // The menu shows the same gestures.
            MenuFlyout menu = Open(window, box);
            Assert.Equal(keys.SelectAll[0], Item(menu, SelectAll).InputGesture);
            Assert.Equal(keys.Copy[0], Item(menu, Copy).InputGesture);
            menu.Hide();
        });

    [Fact]
    public Task TextFieldsOfTheSettingsHaveTheAppMenu() =>
        session.RunAsync(static async scope =>
        {
            SettingsWindowHarness ui = await SettingsWindowHarness.OpenAsync(scope, SettingsWindowHarness.StoreWith(LinkPrinter("a", "Alpha")));
            new UiLanguage().Apply("sk");

            MenuFlyout menu = Open(ui.Window, ui.NameBox);
            Assert.Equal([Cut, Copy, Paste, SelectAll], VisibleItems(menu).Select(Id));
            Assert.Equal(Strings.TextBoxCut, Item(menu, Cut).Header);
            menu.Hide();

            menu = Open(ui.Window, ui.PasswordBox);
            Assert.Equal([Paste, SelectAll], VisibleItems(menu).Select(Id));
            menu.Hide();
        });

    private static Window Show(UiTestScope scope, Control content)
    {
        Window window = scope.Track(new Window { Width = 300, Height = 200, Content = content });
        window.Show();
        window.Settle();
        return window;
    }

    /// <summary>Opens the context menu of <paramref name="box"/> like a right click (or Control-click on macOS) does.</summary>
    private static MenuFlyout Open(Window window, TextBox box)
    {
        box.BringIntoView();
        window.Settle();
        box.RaiseEvent(new ContextRequestedEventArgs());
        window.Settle();
        MenuFlyout menu = Assert.IsType<MenuFlyout>(box.ContextFlyout);
        Assert.True(menu.IsOpen);
        TopLevel.GetTopLevel(Item(menu, Paste))!.Settle();
        return menu;
    }

    private static MenuItem Item(MenuFlyout menu, string automationId) =>
        menu.Items.OfType<MenuItem>().Single(item => Id(item) == automationId);

    private static IEnumerable<MenuItem> VisibleItems(MenuFlyout menu) => menu.Items.OfType<MenuItem>().Where(static item => item.IsVisible);

    private static string? Id(MenuItem item) => Avalonia.Automation.AutomationProperties.GetAutomationId(item);

    /// <summary>Clicks the item in the popup of the menu, like the user would.</summary>
    private static void ClickItem(Window window, MenuFlyout menu, string automationId)
    {
        MenuItem item = Item(menu, automationId);
        TopLevel popup = TopLevel.GetTopLevel(item)!;
        Assert.NotSame(window, popup);
        Point centre = Ui.CentreOf(item, popup);
        popup.MouseMove(centre);
        Ui.Click(popup, centre);
        Dispatcher.UIThread.RunJobs();
        window.Settle();
    }

    private static void Press(Window window, KeyGesture gesture)
    {
        RawInputModifiers modifiers = (RawInputModifiers)gesture.KeyModifiers;
        window.KeyPress(gesture.Key, modifiers, PhysicalKey.None, null);
        window.KeyRelease(gesture.Key, modifiers, PhysicalKey.None, null);
        window.Settle();
    }
}
