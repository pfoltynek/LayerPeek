namespace LayerPeek.Core.Printers;

/// <summary>
/// Why a printer could not provide data, so that the UI can show a translated text instead of the technical English
/// <see cref="PrinterSnapshot.StatusMessage"/> (T-044).
/// </summary>
public enum PrinterProblem
{
    /// <summary>No known reason (or the printer answered); the UI falls back to the status message.</summary>
    None = 0,

    /// <summary>The address is not valid; nothing was sent.</summary>
    InvalidAddress,

    /// <summary>No connection (refused, host unknown, network down).</summary>
    NotReachable,

    /// <summary>The printer did not answer in time.</summary>
    Timeout,

    /// <summary>The secure (https) connection failed.</summary>
    SecureConnectionFailed,

    /// <summary>The printer rejected the user name or password.</summary>
    PasswordRejected,

    /// <summary>The printer denied access.</summary>
    AccessDenied,

    /// <summary>The printer asked for a password but none is configured.</summary>
    PasswordMissing,

    /// <summary>The stored password could not be read.</summary>
    PasswordUnreadable,

    /// <summary>The printer asked for an unsupported sign-in method.</summary>
    UnsupportedAuthentication,

    /// <summary>Something answered, but not like a printer.</summary>
    UnexpectedResponse,
}
