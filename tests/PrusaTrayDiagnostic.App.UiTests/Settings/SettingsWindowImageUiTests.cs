using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;

using PrusaTrayDiagnostic.App.Printers;
using PrusaTrayDiagnostic.App.Resources;
using PrusaTrayDiagnostic.App.Settings;
using PrusaTrayDiagnostic.App.Tests.Printers;
using PrusaTrayDiagnostic.App.Tests.Settings;
using PrusaTrayDiagnostic.App.UiTests.Infrastructure;
using PrusaTrayDiagnostic.App.UiTests.Printers;
using PrusaTrayDiagnostic.Core.Printers;

using static PrusaTrayDiagnostic.App.Tests.Settings.SettingsTestData;

namespace PrusaTrayDiagnostic.App.UiTests.Settings;

/// <summary>
/// The printer image in the settings window (T-039): 24 × 24 in the printer list, the 56 × 56 button with its menu on the
/// Printer sub-tab, importing a custom image through the file dialog, and the import error at the field.
/// </summary>
public sealed class SettingsWindowImageUiTests(HeadlessSession session)
{
    private static readonly PrinterModelCode Mk4SCode = new(1, 4, 1);

    [Fact]
    public Task PrinterListShowsEachPrintersImageAt24() =>
        session.RunAsync(static async scope =>
        {
            SettingsWindowHarness ui = await SettingsWindowHarness.OpenAsync(
                scope,
                SettingsWindowHarness.StoreWith(
                    LinkPrinter("a", "Alpha") with { DetectedModel = Mk4SCode },
                    LinkPrinter("b", "Beta", "192.168.1.21") with { Image = PrinterImageChoice.ForModel(PrinterModel.Xl) },
                    CloudPrinter("c", "Cloud")));

            PrinterImageView[] images = [.. ui.Window.GetVisualDescendantsOf<PrinterImageView>("PrinterListImage")];

            Assert.Equal(3, images.Length);
            Assert.Equal(["mk4", "xl", "generic"], images.Select(static image => image.Picture!.IllustrationId));
            Assert.All(images, static image => Assert.Equal(new Avalonia.Size(24, 24), image.Bounds.Size));
            Assert.All(images, static image => Assert.Equal(24, image.LastIllustrationPixelSize));
        });

    [Fact]
    public Task ImageButtonIs56WithItsNameAndShowsThePicture() =>
        session.RunAsync(static async scope =>
        {
            SettingsWindowHarness ui = await SettingsWindowHarness.OpenAsync(scope, SettingsWindowHarness.StoreWith(LinkPrinter("a", "Alpha") with { DetectedModel = Mk4SCode }));

            Button button = ui.Find<Button>("PrinterImageButton");
            PrinterImageView image = ui.Find<PrinterImageView>("PrinterImage");

            Assert.Equal(new Avalonia.Size(56, 56), button.Bounds.Size);
            Assert.Equal("Printer image: MK4S (detected)", AutomationProperties.GetName(button));
            Assert.Equal(Strings.SettingsImageButtonToolTip, ToolTip.GetTip(button));
            Assert.Equal("mk4", image.Picture!.IllustrationId);
            Assert.Equal(56, image.LastIllustrationPixelSize);
            // Bottom-aligned with the name field next to it.
            Assert.Equal(Bottom(ui.NameBox, ui.Window), Bottom(button, ui.Window), 0.5);
        });

    [Fact]
    public Task MenuOffersAutomaticModelsAndCustomImage() =>
        session.RunAsync(static async scope =>
        {
            SettingsWindowHarness ui = await SettingsWindowHarness.OpenAsync(scope, SettingsWindowHarness.StoreWith(LinkPrinter("a", "Alpha") with { DetectedModel = Mk4SCode }));

            MenuFlyout menu = OpenMenu(ui);

            MenuItem[] items = [.. menu.Items.OfType<MenuItem>()];
            Assert.Equal(["Automatic by model (MK4S)", "Choose model", "Custom image…", "Remove custom image"], items.Select(static item => item.Header as string));
            Assert.True(items[0].IsChecked);
            Assert.False(items[3].IsEffectivelyEnabled);
            MenuItem models = items[1];
            models.Open();
            Dispatcher.UIThread.RunJobs();
            MenuItem[] modelItems = [.. models.GetLogicalChildrenOf<MenuItem>()];
            Assert.Equal(PrinterModels.Selectable.Select(PrinterModels.GetProductName), modelItems.Select(static item => item.Header as string));
            Assert.All(modelItems, static item => Assert.Equal(MenuItemToggleType.Radio, item.ToggleType));

            Click(modelItems.Single(static item => (string?)item.Header == "MINI"));
            ui.Window.Settle();

            Assert.Equal("mini", ui.Find<PrinterImageView>("PrinterImage").Picture!.IllustrationId);
            Assert.Equal("mini", ui.Window.GetVisualDescendantsOf<PrinterImageView>("PrinterListImage").Single().Picture!.IllustrationId);
            Assert.Equal("Printer image: MINI", AutomationProperties.GetName(ui.Find<Button>("PrinterImageButton")));
            Assert.True(ui.ViewModel.IsDirty);
        });

    [Fact]
    public Task CancelledCustomImageLeavesTheAutomaticChoiceChecked() =>
        session.RunAsync(static async scope =>
        {
            // A real click (pointer down/up handled by the menu's interaction handler), then the user cancels the dialog.
            FakeImagePicker picker = new() { Content = null };
            SettingsWindowHarness ui = await SettingsWindowHarness.OpenAsync(scope, SettingsWindowHarness.StoreWith(LinkPrinter("a", "Alpha") with { DetectedModel = Mk4SCode }), imagePicker: picker);
            MenuFlyout menu = OpenMenu(ui);
            MenuItem custom = menu.Items.OfType<MenuItem>().Single(static item => (string?)item.Header == Strings.SettingsImageCustom);
            MenuItem automatic = menu.Items.OfType<MenuItem>().First();
            Assert.False(custom.IsChecked);

            ClickWithThePointer(custom);
            if (ui.ViewModel.SelectedPrinter!.ChooseCustomImageCommand.ExecutionTask is { } running)
            {
                await running;
            }

            Dispatcher.UIThread.RunJobs();
            ui.Window.Settle();

            Assert.Equal(1, picker.Calls);
            Assert.False(menu.IsOpen);
            Assert.True(ui.ViewModel.SelectedPrinter!.IsAutomaticImage);

            // Only an open menu shows its state: a closed flyout detaches its items, which then lose the data context and their
            // bindings fall back to unchecked. So the check marks are read after opening the menu again, as the user sees them.
            menu = OpenMenu(ui);
            Assert.Same(ui.ViewModel.SelectedPrinter, custom.DataContext);
            Assert.False(custom.IsChecked);
            Assert.True(automatic.IsChecked);
        });

    [Fact]
    public Task CustomImageIsImportedThroughTheFileDialog() =>
        session.RunAsync(static async scope =>
        {
            FakeImagePicker picker = new() { Content = UiTestImages.Jpeg(1200, 800, static (_, _) => 0xFFFF0000) };
            FakeEditorStore store = SettingsWindowHarness.StoreWith(LinkPrinter("a", "Alpha"));
            SettingsWindowHarness ui = await SettingsWindowHarness.OpenAsync(scope, store, imagePicker: picker);

            MenuFlyout menu = OpenMenu(ui);
            Click(menu.Items.OfType<MenuItem>().Single(static item => (string?)item.Header == Strings.SettingsImageCustom));
            // A real click closes the menu; the raised click event alone does not.
            menu.Hide();
            PrinterEditorViewModel printer = ui.ViewModel.SelectedPrinter!;
            await printer.ChooseCustomImageCommand.ExecutionTask!;
            ui.Window.Settle();

            Assert.Equal(1, picker.Calls);
            PrinterPicture picture = ui.Find<PrinterImageView>("PrinterImage").Picture!;
            Assert.True(picture.IsCustom);
            (int width, int height, _) = UiTestImages.Read(picture.CustomPng!);
            Assert.Equal((512, 341), (width, height));

            await ui.SaveAsync();

            SettingsChange change = Assert.Single(store.Saves);
            Assert.Same(picture.CustomPng, change.NewImages!["a"]);
            Assert.Equal(PrinterImageChoice.Custom, change.Updated.Printers[0].Image);
        });

    [Fact]
    public Task ImportErrorIsShownInTheStatusLineUntilTheNextChoice() =>
        session.RunAsync(static async scope =>
        {
            FakeImagePicker picker = new() { Content = "GIF89a"u8.ToArray() };
            SettingsWindowHarness ui = await SettingsWindowHarness.OpenAsync(scope, SettingsWindowHarness.StoreWith(LinkPrinter("a", "Alpha")), imagePicker: picker);

            await ui.ViewModel.SelectedPrinter!.ChooseCustomImageCommand.ExecuteAsync(null);
            ui.Window.Settle();

            Assert.Equal(Strings.SettingsImageErrorFormat, ui.ShownText("SettingsStatus"));
            Assert.Contains("isError", ui.Find<TextBlock>("SettingsStatus").Classes);

            ui.ViewModel.SelectedPrinter!.UseAutomaticImageCommand.Execute(null);
            ui.Window.Settle();

            Assert.Null(ui.ShownText("SettingsStatus"));
        });

    private static MenuFlyout OpenMenu(SettingsWindowHarness ui)
    {
        Button button = ui.Find<Button>("PrinterImageButton");
        ui.Click("PrinterImageButton");
        ui.Window.Settle();
        MenuFlyout menu = Assert.IsType<MenuFlyout>(button.Flyout);
        Assert.True(menu.IsOpen);
        return menu;
    }

    /// <summary>A left click on the centre of <paramref name="item"/> in the top level that shows the open menu.</summary>
    private static void ClickWithThePointer(MenuItem item)
    {
        TopLevel topLevel = TopLevel.GetTopLevel(item) ?? throw new InvalidOperationException("The menu item is not shown.");
        topLevel.UpdateLayout();
        Point centre = item.TranslatePoint(new Point(item.Bounds.Width / 2, item.Bounds.Height / 2), topLevel)!.Value;
        Ui.Click(topLevel, centre);
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>What a click on a menu item raises (its command runs on the click event).</summary>
    private static void Click(MenuItem item)
    {
        Assert.True(item.IsEffectivelyEnabled, $"'{item.Header}' is disabled.");
        item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Dispatcher.UIThread.RunJobs();
    }

    private static double Bottom(Control control, Visual window) => control.TranslatePoint(new Avalonia.Point(0, control.Bounds.Height), window)!.Value.Y;

}

/// <summary>Visual and logical tree lookups by automation id for repeated controls (list items, menu items).</summary>
internal static class TreeExtensions
{
    public static IEnumerable<T> GetVisualDescendantsOf<T>(this Avalonia.Visual root, string automationId)
        where T : Control =>
        Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(root).OfType<T>().Where(control => AutomationProperties.GetAutomationId(control) == automationId);

    public static IEnumerable<T> GetLogicalChildrenOf<T>(this Avalonia.LogicalTree.ILogical root)
        where T : class =>
        Avalonia.LogicalTree.LogicalExtensions.GetLogicalChildren(root).OfType<T>();
}
