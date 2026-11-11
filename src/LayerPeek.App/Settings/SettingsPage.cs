namespace LayerPeek.App.Settings;

/// <summary>
/// The tab a click on a notification opens the settings window on (<see cref="SettingsViewModel.ShowPage"/>): the test
/// notification the General tab it came from (T-101), a failed camera recording the Recording tab (T-146, user decision
/// 2026-11-04), a refused update the Startup and updates tab with its error (T-181).
/// </summary>
internal enum SettingsPage
{
    General,
    Recording,
    StartupUpdates,
}
