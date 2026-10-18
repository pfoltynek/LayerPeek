using System.Globalization;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using PrusaTrayDiagnostic.App.Resources;
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
    private string _password = string.Empty;
    private ConnectionTypeOption _connectionType;
    private bool _isNew;
    private bool? _hasSavedPassword;

    // Errors of an untouched new printer are hidden until it is edited or saved.
    private bool _showErrors;
    private string? _nameProblem;
    private string? _addressProblem;
    private string? _passwordProblem;

    private CancellationTokenSource? _test;
    private bool _testCancelledByUser;
    private bool _isTesting;
    private string? _testResultText;
    private TestResultKind _testResultKind;

    /// <param name="saved">The saved definition; <see langword="null"/> for a new printer.</param>
    /// <param name="hasSavedPassword">Whether a password is saved; <see langword="null"/> = unknown.</param>
    internal PrinterEditorViewModel(SettingsViewModel owner, string id, PrinterDefinition? saved, bool? hasSavedPassword)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        _owner = owner;
        Id = id;
        _isNew = saved is null;
        _hasSavedPassword = hasSavedPassword;
        _name = saved?.Name ?? string.Empty;
        _address = saved?.Address ?? string.Empty;
        ConnectionType type = saved?.ConnectionType ?? ConnectionType.PrusaLink;
        ConnectionTypes = owner.ConnectionTypesFor(type);
        _connectionType = Array.Find((ConnectionTypeOption[])ConnectionTypes, option => option.Value == type)!;
        _showErrors = saved is not null;
        TestConnectionCommand = new AsyncRelayCommand(TestConnectionAsync, () => IsPrusaLink && !_isTesting && !_owner.IsDisposed);
        CancelTestCommand = new RelayCommand(CancelTest, () => _isTesting);
        ValidateAddress();
        ValidatePassword();
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
            if (!IsPasswordEnabled)
            {
                // A disabled field must not keep a typed password around (it would not be saved anyway).
                ForgetPassword();
            }

            OnPasswordPresentationChanged();
            TestConnectionCommand.NotifyCanExecuteChanged();
            InvalidateTest();
            ValidateAddress();
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

    public string? PasswordError => _showErrors ? _passwordProblem : null;

    /// <summary>Something is invalid (shown or not yet).</summary>
    public bool HasErrors => _nameProblem is not null || _addressProblem is not null || _passwordProblem is not null;

    /// <summary>The list marks the printer as invalid.</summary>
    public bool ShowsErrorMarker => _showErrors && HasErrors;

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

    /// <summary>The field holds a password that will be saved.</summary>
    internal bool HasNewPassword => IsPasswordEnabled && _password.Length > 0;

    /// <summary>The definition to save; <see langword="null"/> while the name or address is invalid.</summary>
    internal PrinterDefinition? ToDefinition() =>
        _nameProblem is not null || _addressProblem is not null
            ? null
            : new PrinterDefinition { Id = Id, Name = _name, ConnectionType = _connectionType.Value, Address = _address };

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
    }

    /// <summary>The printer was removed or the window closed: stops a running test and forgets a typed password.</summary>
    internal void Detach()
    {
        _test?.Cancel();
        ForgetPassword();
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

        if (_addressProblem is not null)
        {
            // Never send anything to an address the client would refuse; explain it like the inline error does.
            RevealErrors();
            SetTestResult(_addressProblem, TestResultKind.Failure);
            return;
        }

        PrinterDefinition printer = new()
        {
            Id = Id,
            Name = _name.Trim() is { Length: > 0 } name ? name : Id,
            ConnectionType = _connectionType.Value,
            Address = _address,
        };

        // Typed password, else the saved one (null); a printer that has none is tested without one.
        string? password = HasNewPassword ? _password
            : _isNew || _hasSavedPassword == false || !_owner.IsPasswordSupported ? string.Empty
            : null;

        CancellationTokenSource test = CancellationTokenSource.CreateLinkedTokenSource(_owner.LifetimeToken);
        _test = test;
        _testCancelledByUser = false;
        IsTesting = true;
        SetTestResult(Strings.SettingsTesting, TestResultKind.Running);
        try
        {
            ConnectionTestResult result = await _owner.Tester.TestAsync(printer, password, test.Token).ConfigureAwait(true);
            if (!test.IsCancellationRequested)
            {
                SetTestResult(Describe(result), result.Outcome == ConnectionTestOutcome.Success ? TestResultKind.Success : TestResultKind.Failure);
            }
            else if (_testCancelledByUser)
            {
                SetTestResult(Strings.SettingsTestCancelled, TestResultKind.Information);
            }
        }
        catch (OperationCanceledException) when (test.IsCancellationRequested)
        {
            // Cancelled by the user; an edit, removal or closing the window already cleared the result.
            if (_testCancelledByUser)
            {
                SetTestResult(Strings.SettingsTestCancelled, TestResultKind.Information);
            }
        }
        catch (Exception)
        {
            // A command handler must never throw into the UI.
            SetTestResult(Strings.SettingsTestFailed, TestResultKind.Failure);
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

    private void SetTestResult(string? text, TestResultKind kind)
    {
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

    private void ValidateAddress()
    {
        string? problem = _address.Trim().Length == 0 ? Strings.SettingsErrorAddressRequired
            // Exactly the rules of the PrusaLink client and the tray menu (credentials, query, fragment, scheme).
            : IsPrusaLink && !PrusaLinkAddress.TryCreateBaseUri(_address, out _) ? Strings.SettingsErrorAddressInvalid
            : null;
        if (_addressProblem != problem)
        {
            _addressProblem = problem;
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
        OnPropertyChanged(nameof(PasswordError));
        OnPropertyChanged(nameof(HasErrors));
        OnPropertyChanged(nameof(ShowsErrorMarker));
    }

    private void OnPasswordPresentationChanged()
    {
        OnPropertyChanged(nameof(PasswordWatermark));
        OnPropertyChanged(nameof(PasswordHint));
    }

    private static string Format(string format, object? arg0) => string.Format(CultureInfo.CurrentCulture, format, arg0);

    private static string Format(string format, object? arg0, object? arg1) => string.Format(CultureInfo.CurrentCulture, format, arg0, arg1);
}
