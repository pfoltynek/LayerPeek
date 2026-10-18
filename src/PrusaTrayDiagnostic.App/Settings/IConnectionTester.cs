using PrusaTrayDiagnostic.Core.Printers;

namespace PrusaTrayDiagnostic.App.Settings;

/// <summary>Outcome of "Test connection" in the settings window. Public only because xUnit theory data needs it.</summary>
public enum ConnectionTestOutcome
{
    Success,
    InvalidAddress,
    NotReachable,
    Timeout,
    SecureConnectionFailed,
    PasswordRejected,
    AccessDenied,
    PasswordMissing,

    /// <summary>The saved password could not be read (secret store failure or no store on this OS).</summary>
    PasswordUnavailable,
    UnsupportedAuthentication,
    UnexpectedResponse,

    /// <summary>The connection type cannot be tested (Prusa Connect, T-005).</summary>
    NotSupported,
}

/// <param name="PrinterModel">Model code reported by the printer ("type.version.subversion"), when known.</param>
internal sealed record ConnectionTestResult(
    ConnectionTestOutcome Outcome,
    string? PrinterModel = null,
    string? Firmware = null,
    string? Hostname = null);

/// <summary>Tries the connection to a printer with values that are not saved yet.</summary>
internal interface IConnectionTester
{
    /// <summary>
    /// Connects to <paramref name="printer"/> once, within a short timeout. Expected failures are reported in the result;
    /// the password is never logged.
    /// </summary>
    /// <param name="password">
    /// Password entered in the form (not saved); empty = no password; <see langword="null"/> = the password saved for
    /// <see cref="PrinterDefinition.Id"/>.
    /// </param>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    Task<ConnectionTestResult> TestAsync(PrinterDefinition printer, string? password, CancellationToken cancellationToken);
}
