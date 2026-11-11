using Avalonia.Automation;
using Avalonia.Controls;

using LayerPeek.App.Resources;
using LayerPeek.App.Settings;
using LayerPeek.App.Tests.Settings;
using LayerPeek.App.UiTests.Infrastructure;
using LayerPeek.Core.Cameras;
using LayerPeek.Core.Settings;

using static LayerPeek.App.Tests.Settings.SettingsTestData;

namespace LayerPeek.App.UiTests.Settings;

/// <summary>
/// T-185: the main tab Camera of the real settings window: the frame rate of the mini preview with its profile, slider,
/// limit and description, saved for every camera.
/// </summary>
public sealed class SettingsWindowCameraTabUiTests(HeadlessSession session)
{
    private static async Task<(SettingsWindowHarness Ui, FakeEditorStore Store)> OpenAsync(UiTestScope scope, int? rate = MiniPreviewFrameRate.Default)
    {
        FakeEditorStore store = SettingsWindowHarness.StoreWith(LinkPrinter("a", "Alpha"));
        store.Data = store.Data with { Settings = store.Data.Settings with { MiniPreviewFrameRate = rate } };
        SettingsWindowHarness ui = await SettingsWindowHarness.OpenAsync(scope, store);
        ui.Click("SettingsTabCamera");
        ui.Window.Settle();
        Assert.Equal(SettingsViewModel.CameraTab, ui.ViewModel.SelectedTabIndex);
        return (ui, store);
    }

    [Fact]
    public Task TheTabShowsTheDefaultBalancedTenFps() =>
        session.RunAsync(static async scope =>
        {
            (SettingsWindowHarness ui, _) = await OpenAsync(scope);

            Assert.Equal(Strings.SettingsCameraTabHint, ui.ShownText("SettingsCameraTabHint"));
            Assert.True(Ui.ShowsText(ui.Window, Strings.SettingsMiniFrameRate));
            ComboBox profile = ui.Find<ComboBox>("MiniFrameRateProfile");
            Assert.Equal(4, profile.ItemCount);
            Assert.Equal(MiniPreviewFrameRateProfile.Balanced, Assert.IsType<MiniFrameRateProfileOption>(profile.SelectedItem).Value);
            Assert.Equal(Strings.SettingsMiniFrameRate, AutomationProperties.GetName(profile));
            Slider slider = ui.Find<Slider>("MiniFrameRateSlider");
            Assert.Equal((1, 30, 10), (slider.Minimum, slider.Maximum, slider.Value));
            Assert.Equal(Strings.SettingsMiniFrameRateSlider, AutomationProperties.GetName(slider));
            Assert.Equal("10 fps", ui.ShownText("MiniFrameRateValue"));
            Assert.Equal(Strings.SettingsMiniFrameRateBalancedHint, ui.ShownText("MiniFrameRateDescription"));
            Assert.Equal(Strings.SettingsMiniFrameRateBalancedHint, AutomationProperties.GetHelpText(slider));
        });

    [Fact]
    public Task SmoothPutsTheSliderAtItsEndAndShowsNoLimit() =>
        session.RunAsync(static async scope =>
        {
            (SettingsWindowHarness ui, FakeEditorStore store) = await OpenAsync(scope);
            ComboBox profile = ui.Find<ComboBox>("MiniFrameRateProfile");

            profile.SelectedItem = ui.ViewModel.MiniFrameRateProfiles[(int)MiniPreviewFrameRateProfile.Smooth];
            ui.Window.Settle();

            Assert.Equal(30, ui.Find<Slider>("MiniFrameRateSlider").Value);
            Assert.Equal(Strings.SettingsMiniFrameRateNoLimit, ui.ShownText("MiniFrameRateValue"));
            Assert.Equal(Strings.SettingsMiniFrameRateSmoothHint, ui.ShownText("MiniFrameRateDescription"));
            await ui.SaveAsync();
            Assert.Null(Assert.Single(store.Saves).Updated.MiniPreviewFrameRate);
        });

    [Fact]
    public Task MovingTheSliderSelectsCustomAndIsSaved() =>
        session.RunAsync(static async scope =>
        {
            (SettingsWindowHarness ui, FakeEditorStore store) = await OpenAsync(scope, rate: 5);
            Assert.Equal(MiniPreviewFrameRateProfile.Economy, Assert.IsType<MiniFrameRateProfileOption>(ui.Find<ComboBox>("MiniFrameRateProfile").SelectedItem).Value);

            ui.Find<Slider>("MiniFrameRateSlider").Value = 17;
            ui.Window.Settle();

            Assert.Equal(MiniPreviewFrameRateProfile.Custom, Assert.IsType<MiniFrameRateProfileOption>(ui.Find<ComboBox>("MiniFrameRateProfile").SelectedItem).Value);
            Assert.Equal("17 fps", ui.ShownText("MiniFrameRateValue"));
            Assert.Equal(Strings.SettingsMiniFrameRateCustomHint, ui.ShownText("MiniFrameRateDescription"));
            await ui.SaveAsync();
            Assert.Equal(17, Assert.Single(store.Saves).Updated.MiniPreviewFrameRate);
        });
}
