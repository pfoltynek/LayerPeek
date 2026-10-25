using System.Globalization;

using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.VisualTree;

using LayerPeek.App.Localization;
using LayerPeek.App.Resources;
using LayerPeek.App.Settings;
using LayerPeek.App.Tests.Settings;
using LayerPeek.App.UiTests.Infrastructure;
using LayerPeek.Core.Settings;

using static LayerPeek.App.Tests.Settings.SettingsTestData;

namespace LayerPeek.App.UiTests.Settings;

/// <summary>
/// The "Printer cooled down" switch on the General tab (T-080, design T-076 section 9): in the left notification column
/// after "Print stopped", off by default, saved, disabled with the master switch, read out with its help text, in reading
/// order and translated on a language switch.
/// </summary>
public sealed class SettingsWindowCooledDownNotificationUiTests(HeadlessSession session)
{
    public static TheoryData<string> Languages => [.. AppLanguages.Supported];

    [Fact]
    public Task SwitchIsOffByDefaultAndSavedByClicking() =>
        session.RunAsync(static async scope =>
        {
            FakeEditorStore store = SettingsWindowHarness.StoreWith(LinkPrinter("a", "Alpha"));
            SettingsWindowHarness ui = await SettingsWindowHarness.OpenAsync(scope, store);
            ui.Click("SettingsTabGeneral");
            CheckBox box = ui.Find<CheckBox>("SettingsNotifyCooledDown");

            Assert.True(box.IsEffectivelyVisible);
            Assert.False(box.IsChecked);
            Assert.Equal(Strings.SettingsNotifyCooledDown, box.Content);
            Assert.Equal(Strings.SettingsNotifyCooledDownHelp, AutomationProperties.GetHelpText(box));

            ui.Click("SettingsNotifyCooledDown");
            Assert.True(ui.ViewModel.NotifyCooledDown);
            await ui.SaveAsync();

            SettingsChange change = Assert.Single(store.Saves);
            Assert.True(change.Updated.Notifications.CooledDown);
        });

    [Fact]
    public Task SavedSwitchIsShownChecked() =>
        session.RunAsync(static async scope =>
        {
            FakeEditorStore store = SettingsWindowHarness.StoreWith(LinkPrinter("a", "Alpha"));
            store.Data = store.Data with { Settings = store.Data.Settings with { Notifications = new NotificationSettings { CooledDown = true } } };
            SettingsWindowHarness ui = await SettingsWindowHarness.OpenAsync(scope, store);
            ui.Click("SettingsTabGeneral");

            Assert.True(ui.Find<CheckBox>("SettingsNotifyCooledDown").IsChecked);
            Assert.False(ui.ViewModel.IsDirty);
        });

    [Fact]
    public Task MasterSwitchDisablesIt() =>
        session.RunAsync(static async scope =>
        {
            SettingsWindowHarness ui = await SettingsWindowHarness.OpenAsync(scope, SettingsWindowHarness.StoreWith(LinkPrinter("a", "Alpha")));
            ui.Click("SettingsTabGeneral");
            Assert.True(ui.Find<CheckBox>("SettingsNotifyCooledDown").IsEffectivelyEnabled);

            ui.Click("SettingsNotificationsEnabled");

            Assert.False(ui.Find<CheckBox>("SettingsNotifyCooledDown").IsEffectivelyEnabled);
        });

    [Fact]
    public Task SwitchFollowsPrintStoppedInTheLeftColumn() =>
        session.RunAsync(static async scope =>
        {
            SettingsWindowHarness ui = await SettingsWindowHarness.OpenAsync(scope, SettingsWindowHarness.StoreWith(LinkPrinter("a", "Alpha")));
            ui.Click("SettingsTabGeneral");
            CheckBox cooled = ui.Find<CheckBox>("SettingsNotifyCooledDown");
            CheckBox stopped = ui.Window.GetVisualDescendants().OfType<CheckBox>()
                .Single(static box => Equals(box.Content, Strings.SettingsNotifyPrintStopped));

            Assert.Same(stopped.Parent, cooled.Parent);
            Panel column = Assert.IsAssignableFrom<Panel>(cooled.Parent);
            Assert.Equal(column.Children.IndexOf(stopped) + 1, column.Children.IndexOf(cooled));
            Assert.Same(cooled, column.Children[^1]);
            Assert.Equal(stopped.Bounds.X, cooled.Bounds.X);
            Assert.True(cooled.Bounds.Y > stopped.Bounds.Y);
        });

    [Fact]
    public Task TabReachesItAfterTheOtherPrintSwitches() =>
        session.RunAsync(static async scope =>
        {
            SettingsWindowHarness ui = await SettingsWindowHarness.OpenAsync(scope, SettingsWindowHarness.StoreWith(LinkPrinter("a", "Alpha")));
            ui.Click("SettingsTabGeneral");
            Assert.True(ui.Find<TabItem>("SettingsTabGeneral").Focus());

            List<string> order = [];
            for (int i = 0; i < 40; i++)
            {
                ui.Window.KeyPressQwerty(PhysicalKey.Tab, RawInputModifiers.None);
                ui.Window.KeyReleaseQwerty(PhysicalKey.Tab, RawInputModifiers.None);
                ui.Window.Settle();
                if (ui.Window.FocusManager?.GetFocusedElement() is not Control focused)
                {
                    break;
                }

                // The text box inside a number box stands for the number box.
                Control stop = focused.GetSelfAndVisualAncestors().OfType<NumericUpDown>().FirstOrDefault() ?? focused;
                string id = AutomationProperties.GetAutomationId(stop) ?? stop.GetType().Name;
                if (id == "SettingsSave" && order.Contains(id))
                {
                    break;
                }

                order.Add(id);
            }

            int sound = order.IndexOf("SettingsSound");
            int cooled = order.IndexOf("SettingsNotifyCooledDown");
            int remaining = order.IndexOf("SettingsRemainingTime");
            Assert.True(sound >= 0 && cooled > sound && remaining > cooled, string.Join(", ", order));
            // The five print switches of the left column come right before it.
            Assert.Equal(["CheckBox", "CheckBox", "CheckBox", "CheckBox", "CheckBox"], order.Skip(cooled - 5).Take(5));
        });

    [Theory]
    [MemberData(nameof(Languages))]
    public Task TextFitsAndFollowsALanguageSwitch(string code) =>
        session.RunAsync(async scope =>
        {
            SettingsWindowHarness ui = await SettingsWindowHarness.OpenAsync(scope, SettingsWindowHarness.StoreWith(LinkPrinter("a", "Alpha")));
            ui.Click("SettingsTabGeneral");

            Assert.True(new UiLanguage().Apply(code));
            ui.Slot.RefreshTexts();
            ui.Window.Settle();

            CultureInfo target = CultureInfo.GetCultureInfo(code);
            CheckBox box = ui.Find<CheckBox>("SettingsNotifyCooledDown");
            Assert.Equal(Strings.ResourceManager.GetString(nameof(Strings.SettingsNotifyCooledDown), target), box.Content);
            Assert.Equal(Strings.ResourceManager.GetString(nameof(Strings.SettingsNotifyCooledDownHelp), target), AutomationProperties.GetHelpText(box));
            // One line and not cut by its column.
            TextBlock text = box.GetVisualDescendants().OfType<TextBlock>().Single(block => Equals(block.Text, box.Content));
            Assert.Single(text.TextLayout.TextLines);
            TextBlock probe = new() { Text = text.Text, FontFamily = text.FontFamily, FontSize = text.FontSize, FontWeight = text.FontWeight };
            probe.Measure(Avalonia.Size.Infinity);
            Assert.True(probe.DesiredSize.Width <= text.Bounds.Width + 0.5, $"{code}: '{text.Text}' needs {probe.DesiredSize.Width:0.#} DIP but has {text.Bounds.Width:0.#}.");
        });
}
