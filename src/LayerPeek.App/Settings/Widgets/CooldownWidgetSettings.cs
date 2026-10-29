using System.ComponentModel;

using CommunityToolkit.Mvvm.Input;

using LayerPeek.App.Resources;
using LayerPeek.Core.Events;

namespace LayerPeek.App.Settings.Widgets;

/// <summary>
/// The settings panel of the Cool-down widget (T-092, design 4.3; the threshold logic of T-080): the nozzle and bed
/// temperature thresholds of the printer (T-109), "Use thresholds for all printers", and the state of the "Printer cooled down" notification with
/// a link to the General tab, where its switch is (user decision 2026-10-25, Q3). Registered by the Cool-down widget (T-088)
/// as <c>Settings = CooldownWidgetSettings.Instance</c>.
/// </summary>
public sealed class CooldownWidgetSettings : IOverlayWidgetSettingsFactory
{
    private CooldownWidgetSettings()
    {
    }

    public static CooldownWidgetSettings Instance { get; } = new();

    public OverlayWidgetSettingsViewModel Create(IOverlayWidgetSettingsHost host) => new CooldownWidgetSettingsViewModel(host);

    public bool HasError(PrinterEditorViewModel printer)
    {
        ArgumentNullException.ThrowIfNull(printer);
        return printer.CooldownThresholdError is not null;
    }
}

/// <summary>
/// The Cool-down panel for one printer. The thresholds, their error and "Use thresholds for all printers" are those of the
/// printer (<see cref="PrinterEditorViewModel.CooldownNozzleThresholdCelsius"/>,
/// <see cref="PrinterEditorViewModel.CooldownBedThresholdCelsius"/>, T-080, T-109); the panel adds the state of the
/// notification as edited on the General tab and the link there. Stays usable while the camera is off (the notification
/// needs the thresholds too).
/// </summary>
public sealed class CooldownWidgetSettingsViewModel : OverlayWidgetSettingsViewModel
{
    internal CooldownWidgetSettingsViewModel(IOverlayWidgetSettingsHost host)
        : base(host)
    {
        ShowNotificationSettingCommand = new RelayCommand(() => Host.ShowNotificationSetting(PrintEventKind.CooledDown));
        Host.PropertyChanged += OnHostChanged;
    }

    /// <summary>
    /// "“Printer cooled down” notification: on / off", or "Notifications are turned off." when the master switch is off; the
    /// edited values of the General tab, not the saved ones.
    /// </summary>
    public string NotifyStateText =>
        !Host.NotificationsEnabled ? Strings.SettingsWidgetsNotificationsOff
        : Host.IsNotificationOn(PrintEventKind.CooledDown) ? Strings.SettingsWidgetsCooledDownNotifyOn
        : Strings.SettingsWidgetsCooledDownNotifyOff;

    /// <summary>"Change on the General tab": the General tab with the focus on the "Printer cooled down" switch.</summary>
    public IRelayCommand ShowNotificationSettingCommand { get; }

    internal override void RefreshTexts() => OnPropertyChanged(nameof(NotifyStateText));

    protected override void Dispose(bool disposing)
    {
        Host.PropertyChanged -= OnHostChanged;
        base.Dispose(disposing);
    }

    private void OnHostChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(IOverlayWidgetSettingsHost.NotificationsEnabled) or nameof(SettingsViewModel.NotifyCooledDown) or null or "")
        {
            OnPropertyChanged(nameof(NotifyStateText));
        }
    }
}
