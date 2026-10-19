using System.Globalization;

using Avalonia.Media;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using PrusaTrayDiagnostic.App.Resources;
using PrusaTrayDiagnostic.Core.Cameras;
using PrusaTrayDiagnostic.Core.Printers;
using PrusaTrayDiagnostic.PrusaLink;

namespace PrusaTrayDiagnostic.App.Settings;

/// <summary>
/// One printer in the settings window: list entry and edit form, with inline validation and "Test connection".
/// The id never changes (passwords, history and notification state are keyed by it), so a rename keeps everything.
/// An empty password field keeps the saved password. UI thread only.
/// </summary>
public sealed class PrinterEditorViewModel : ObservableObject
{
    private const string Unknown = "–";

    private readonly SettingsViewModel _owner;
    private string _name;
    private string _address;
    private string _userName;

    // The saved user name (default for a new printer); kept for Prusa Connect, which does not use it.
    private readonly string _savedUserName;
    private string _password = string.Empty;
    private ConnectionTypeOption _connectionType;
    private bool _isNew;
    private bool? _hasSavedPassword;

    // Errors of an untouched new printer are hidden until it is edited or saved.
    private bool _showErrors;
    private string? _nameProblem;
    private string? _addressProblem;
    private string? _userNameProblem;
    private string? _passwordProblem;

    private CancellationTokenSource? _test;
    private bool _testCancelledByUser;
    private bool _isTesting;
    private string? _testResultText;

    // What produced the test result, so that a language switch formats it again.
    private Func<string>? _testResultSource;
    private TestResultKind _testResultKind;

    // Camera (T-014). The address field keeps an invalid address loaded from the file for correction.
    private bool _cameraEnabled;
    private string _cameraUrl;
    private bool _cameraCredentialsRemoved;
    private string? _cameraUrlProblem;
    private bool _cameraBlocksSave;

    private CancellationTokenSource? _cameraTest;
    private bool _cameraTestCancelledByUser;
    private bool _isTestingCamera;
    private string? _cameraTestResultText;

    // What produced the camera test result, so that a language switch formats it again.
    private Func<string>? _cameraTestResultSource;
    private TestResultKind _cameraTestResultKind;

    // Snapshot of the last successful camera test (T-034); owned here (a bitmap is disposed when it is replaced or cleared).
    private IImage? _cameraPreview;

    // Camera window (T-035). _savedWindow: the saved defaults, also the fallback for a size typed invalid while the
    // camera is off (Q6). The size boxes keep the text as typed.
    private CameraWindowDefaults _savedWindow;
    private bool _rememberLastPlacement;
    private CameraStartModeOption _startMode;
    private CameraMonitorOption _monitor;
    private readonly IReadOnlyList<CameraMonitorOption> _monitorOptions;

    // Accessible names of the size and position boxes ("{row} – {column}"), formatted again after a language switch.
    private string _normalSizeName = string.Empty;
    private string _normalCornerName = string.Empty;
    private string _miniSizeName = string.Empty;
    private string _miniCornerName = string.Empty;
    private string _normalSizeText;
    private CameraCornerOption _normalCorner;
    private string _miniSizeText;
    private CameraCornerOption _miniCorner;

    /// <param name="saved">The saved definition; <see langword="null"/> for a new printer.</param>
    /// <param name="hasSavedPassword">Whether a password is saved; <see langword="null"/> = unknown.</param>
    /// <param name="cameraCredentialsRemoved">Credentials were removed from the stored camera address (notice shown until answered or saved).</param>
    internal PrinterEditorViewModel(SettingsViewModel owner, string id, PrinterDefinition? saved, bool? hasSavedPassword, bool cameraCredentialsRemoved = false)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        _owner = owner;
        Id = id;
        _isNew = saved is null;
        _hasSavedPassword = hasSavedPassword;
        _name = saved?.Name ?? string.Empty;
        _address = saved?.Address ?? string.Empty;
        _savedUserName = saved?.UserName ?? PrinterUserName.Default;
        _userName = _savedUserName;
        ConnectionType type = saved?.ConnectionType ?? ConnectionType.PrusaLink;
        ConnectionTypes = owner.ConnectionTypesFor(type);
        _connectionType = Array.Find((ConnectionTypeOption[])ConnectionTypes, option => option.Value == type)!;
        _showErrors = saved is not null;
        TestConnectionCommand = new AsyncRelayCommand(TestConnectionAsync, () => IsPrusaLink && !_isTesting && !_owner.IsDisposed);
        CancelTestCommand = new RelayCommand(CancelTest, () => _isTesting);
        CameraSettings camera = saved?.Camera ?? CameraSettings.Disabled;
        _cameraEnabled = camera.Enabled;
        _cameraUrl = camera.StreamUri?.OriginalString ?? camera.InvalidStreamUrl ?? string.Empty;
        _cameraCredentialsRemoved = cameraCredentialsRemoved || camera.CredentialsRemoved;
        TestCameraCommand = new AsyncRelayCommand(TestCameraAsync, () => !_isTestingCamera && !_owner.IsDisposed);
        CancelCameraTestCommand = new RelayCommand(CancelCameraTest, () => _isTestingCamera);
        _savedWindow = camera.Window;
        _rememberLastPlacement = _savedWindow.RememberLastPlacement;
        _startMode = owner.StartModeOptions.First(option => option.Value == _savedWindow.StartMode);
        _monitorOptions = CreateMonitorOptions(_savedWindow.Monitor, owner.Screens.GetScreens());
        _monitor = _monitorOptions.First(option => option.Value == _savedWindow.Monitor);
        _normalSizeText = CameraWindowSizeText.Format(_savedWindow.Normal.Size);
        _normalCorner = owner.CornerOptions.First(option => option.Value == _savedWindow.Normal.Corner);
        _miniSizeText = CameraWindowSizeText.Format(_savedWindow.Mini.Size);
        _miniCorner = owner.CornerOptions.First(option => option.Value == _savedWindow.Mini.Corner);
        FormatCellNames();
        ValidateAddress();
        ValidateUserName();
        ValidatePassword();
        ValidateCamera();
    }

    private enum TestResultKind
    {
        None,
        Running,
        Success,
        Failure,
        Information,
    }

    /// <summary>Stable identifier (see <see cref="PrinterDefinition.Id"/>); generated once for a new printer.</summary>
    public string Id { get; }

    /// <summary>Not saved yet.</summary>
    public bool IsNew
    {
        get => _isNew;
        private set => SetProperty(ref _isNew, value);
    }

    public string Name
    {
        get => _name;
        set
        {
            if (SetProperty(ref _name, value ?? string.Empty))
            {
                OnPropertyChanged(nameof(DisplayName));
                RevealErrors();
                _owner.OnPrinterEdited(nameChanged: true);
            }
        }
    }

    /// <summary>Name for the list; "New printer" while the name is empty.</summary>
    public string DisplayName => _name.Trim() is { Length: > 0 } name ? name : Strings.SettingsNewPrinter;

    public IReadOnlyList<ConnectionTypeOption> ConnectionTypes { get; }

    public ConnectionTypeOption SelectedConnectionType
    {
        get => _connectionType;
        set
        {
            // Disabled choices (Prusa Connect for a PrusaLink printer, T-005) and a transient null from a ComboBox are ignored.
            if (value is null || !value.IsEnabled || !SetProperty(ref _connectionType, value))
            {
                return;
            }

            OnPropertyChanged(nameof(IsPrusaLink));
            OnPropertyChanged(nameof(IsPasswordEnabled));
            OnPropertyChanged(nameof(ShowsAddressHint));
            if (!IsPasswordEnabled)
            {
                // A disabled field must not keep a typed password around (it would not be saved anyway).
                ForgetPassword();
            }

            if (!IsPrusaLink)
            {
                // Prusa Connect does not use the disabled field, so it shows (and keeps) the saved user name.
                SetProperty(ref _userName, _savedUserName, nameof(UserName));
            }

            OnPropertyChanged(nameof(UserNameHint));
            OnPasswordPresentationChanged();
            TestConnectionCommand.NotifyCanExecuteChanged();
            InvalidateTest();
            ValidateAddress();
            ValidateUserName();
            ValidatePassword();
            RevealErrors();
            _owner.OnPrinterEdited(nameChanged: false);
        }
    }

    public bool IsPrusaLink => _connectionType.Value == ConnectionType.PrusaLink;

    public string Address
    {
        get => _address;
        set
        {
            if (SetProperty(ref _address, value ?? string.Empty))
            {
                InvalidateTest();
                ValidateAddress();
                RevealErrors();
                _owner.OnPrinterEdited(nameChanged: false);
            }
        }
    }

    /// <summary>
    /// PrusaLink user name (not a secret; saved trimmed with the definition). Validated by <see cref="PrinterUserName"/>;
    /// ignored for Prusa Connect, which keeps the saved one.
    /// </summary>
    public string UserName
    {
        get => _userName;
        set
        {
            if (SetProperty(ref _userName, value ?? string.Empty))
            {
                InvalidateTest();
                ValidateUserName();
                RevealErrors();
                _owner.OnPrinterEdited(nameChanged: false);
            }
        }
    }

    /// <summary>Explanation under the user name field.</summary>
    public string UserNameHint => IsPrusaLink ? Strings.SettingsUserNameHint : Strings.SettingsUserNameNotUsed;

    /// <summary>A new password (not saved yet); empty keeps the saved one. Never logged.</summary>
    public string Password
    {
        get => _password;
        set
        {
            if (SetProperty(ref _password, value ?? string.Empty))
            {
                InvalidateTest();
                ValidatePassword();
                RevealErrors();
                _owner.OnPrinterEdited(nameChanged: false);
            }
        }
    }

    /// <summary>PrusaLink printer on an OS that can store passwords.</summary>
    public bool IsPasswordEnabled => _owner.IsPasswordSupported && IsPrusaLink;

    /// <summary>Placeholder of the empty password field: "Unchanged" when a password is (or may be) saved.</summary>
    public string PasswordWatermark =>
        !IsPasswordEnabled || _isNew ? string.Empty
        : _hasSavedPassword == false ? Strings.SettingsPasswordNotSet
        : Strings.SettingsPasswordUnchanged;

    /// <summary>Explanation under the password field.</summary>
    public string PasswordHint =>
        !IsPrusaLink ? Strings.SettingsPasswordNotUsed
        : !_owner.IsPasswordSupported ? Strings.SettingsPasswordUnsupported
        : _isNew || _hasSavedPassword == false ? Strings.SettingsPasswordNewHint
        : Strings.SettingsPasswordKeepHint;

    public string? NameError => _showErrors ? _nameProblem : null;

    public string? AddressError => _showErrors ? _addressProblem : null;

    public string? UserNameError => _showErrors ? _userNameProblem : null;

    public string? PasswordError => _showErrors ? _passwordProblem : null;

    /// <summary>Something is invalid (shown or not yet).</summary>
    public bool HasErrors => HasConnectionErrors || HasCameraErrors || HasCameraWindowErrors;

    /// <summary>A field of the Printer sub-tab (name, address, user name, password) is invalid (shown or not yet).</summary>
    public bool HasConnectionErrors =>
        _nameProblem is not null || _addressProblem is not null || _userNameProblem is not null || _passwordProblem is not null;

    /// <summary>The camera cannot be saved as entered (shown or not yet); same rule as for saving.</summary>
    public bool HasCameraErrors => _cameraBlocksSave;

    /// <summary>The list marks the printer as invalid.</summary>
    public bool ShowsErrorMarker => _showErrors && HasErrors;

    /// <summary>The "!" on the Printer sub-tab header; same rule as the list marker.</summary>
    public bool ShowsConnectionErrorMarker => _showErrors && HasConnectionErrors;

    /// <summary>The "!" on the Camera sub-tab header; same rule as the list marker.</summary>
    public bool ShowsCameraErrorMarker => _showErrors && HasCameraErrors;

    /// <summary>Accessible name of the Printer sub-tab header: says when it contains errors (the "!" alone is not read out).</summary>
    public string ConnectionTabName => TabName(Strings.SettingsPrinterTabConnection, ShowsConnectionErrorMarker);

    /// <summary>Accessible name of the Camera sub-tab header.</summary>
    public string CameraTabName => TabName(Strings.SettingsCamera, ShowsCameraErrorMarker);

    /// <summary>The address hint (PrusaLink only) gives way to the address error (T-033).</summary>
    public bool ShowsAddressHint => IsPrusaLink && AddressError is null;

    /// <summary>The user name hint gives way to the user name error.</summary>
    public bool ShowsUserNameHint => UserNameError is null;

    /// <summary>The password hint gives way to the password error.</summary>
    public bool ShowsPasswordHint => PasswordError is null;

    /// <summary>The camera address hint gives way to the camera address error.</summary>
    public bool ShowsCameraUrlHint => CameraUrlError is null;

    /// <summary>Tests the values in the form (not saved). Disabled while running, so a double click tests once.</summary>
    public IAsyncRelayCommand TestConnectionCommand { get; }

    public IRelayCommand CancelTestCommand { get; }

    public bool IsTesting
    {
        get => _isTesting;
        private set
        {
            if (SetProperty(ref _isTesting, value))
            {
                TestConnectionCommand.NotifyCanExecuteChanged();
                CancelTestCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public string? TestResultText => _testResultText;

    public bool HasTestResult => _testResultText is not null;

    public bool TestSucceeded => _testResultKind == TestResultKind.Success;

    public bool TestFailed => _testResultKind == TestResultKind.Failure;

    /// <summary>Show the camera in the tray menu (needs a valid address).</summary>
    public bool CameraEnabled
    {
        get => _cameraEnabled;
        set
        {
            if (SetProperty(ref _cameraEnabled, value))
            {
                if (value)
                {
                    // Turning the camera on answers the notice ("check the address and turn the camera on again").
                    CameraCredentialsRemoved = false;
                }

                ValidateCamera();
                // The window sizes are validated only while the camera is on (T-033 Q9).
                OnErrorsChanged();
                RevealErrors();
                _owner.OnPrinterEdited(nameChanged: false);
            }
        }
    }

    /// <summary>RTSP address of the stream (an invalid address loaded from the file is shown here for correction).</summary>
    public string CameraUrl
    {
        get => _cameraUrl;
        set
        {
            if (SetProperty(ref _cameraUrl, value ?? string.Empty))
            {
                // Editing the address answers the notice too.
                CameraCredentialsRemoved = false;
                InvalidateCameraTest();
                ValidateCamera();
                RevealErrors();
                _owner.OnPrinterEdited(nameChanged: false);
            }
        }
    }

    /// <summary>
    /// Why the address cannot be used. Blocks saving while the camera is on or the address contains credentials; a
    /// disabled camera may keep an invalid address (it stays off until corrected).
    /// </summary>
    public string? CameraUrlError => _showErrors ? _cameraUrlProblem : null;

    /// <summary>
    /// Credentials were removed from the stored address; the camera stays off until the user checks it. The notice goes
    /// away once the user reacts (turns the camera on, edits the address or tests it successfully) or saves (T-033, Q3).
    /// </summary>
    public bool CameraCredentialsRemoved
    {
        get => _cameraCredentialsRemoved;
        private set => SetProperty(ref _cameraCredentialsRemoved, value);
    }

    /// <summary>Opens the stream in the form (not saved) and waits for one image; disabled while running.</summary>
    public IAsyncRelayCommand TestCameraCommand { get; }

    public IRelayCommand CancelCameraTestCommand { get; }

    public bool IsTestingCamera
    {
        get => _isTestingCamera;
        private set
        {
            if (SetProperty(ref _isTestingCamera, value))
            {
                TestCameraCommand.NotifyCanExecuteChanged();
                CancelCameraTestCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public string? CameraTestResultText => _cameraTestResultText;

    public bool HasCameraTestResult => _cameraTestResultText is not null;

    public bool CameraTestSucceeded => _cameraTestResultKind == TestResultKind.Success;

    public bool CameraTestFailed => _cameraTestResultKind == TestResultKind.Failure;

    /// <summary>
    /// The image of the last successful camera test (T-034), or <see langword="null"/>. Set only by the Test button; cleared
    /// when the address changes, when a new test starts (so a failed, timed-out or cancelled test leaves none) and when the
    /// printer is removed or the window closes. A language switch, turning the camera off or saving keeps it.
    /// </summary>
    public IImage? CameraPreview => _cameraPreview;

    public bool HasCameraPreview => _cameraPreview is not null;

    // ---- Camera window (T-035): how the camera window opens. Edited only while the camera is on (values are kept). ----

    /// <summary>Use the remembered position and size when there are any (default); off: always the values below.</summary>
    public bool RememberLastPlacement
    {
        get => _rememberLastPlacement;
        set
        {
            if (SetProperty(ref _rememberLastPlacement, value))
            {
                OnWindowEdited();
            }
        }
    }

    public IReadOnlyList<CameraStartModeOption> StartModeOptions => _owner.StartModeOptions;

    public CameraStartModeOption SelectedStartMode
    {
        get => _startMode;
        set
        {
            // A ComboBox may write null while its items change.
            if (value is not null && SetProperty(ref _startMode, value))
            {
                OnWindowEdited();
            }
        }
    }

    /// <summary>
    /// "Primary monitor", the connected monitors ("Monitor 2 – DELL U2719D (1440 × 2560)") and the saved monitor when it
    /// is not among them ("… – not connected" when no monitor with its name is connected). Read when the printer is loaded.
    /// </summary>
    public IReadOnlyList<CameraMonitorOption> MonitorOptions => _monitorOptions;

    public CameraMonitorOption SelectedMonitor
    {
        get => _monitor;
        set
        {
            if (value is not null && SetProperty(ref _monitor, value))
            {
                OnWindowEdited();
            }
        }
    }


    public IReadOnlyList<CameraCornerOption> CornerOptions => _owner.CornerOptions;

    /// <summary>Size of the normal window as typed ("960 × 540", also "960x540"); validated while the camera is on.</summary>
    public string NormalSizeText
    {
        get => _normalSizeText;
        set
        {
            if (SetProperty(ref _normalSizeText, value ?? string.Empty))
            {
                OnWindowEdited(sizeChanged: true);
            }
        }
    }

    public CameraCornerOption SelectedNormalCorner
    {
        get => _normalCorner;
        set
        {
            if (value is not null && SetProperty(ref _normalCorner, value))
            {
                OnWindowEdited();
            }
        }
    }

    /// <summary>Size of the mini preview as typed.</summary>
    public string MiniSizeText
    {
        get => _miniSizeText;
        set
        {
            if (SetProperty(ref _miniSizeText, value ?? string.Empty))
            {
                OnWindowEdited(sizeChanged: true);
            }
        }
    }

    public CameraCornerOption SelectedMiniCorner
    {
        get => _miniCorner;
        set
        {
            if (value is not null && SetProperty(ref _miniCorner, value))
            {
                OnWindowEdited();
            }
        }
    }

    /// <summary>Why the size of the normal window cannot be used; only while the camera is on (T-033 Q9).</summary>
    public string? NormalSizeError => _showErrors && IsInvalidSize(_normalSizeText) ? SizeErrorText : null;

    /// <summary>Why the size of the mini preview cannot be used; only while the camera is on.</summary>
    public string? MiniSizeError => _showErrors && IsInvalidSize(_miniSizeText) ? SizeErrorText : null;

    /// <summary>A size of the camera window sub-tab blocks saving (shown or not yet): only while the camera is on.</summary>
    public bool HasCameraWindowErrors => IsInvalidSize(_normalSizeText) || IsInvalidSize(_miniSizeText);

    /// <summary>The "!" on the Camera window sub-tab header; same rule as the list marker.</summary>
    public bool ShowsCameraWindowErrorMarker => _showErrors && HasCameraWindowErrors;

    /// <summary>Accessible name of the Camera window sub-tab header.</summary>
    public string CameraWindowTabName => TabName(Strings.SettingsPrinterTabCameraWindow, ShowsCameraWindowErrorMarker);

    /// <summary>Accessible names of the size and position boxes: "{row} – {column}".</summary>
    public string NormalSizeName => _normalSizeName;

    public string NormalCornerName => _normalCornerName;

    public string MiniSizeName => _miniSizeName;

    public string MiniCornerName => _miniCornerName;

    /// <summary>The camera window defaults as entered, to compare with the saved ones (tests and saving).</summary>
    internal CameraWindowDefaults? CameraWindow => BuildWindow();

    /// <summary>The field holds a password that will be saved.</summary>
    internal bool HasNewPassword => IsPasswordEnabled && _password.Length > 0;

    /// <summary>The definition to save; <see langword="null"/> while the name, address, user name or camera is invalid.</summary>
    internal PrinterDefinition? ToDefinition() =>
        _nameProblem is not null || _addressProblem is not null || _userNameProblem is not null || BuildCamera() is not { } camera
            ? null
            : new PrinterDefinition
            {
                Id = Id,
                Name = _name,
                ConnectionType = _connectionType.Value,
                Address = _address,
                UserName = EffectiveUserName,
                Camera = camera,
            };

    /// <summary>The user name that is saved and tested: the field for PrusaLink, the saved one otherwise.</summary>
    private string EffectiveUserName => IsPrusaLink ? _userName : _savedUserName;

    /// <summary>
    /// The camera as entered, with its window defaults; <see langword="null"/> when it cannot be saved (an enabled camera
    /// without a valid address or with an invalid window size, or credentials in the address, which are never stored). A
    /// disabled camera keeps an invalid address unchanged and its window defaults (T-035).
    /// </summary>
    private CameraSettings? BuildCamera() => BuildWindow() is { } window ? BuildCamera(window) : null;

    /// <summary>The camera with the address as entered and <paramref name="window"/>; <see langword="null"/> when the address blocks saving.</summary>
    private CameraSettings? BuildCamera(CameraWindowDefaults window)
    {
        if (CameraSettings.TryParseStreamUri(_cameraUrl, out Uri? uri, out CameraUrlProblem problem))
        {
            return new CameraSettings(_cameraEnabled, uri) { Window = window };
        }

        if (_cameraEnabled || problem == CameraUrlProblem.ContainsCredentials)
        {
            return null;
        }

        if (problem == CameraUrlProblem.Missing)
        {
            // No address and the default window: the shared "no camera" value.
            return window == CameraSettings.Disabled.Window ? CameraSettings.Disabled : CameraSettings.Disabled with { Window = window };
        }

        try
        {
            return CameraSettings.FromInvalidStreamUrl(_cameraUrl) with { Window = window };
        }
        catch (ArgumentException)
        {
            // Something credential-like that the parser did not recognise as such: never kept.
            return null;
        }
    }

    /// <summary>
    /// The camera window defaults as entered; <see langword="null"/> while the camera is on and a size is invalid. While
    /// the camera is off an invalid size keeps the saved one (T-035 Q6); everything else is taken as entered.
    /// </summary>
    private CameraWindowDefaults? BuildWindow()
    {
        CameraWindowSize? normal = CameraWindowSizeText.Parse(_normalSizeText);
        CameraWindowSize? mini = CameraWindowSizeText.Parse(_miniSizeText);
        if (_cameraEnabled && (normal is null || mini is null))
        {
            return null;
        }

        return _savedWindow with
        {
            RememberLastPlacement = _rememberLastPlacement,
            StartMode = _startMode.Value,
            Monitor = _monitor.Value,
            Normal = new CameraWindowLayout(normal ?? _savedWindow.Normal.Size, _normalCorner.Value),
            Mini = new CameraWindowLayout(mini ?? _savedWindow.Mini.Size, _miniCorner.Value),
        };
    }

    private bool IsInvalidSize(string text) => _cameraEnabled && CameraWindowSizeText.Parse(text) is null;

    private static string SizeErrorText =>
        Format(Strings.SettingsCameraWindowSizeError, CameraWindowSize.MinWidth, CameraWindowSize.MaxSize, CameraWindowSize.MinHeight, CameraWindowSize.MaxSize);

    private static string CellName(string row, string column) => string.Concat(row, " – ", column);

    private void FormatCellNames()
    {
        _normalSizeName = CellName(Strings.SettingsCameraWindowNormal, Strings.SettingsCameraWindowSize);
        _normalCornerName = CellName(Strings.SettingsCameraWindowNormal, Strings.SettingsCameraWindowPosition);
        _miniSizeName = CellName(Strings.SettingsCameraWindowMini, Strings.SettingsCameraWindowSize);
        _miniCornerName = CellName(Strings.SettingsCameraWindowMini, Strings.SettingsCameraWindowPosition);
    }

    /// <summary>A field of the camera window sub-tab changed.</summary>
    private void OnWindowEdited(bool sizeChanged = false)
    {
        RevealErrors();
        if (sizeChanged)
        {
            OnErrorsChanged();
        }

        _owner.OnPrinterEdited(nameChanged: false);
    }

    /// <summary>
    /// "Primary monitor", then every connected monitor that can be stored (a name and a resolution; two identical ones
    /// cannot be told apart and appear once), numbered in the order of the catalog, then the saved monitor when it is not
    /// one of them.
    /// </summary>
    private static CameraMonitorOption[] CreateMonitorOptions(CameraMonitorId? saved, IReadOnlyList<CameraScreen> screens)
    {
        List<CameraMonitorOption> options = [new CameraMonitorOption(null, isConnected: true, static () => Strings.SettingsCameraWindowMonitorPrimary)];
        for (int i = 0; i < screens.Count; i++)
        {
            CameraScreen screen = screens[i];
            if (!CameraMonitorId.IsValid(screen.DisplayName, screen.Bounds.Width, screen.Bounds.Height))
            {
                continue;
            }

            CameraMonitorId id = CameraMonitorId.FromScreen(screen);
            if (options.Exists(option => option.Value == id))
            {
                continue;
            }

            int number = i + 1;
            options.Add(new CameraMonitorOption(id, isConnected: true, () => IsFriendlyName(id.DisplayName)
                ? Format(Strings.SettingsCameraWindowMonitorNumbered, number, id.DisplayName, id.Width, id.Height)
                : Format(Strings.SettingsCameraWindowMonitorNumberedUnnamed, number, id.Width, id.Height)));
        }

        if (saved is not null && !options.Exists(option => option.Value == saved))
        {
            bool connected = CameraWindowDefaultPlacement.IsConnected(saved, screens);
            options.Add(new CameraMonitorOption(saved, connected, () => Format(
                connected ? Strings.SettingsCameraWindowMonitorSaved : Strings.SettingsCameraWindowMonitorNotConnected,
                IsFriendlyName(saved.DisplayName) ? saved.DisplayName : Strings.SettingsCameraWindowMonitorGeneric,
                saved.Width,
                saved.Height)));
        }

        return [.. options];
    }

    /// <summary>A name people recognise (not the Windows device name <c>\\.\DISPLAY1</c> used when there is none).</summary>
    private static bool IsFriendlyName(string name) => !name.StartsWith(@"\\.\", StringComparison.Ordinal);

    internal void SetNameProblem(string? problem)
    {
        if (_nameProblem != problem)
        {
            _nameProblem = problem;
            OnErrorsChanged();
        }
    }

    /// <summary>Shows the errors (after an edit or a save attempt).</summary>
    internal void RevealErrors()
    {
        if (!_showErrors)
        {
            _showErrors = true;
            OnErrorsChanged();
        }
    }

    /// <summary>After a successful save: the saved state is the new baseline, a typed password is now the saved one.</summary>
    internal void MarkSaved()
    {
        if (HasNewPassword)
        {
            _hasSavedPassword = true;
        }

        ForgetPassword();
        IsNew = false;
        OnPasswordPresentationChanged();
        // The user saved after seeing the notice (see RemovedCameraCredentials).
        CameraCredentialsRemoved = false;
        // The saved window defaults are the new fallback for an invalid size of a turned-off camera (Q6).
        _savedWindow = BuildWindow() ?? _savedWindow;
    }

    /// <summary>
    /// UI thread: the language changed. Validation messages, the connection and camera test results, the user name hint
    /// and the password texts are formatted again (the owner refreshes the name problem); nothing that was typed or tested
    /// changes.
    /// </summary>
    internal void RefreshTexts()
    {
        ValidateAddress();
        ValidateUserName();
        ValidatePassword();
        if (_testResultSource is { } source)
        {
            _testResultText = source();
            OnPropertyChanged(nameof(TestResultText));
        }

        ValidateCamera();
        if (_cameraTestResultSource is { } cameraSource)
        {
            _cameraTestResultText = cameraSource();
            OnPropertyChanged(nameof(CameraTestResultText));
        }

        OnPropertyChanged(nameof(DisplayName));
        OnPropertyChanged(nameof(UserNameHint));
        OnPropertyChanged(nameof(ConnectionTabName));
        OnPropertyChanged(nameof(CameraTabName));
        OnPasswordPresentationChanged();

        // Camera window (T-035): the monitor names (the start modes and corners are shared, refreshed by the owner).
        foreach (CameraMonitorOption option in _monitorOptions)
        {
            option.RefreshText();
        }

        OnPropertyChanged(nameof(NormalSizeError));
        OnPropertyChanged(nameof(MiniSizeError));
        OnPropertyChanged(nameof(CameraWindowTabName));
        FormatCellNames();
        OnPropertyChanged(nameof(NormalSizeName));
        OnPropertyChanged(nameof(NormalCornerName));
        OnPropertyChanged(nameof(MiniSizeName));
        OnPropertyChanged(nameof(MiniCornerName));
    }

    /// <summary>
    /// The printer was removed or the window closed: stops running tests, forgets a typed password and releases the camera
    /// preview.
    /// </summary>
    internal void Detach()
    {
        _test?.Cancel();
        _cameraTest?.Cancel();
        ForgetPassword();
        SetCameraPreview(null);
    }

    private void ForgetPassword()
    {
        if (_password.Length > 0)
        {
            _password = string.Empty;
            OnPropertyChanged(nameof(Password));
            ValidatePassword();
        }
    }

    private async Task TestConnectionAsync()
    {
        if (_isTesting || _owner.IsDisposed)
        {
            return;
        }

        if (_addressProblem is not null || _userNameProblem is not null)
        {
            // Never send anything to an address the client would refuse or with a user name the definition would reject.
            // The explanation is at the field (shown now); the result only points there, so it is not repeated (T-033).
            RevealErrors();
            SetTestResult(static () => Strings.SettingsFixErrors, TestResultKind.Failure);
            return;
        }

        PrinterDefinition printer = new()
        {
            Id = Id,
            Name = _name.Trim() is { Length: > 0 } name ? name : Id,
            ConnectionType = _connectionType.Value,
            Address = _address,
            UserName = EffectiveUserName,
        };

        // Typed password, else the saved one (null); a printer that has none is tested without one.
        string? password = HasNewPassword ? _password
            : _isNew || _hasSavedPassword == false || !_owner.IsPasswordSupported ? string.Empty
            : null;

        CancellationTokenSource test = CancellationTokenSource.CreateLinkedTokenSource(_owner.LifetimeToken);
        _test = test;
        _testCancelledByUser = false;
        IsTesting = true;
        SetTestResult(static () => Strings.SettingsTesting, TestResultKind.Running);
        try
        {
            ConnectionTestResult result = await _owner.Tester.TestAsync(printer, password, test.Token).ConfigureAwait(true);
            if (!test.IsCancellationRequested)
            {
                SetTestResult(() => Describe(result), result.Outcome == ConnectionTestOutcome.Success ? TestResultKind.Success : TestResultKind.Failure);
            }
            else if (_testCancelledByUser)
            {
                SetTestResult(static () => Strings.SettingsTestCancelled, TestResultKind.Information);
            }
        }
        catch (OperationCanceledException) when (test.IsCancellationRequested)
        {
            // Cancelled by the user; an edit, removal or closing the window already cleared the result.
            if (_testCancelledByUser)
            {
                SetTestResult(static () => Strings.SettingsTestCancelled, TestResultKind.Information);
            }
        }
        catch (Exception)
        {
            // A command handler must never throw into the UI.
            SetTestResult(static () => Strings.SettingsTestFailed, TestResultKind.Failure);
        }
        finally
        {
            if (ReferenceEquals(_test, test))
            {
                _test = null;
            }

            test.Dispose();
            IsTesting = false;
        }
    }

    private async Task TestCameraAsync()
    {
        if (_isTestingCamera || _owner.IsDisposed)
        {
            return;
        }

        // A new test replaces the old snapshot; until (unless) it succeeds there is none.
        SetCameraPreview(null);
        if (!CameraSettings.TryParseStreamUri(_cameraUrl, out Uri? uri, out CameraUrlProblem problem))
        {
            // Never open an address that could not be saved. The explanation is at the field (shown now), so the result only
            // points there (T-033); a turned-off camera without an address shows no field error, so the result says it.
            RevealErrors();
            SetCameraTestResult(
                _cameraUrlProblem is not null ? static () => Strings.SettingsFixErrors : () => CameraProblemText(problem),
                TestResultKind.Failure);
            return;
        }

        CancellationTokenSource test = CancellationTokenSource.CreateLinkedTokenSource(_owner.LifetimeToken);
        _cameraTest = test;
        _cameraTestCancelledByUser = false;
        IsTestingCamera = true;
        SetCameraTestResult(static () => Strings.SettingsCameraTesting, TestResultKind.Running);
        try
        {
            CameraTestResult result = await _owner.CameraTester.TestAsync(uri, test.Token).ConfigureAwait(true);
            if (!test.IsCancellationRequested)
            {
                bool success = result.Outcome == CameraTestOutcome.Success;
                // The text source lives as long as the result is shown: it must not keep the image's pixels alive.
                CameraTestResult described = result with { Image = null };
                SetCameraTestResult(() => Describe(described), success ? TestResultKind.Success : TestResultKind.Failure);
                if (success)
                {
                    // The address works without the removed credentials: the notice did its job.
                    CameraCredentialsRemoved = false;
                    ShowCameraPreview(result.Image);
                }
            }
            else if (_cameraTestCancelledByUser)
            {
                SetCameraTestResult(static () => Strings.SettingsTestCancelled, TestResultKind.Information);
            }
        }
        catch (OperationCanceledException) when (test.IsCancellationRequested)
        {
            // Cancelled by the user; an edit, removal or closing the window already cleared the result.
            if (_cameraTestCancelledByUser)
            {
                SetCameraTestResult(static () => Strings.SettingsTestCancelled, TestResultKind.Information);
            }
        }
        catch (Exception)
        {
            // A command handler must never throw into the UI.
            SetCameraTestResult(static () => Strings.SettingsCameraTestFailedUnknown, TestResultKind.Failure);
        }
        finally
        {
            if (ReferenceEquals(_cameraTest, test))
            {
                _cameraTest = null;
            }

            test.Dispose();
            IsTestingCamera = false;
        }
    }

    private void CancelCameraTest()
    {
        if (_cameraTest is { } test)
        {
            _cameraTestCancelledByUser = true;
            test.Cancel();
        }
    }

    /// <summary>The tested address changed: a running test is stopped and an old result and snapshot no longer apply.</summary>
    private void InvalidateCameraTest()
    {
        _cameraTest?.Cancel();
        SetCameraTestResult(null, TestResultKind.None);
        SetCameraPreview(null);
    }

    /// <summary>UI thread: draws the snapshot of a successful test (once per result); without one nothing is shown.</summary>
    private void ShowCameraPreview(CameraImage? image)
    {
        if (image is null)
        {
            return;
        }

        IImage preview;
        try
        {
            preview = _owner.CameraPreviewRenderer.Render(image);
        }
        catch (Exception ex)
        {
            // The test still succeeded; only its picture is missing (a command handler must never throw into the UI).
            // The renderer never sees the address, so its exception cannot contain it.
            try
            {
                SettingsLog.CameraPreviewNotDrawn(_owner.Logger, ex, ex.GetType().Name, ex.Message);
            }
            catch (Exception)
            {
                // A logger that is gone (e.g. during quit) must not break the window either.
            }

            return;
        }

        SetCameraPreview(preview);
    }

    /// <summary>Replaces the shown snapshot; the old one is disposed after the view stopped showing it.</summary>
    private void SetCameraPreview(IImage? preview)
    {
        IImage? old = _cameraPreview;
        if (ReferenceEquals(old, preview))
        {
            return;
        }

        _cameraPreview = preview;
        OnPropertyChanged(nameof(CameraPreview));
        if ((old is null) != (preview is null))
        {
            OnPropertyChanged(nameof(HasCameraPreview));
        }

        (old as IDisposable)?.Dispose();
    }

    private string Describe(CameraTestResult result) => result.Outcome switch
    {
        CameraTestOutcome.Success => Format(Strings.SettingsCameraTestSuccess, result.Width, result.Height),
        CameraTestOutcome.Timeout => Format(Strings.SettingsCameraTestTimeout, (int)Math.Ceiling(_owner.CameraTester.Timeout.TotalSeconds)),
        _ when string.IsNullOrWhiteSpace(result.Reason) => Strings.SettingsCameraTestFailedUnknown,
        _ => Format(Strings.SettingsCameraTestFailed, result.Reason),
    };

    private void SetCameraTestResult(Func<string>? source, TestResultKind kind)
    {
        _cameraTestResultSource = source;
        string? text = source?.Invoke();
        bool textChanged = _cameraTestResultText != text;
        bool kindChanged = _cameraTestResultKind != kind;
        _cameraTestResultText = text;
        _cameraTestResultKind = kind;
        if (textChanged)
        {
            OnPropertyChanged(nameof(CameraTestResultText));
            OnPropertyChanged(nameof(HasCameraTestResult));
        }

        if (kindChanged)
        {
            OnPropertyChanged(nameof(CameraTestSucceeded));
            OnPropertyChanged(nameof(CameraTestFailed));
        }
    }

    private void ValidateCamera()
    {
        // The address only (the window sizes have their own sub-tab and marker).
        bool blocks = BuildCamera(_savedWindow) is null;
        string? problem = null;
        if (!CameraSettings.TryParseStreamUri(_cameraUrl, out _, out CameraUrlProblem kind))
        {
            problem = kind == CameraUrlProblem.Missing && !_cameraEnabled ? null
                // A disabled camera blocks only because of credentials the parser did not name as such.
                : blocks && !_cameraEnabled ? CameraProblemText(CameraUrlProblem.ContainsCredentials)
                : CameraProblemText(kind);
        }

        if (_cameraUrlProblem != problem || _cameraBlocksSave != blocks)
        {
            _cameraUrlProblem = problem;
            _cameraBlocksSave = blocks;
            OnErrorsChanged();
        }
    }

    private static string CameraProblemText(CameraUrlProblem problem) => problem switch
    {
        CameraUrlProblem.Missing => Strings.SettingsCameraErrorMissing,
        CameraUrlProblem.UnsupportedScheme => Strings.SettingsCameraErrorScheme,
        CameraUrlProblem.MissingHost => Strings.SettingsCameraErrorHost,
        CameraUrlProblem.ContainsCredentials => Strings.SettingsCameraErrorCredentials,
        _ => Strings.SettingsCameraErrorInvalid,
    };

    private void CancelTest()
    {
        if (_test is { } test)
        {
            _testCancelledByUser = true;
            test.Cancel();
        }
    }

    /// <summary>The tested values changed: a running test is stopped and an old result no longer applies.</summary>
    private void InvalidateTest()
    {
        _test?.Cancel();
        SetTestResult(null, TestResultKind.None);
    }

    private string Describe(ConnectionTestResult result) => result.Outcome switch
    {
        ConnectionTestOutcome.Success => Format(
            Strings.SettingsTestSuccess,
            string.IsNullOrWhiteSpace(result.PrinterModel) ? Unknown : result.PrinterModel,
            string.IsNullOrWhiteSpace(result.Firmware) ? Unknown : result.Firmware),
        ConnectionTestOutcome.InvalidAddress => Strings.SettingsErrorAddressInvalid,
        ConnectionTestOutcome.NotReachable => Strings.SettingsTestNotReachable,
        ConnectionTestOutcome.Timeout => Strings.SettingsTestTimeout,
        ConnectionTestOutcome.SecureConnectionFailed => Strings.SettingsTestSecureConnection,
        ConnectionTestOutcome.PasswordRejected => Strings.SettingsTestPasswordRejected,
        ConnectionTestOutcome.AccessDenied => Strings.SettingsTestAccessDenied,
        // Without a secret store the field is disabled, so "enter it above" would not help.
        ConnectionTestOutcome.PasswordMissing or ConnectionTestOutcome.PasswordUnavailable when !_owner.IsPasswordSupported => Strings.SettingsPasswordUnsupported,
        ConnectionTestOutcome.PasswordMissing => Strings.SettingsTestPasswordMissing,
        ConnectionTestOutcome.PasswordUnavailable => Strings.SettingsTestPasswordUnavailable,
        ConnectionTestOutcome.UnsupportedAuthentication => Strings.SettingsTestUnsupportedAuthentication,
        ConnectionTestOutcome.NotSupported => Strings.SettingsTestNotSupported,
        _ => Strings.SettingsTestUnexpectedResponse,
    };

    private void SetTestResult(Func<string>? source, TestResultKind kind)
    {
        _testResultSource = source;
        string? text = source?.Invoke();
        bool textChanged = _testResultText != text;
        bool kindChanged = _testResultKind != kind;
        _testResultText = text;
        _testResultKind = kind;
        if (textChanged)
        {
            OnPropertyChanged(nameof(TestResultText));
            OnPropertyChanged(nameof(HasTestResult));
        }

        if (kindChanged)
        {
            OnPropertyChanged(nameof(TestSucceeded));
            OnPropertyChanged(nameof(TestFailed));
        }
    }

    private string? AddressProblem() =>
        _address.Trim().Length == 0 ? Strings.SettingsErrorAddressRequired
        // Exactly the rules of the PrusaLink client and the tray menu (credentials, query, fragment, scheme).
        : IsPrusaLink && !PrusaLinkAddress.TryCreateBaseUri(_address, out _) ? Strings.SettingsErrorAddressInvalid
        : null;

    private void ValidateAddress()
    {
        string? problem = AddressProblem();
        if (_addressProblem != problem)
        {
            _addressProblem = problem;
            OnErrorsChanged();
        }
    }

    // The rules live in Core (PrinterUserName); only the wording is decided here.
    private string? UserNameProblem() =>
        (IsPrusaLink ? PrinterUserName.Validate(_userName) : PrinterUserNameError.None) switch
        {
            PrinterUserNameError.None => null,
            PrinterUserNameError.Empty => Strings.SettingsErrorUserNameRequired,
            PrinterUserNameError.TooLong => Format(Strings.SettingsErrorUserNameTooLong, PrinterUserName.MaxLength),
            PrinterUserNameError.ContainsControlCharacter => Strings.SettingsErrorUserNameControlCharacter,
            PrinterUserNameError.ContainsColon => Strings.SettingsErrorUserNameColon,
            _ => Strings.SettingsErrorUserNameUnsupported,
        };

    private void ValidateUserName()
    {
        string? problem = UserNameProblem();
        if (_userNameProblem != problem)
        {
            _userNameProblem = problem;
            OnErrorsChanged();
        }
    }

    private void ValidatePassword()
    {
        string? problem = !HasNewPassword ? null : _owner.Policy.Check(_password) switch
        {
            PasswordProblem.TooLong => Format(Strings.SettingsErrorPasswordTooLong, _owner.Policy.MaxBytes),
            PasswordProblem.InvalidCharacters => Strings.SettingsErrorPasswordInvalid,
            _ => null,
        };
        if (_passwordProblem != problem)
        {
            _passwordProblem = problem;
            OnErrorsChanged();
        }
    }

    private void OnErrorsChanged()
    {
        OnPropertyChanged(nameof(NameError));
        OnPropertyChanged(nameof(AddressError));
        OnPropertyChanged(nameof(UserNameError));
        OnPropertyChanged(nameof(PasswordError));
        OnPropertyChanged(nameof(CameraUrlError));
        OnPropertyChanged(nameof(HasErrors));
        OnPropertyChanged(nameof(HasConnectionErrors));
        OnPropertyChanged(nameof(HasCameraErrors));
        OnPropertyChanged(nameof(ShowsErrorMarker));
        OnPropertyChanged(nameof(ShowsConnectionErrorMarker));
        OnPropertyChanged(nameof(ShowsCameraErrorMarker));
        OnPropertyChanged(nameof(ConnectionTabName));
        OnPropertyChanged(nameof(CameraTabName));
        OnPropertyChanged(nameof(NormalSizeError));
        OnPropertyChanged(nameof(MiniSizeError));
        OnPropertyChanged(nameof(HasCameraWindowErrors));
        OnPropertyChanged(nameof(ShowsCameraWindowErrorMarker));
        OnPropertyChanged(nameof(CameraWindowTabName));
        OnPropertyChanged(nameof(ShowsAddressHint));
        OnPropertyChanged(nameof(ShowsUserNameHint));
        OnPropertyChanged(nameof(ShowsPasswordHint));
        OnPropertyChanged(nameof(ShowsCameraUrlHint));
    }

    private static string TabName(string name, bool hasErrors) =>
        hasErrors ? Format(Strings.SettingsTabContainsErrors, name) : name;

    private void OnPasswordPresentationChanged()
    {
        OnPropertyChanged(nameof(PasswordWatermark));
        OnPropertyChanged(nameof(PasswordHint));
    }

    private static string Format(string format, object? arg0) => string.Format(CultureInfo.CurrentCulture, format, arg0);

    private static string Format(string format, object? arg0, object? arg1) => string.Format(CultureInfo.CurrentCulture, format, arg0, arg1);

    private static string Format(string format, object? arg0, object? arg1, object? arg2) => string.Format(CultureInfo.CurrentCulture, format, arg0, arg1, arg2);

    private static string Format(string format, object? arg0, object? arg1, object? arg2, object? arg3) =>
        string.Format(CultureInfo.CurrentCulture, format, [arg0, arg1, arg2, arg3]);
}
