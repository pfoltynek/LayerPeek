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
    public bool HasErrors => HasConnectionErrors || HasCameraErrors;

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
    /// The camera as entered; <see langword="null"/> when it cannot be saved (an enabled camera without a valid address,
    /// or credentials in the address, which are never stored). A disabled camera keeps an invalid address unchanged.
    /// </summary>
    private CameraSettings? BuildCamera()
    {
        if (CameraSettings.TryParseStreamUri(_cameraUrl, out Uri? uri, out CameraUrlProblem problem))
        {
            return new CameraSettings(_cameraEnabled, uri);
        }

        if (_cameraEnabled || problem == CameraUrlProblem.ContainsCredentials)
        {
            return null;
        }

        if (problem == CameraUrlProblem.Missing)
        {
            return CameraSettings.Disabled;
        }

        try
        {
            return CameraSettings.FromInvalidStreamUrl(_cameraUrl);
        }
        catch (ArgumentException)
        {
            // Something credential-like that the parser did not recognise as such: never kept.
            return null;
        }
    }

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
        catch (Exception)
        {
            // The test still succeeded; only its picture is missing (a command handler must never throw into the UI).
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
        bool blocks = BuildCamera() is null;
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
}
