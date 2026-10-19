using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Globalization;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Microsoft.Extensions.Logging.Abstractions;

using PrusaTrayDiagnostic.App.Cameras;
using PrusaTrayDiagnostic.App.Localization;
using PrusaTrayDiagnostic.App.Resources;
using PrusaTrayDiagnostic.Core.Printers;
using PrusaTrayDiagnostic.Core.Settings;

namespace PrusaTrayDiagnostic.App.Settings;

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
public sealed class SettingsViewModel : ObservableObject, IDisposable, ILanguageAware
{
    /// <summary>Longer names would be cut in the tray menu anyway (<c>TrayMenuFormatter</c> limit).</summary>
    public const int MaxNameLength = 100;

    /// <summary>Main tabs (<see cref="SelectedTabIndex"/>).</summary>
    public const int PrintersTab = 0;

    /// <inheritdoc cref="PrintersTab"/>
    public const int GeneralTab = 1;

    /// <summary>Sub-tabs of the selected printer (<see cref="SelectedPrinterTabIndex"/>, T-033): connection fields.</summary>
    public const int ConnectionPrinterTab = 0;

    /// <summary>Sub-tab of the selected printer: camera fields.</summary>
    public const int CameraPrinterTab = 1;

    /// <summary>How many sub-tabs a printer has (T-035 and T-036 add more).</summary>
    public const int PrinterTabCount = 2;

    private const int TabCount = 2;

    private const int MaxIdAttempts = 100;

    private static readonly decimal MinPollingSeconds = (decimal)AppSettings.MinPollingInterval.TotalSeconds;
    private static readonly decimal MaxPollingSeconds = (decimal)AppSettings.MaxPollingInterval.TotalSeconds;

    private readonly ISettingsEditorStore _store;
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
    private PrinterEditorViewModel? _selectedPrinter;
    private int _selectedTabIndex;
    private int _selectedPrinterTabIndex;
    private string? _notice;
    private string? _statusText;

    // What produced the notice and the status, so that a language switch formats them again.
    private Func<string>? _noticeSource;
    private Func<string>? _statusSource;
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
    private decimal? _remainingTimeMinutes;
    private string? _remainingTimeError;
    private SoundOption _selectedSound;
    private LanguageOption _selectedLanguage;

    /// <param name="newId">Creates ids for new printers; default: a new GUID ("N" format). Tests supply their own.</param>
    /// <param name="cameraTester">Camera "Test" button; default: none available (every test fails).</param>
    internal SettingsViewModel(ISettingsEditorStore store, IConnectionTester tester, PasswordPolicy policy, Func<string>? newId = null, ICameraTester? cameraTester = null)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(tester);
        ArgumentNullException.ThrowIfNull(policy);
        _store = store;
        Tester = tester;
        Policy = policy;
        _newId = newId ?? (static () => Guid.NewGuid().ToString("N"));
        CameraTester = cameraTester ?? new CameraSnapshotTester(UnavailableCameraFeedFactory.Instance, TimeProvider.System, NullLogger<CameraSnapshotTester>.Instance);
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
        Printers.CollectionChanged += OnPrintersChanged;
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

    /// <summary><see cref="PrintersTab"/> or <see cref="GeneralTab"/>; switched by a failed save to the part with the error.</summary>
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
    /// Sub-tab of the printer form (<see cref="ConnectionPrinterTab"/>, <see cref="CameraPrinterTab"/>). Belongs to the
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

    /// <summary>Passwords can be saved on this OS (false on macOS until T-021).</summary>
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

        if (!_disposed)
        {
            Populate(data);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _lifetime.Cancel();
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

        LocalizedOption[] options = [.. SoundOptions, .. LanguageOptions, _prusaLinkOption, _prusaConnectOption, _prusaConnectExistingOption];
        foreach (LocalizedOption option in options)
        {
            option.RefreshText();
        }

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

    private void Populate(SettingsEditorData data)
    {
        AppSettings settings = data.Settings;
        _baseline = settings;
        foreach (PrinterDefinition printer in settings.Printers)
        {
            _usedIds.Add(printer.Id);
            bool? hasPassword = data.PrintersWithPassword is { } withPassword ? withPassword.Contains(printer.Id) : null;
            bool cameraCredentialsRemoved = printer.Camera.CredentialsRemoved || data.CamerasWithRemovedCredentials?.Contains(printer.Id) == true;
            Printers.Add(new PrinterEditorViewModel(this, printer.Id, printer, hasPassword, cameraCredentialsRemoved));
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
        _remainingTimeMinutes = notifications.RemainingTimeMinutes;
        _selectedSound = FindSound(notifications.Sound);
        _selectedLanguage = FindLanguage(settings.Language);

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
        SelectedPrinter = Printers.Count > 0 ? Printers[0] : null;
        UpdateDirty();
        OnEditableChanged();
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

    private async Task SaveAsync()
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
            // The first printer with an error, then its first sub-tab with an error (in the order of the sub-tabs).
            SelectedPrinter = invalid;
            SelectedTabIndex = PrintersTab;
            SelectedPrinterTabIndex = invalid.HasConnectionErrors ? ConnectionPrinterTab : CameraPrinterTab;
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

        if (passwords.Count == 0 && updated.Equals(_baseline))
        {
            CloseRequested?.Invoke(this, EventArgs.Empty);
            return;
        }

        IsSaving = true;
        ShowStatus(static () => Strings.SettingsSaving, isError: false);
        SettingsSaveResult result;
        try
        {
            result = await _store.SaveAsync(new SettingsChange(_baseline, updated, passwords)).ConfigureAwait(true);
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
            ShowStatus(
                result.RollbackIncomplete ? static () => Strings.SettingsSaveRollbackIncomplete
                : result.Status == SettingsSaveStatus.PasswordNotSaved ? static () => Strings.SettingsSaveFailedPassword
                : static () => Strings.SettingsSaveFailedSettings,
                isError: true);
            return;
        }

        _baseline = updated;
        foreach (PrinterEditorViewModel printer in Printers)
        {
            printer.MarkSaved();
        }

        UpdateDirty();
        if (result.UndeletedPasswords > 0)
        {
            ShowStatus(static () => Strings.SettingsSavedPasswordsNotDeleted, isError: false);
            return;
        }

        ShowStatus(null, isError: false);
        CloseRequested?.Invoke(this, EventArgs.Empty);
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

        bool dirty = false;
        foreach (PrinterEditorViewModel printer in Printers)
        {
            dirty |= printer.HasNewPassword;
        }

        IsDirty = dirty || !TryBuildSettings(out AppSettings? current) || !current.Equals(_baseline);
    }

    private void ShowStatus(Func<string>? text, bool isError)
    {
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
    }

    private void OnPrintersChanged(object? sender, NotifyCollectionChangedEventArgs e) => OnPropertyChanged(nameof(HasNoPrinters));

    private static int Adjacent(int index, int count, bool forward) => (index + (forward ? 1 : count - 1)) % count;

    private static string Format(string format, object? arg0) => string.Format(CultureInfo.CurrentCulture, format, arg0);

    private static string Format(string format, object? arg0, object? arg1) => string.Format(CultureInfo.CurrentCulture, format, arg0, arg1);
}
