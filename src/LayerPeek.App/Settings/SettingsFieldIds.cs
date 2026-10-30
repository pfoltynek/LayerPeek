namespace LayerPeek.App.Settings;

/// <summary>
/// The automation ids of the validated fields of the settings window (T-112), the same as in <c>SettingsWindow.axaml</c>
/// and <c>WidgetSettingsTemplates.axaml</c>. A failed Save (or a refused Test connection / Test camera, Q5) asks the window
/// to focus the first invalid field by one of these (<see cref="SettingsViewModel.FieldFocusRequested"/>); the window finds
/// it by walking its visual tree (no reflection, Native AOT).
/// </summary>
public static class SettingsFieldIds
{
    public const string PrinterName = "PrinterName";

    public const string PrinterAddress = "PrinterAddress";

    public const string PrinterUserName = "PrinterUserName";

    public const string PrinterPassword = "PrinterPassword";

    public const string PrinterCameraUrl = "PrinterCameraUrl";

    public const string PrinterCameraWindowNormalSize = "PrinterCameraWindowNormalSize";

    public const string PrinterCameraWindowMiniSize = "PrinterCameraWindowMiniSize";

    public const string WidgetCooldownNozzleThreshold = "WidgetCooldownNozzleThreshold";

    public const string WidgetCooldownBedThreshold = "WidgetCooldownBedThreshold";

    public const string SettingsPollingInterval = "SettingsPollingInterval";

    public const string SettingsRemainingTime = "SettingsRemainingTime";
}
