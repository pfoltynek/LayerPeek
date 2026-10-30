using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.VisualTree;

using LayerPeek.App.Localization;
using LayerPeek.App.Resources;
using LayerPeek.App.Settings;
using LayerPeek.App.Settings.Widgets;
using LayerPeek.App.Tests.Settings;
using LayerPeek.App.UiTests.Infrastructure;
using LayerPeek.Core.Cameras;
using LayerPeek.Core.Printers;
using LayerPeek.Core.Settings;

using static LayerPeek.App.Tests.Settings.SettingsTestData;

namespace LayerPeek.App.UiTests.Settings;

/// <summary>
/// The "Mini preview" item of the Widgets tab in the real settings window (T-110, design T-104 2): the item without a check
/// box below the separator, its panel of seven check boxes and the hint, keyboard (Space, Tab), camera off, no printer,
/// saving, and the fit in every language at 780 × 532 (the extent of the tab is checked by
/// <see cref="SettingsWindowLayoutTests"/>).
/// </summary>
public sealed class SettingsWindowMiniStripUiTests(HeadlessSession session)
{
    private static readonly Uri StreamUri = new("rtsp://192.168.0.227/live");

    private static readonly string[] Boxes =
        ["MiniStripProgressBar", "MiniStripPercent", "MiniStripRemaining", "MiniStripFinish", "MiniStripCooldown", "MiniStripNameRow", "MiniStripChartRow"];

    public static TheoryData<string> Languages => [.. AppLanguages.Supported];

    private static PrinterDefinition Printer(string id, string name, bool camera = true, CameraMiniStripSettings? strip = null) =>
        LinkPrinter(id, name, $"192.168.1.{20 + id.Length}") with
        {
            Camera = new CameraSettings(camera, StreamUri) { MiniStrip = strip ?? CameraMiniStripSettings.Default },
        };

    private static async Task<SettingsWindowHarness> OpenAsync(UiTestScope scope, FakeEditorStore store)
    {
        SettingsWindowHarness ui = await SettingsWindowHarness.OpenAsync(scope, store);
        ui.Click("SettingsTabWidgets");
        return ui;
    }

    private static Task<SettingsWindowHarness> OpenAsync(UiTestScope scope, params PrinterDefinition[] printers) =>
        OpenAsync(scope, SettingsWindowHarness.StoreWith(printers));

    private static ListBoxItem Item(SettingsWindowHarness ui) => ui.Find<ListBoxItem>("WidgetsItem-miniStrip");

    private static CheckBox Box(SettingsWindowHarness ui, string id) => ui.Find<CheckBox>(id);

    private static void Press(Window window, PhysicalKey key)
    {
        window.KeyPressQwerty(key, RawInputModifiers.None);
        window.KeyReleaseQwerty(key, RawInputModifiers.None);
        window.Settle();
    }

    private static string? FocusedId(Window window) =>
        (window.FocusManager?.GetFocusedElement() as Visual)?.GetSelfAndVisualAncestors().OfType<Control>()
            .Select(AutomationProperties.GetAutomationId)
            .FirstOrDefault(static id => !string.IsNullOrEmpty(id));

    private static Rect BoundsIn(Control control, Visual root) =>
        new(control.TranslatePoint(default, root) ?? throw new InvalidOperationException("Not in the window."), control.Bounds.Size);

    [Fact]
    public Task ItemBelowTheSeparatorOpensThePanel() =>
        session.RunAsync(static async scope =>
        {
            SettingsWindowHarness ui = await OpenAsync(scope, Printer("a", "Alpha", strip: new CameraMiniStripSettings { Remaining = false }), Printer("b", "Beta"));
            ListBoxItem item = Item(ui);
            ListBoxItem cooldown = ui.Find<ListBoxItem>("WidgetsItem-cooldown");
            ListBox list = ui.Find<ListBox>("WidgetsList");
            ListBoxItem separator = list.GetVisualDescendants().OfType<ListBoxItem>().Single(static row => row.DataContext is WidgetsSeparatorEntry);

            // The separator: 9 high, below the last widget, not selectable and not an element of UI Automation.
            Assert.Equal(9, separator.Bounds.Height, 0.5);
            Assert.Equal(BoundsIn(cooldown, ui.Window).Bottom, BoundsIn(separator, ui.Window).Top, 0.5);
            Assert.False(separator.IsEffectivelyEnabled);
            Assert.Equal(AccessibilityView.Raw, AutomationProperties.GetAccessibilityView(separator));
            Assert.Equal(32, item.Bounds.Height, 0.5);
            // No check box; the icon and the name line up with those of the widgets.
            Assert.Empty(item.GetVisualDescendants().OfType<CheckBox>());
            WidgetIcon icon = item.GetVisualDescendants().OfType<WidgetIcon>().Single();
            Assert.NotNull(icon.Data);
            Assert.Equal(BoundsIn(cooldown.GetVisualDescendants().OfType<WidgetIcon>().Single(), ui.Window).Left, BoundsIn(icon, ui.Window).Left, 0.5);
            TextBlock name = item.GetVisualDescendants().OfType<TextBlock>().Single();
            Assert.Equal(BoundsIn(cooldown.GetVisualDescendants().OfType<TextBlock>().First(static text => text.Text == "Cool-down"), ui.Window).Left, BoundsIn(name, ui.Window).Left, 0.5);
            Assert.Equal("Mini preview", AutomationProperties.GetName(item));

            ui.Window.Click(item);

            Assert.True(item.IsSelected);
            Assert.Equal("Mini preview", ui.ShownText("WidgetsDetailTitle"));
            Assert.Equal("Mini preview", AutomationProperties.GetName(ui.Find<StackPanel>("WidgetsDetail")));
            Assert.Equal(Strings.SettingsMiniStripDescription, ui.ShownText("WidgetsDetailDescription"));
            Assert.False(ui.Window.IsShown<TextBlock>("WidgetsDetailNoSettings"));
            Assert.Equal([true, true, false, true, true, true, true], Boxes.Select(id => Box(ui, id).IsChecked == true));
            Assert.All(Boxes, id => Assert.True(Box(ui, id).IsEffectivelyEnabled));
            Assert.All(Boxes, id => Assert.True(Box(ui, id).Bounds.Height >= 32));
            Assert.Equal(Strings.SettingsMiniStripHint, ui.ShownText("MiniStripHint"));
            Assert.Equal(Strings.SettingsMiniStripNameRowHelp, ToolTip.GetTip(Box(ui, "MiniStripNameRow")));
            Assert.Equal(Strings.SettingsMiniStripChartRowHelp, ToolTip.GetTip(Box(ui, "MiniStripChartRow")));
            Assert.Equal(Strings.SettingsMiniStripChartRowHelp, AutomationProperties.GetHelpText(Box(ui, "MiniStripChartRow")));
            Assert.Equal(Strings.SettingsMiniStripPercent, AutomationProperties.GetName(Box(ui, "MiniStripPercent")));
            // Left column 1–4 from the top, right column 5–7 (design 2).
            Rect progress = BoundsIn(Box(ui, "MiniStripProgressBar"), ui.Window);
            Rect finish = BoundsIn(Box(ui, "MiniStripFinish"), ui.Window);
            Rect cooldownBox = BoundsIn(Box(ui, "MiniStripCooldown"), ui.Window);
            Assert.Equal(progress.Left, finish.Left, 0.5);
            Assert.True(finish.Top > progress.Top);
            Assert.Equal(progress.Top, cooldownBox.Top, 0.5);
            Assert.True(cooldownBox.Left >= progress.Right);
        });

    [Fact]
    public Task SpaceOnTheItemChangesNothingAndTabGoesThroughTheColumns() =>
        session.RunAsync(static async scope =>
        {
            SettingsWindowHarness ui = await OpenAsync(scope, Printer("a", "Alpha"), Printer("b", "Beta"));
            ui.Window.Click(Item(ui));
            Assert.True(Item(ui).Focus(NavigationMethod.Tab));

            Press(ui.Window, PhysicalKey.Space);

            Assert.True(Item(ui).IsSelected);
            Assert.False(ui.ViewModel.IsDirty);
            Assert.All(Boxes, id => Assert.True(Box(ui, id).IsChecked));

            List<string?> order = [];
            for (int i = 0; i < 9; i++)
            {
                Press(ui.Window, PhysicalKey.Tab);
                order.Add(FocusedId(ui.Window));
            }

            Assert.Equal([.. Boxes, "WidgetsEditLayout", "WidgetsReset"], order);

            // Space on a check box switches it.
            Assert.True(Box(ui, "MiniStripPercent").Focus(NavigationMethod.Tab));
            Press(ui.Window, PhysicalKey.Space);
            Assert.False(ui.ViewModel.Printers[0].MiniStripPercent);
            Assert.True(ui.ViewModel.IsDirty);
        });

    [Fact]
    public Task SavedWithTheOtherSettingsAndPerPrinter() =>
        session.RunAsync(static async scope =>
        {
            FakeEditorStore store = SettingsWindowHarness.StoreWith(Printer("a", "Alpha"), Printer("b", "Beta"));
            SettingsWindowHarness ui = await OpenAsync(scope, store);
            ui.Window.Click(Item(ui));

            ui.Window.Click(Box(ui, "MiniStripCooldown"));
            ui.ViewModel.SelectedPrinter = ui.ViewModel.Printers[1];
            ui.Window.Settle();
            Assert.True(Box(ui, "MiniStripCooldown").IsChecked);
            ui.Window.Click(Box(ui, "MiniStripNameRow"));
            ui.Window.Click(Box(ui, "MiniStripChartRow"));
            ui.ViewModel.SelectedPrinter = ui.ViewModel.Printers[0];
            ui.Window.Settle();
            Assert.False(Box(ui, "MiniStripCooldown").IsChecked);
            Assert.True(Box(ui, "MiniStripNameRow").IsChecked);

            await ui.SaveAsync();

            AppSettings saved = Assert.Single(store.Saves).Updated;
            Assert.Equal(new CameraMiniStripSettings { Cooldown = false }, saved.Printers[0].Camera.MiniStrip);
            Assert.Equal(new CameraMiniStripSettings { NameRow = false, ChartRow = false }, saved.Printers[1].Camera.MiniStrip);
        });

    [Fact]
    public Task CameraOffDisablesTheCheckBoxesAndKeepsTheValues() =>
        session.RunAsync(static async scope =>
        {
            SettingsWindowHarness ui = await OpenAsync(scope, Printer("a", "Alpha", camera: false, strip: new CameraMiniStripSettings { Finish = false }), Printer("b", "Beta"));
            ui.Window.Click(Item(ui));

            Assert.True(Item(ui).IsSelected);
            Assert.All(Boxes, id => Assert.False(Box(ui, id).IsEffectivelyEnabled));
            Assert.False(Box(ui, "MiniStripFinish").IsChecked);
            Assert.True(ToolTip.GetShowOnDisabled(Box(ui, "MiniStripNameRow")));

            ui.Window.Click(Box(ui, "MiniStripFinish"));

            Assert.False(Box(ui, "MiniStripFinish").IsChecked);
            Assert.False(ui.ViewModel.IsDirty);
        });

    [Fact]
    public Task WithoutPrintersTheItemIsNotShown() =>
        session.RunAsync(static async scope =>
        {
            SettingsWindowHarness ui = await OpenAsync(scope);

            Assert.False(ui.Window.IsShown<ListBoxItem>("WidgetsItem-miniStrip"));
            Assert.False(ui.Window.IsShown<CheckBox>("MiniStripPercent"));
        });

    [Theory]
    [MemberData(nameof(Languages))]
    public Task PanelFitsWithoutClippingInEveryLanguage(string language) =>
        session.RunAsync(async scope =>
        {
            Assert.True(new UiLanguage().Apply(language));
            SettingsWindowHarness ui = await OpenAsync(scope, Printer("a", "MK4S workshop"), Printer("b", "Beta"));
            ui.Window.Click(Item(ui));
            Assert.Equal(new Size(780, 532), ui.Window.ClientSize);

            List<string> problems = [];
            StackPanel detail = ui.Find<StackPanel>("WidgetsDetail");
            Rect panel = BoundsIn(detail, ui.Window);
            Rect section = BoundsIn(ui.Find<Grid>("WidgetsAllSection"), ui.Window);
            foreach (string id in Boxes)
            {
                CheckBox box = Box(ui, id);
                Rect bounds = BoundsIn(box, ui.Window);
                TextBlock text = box.GetVisualDescendants().OfType<TextBlock>().Single();
                if (text.TextLayout.TextLines.Count > 2 || text.TextLayout.Height > text.Bounds.Height + 0.5 || text.TextLayout.WidthIncludingTrailingWhitespace > text.Bounds.Width + 0.5)
                {
                    problems.Add($"{language}: '{text.Text}' does not fit ({text.TextLayout.TextLines.Count} lines in {text.Bounds}).");
                }

                if (bounds.Right > panel.Right + 0.5 || bounds.Bottom > section.Top + 0.5)
                {
                    problems.Add($"{language}: {id} {bounds} leaves the panel {panel} / reaches the section at {section.Top}.");
                }
            }

            // ui-designer review 2026-10-30 (finding 1): the rows of both columns stay level even when a text wraps.
            for (int row = 0; row < 3; row++)
            {
                double left = BoundsIn(Box(ui, Boxes[row]), ui.Window).Top;
                double right = BoundsIn(Box(ui, Boxes[row + 4]), ui.Window).Top;
                if (Math.Abs(left - right) > 0.5)
                {
                    problems.Add($"{language}: row {row + 1} starts at {left:0.#} on the left and {right:0.#} on the right.");
                }
            }

            TextBlock hint = ui.Find<TextBlock>("MiniStripHint");
            if (hint.TextLayout.TextLines.Count != 1 || hint.TextLayout.WidthIncludingTrailingWhitespace > hint.Bounds.Width + 0.5)
            {
                problems.Add($"{language}: the hint '{hint.Text}' is not one whole line.");
            }

            if (BoundsIn(hint, ui.Window).Bottom > section.Top + 0.5)
            {
                problems.Add($"{language}: the hint reaches the section.");
            }

            TextBlock description = ui.Find<TextBlock>("WidgetsDetailDescription");
            if (description.TextLayout.TextLines.Count > 2 || description.TextLayout.Height > description.Bounds.Height + 0.5)
            {
                problems.Add($"{language}: the description has {description.TextLayout.TextLines.Count} lines.");
            }

            ListBoxItem item = Item(ui);
            TextBlock name = item.GetVisualDescendants().OfType<TextBlock>().Single();
            if (name.TextLayout.WidthIncludingTrailingWhitespace > name.Bounds.Width + 0.5)
            {
                problems.Add($"{language}: the item name '{name.Text}' is trimmed.");
            }

            Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
        });
}
