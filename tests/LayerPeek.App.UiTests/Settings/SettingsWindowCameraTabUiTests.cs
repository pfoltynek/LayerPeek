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
/// limit and description, saved for every camera. T-204: hardware decoding (Automatic / Off) with the description of the
/// selected option and, where Automatic is still software, the note.
/// </summary>
public sealed class SettingsWindowCameraTabUiTests(HeadlessSession session)
{
    private static async Task<(SettingsWindowHarness Ui, FakeEditorStore Store)> OpenAsync(
        UiTestScope scope,
        int? rate = MiniPreviewFrameRate.Default,
        CameraHardwareDecoding hardwareDecoding = CameraHardwareDecoding.Automatic,
        bool softwareNote = false)
    {
        FakeEditorStore store = SettingsWindowHarness.StoreWith(LinkPrinter("a", "Alpha"));
        store.Data = store.Data with { Settings = store.Data.Settings with { MiniPreviewFrameRate = rate, CameraHardwareDecoding = hardwareDecoding } };
        SettingsWindowHarness ui = await SettingsWindowHarness.OpenAsync(scope, store, hardwareDecodingSoftwareNote: softwareNote);
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

    [Fact]
    public Task HardwareDecodingIsAutomaticByDefault() =>
        session.RunAsync(static async scope =>
        {
            (SettingsWindowHarness ui, _) = await OpenAsync(scope);

            Assert.True(Ui.ShowsText(ui.Window, Strings.SettingsHardwareDecoding));
            ComboBox mode = ui.Find<ComboBox>("HardwareDecoding");
            Assert.Equal(2, mode.ItemCount);
            Assert.Equal(CameraHardwareDecoding.Automatic, Assert.IsType<HardwareDecodingOption>(mode.SelectedItem).Value);
            Assert.Equal(Strings.SettingsHardwareDecoding, AutomationProperties.GetName(mode));
            Assert.Equal(Strings.SettingsHardwareDecodingAutomaticHint, AutomationProperties.GetHelpText(mode));
            Assert.Equal(Strings.SettingsHardwareDecodingAutomaticHint, ui.ShownText("HardwareDecodingDescription"));
            Assert.Null(ui.ShownText("HardwareDecodingSoftwareNote"));
            Assert.False(ui.ViewModel.IsDirty);
        });

    [Fact]
    public Task OffIsAnUnsavedChangeUntilSavedAndIsLoadedAgain() =>
        session.RunAsync(static async scope =>
        {
            (SettingsWindowHarness ui, FakeEditorStore store) = await OpenAsync(scope);
            ComboBox mode = ui.Find<ComboBox>("HardwareDecoding");

            mode.SelectedItem = ui.ViewModel.HardwareDecodingOptions[(int)CameraHardwareDecoding.Off];
            ui.Window.Settle();

            Assert.Equal(Strings.SettingsHardwareDecodingOffHint, ui.ShownText("HardwareDecodingDescription"));
            Assert.True(ui.ViewModel.IsDirty);
            await ui.SaveAsync();
            AppSettings saved = Assert.Single(store.Saves).Updated;
            Assert.Equal(CameraHardwareDecoding.Off, saved.CameraHardwareDecoding);

            // Opened again with what was saved.
            (SettingsWindowHarness again, _) = await OpenAsync(scope, hardwareDecoding: saved.CameraHardwareDecoding);
            Assert.Equal(CameraHardwareDecoding.Off, Assert.IsType<HardwareDecodingOption>(again.Find<ComboBox>("HardwareDecoding").SelectedItem).Value);
            Assert.Equal(Strings.SettingsHardwareDecodingOffHint, again.ShownText("HardwareDecodingDescription"));
            Assert.False(again.ViewModel.IsDirty);
        });

    [Fact]
    public Task CancelDiscardsAChangedHardwareDecoding() =>
        session.RunAsync(static async scope =>
        {
            (SettingsWindowHarness ui, FakeEditorStore store) = await OpenAsync(scope, hardwareDecoding: CameraHardwareDecoding.Off);

            ui.Find<ComboBox>("HardwareDecoding").SelectedItem = ui.ViewModel.HardwareDecodingOptions[(int)CameraHardwareDecoding.Automatic];
            ui.Window.Settle();
            Assert.True(ui.ViewModel.IsDirty);
            ui.Click("SettingsCancel");

            Assert.Empty(store.Saves);
            Assert.False(ui.IsOpen);
        });

    [Fact]
    public Task WhereAutomaticIsStillSoftwareTheNoteIsShown() =>
        session.RunAsync(static async scope =>
        {
            (SettingsWindowHarness ui, _) = await OpenAsync(scope, softwareNote: true);

            Assert.Equal(Strings.SettingsHardwareDecodingSoftwareNote, ui.ShownText("HardwareDecodingSoftwareNote"));
        });
}
