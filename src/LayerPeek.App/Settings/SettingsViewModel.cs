using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Globalization;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using LayerPeek.App.Cameras;
using LayerPeek.App.Cameras.Layout;
using LayerPeek.App.Cameras.Overlay;
using LayerPeek.App.Localization;
using LayerPeek.App.Platform;
using LayerPeek.App.Printers;
using LayerPeek.App.Resources;
using LayerPeek.App.Tray;
using LayerPeek.Core.Cameras;
using LayerPeek.Core.Images;
using LayerPeek.Core.Printers;
using LayerPeek.Core.Settings;
using LayerPeek.Core.Updates;

namespace LayerPeek.App.Settings;

/// <summary>An item of the sound selection.</summary>
public sealed class SoundOption(NotificationSound value, Func<string> text) : LocalizedOption(text)
{
    public NotificationSound Value { get; } = value;
}

/// <summary>An item of the connection type selection; Prusa Connect is shown but disabled until T-005.</summary>
public sealed class ConnectionTypeOption(ConnectionType value, Func<string> text, bool isEnabled) : LocalizedOption(text)
{
    public ConnectionType Value { get; } = value;

    public bool IsEnabled { get; } = isEnabled;
}

/// <summary>
/// The settings window (T-015): printers (add / edit / remove, test connection) and general settings (polling interval,
/// notifications). One instance per opened window; it does nothing until <see cref="LoadAsync"/> and nothing after
/// <see cref="Dispose"/> (running tests are cancelled, typed passwords forgotten). Edits stay in the view model until
/// saved; cancelling or closing discards them. Call on the UI thread.
/// </summary>
public sealed partial class SettingsViewModel : ObservableObject, IDisposable, ILanguageAware
{
    /// <summary>Longest printer name (the tray menu shows at most <c>TrayMenuFormatter.MaxNameLength</c> of it, T-042).</summary>
    public const int MaxNameLength = 100;

    /// <summary>Main tabs (<see cref="SelectedTabIndex"/>).</summary>
    public const int PrintersTab = 0;

    /// <summary>Main tab of the widgets of the camera overlay (T-092), right after the Printers tab.</summary>
    public const int WidgetsTab = 1;

    /// <inheritdoc cref="PrintersTab"/>
    public const int GeneralTab = 2;

    /// <summary>Main tab of the start after logon and the updates (T-017).</summary>
    public const int StartupUpdatesTab = 3;

    /// <summary>Sub-tabs of the selected printer (<see cref="SelectedPrinterTabIndex"/>, T-033): connection fields.</summary>
    public const int ConnectionPrinterTab = 0;

    /// <summary>Sub-tab of the selected printer: camera fields.</summary>
    public const int CameraPrinterTab = 1;

    /// <summary>Sub-tab of the selected printer: how the camera window opens (T-035).</summary>
    public const int CameraWindowPrinterTab = 2;

    /// <summary>How many sub-tabs a printer has (the Overlay sub-tab of T-036 gave way to the Widgets tab, T-092).</summary>
    public const int PrinterTabCount = 3;

    private const int TabCount = 4;

    private const int MaxIdAttempts = 100;

    /// <summary>How long "Saved" stays in the status line after Apply (T-040).</summary>
    internal static readonly TimeSpan SavedConfirmationDuration = TimeSpan.FromSeconds(3);

    private static readonly Func<string> SavedConfirmation = static () => Strings.SettingsSavedConfirmation;

    private static readonly decimal MinPollingSeconds = (decimal)AppSettings.MinPollingInterval.TotalSeconds;
    private static readonly decimal MaxPollingSeconds = (decimal)AppSettings.MaxPollingInterval.TotalSeconds;

    private readonly ISettingsEditorStore _store;
    private readonly ICameraWindowStateStore? _cameraWindowStates;
    private readonly ICameraWindowPlacer? _cameraWindowPlacer;
    private readonly TimeProvider _timeProvider;
    private readonly IScreenCatalog _screens;
    private IReadOnlyList<CameraScreen>? _screenList;
    private readonly Func<string> _newId;
    private readonly CancellationTokenSource _lifetime = new();

    // Ids that must never be generated again: loaded printers (a removed one's password is deleted on save) and generated ones.
    private readonly HashSet<string> _usedIds = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConnectionTypeOption _prusaLinkOption = new(ConnectionType.PrusaLink, static () => Strings.SettingsConnectionPrusaLink, isEnabled: true);
    private readonly ConnectionTypeOption _prusaConnectOption = new(ConnectionType.PrusaConnect, static () => Strings.SettingsConnectionPrusaConnect, isEnabled: false);
    private readonly ConnectionTypeOption _prusaConnectExistingOption = new(ConnectionType.PrusaConnect, static () => Strings.SettingsConnectionPrusaConnect, isEnabled: true);

    private AppSettings _baseline = AppSettings.Default;
    private bool _loadStarted;
    private bool _loaded;
    private bool _isSaving;
    private bool _disposed;
    private bool _isDirty;

    // "Add printer…" from the tray (T-042) arrived before the settings were loaded.
    private bool _addPrinterWhenLoaded;
    private PrinterEditorViewModel? _selectedPrinter;
    private int _selectedTabIndex;
    private int _selectedPrinterTabIndex;
    private string? _notice;
    private string? _statusText;

    // What produced the notice and the status, so that a language switch formats them again.
    private Func<string>? _noticeSource;
    private Func<string>? _statusSource;

    // "Saved" after Apply (T-040): shown for SavedConfirmationDuration; _confirmationVersion tells a stale timer apart.
    private int _confirmationVersion;
    private bool _statusIsError;

    private decimal? _pollingIntervalSeconds;
    private string? _pollingIntervalError;
    private bool _notificationsEnabled;
    private bool _notifyPrintStarted;
    private bool _notifyPrintFinished;
    private bool _notifyPrintPaused;
    private bool _notifyPrintResumed;
    private bool _notifyPrintStopped;
    private bool _notifyError;
    private bool _notifyAttention;
    private bool _notifyOffline;
    private bool _notifyCooledDown;
    private decimal? _remainingTimeMinutes;
    private string? _remainingTimeError;
    private SoundOption _selectedSound;
    private LanguageOption _selectedLanguage;
    private bool _debugLogging;
    private bool _offerPrereleaseUpdates;
    private bool _startWithWindows;

    // Whether the app starts after logon as the OS said when loading, or since the last change Save made.
    private bool _autostartBaseline;
    private readonly IAutostart? _autostart;
    private readonly bool _autostartNeedsInstallation;

    // T-019: the autostart was read when loading, so the approval can be read again (window activated); the version
    // discards a read overtaken by a newer one or by Save switching the autostart meanwhile.
    private bool _autostartLoaded;
    private int _autostartReadVersion;
    private bool _switchingAutostart;

    // T-020: the OS settings of the app's notifications (macOS) and whether they are turned off.
    private readonly ISystemNotificationSettings? _notificationSettings;
    private bool _showsNotificationsTurnedOff;
    private bool _showsAutostartApproval;
    private readonly string _productVersion;

    /// <param name="newId">Creates ids for new printers; default: a new GUID ("N" format). Tests supply their own.</param>
    /// <param name="cameraTester">Camera "Test" button; default: none available (every test fails).</param>
    /// <param name="cameraPreviewRenderer">Draws the snapshot of a successful camera test; default: a bitmap (needs Avalonia).</param>
    /// <param name="logger">Problems the window works around (e.g. a camera preview that cannot be drawn); default: none.</param>
    /// <param name="screens">The monitors offered for the camera window (T-035); default: none (only "Primary monitor").</param>
    /// <param name="cameraWindowStates">
    /// Remembered camera windows; a saved change of a printer's window defaults forgets its state there (T-035). Default:
    /// none (nothing to forget).
    /// </param>
    /// <param name="cameraWindowPlacer">
    /// Places an open camera window again after "Apply" (T-040); default: none (Apply only saves).
    /// </param>
    /// <param name="timeProvider">How long "Saved" stays after Apply (T-040); default: the system clock.</param>
    /// <param name="imageImporter">Checks and converts custom printer images (T-039); default: Avalonia/Skia (needs the platform).</param>
    /// <param name="autostart">Start after logon (T-017); default: none (the option is hidden).</param>
    /// <param name="autostartNeedsInstallation">
    /// Without <paramref name="autostart"/>: the app is not installed (T-017), so autostart and "Offer pre-release versions"
    /// are shown disabled with the reason (user decision 2026-10-24) instead of hidden.
    /// </param>
    /// <param name="productVersion">
    /// The running version, for the default of "Offer pre-release versions" (T-017); default: the version of the app.
    /// </param>
    /// <param name="widgets">The overlay widgets listed on the Widgets tab (T-092); default: none.</param>
    /// <param name="layoutModeLauncher">
    /// "Edit layout in camera window…" (T-092): starts the layout mode of the camera window (T-091); default: none (the
    /// button is disabled).
    /// </param>
    /// <param name="layoutWriter">
    /// Announces a layout saved by the layout mode of a camera window, which the window takes over (T-077 Q3); default: none.
    /// </param>
    /// <param name="uiDispatcher">Brings those announcements to the UI thread; default: Avalonia's UI thread.</param>
    /// <param name="notificationSettings">
    /// The OS settings of the app's notifications (macOS, T-020): a warning when they are turned off, with a button that opens
    /// them; default: none (no warning).
    /// </param>
    internal SettingsViewModel(
        ISettingsEditorStore store,
        IConnectionTester tester,
        PasswordPolicy policy,
        Func<string>? newId = null,
        ICameraTester? cameraTester = null,
        ICameraPreviewRenderer? cameraPreviewRenderer = null,
        ILogger<SettingsViewModel>? logger = null,
        IScreenCatalog? screens = null,
        ICameraWindowStateStore? cameraWindowStates = null,
        ICameraWindowPlacer? cameraWindowPlacer = null,
        TimeProvider? timeProvider = null,
        PrinterImageImporter? imageImporter = null,
        IAutostart? autostart = null,
        bool autostartNeedsInstallation = false,
        string? productVersion = null,
        OverlayWidgetRegistry? widgets = null,
        ICameraLayoutModeLauncher? layoutModeLauncher = null,
        ICameraOverlayLayoutWriter? layoutWriter = null,
        IUiDispatcher? uiDispatcher = null,
        ISystemNotificationSettings? notificationSettings = null)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(tester);
        ArgumentNullException.ThrowIfNull(policy);
        _store = store;
        Tester = tester;
        Policy = policy;
        _newId = newId ?? (static () => Guid.NewGuid().ToString("N"));
        CameraTester = cameraTester ?? new CameraSnapshotTester(UnavailableCameraFeedFactory.Instance, TimeProvider.System, NullLogger<CameraSnapshotTester>.Instance);
        CameraPreviewRenderer = cameraPreviewRenderer ?? BitmapCameraPreviewRenderer.Instance;
        Logger = logger ?? NullLogger<SettingsViewModel>.Instance;
        _screens = screens ?? EmptyScreenCatalog.Instance;
        _cameraWindowStates = cameraWindowStates;
        _cameraWindowPlacer = cameraWindowPlacer;
        _timeProvider = timeProvider ?? TimeProvider.System;
        ImageImporter = imageImporter ?? new PrinterImageImporter(AvaloniaPrinterImageProcessor.Instance);
        _autostart = autostart;
        _autostartNeedsInstallation = autostart is null && autostartNeedsInstallation;
        _productVersion = productVersion ?? Windows.AboutViewModel.ProductVersion(typeof(SettingsViewModel).Assembly);
        StartModeOptions =
        [
            new CameraStartModeOption(CameraWindowStartMode.Last, static () => Strings.SettingsCameraWindowStartLast),
            new CameraStartModeOption(CameraWindowStartMode.Normal, static () => Strings.SettingsCameraWindowStartNormal),
            new CameraStartModeOption(CameraWindowStartMode.Mini, static () => Strings.SettingsCameraWindowStartMini),
            new CameraStartModeOption(CameraWindowStartMode.FullScreen, static () => Strings.SettingsCameraWindowStartFullScreen),
        ];
        CornerOptions =
        [
            new CameraCornerOption(CameraWindowCorner.TopLeft, static () => Strings.SettingsCameraWindowCornerTopLeft),
            new CameraCornerOption(CameraWindowCorner.TopRight, static () => Strings.SettingsCameraWindowCornerTopRight),
            new CameraCornerOption(CameraWindowCorner.BottomLeft, static () => Strings.SettingsCameraWindowCornerBottomLeft),
            new CameraCornerOption(CameraWindowCorner.BottomRight, static () => Strings.SettingsCameraWindowCornerBottomRight),
            new CameraCornerOption(CameraWindowCorner.Center, static () => Strings.SettingsCameraWindowCornerCenter),
        ];
        SoundOptions =
        [
            new SoundOption(NotificationSound.Off, static () => Strings.SettingsSoundOff),
            new SoundOption(NotificationSound.ImportantOnly, static () => Strings.SettingsSoundImportantOnly),
            new SoundOption(NotificationSound.All, static () => Strings.SettingsSoundAll),
        ];
        _selectedSound = SoundOptions[1];
        LanguageOptions = LanguageOption.CreateAll();
        _selectedLanguage = LanguageOptions[0];
        AddPrinterCommand = new RelayCommand(AddPrinter, () => IsEditable);
        RemovePrinterCommand = new RelayCommand(RemovePrinter, () => IsEditable && _selectedPrinter is not null);
        SaveCommand = new AsyncRelayCommand(SaveAsync, () => IsEditable);
        CancelCommand = new RelayCommand(Cancel, () => !_isSaving);
        OpenAutostartApprovalSettingsCommand = new RelayCommand(OpenAutostartApprovalSettings, () => _showsAutostartApproval);
        _notificationSettings = notificationSettings;
        OpenNotificationSettingsCommand = new RelayCommand(OpenNotificationSettings, () => _showsNotificationsTurnedOff);
        Printers.CollectionChanged += OnPrintersChanged;
        InitializeWidgets(widgets ?? OverlayWidgetRegistry.Empty, layoutModeLauncher, layoutWriter, uiDispatcher);
    }

    /// <summary>Save succeeded or Cancel was chosen: the window should close (which disposes this view model).</summary>
    public event EventHandler? CloseRequested;

    public ObservableCollection<PrinterEditorViewModel> Printers { get; } = [];

    public PrinterEditorViewModel? SelectedPrinter
    {
        get => _selectedPrinter;
        set
        {
            // A list control may clear the selection while items are replaced; a removed printer cannot be selected.
            if (value is not null && !Printers.Contains(value))
            {
                return;
            }

            if (SetProperty(ref _selectedPrinter, value))
            {
                OnPropertyChanged(nameof(HasSelectedPrinter));
                RemovePrinterCommand.NotifyCanExecuteChanged();
                OnSelectedPrinterChanged();
            }
        }
    }

    public bool HasSelectedPrinter => _selectedPrinter is not null;

    /// <summary>Loaded, and no printer is configured.</summary>
    public bool HasNoPrinters => _loaded && Printers.Count == 0;

    /// <summary>Until <see cref="LoadAsync"/> completed.</summary>
    public bool IsLoading => !_loaded;

    public bool IsSaving
    {
        get => _isSaving;
        private set
        {
            if (SetProperty(ref _isSaving, value))
            {
                OnPropertyChanged(nameof(BlocksClosing));
                OnEditableChanged();
            }
        }
    }

    /// <summary>Loaded and not saving: inputs and commands are enabled.</summary>
    public bool IsEditable => _loaded && !_isSaving && !_disposed;

    /// <summary>Why the settings shown are not exactly the stored ones (damaged file...); <see langword="null"/> when they are.</summary>
    public string? Notice
    {
        get => _notice;
        private set
        {
            if (SetProperty(ref _notice, value))
            {
                OnPropertyChanged(nameof(HasNotice));
            }
        }
    }

    public bool HasNotice => _notice is not null;

    /// <summary>Outcome of the last save attempt (error, or a warning after a successful save).</summary>
    public string? StatusText
    {
        get => _statusText;
        private set
        {
            if (SetProperty(ref _statusText, value))
            {
                OnPropertyChanged(nameof(HasStatus));
            }
        }
    }

    public bool HasStatus => _statusText is not null;

    public bool StatusIsError
    {
        get => _statusIsError;
        private set => SetProperty(ref _statusIsError, value);
    }

    /// <summary><see cref="PrintersTab"/>, <see cref="GeneralTab"/> or <see cref="StartupUpdatesTab"/>; switched by a failed save to the part with the error.</summary>
    public int SelectedTabIndex
    {
        get => _selectedTabIndex;
        set
        {
            // A tab control may write -1 while its items change.
            if (value is >= 0 and < TabCount)
            {
                SetProperty(ref _selectedTabIndex, value);
            }
        }
    }

    /// <summary>
    /// Sub-tab of the printer form (<see cref="ConnectionPrinterTab"/>, <see cref="CameraPrinterTab"/>,
    /// <see cref="CameraWindowPrinterTab"/>). Belongs to the
    /// window, not to a printer: selecting another printer keeps it (Camera stays Camera). A failed save switches it to the
    /// first sub-tab with an error.
    /// </summary>
    public int SelectedPrinterTabIndex
    {
        get => _selectedPrinterTabIndex;
        set
        {
            if (value is >= 0 and < PrinterTabCount)
            {
                SetProperty(ref _selectedPrinterTabIndex, value);
            }
        }
    }

    /// <summary>The hint under the polling interval gives way to its error (T-033).</summary>
    public bool ShowsPollingIntervalHint => _pollingIntervalError is null;

    public static decimal MinPollingIntervalSeconds => MinPollingSeconds;

    public static decimal MaxPollingIntervalSeconds => MaxPollingSeconds;

    public decimal? PollingIntervalSeconds
    {
        get => _pollingIntervalSeconds;
        set => SetGeneral(ref _pollingIntervalSeconds, value);
    }

    public string? PollingIntervalError
    {
        get => _pollingIntervalError;
        private set
        {
            if (SetProperty(ref _pollingIntervalError, value))
            {
                OnPropertyChanged(nameof(ShowsPollingIntervalHint));
            }
        }
    }

    /// <summary>Master switch; the per-event switches apply only when it is on.</summary>
    public bool NotificationsEnabled
    {
        get => _notificationsEnabled;
        set => SetGeneral(ref _notificationsEnabled, value);
    }

    public bool NotifyPrintStarted
    {
        get => _notifyPrintStarted;
        set => SetGeneral(ref _notifyPrintStarted, value);
    }

    public bool NotifyPrintFinished
    {
        get => _notifyPrintFinished;
        set => SetGeneral(ref _notifyPrintFinished, value);
    }

    public bool NotifyPrintPaused
    {
        get => _notifyPrintPaused;
        set => SetGeneral(ref _notifyPrintPaused, value);
    }

    public bool NotifyPrintResumed
    {
        get => _notifyPrintResumed;
        set => SetGeneral(ref _notifyPrintResumed, value);
    }

    public bool NotifyPrintStopped
    {
        get => _notifyPrintStopped;
        set => SetGeneral(ref _notifyPrintStopped, value);
    }

    public bool NotifyError
    {
        get => _notifyError;
        set => SetGeneral(ref _notifyError, value);
    }

    public bool NotifyAttention
    {
        get => _notifyAttention;
        set => SetGeneral(ref _notifyAttention, value);
    }

    public bool NotifyOffline
    {
        get => _notifyOffline;
        set => SetGeneral(ref _notifyOffline, value);
    }

    /// <summary>"Printer cooled down" (T-080), off by default; the threshold is per printer (field in T-092).</summary>
    public bool NotifyCooledDown
    {
        get => _notifyCooledDown;
        set => SetGeneral(ref _notifyCooledDown, value);
    }

    public static decimal MaxRemainingTimeMinutes => NotificationSettings.MaxRemainingTimeMinutes;

    /// <summary>"N minutes remain" threshold; 0 = off.</summary>
    public decimal? RemainingTimeMinutes
    {
        get => _remainingTimeMinutes;
        set => SetGeneral(ref _remainingTimeMinutes, value);
    }

    public string? RemainingTimeError
    {
        get => _remainingTimeError;
        private set => SetProperty(ref _remainingTimeError, value);
    }

    public IReadOnlyList<SoundOption> SoundOptions { get; }

    public SoundOption SelectedSound
    {
        get => _selectedSound;
        set
        {
            // A ComboBox may write null while its items change.
            if (value is not null)
            {
                SetGeneral(ref _selectedSound, value);
            }
        }
    }

    /// <summary>"System default" first, then every supported language under its own name.</summary>
    public IReadOnlyList<LanguageOption> LanguageOptions { get; }

    /// <summary>Language of the texts (T-029); applied right after saving, without a restart.</summary>
    public LanguageOption SelectedLanguage
    {
        get => _selectedLanguage;
        set
        {
            // A ComboBox may write null while its items change.
            if (value is not null)
            {
                SetGeneral(ref _selectedLanguage, value);
            }
        }
    }

    /// <summary>Detailed logging (Debug) in the log file (T-047); applied right after saving, without a restart.</summary>
    public bool DebugLogging
    {
        get => _debugLogging;
        set => SetGeneral(ref _debugLogging, value);
    }

    /// <summary>
    /// "Offer pre-release versions" of the updates (T-017): the saved choice, or while the user never chose, the default of
    /// the running version (<see cref="PrereleaseUpdates"/>). Saved as a choice only once it differs from that default.
    /// </summary>
    public bool OfferPrereleaseUpdates
    {
        get => _offerPrereleaseUpdates;
        set
        {
            // Disabled without an installation (user decision 2026-10-24): a .zip or a development build gets no updates.
            if (!_autostartNeedsInstallation)
            {
                SetGeneral(ref _offerPrereleaseUpdates, value);
            }
        }
    }

    /// <summary>"Offer pre-release versions" can be changed: not in a <c>.zip</c> or a development build on Windows (T-017).</summary>
    public bool IsPrereleaseOptionAvailable => !_autostartNeedsInstallation;

    /// <summary>
    /// Screen readers: a disabled check box cannot be focused, so its reason goes into the help text; otherwise the hint.
    /// </summary>
    public string PrereleaseHelpText => _autostartNeedsInstallation ? Strings.SettingsNeedsInstallation : Strings.SettingsOfferPrereleaseUpdatesHint;

    /// <summary>The reason of the disabled autostart for screen readers (T-017); <see langword="null"/> when it is available.</summary>
    public string? AutostartHelpText => _autostartNeedsInstallation ? AutostartNeedsInstallationText : null;

    /// <summary>
    /// The terms of macOS (T-019, user decision 2026-10-29): the option is "Open at Login" and is missing outside
    /// <c>LayerPeek.app</c>; otherwise the Windows texts of T-017. Set by the composition root by OS.
    /// </summary>
    internal bool UsesMacTerms { get; init; }

    /// <summary>
    /// "Offer pre-release versions" is part of the tab (T-017). Not on macOS until updates exist there (T-019, user decision
    /// 2026-10-29: without Velopack for macOS the tab has no pre-release option).
    /// </summary>
    public bool ShowsPrereleaseOption { get; init; } = true;

    /// <summary>The text of the autostart option: "Start … Windows" (T-017) or "Open at Login" (macOS, T-019).</summary>
    public string AutostartLabel => UsesMacTerms ? Strings.SettingsOpenAtLogin : Strings.SettingsStartWithWindows;

    /// <summary>Why the autostart is disabled: only the installed app (Windows), only <c>LayerPeek.app</c> (macOS).</summary>
    public string AutostartNeedsInstallationText => UsesMacTerms ? Strings.SettingsNeedsAppBundle : Strings.SettingsNeedsInstallation;

    /// <summary>The app can start after logon (T-017): Windows, installed by the installer.</summary>
    public bool IsAutostartAvailable => _autostart is not null;

    /// <summary>
    /// The option is shown: available, or (a <c>.zip</c> or a development build on Windows) disabled with
    /// <see cref="ShowsAutostartNeedsInstallation"/> (user decision 2026-10-24).
    /// </summary>
    public bool IsAutostartShown => _autostart is not null || _autostartNeedsInstallation;

    /// <summary>Why the option is disabled: only the installed app can start after logon.</summary>
    public bool ShowsAutostartNeedsInstallation => _autostartNeedsInstallation;

    /// <summary>
    /// macOS (T-019, user decisions 2026-10-29): macOS reports that only the user can allow "Open at Login", in System
    /// Settings &gt; General &gt; Login Items (the Login Item is <c>requiresApproval</c>). The Startup and updates tab says so
    /// under the option and offers to open those settings (<see cref="OpenAutostartApprovalSettingsCommand"/>). Read when
    /// the window loads, whenever it becomes active again (<see cref="RefreshAutostartApprovalAsync"/>) and after Save
    /// switched the option; never on Windows (no <see cref="IAutostartApproval"/>).
    /// </summary>
    public bool ShowsAutostartApproval
    {
        get => _showsAutostartApproval;
        private set
        {
            if (SetProperty(ref _showsAutostartApproval, value))
            {
                OpenAutostartApprovalSettingsCommand.NotifyCanExecuteChanged();
            }
        }
    }

    /// <summary>Opens the settings of the OS where the user allows the autostart (<see cref="IAutostartApproval"/>).</summary>
    public IRelayCommand OpenAutostartApprovalSettingsCommand { get; }

    /// <summary>
    /// macOS (T-020, user decision 2026-10-29): the user turned LayerPeek's notifications off in System Settings; the General
    /// tab says so next to the notification settings and offers to open them (<see cref="OpenNotificationSettingsCommand"/>).
    /// Read when the window loads and whenever it becomes active again (<see cref="RefreshNotificationPermissionAsync"/>).
    /// </summary>
    public bool ShowsNotificationsTurnedOff
    {
        get => _showsNotificationsTurnedOff;
        private set
        {
            if (SetProperty(ref _showsNotificationsTurnedOff, value))
            {
                OpenNotificationSettingsCommand.NotifyCanExecuteChanged();
            }
        }
    }

    /// <summary>Opens the OS settings of the app's notifications (<see cref="ISystemNotificationSettings.Open"/>).</summary>
    public IRelayCommand OpenNotificationSettingsCommand { get; }

    /// <summary>
    /// Reads again whether the OS has the app's notifications turned off (T-020); UI thread. Never throws; nothing without
    /// <see cref="ISystemNotificationSettings"/> or after the window closed.
    /// </summary>
    public async Task RefreshNotificationPermissionAsync()
    {
        if (_notificationSettings is not { } settings || _disposed)
        {
            return;
        }

        bool turnedOff = await settings.AreTurnedOffAsync(LifetimeToken).ConfigureAwait(true);
        if (!_disposed)
        {
            ShowsNotificationsTurnedOff = turnedOff;
        }
    }

    /// <summary>
    /// Reads again whether only the user can allow the autostart in the settings of the OS (T-019, macOS); UI thread. Never
    /// throws; nothing without <see cref="IAutostartApproval"/>, before the autostart was loaded or after the window closed.
    /// The user may have switched LayerPeek on or off there meanwhile: an option the user has not changed in the window
    /// follows the state of the OS (it stays unchanged, not dirty); an edited option is left to Save.
    /// </summary>
    public async Task RefreshAutostartApprovalAsync()
    {
        if (_autostart is not { } autostart || autostart is not IAutostartApproval approval || !_autostartLoaded || _switchingAutostart || _disposed)
        {
            return;
        }

        int version = ++_autostartReadVersion;
        (bool needsApproval, bool? enabled) = await Task.Run(() => (approval.NeedsApproval(), ReadEnabledOrNull(autostart))).ConfigureAwait(true);
        if (_disposed || version != _autostartReadVersion || _switchingAutostart)
        {
            return;
        }

        ShowsAutostartApproval = needsApproval;
        if (enabled is { } now && now != _autostartBaseline && _startWithWindows == _autostartBaseline)
        {
            _autostartBaseline = now;
            _startWithWindows = now;
            OnPropertyChanged(nameof(StartWithWindows));
            UpdateDirty();
        }
    }

    private bool? ReadEnabledOrNull(IAutostart autostart)
    {
        try
        {
            return autostart.IsEnabled();
        }
        catch (Exception ex)
        {
            SettingsLog.AutostartUnreadable(Logger, ex);
            return null;
        }
    }

    /// <summary>Start after logon (T-017), off by default; switched by Save, not by the check box itself.</summary>
    public bool StartWithWindows
    {
        get => _startWithWindows;
        set
        {
            // Disabled without an installation: nothing to switch.
            if (_autostart is not null)
            {
                SetGeneral(ref _startWithWindows, value);
            }
        }
    }

    /// <summary>Passwords can be saved on this OS (false where no secret store exists, see <see cref="PasswordPolicy"/>).</summary>
    public bool IsPasswordSupported => Policy.IsSupported;

    /// <summary>
    /// The user must not close the window now: a save is running and its outcome (an error in particular) has to stay
    /// visible. Closing by the app (quit) is never blocked; the save is awaited there instead.
    /// </summary>
    public bool BlocksClosing => _isSaving;

    /// <summary>Something differs from the saved state (including a typed password).</summary>
    public bool IsDirty
    {
        get => _isDirty;
        private set => SetProperty(ref _isDirty, value);
    }

    public IRelayCommand AddPrinterCommand { get; }

    public IRelayCommand RemovePrinterCommand { get; }

    /// <summary>Validates, saves, applies and closes; without changes it just closes.</summary>
    public IAsyncRelayCommand SaveCommand { get; }

    /// <summary>Discards the edits and closes; disabled while saving (the outcome must stay visible).</summary>
    public IRelayCommand CancelCommand { get; }

    internal PasswordPolicy Policy { get; }

    internal IConnectionTester Tester { get; }

    internal ICameraTester CameraTester { get; }

    internal ICameraPreviewRenderer CameraPreviewRenderer { get; }

    internal ILogger Logger { get; }

    /// <summary>Checks and converts a custom printer image chosen in the window (T-039).</summary>
    internal PrinterImageImporter ImageImporter { get; }

    /// <summary>
    /// The file dialog for custom printer images (T-039), set by the window; <see langword="null"/>: no dialog (the "Custom
    /// image…" item is disabled).
    /// </summary>
    internal IImageFilePicker? ImageFilePicker { get; set; }

    /// <summary>
    /// The folder the image dialog opens in; <see langword="null"/>: where the OS opens it. The test mode (Debug builds) sets
    /// its data folder (T-067).
    /// </summary>
    internal string? ImageStartDirectory { get; init; }

    /// <summary>"Open as" choices of the camera window (shared by all printers).</summary>
    internal IReadOnlyList<CameraStartModeOption> StartModeOptions { get; }

    /// <summary>Position choices of the camera window (shared by all printers).</summary>
    internal IReadOnlyList<CameraCornerOption> CornerOptions { get; }

    /// <summary>
    /// The monitors offered for the camera window, read once per settings window (T-035 Q10: no live refresh) and shared by
    /// all printers.
    /// </summary>
    internal IReadOnlyList<CameraScreen> ScreenList => _screenList ??= _screens.GetScreens();

    internal bool IsDisposed => _disposed;

    /// <summary>Cancelled when the window closes.</summary>
    internal CancellationToken LifetimeToken => _disposed ? new CancellationToken(canceled: true) : _lifetime.Token;

    /// <summary>Loads the settings and saved-password status; call once after creating the window.</summary>
    public async Task LoadAsync()
    {
        if (_loadStarted || _disposed)
        {
            return;
        }

        _loadStarted = true;
        SettingsEditorData data;
        try
        {
            data = await _store.LoadAsync(_lifetime.Token).ConfigureAwait(true);
        }
        catch (OperationCanceledException) when (_disposed)
        {
            return;
        }
        catch (Exception)
        {
            // The store reports file problems in the result; this is only a safety net for the window.
            data = new SettingsEditorData(AppSettings.Default, SettingsLoadStatus.Unreadable, [], null);
        }

        _autostartBaseline = await ReadAutostartAsync().ConfigureAwait(true);
        if (!_disposed)
        {
            Populate(data);
        }

        _autostartLoaded = true;
        await RefreshAutostartApprovalAsync().ConfigureAwait(true);
        await RefreshNotificationPermissionAsync().ConfigureAwait(true);
    }

    /// <summary>Whether the app starts after logon now; off when there is no autostart or it cannot be read.</summary>
    private async Task<bool> ReadAutostartAsync()
    {
        if (_autostart is not { } autostart)
        {
            return false;
        }

        try
        {
            // A shortcut read through the shell: off the UI thread.
            return await Task.Run(autostart.IsEnabled).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            SettingsLog.AutostartUnreadable(Logger, ex);
            return false;
        }
    }

    /// <summary>
    /// Switches the start after logon when it was changed; the warning for the status line when that failed. On macOS also
    /// updates <see cref="ShowsAutostartApproval"/> (T-019).
    /// </summary>
    private async Task<Func<string>?> ApplyAutostartAsync()
    {
        if (_autostart is not { } autostart || _startWithWindows == _autostartBaseline)
        {
            return null;
        }

        bool wanted = _startWithWindows;
        // A read of the approval started before this switch is out of date.
        _autostartReadVersion++;
        _switchingAutostart = true;
        try
        {
            await Task.Run(() => autostart.SetEnabled(wanted)).ConfigureAwait(true);
            _autostartBaseline = wanted;
            if (autostart is IAutostartApproval)
            {
                // Switched on: allowed; switched off: no Login Item left to allow.
                ShowsAutostartApproval = false;
            }

            return null;
        }
        catch (Exception ex)
        {
            // Still a change to save: the next Save tries again.
            SettingsLog.AutostartNotChanged(Logger, ex, wanted);
            if (ex is AutostartNeedsApprovalException && autostart is IAutostartApproval)
            {
                // T-019 (user decisions 2026-10-29): only the user can allow it in System Settings; the status says so, the
                // Startup and updates tab offers to open them.
                ShowsAutostartApproval = true;
                return static () => Strings.SettingsSavedOpenAtLoginNeedsApproval;
            }

            return static () => Strings.SettingsSavedAutostartFailed;
        }
        finally
        {
            _switchingAutostart = false;
        }
    }

    /// <summary>The pre-release choice to save: none while the shown value is the default of a never-made choice.</summary>
    private bool? PrereleaseChoice() =>
        _baseline.OfferPrereleaseUpdates is null && _offerPrereleaseUpdates == PrereleaseUpdates.IsDefaultFor(_productVersion)
            ? null
            : _offerPrereleaseUpdates;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _lifetime.Cancel();
        DisposeWidgets();
        foreach (PrinterEditorViewModel printer in Printers)
        {
            printer.Detach();
        }

        _lifetime.Dispose();
        Printers.CollectionChanged -= OnPrintersChanged;
    }

    /// <summary>
    /// UI thread: the language changed. Option texts, the notice, the save status and every validation message are
    /// formatted again; edits, the selection and a running test are kept.
    /// </summary>
    public void RefreshTexts()
    {
        if (_disposed)
        {
            return;
        }

        LocalizedOption[] options = [.. SoundOptions, .. LanguageOptions, _prusaLinkOption, _prusaConnectOption, _prusaConnectExistingOption, .. StartModeOptions, .. CornerOptions];
        foreach (LocalizedOption option in options)
        {
            option.RefreshText();
        }

        OnPropertyChanged(nameof(PrereleaseHelpText));
        OnPropertyChanged(nameof(AutostartHelpText));
        OnPropertyChanged(nameof(AutostartLabel));
        OnPropertyChanged(nameof(AutostartNeedsInstallationText));

        Notice = _noticeSource?.Invoke();
        StatusText = _statusSource?.Invoke();
        if (_loaded)
        {
            ValidateGeneral();
        }

        ValidateNames();
        foreach (PrinterEditorViewModel printer in Printers)
        {
            printer.RefreshTexts();
        }

        RefreshWidgetTexts();
    }

    /// <summary>
    /// "Add printer…" of the tray menu (T-042, offered while no printer is configured): shows the Printers tab with the
    /// connection fields and adds a new printer through <see cref="AddPrinterCommand"/>. Only when the window lists no
    /// printer yet: a window that is already open with an unsaved printer (the menu still offers the item until it is
    /// saved) just shows it, so that repeated clicks do not pile up empty printers. Before the settings are loaded it
    /// happens right after loading. UI thread.
    /// </summary>
    public void AddPrinterFromTray()
    {
        if (_disposed)
        {
            return;
        }

        if (!_loaded)
        {
            _addPrinterWhenLoaded = true;
            return;
        }

        SelectedTabIndex = PrintersTab;
        SelectedPrinterTabIndex = ConnectionPrinterTab;
        if (Printers.Count == 0 && AddPrinterCommand.CanExecute(null))
        {
            AddPrinterCommand.Execute(null);
        }
    }

    /// <summary>Ctrl+Tab / Ctrl+Shift+Tab: the next or previous main tab, wrapping around. False when nothing can be switched.</summary>
    public bool SelectAdjacentTab(bool forward)
    {
        if (!IsEditable)
        {
            return false;
        }

        SelectedTabIndex = Adjacent(_selectedTabIndex, TabCount, forward);
        return true;
    }

    /// <summary>
    /// Ctrl+PageDown / Ctrl+PageUp: the next or previous sub-tab of the printer form, wrapping around. Only while the form
    /// is shown (Printers tab with a selected printer); false otherwise.
    /// </summary>
    public bool SelectAdjacentPrinterTab(bool forward)
    {
        if (!IsEditable || _selectedTabIndex != PrintersTab || _selectedPrinter is null)
        {
            return false;
        }

        SelectedPrinterTabIndex = Adjacent(_selectedPrinterTabIndex, PrinterTabCount, forward);
        return true;
    }

    internal ConnectionTypeOption[] ConnectionTypesFor(ConnectionType current) =>
        current == ConnectionType.PrusaConnect
            ? [_prusaLinkOption, _prusaConnectExistingOption]
            : [_prusaLinkOption, _prusaConnectOption];

    /// <summary>A printer's field changed: names are unique across printers, and the dirty state covers everything.</summary>
    internal void OnPrinterEdited(bool nameChanged)
    {
        if (nameChanged)
        {
            ValidateNames();
        }

        UpdateDirty();
    }

    /// <summary>
    /// User decision 2026-10-23: a saved camera monitor connected by its device path gets the connected monitors with its
    /// name as further known twins (<see cref="CameraMonitorId.WithTwinsFrom"/>). Part of what the window starts with, so it
    /// is no change of the user (the window is not dirty) and is written with the next save.
    /// </summary>
    private AppSettings WithRefreshedTwins(AppSettings settings)
    {
        AppSettings refreshed = settings;
        foreach (PrinterDefinition printer in settings.Printers)
        {
            if (printer.Camera.Window.Monitor is { DevicePath: not null } monitor)
            {
                // The same instance when nothing is added.
                refreshed = refreshed.WithCameraMonitor(printer.Id, monitor, monitor.WithTwinsFrom(ScreenList));
            }
        }

        return refreshed;
    }

    private void Populate(SettingsEditorData data)
    {
        AppSettings settings = WithRefreshedTwins(data.Settings);
        _baseline = settings;
        foreach (PrinterDefinition printer in settings.Printers)
        {
            _usedIds.Add(printer.Id);
            bool? hasPassword = data.PrintersWithPassword is { } withPassword ? withPassword.Contains(printer.Id) : null;
            bool cameraCredentialsRemoved = printer.Camera.CredentialsRemoved || data.CamerasWithRemovedCredentials?.Contains(printer.Id) == true;
            byte[]? customImage = data.CustomImages?.GetValueOrDefault(printer.Id);
            Printers.Add(new PrinterEditorViewModel(this, printer.Id, printer, hasPassword, cameraCredentialsRemoved, customImage));
        }

        _pollingIntervalSeconds = (decimal)settings.PollingInterval.TotalSeconds;
        NotificationSettings notifications = settings.Notifications;
        _notificationsEnabled = notifications.Enabled;
        _notifyPrintStarted = notifications.PrintStarted;
        _notifyPrintFinished = notifications.PrintFinished;
        _notifyPrintPaused = notifications.PrintPaused;
        _notifyPrintResumed = notifications.PrintResumed;
        _notifyPrintStopped = notifications.PrintStopped;
        _notifyError = notifications.Error;
        _notifyAttention = notifications.Attention;
        _notifyOffline = notifications.Offline;
        _notifyCooledDown = notifications.CooledDown;
        _remainingTimeMinutes = notifications.RemainingTimeMinutes;
        _selectedSound = FindSound(notifications.Sound);
        _selectedLanguage = FindLanguage(settings.Language);
        _debugLogging = settings.DebugLogging;
        _offerPrereleaseUpdates = PrereleaseUpdates.Offered(settings.OfferPrereleaseUpdates, _productVersion);
        _startWithWindows = _autostartBaseline;
        _overlayOpacityPercent = settings.CameraOverlayOpacityPercent;

        _noticeSource = data.Status switch
        {
            SettingsLoadStatus.Corrupted => static () => Strings.SettingsNoticeCorrupted,
            SettingsLoadStatus.Unreadable => static () => Strings.SettingsNoticeUnreadable,
            _ => data.Warnings.Count > 0 ? static () => Strings.SettingsNoticeWarnings : null,
        };
        Notice = _noticeSource?.Invoke();

        _loaded = true;
        // Everything general at once: the fields above were set without notifications.
        OnPropertyChanged(string.Empty);
        ValidateNames();
        ValidateGeneral();
        // Layouts saved by a camera window while loading (T-092 follow-up): newer than the file that was read, or the same.
        TakeWaitingLayouts();
        SelectedPrinter = Printers.Count > 0 ? Printers[0] : null;
        UpdateDirty();
        OnEditableChanged();
        if (_addPrinterWhenLoaded)
        {
            _addPrinterWhenLoaded = false;
            AddPrinterFromTray();
        }
    }

    private SoundOption FindSound(NotificationSound sound)
    {
        foreach (SoundOption option in SoundOptions)
        {
            if (option.Value == sound)
            {
                return option;
            }
        }

        return SoundOptions[1];
    }

    private LanguageOption FindLanguage(string? code)
    {
        foreach (LanguageOption option in LanguageOptions)
        {
            if (string.Equals(option.Code, code, StringComparison.Ordinal))
            {
                return option;
            }
        }

        return LanguageOptions[0];
    }

    /// <summary>
    /// T-019: System Settings &gt; General &gt; Login Items (macOS). The offer stays until the window reads the state again
    /// (it becomes active again after the user comes back) or Save switches the option.
    /// </summary>
    private void OpenAutostartApprovalSettings()
    {
        if (_showsAutostartApproval && _autostart is IAutostartApproval approval)
        {
            approval.OpenApprovalSettings();
        }
    }

    /// <summary>
    /// T-020: System Settings &gt; Notifications &gt; LayerPeek (macOS). The warning stays until the window reads the
    /// permission again (it becomes active again after the user comes back).
    /// </summary>
    private void OpenNotificationSettings()
    {
        if (_showsNotificationsTurnedOff)
        {
            _notificationSettings?.Open();
        }
    }

    private void Cancel()
    {
        if (!_isSaving)
        {
            CloseRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    private void AddPrinter()
    {
        if (!IsEditable)
        {
            return;
        }

        PrinterEditorViewModel printer = new(this, NewId(), saved: null, hasSavedPassword: false);
        Printers.Add(printer);
        SelectedPrinter = printer;
        ValidateNames();
        UpdateDirty();
    }

    private string NewId()
    {
        for (int attempt = 0; attempt < MaxIdAttempts; attempt++)
        {
            string id = _newId();
            if (!string.IsNullOrWhiteSpace(id) && id.Trim() == id && _usedIds.Add(id))
            {
                return id;
            }
        }

        throw new InvalidOperationException("No unique printer id could be generated.");
    }

    private void RemovePrinter()
    {
        if (!IsEditable || _selectedPrinter is not { } printer)
        {
            return;
        }

        int index = Printers.IndexOf(printer);
        printer.Detach();
        Printers.RemoveAt(index);
        SelectedPrinter = Printers.Count == 0 ? null : Printers[Math.Min(index, Printers.Count - 1)];
        ValidateNames();
        UpdateDirty();
    }

    private Task SaveAsync() => SaveAsync(applying: null);

    /// <summary>
    /// "Apply" of the Camera window sub-tab of <paramref name="printer"/> (T-040): saves exactly like Save, but the window
    /// stays open, and the camera window of that printer (only that one) is placed again by its saved settings, or opens
    /// placed by them when it was closed, also when nothing had to be saved. Nothing happens when saving failed or the input
    /// has errors.
    /// </summary>
    internal Task ApplyAsync(PrinterEditorViewModel printer)
    {
        ArgumentNullException.ThrowIfNull(printer);
        return Printers.Contains(printer) ? SaveAsync(printer) : Task.CompletedTask;
    }

    /// <param name="applying">
    /// <see langword="null"/>: Save (closes after success); otherwise Apply for this printer (stays open, places its camera
    /// window).
    /// </param>
    private async Task SaveAsync(PrinterEditorViewModel? applying)
    {
        if (!IsEditable)
        {
            return;
        }

        foreach (PrinterEditorViewModel printer in Printers)
        {
            printer.RevealErrors();
        }

        ValidateGeneral();
        if (FirstInvalidPrinter() is { } invalid)
        {
            // The first printer with an error, then its first sub-tab with an error (in the order of the sub-tabs), else the
            // Widgets tab with the first widget whose settings have the error (T-092).
            SelectedPrinter = invalid;
            if (invalid.HasConnectionErrors || invalid.HasCameraErrors || invalid.HasCameraWindowErrors)
            {
                SelectedTabIndex = PrintersTab;
                SelectedPrinterTabIndex = invalid.HasConnectionErrors ? ConnectionPrinterTab
                    : invalid.HasCameraErrors ? CameraPrinterTab
                    : CameraWindowPrinterTab;
            }
            else
            {
                SelectedTabIndex = WidgetsTab;
                SelectFirstWidgetWithError(invalid);
            }

            ShowStatus(static () => Strings.SettingsFixErrors, isError: true);
            return;
        }

        if (_pollingIntervalError is not null || _remainingTimeError is not null || !TryBuildSettings(out AppSettings? updated))
        {
            SelectedTabIndex = GeneralTab;
            ShowStatus(static () => Strings.SettingsFixErrors, isError: true);
            return;
        }

        Dictionary<string, string> passwords = new(StringComparer.OrdinalIgnoreCase);
        foreach (PrinterEditorViewModel printer in Printers)
        {
            if (printer.HasNewPassword)
            {
                passwords[printer.Id] = printer.Password;
            }
        }

        // Custom images imported in the window (T-039), stored by the store only after the settings were saved.
        Dictionary<string, byte[]> images = new(StringComparer.OrdinalIgnoreCase);
        foreach (PrinterEditorViewModel printer in Printers)
        {
            if (printer.PendingImage is { } png)
            {
                images[printer.Id] = png;
            }
        }

        if (passwords.Count == 0 && images.Count == 0 && updated.Equals(_baseline))
        {
            // Nothing to write; "Factory settings" with the saved values still forgets the remembered window (T-040).
            ForgetRememberedCameraWindows(_baseline, updated);
            Func<string>? autostartOnly = await ApplyAutostartAsync().ConfigureAwait(true);
            if (_disposed)
            {
                return;
            }

            UpdateDirty();
            Completed(applying, updated, autostartOnly);
            return;
        }

        IsSaving = true;
        ShowStatus(static () => Strings.SettingsSaving, isError: false);
        SettingsSaveResult result;
        // The store reads the layouts saved by a camera window that this window has not taken over yet when it writes
        // (T-092 follow-up): a layout the window did not change is saved as the camera window saved it.
        AnnouncedOverlayLayouts.Reader latestLayouts = _announcedLayouts.CreateReader();
        try
        {
            result = await _store.SaveAsync(new SettingsChange(_baseline, updated, passwords, images.Count > 0 ? images : null, latestLayouts)).ConfigureAwait(true);
        }
        catch (Exception)
        {
            // The store reports storage failures in the result; this is only a safety net for the window.
            result = new SettingsSaveResult(SettingsSaveStatus.SettingsNotSaved);
        }
        finally
        {
            IsSaving = false;
        }

        if (_disposed)
        {
            return;
        }

        if (result.Status != SettingsSaveStatus.Saved)
        {
            // Layouts saved by a camera window while saving: the file has them, the window takes them now.
            TakeWaitingLayouts();
            UpdateDirty();
            ShowStatus(
                result.RollbackIncomplete ? static () => Strings.SettingsSaveRollbackIncomplete
                : result.Status == SettingsSaveStatus.PasswordNotSaved ? static () => Strings.SettingsSaveFailedPassword
                : static () => Strings.SettingsSaveFailedSettings,
                isError: true);
            return;
        }

        ForgetRememberedCameraWindows(_baseline, updated);
        _baseline = updated;
        // Before MarkSaved: a printer new in this save is no target of a camera window's layout.
        TakeLayoutsAfterSave(latestLayouts, result.KeptOverlayLayouts);
        // T-039: a printer whose new image could not be stored keeps it (still unsaved), so that Save tries again; it shows
        // its previous custom image meanwhile when there is one, otherwise its illustration.
        bool unsavedImages = false;
        bool allKeepPreviousImage = true;
        foreach (PrinterEditorViewModel printer in Printers)
        {
            bool imageStored = result.UnsavedImages?.Contains(printer.Id) != true || printer.PendingImage is null;
            if (!imageStored)
            {
                unsavedImages = true;
                allKeepPreviousImage &= printer.HasStoredCustomImage;
            }

            printer.MarkSaved(imageStored);
        }

        // T-017: after the settings were saved, so that a failed save changes nothing of the OS either.
        Func<string>? autostartWarning = await ApplyAutostartAsync().ConfigureAwait(true);
        if (_disposed)
        {
            return;
        }

        UpdateDirty();
        Func<string>? imageNotice = !unsavedImages ? null
            : allKeepPreviousImage ? static () => Strings.SettingsSavedImageNotStoredPrevious
            : static () => Strings.SettingsSavedImageNotStored;
        // Both notices when both happened (the window stays open for them).
        Func<string>? warning = result.UndeletedPasswords == 0 ? imageNotice
            : imageNotice is null ? static () => Strings.SettingsSavedPasswordsNotDeleted
            : () => string.Concat(Strings.SettingsSavedPasswordsNotDeleted, " ", imageNotice());
        if (autostartWarning is not null)
        {
            Func<string>? others = warning;
            warning = others is null ? autostartWarning : () => string.Concat(others(), " ", autostartWarning());
        }

        // The saved settings with the layouts saved by a camera window (a closed camera window opens from them).
        Completed(applying, _baseline, warning);
    }

    /// <summary>
    /// After a successful save (or nothing to save): Save closes the window unless there is a warning to read; Apply keeps
    /// it open and places the camera window of its printer again (T-040).
    /// </summary>
    private void Completed(PrinterEditorViewModel? applying, AppSettings saved, Func<string>? warning)
    {
        ShowStatus(warning, isError: false);
        if (applying is null)
        {
            if (warning is null)
            {
                CloseRequested?.Invoke(this, EventArgs.Empty);
            }

            return;
        }

        // Apply keeps the window open: a short "Saved" unless a warning has to stay (user decision 2026-10-20).
        if (warning is null)
        {
            ShowSavedConfirmation();
        }

        // T-040 (exception to T-035 Q21 for Apply only): the camera window takes the saved settings right away; a closed one
        // opens (user decision 2026-10-21).
        PrinterDefinition? printer = saved.Printers.FirstOrDefault(candidate => string.Equals(candidate.Id, applying.Id, StringComparison.OrdinalIgnoreCase));
        if (printer is { Camera.Enabled: true })
        {
            _cameraWindowPlacer?.ApplySettings(printer);
        }
    }

    /// <summary>
    /// T-035: a printer whose saved window defaults changed (start mode, monitor, size or position; not the "remember"
    /// switch) forgets its remembered camera window, so that the new defaults apply the next time it opens. An open
    /// window does not move. New and removed printers have nothing to forget. T-040: after "Factory settings" the printer
    /// forgets it in any case.
    /// </summary>
    private void ForgetRememberedCameraWindows(AppSettings saved, AppSettings updated)
    {
        HashSet<string> forget = new(StringComparer.OrdinalIgnoreCase);
        foreach (PrinterEditorViewModel editor in Printers)
        {
            if (editor.ForgetsRememberedCameraWindow)
            {
                forget.Add(editor.Id);
                editor.OnRememberedCameraWindowForgotten();
            }
        }

        foreach (PrinterDefinition printer in updated.Printers)
        {
            PrinterDefinition? old = saved.Printers.FirstOrDefault(candidate => string.Equals(candidate.Id, printer.Id, StringComparison.OrdinalIgnoreCase));
            if (old is not null && CameraWindowDefaults.RequiresForgettingRememberedState(old.Camera.Window, printer.Camera.Window))
            {
                forget.Add(printer.Id);
            }
        }

        if (_cameraWindowStates is { } states)
        {
            foreach (string id in forget)
            {
                states.Remove(id);
            }
        }
    }

    private PrinterEditorViewModel? FirstInvalidPrinter()
    {
        foreach (PrinterEditorViewModel printer in Printers)
        {
            if (printer.HasErrors)
            {
                return printer;
            }
        }

        return null;
    }

    private bool TryBuildSettings([System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out AppSettings? settings)
    {
        settings = null;
        if (_pollingIntervalSeconds is not { } seconds || _remainingTimeMinutes is not { } minutes || _pollingIntervalError is not null || _remainingTimeError is not null)
        {
            return false;
        }

        PrinterDefinition[] printers = new PrinterDefinition[Printers.Count];
        for (int i = 0; i < printers.Length; i++)
        {
            if (Printers[i].ToDefinition() is not { } definition)
            {
                return false;
            }

            printers[i] = definition;
        }

        try
        {
            settings = new AppSettings
            {
                PollingInterval = TimeSpan.FromSeconds((double)seconds),
                Language = _selectedLanguage.Code,
                DebugLogging = _debugLogging,
                OfferPrereleaseUpdates = PrereleaseChoice(),

                // The slider of the Widgets tab (T-092); always valid (clamped and rounded by the property).
                CameraOverlayOpacityPercent = _overlayOpacityPercent,
                Notifications = new NotificationSettings
                {
                    Enabled = _notificationsEnabled,
                    PrintStarted = _notifyPrintStarted,
                    PrintFinished = _notifyPrintFinished,
                    PrintPaused = _notifyPrintPaused,
                    PrintResumed = _notifyPrintResumed,
                    PrintStopped = _notifyPrintStopped,
                    Error = _notifyError,
                    Attention = _notifyAttention,
                    Offline = _notifyOffline,
                    CooledDown = _notifyCooledDown,
                    RemainingTimeMinutes = (int)minutes,
                    Sound = _selectedSound.Value,
                },
                Printers = printers,
            };
            return true;
        }
        catch (ArgumentException)
        {
            // Out of range after all (rounding at the bounds); the inline validation shows why.
            return false;
        }
    }

    private void SetGeneral<T>(ref T field, T value, [System.Runtime.CompilerServices.CallerMemberName] string? propertyName = null)
    {
        if (SetProperty(ref field, value, propertyName))
        {
            ValidateGeneral();
            UpdateDirty();
        }
    }

    private void ValidateGeneral()
    {
        PollingIntervalError = _pollingIntervalSeconds is { } seconds && seconds >= MinPollingSeconds && seconds <= MaxPollingSeconds
            ? null
            : Format(Strings.SettingsErrorPollingInterval, MinPollingSeconds, MaxPollingSeconds);
        RemainingTimeError = _remainingTimeMinutes is { } minutes && minutes >= 0 && minutes <= NotificationSettings.MaxRemainingTimeMinutes && minutes == decimal.Truncate(minutes)
            ? null
            : Format(Strings.SettingsErrorRemainingTime, NotificationSettings.MaxRemainingTimeMinutes);
    }

    /// <summary>Names must be unique (trimmed, case-insensitive) so that the tray menu and notifications are unambiguous.</summary>
    private void ValidateNames()
    {
        Dictionary<string, int> counts = new(Printers.Count, StringComparer.OrdinalIgnoreCase);
        foreach (PrinterEditorViewModel printer in Printers)
        {
            string name = printer.Name.Trim();
            if (name.Length > 0)
            {
                counts[name] = counts.GetValueOrDefault(name) + 1;
            }
        }

        foreach (PrinterEditorViewModel printer in Printers)
        {
            string name = printer.Name.Trim();
            printer.SetNameProblem(
                name.Length == 0 ? Strings.SettingsErrorNameRequired
                : name.Length > MaxNameLength ? Format(Strings.SettingsErrorNameTooLong, MaxNameLength)
                : counts[name] > 1 ? Strings.SettingsErrorNameDuplicate
                : null);
        }
    }

    private void UpdateDirty()
    {
        if (!_loaded)
        {
            return;
        }

        HideSavedConfirmation();

        bool dirty = false;
        foreach (PrinterEditorViewModel printer in Printers)
        {
            // T-040: "Factory settings" is something to save even when the values equal the saved ones. T-039: an imported
            // image is unsaved even when the choice stays "custom".
            dirty |= printer.HasNewPassword || printer.ForgetsRememberedCameraWindow || printer.PendingImage is not null;
        }

        // T-017: the start after logon is the state of the OS, not part of the settings file.
        dirty |= _autostart is not null && _startWithWindows != _autostartBaseline;
        IsDirty = dirty || !TryBuildSettings(out AppSettings? current) || !current.Equals(_baseline);
    }

    /// <summary>
    /// A custom printer image could not be imported (T-039): the reason goes to the status line, which has room for it in
    /// every language without making the printer form scroll; <see langword="null"/> clears it when it is still shown.
    /// </summary>
    internal void ShowImageError(Func<string>? previous, Func<string>? text)
    {
        if (text is not null)
        {
            ShowStatus(text, isError: true);
        }
        else if (previous is not null && ReferenceEquals(_statusSource, previous))
        {
            ShowStatus(null, isError: false);
        }
    }

    /// <summary>"Saved" after Apply, for <see cref="SavedConfirmationDuration"/> (TimeProvider) or until the next edit.</summary>
    private void ShowSavedConfirmation()
    {
        ShowStatus(SavedConfirmation, isError: false);
        _ = HideSavedConfirmationLaterAsync(_confirmationVersion);
    }

    private async Task HideSavedConfirmationLaterAsync(int version)
    {
        try
        {
            // Resumes on the UI thread (its synchronization context).
            await Task.Delay(SavedConfirmationDuration, _timeProvider, LifetimeToken).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            // The window closed.
            return;
        }

        if (!_disposed && version == _confirmationVersion)
        {
            ShowStatus(null, isError: false);
        }
    }

    /// <summary>An edit after Apply: "Saved" no longer describes what is shown.</summary>
    private void HideSavedConfirmation()
    {
        if (ReferenceEquals(_statusSource, SavedConfirmation))
        {
            ShowStatus(null, isError: false);
        }
    }

    private void ShowStatus(Func<string>? text, bool isError)
    {
        _confirmationVersion++;
        _statusSource = text;
        StatusIsError = isError;
        StatusText = text?.Invoke();
    }

    private void OnEditableChanged()
    {
        OnPropertyChanged(nameof(IsEditable));
        AddPrinterCommand.NotifyCanExecuteChanged();
        RemovePrinterCommand.NotifyCanExecuteChanged();
        SaveCommand.NotifyCanExecuteChanged();
        CancelCommand.NotifyCanExecuteChanged();
        foreach (PrinterEditorViewModel printer in Printers)
        {
            printer.OnOwnerEditableChanged();
        }

        OnWidgetsStateChanged();
    }

    private void OnPrintersChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(HasNoPrinters));
        foreach (PrinterEditorViewModel printer in Printers)
        {
            printer.OnPrinterCountChanged();
        }

        OnWidgetsStateChanged();
    }

    /// <summary>
    /// "Use threshold for all printers" of <paramref name="source"/> (T-080): its valid threshold goes to every other printer
    /// of the window, saved with the other changes. An invalid threshold copies nothing (its error is shown).
    /// </summary>
    internal void ApplyCooldownThresholdToAll(PrinterEditorViewModel source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (!IsEditable || Printers.Count < 2 || !Printers.Contains(source))
        {
            return;
        }

        if (source.ValidCooldownThreshold is not { } threshold)
        {
            source.RevealErrors();
            return;
        }

        ApplyToOtherPrinters(source, printer => printer.CooldownThresholdCelsius = threshold, static () => Strings.SettingsCooldownAppliedAll);
    }

    private static int Adjacent(int index, int count, bool forward) => (index + (forward ? 1 : count - 1)) % count;

    private static string Format(string format, object? arg0) => string.Format(CultureInfo.CurrentCulture, format, arg0);

    private static string Format(string format, object? arg0, object? arg1) => string.Format(CultureInfo.CurrentCulture, format, arg0, arg1);
}
