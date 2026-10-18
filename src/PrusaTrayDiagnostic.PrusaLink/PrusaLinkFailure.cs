namespace PrusaTrayDiagnostic.PrusaLink;

/// <summary>Why a PrusaLink request failed, so that the UI can explain it (e.g. "test connection" in the settings window).</summary>
public enum PrusaLinkFailure
{
    /// <summary>The request succeeded.</summary>
    None = 0,

    /// <summary>The address is not a plain http(s) host address (see <see cref="PrusaLinkAddress"/>); nothing was sent.</summary>
    InvalidAddress,

    /// <summary>No connection (refused, host unknown, network down, connection dropped).</summary>
    NotReachable,

    /// <summary>The printer did not answer in time.</summary>
    Timeout,

    /// <summary>The https (TLS) handshake failed, e.g. an untrusted certificate or https against an http-only printer.</summary>
    SecureConnectionFailed,

    /// <summary>The printer rejected the password (HTTP 401 after answering the challenge).</summary>
    PasswordRejected,

    /// <summary>The printer denied access (HTTP 403).</summary>
    AccessDenied,

    /// <summary>The printer asked for a password but none is configured.</summary>
    PasswordMissing,

    /// <summary>The printer asked for an authentication method other than HTTP digest.</summary>
    UnsupportedAuthentication,

    /// <summary>The password could not be read from the secret store.</summary>
    PasswordUnreadable,

    /// <summary>Something answered, but not like a PrusaLink printer (unexpected status code or body).</summary>
    UnexpectedResponse,
}
