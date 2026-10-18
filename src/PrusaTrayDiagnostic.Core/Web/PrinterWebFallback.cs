namespace PrusaTrayDiagnostic.Core.Web;

/// <summary>Why <see cref="IPrinterWebGateway"/> returned the plain printer address instead of a proxy address.</summary>
public enum PrinterWebFallback
{
    /// <summary>No fallback: the address is the proxy address.</summary>
    None = 0,

    /// <summary>The gateway does not handle this kind of printer (e.g. Prusa Connect); no address is returned.</summary>
    NotSupported,

    /// <summary>The printer address is not valid; no address is returned.</summary>
    InvalidAddress,

    /// <summary>The printer asks for a password but none is stored.</summary>
    PasswordMissing,

    /// <summary>The stored password could not be read from the secret store.</summary>
    PasswordUnreadable,

    /// <summary>The printer rejected the user name or password.</summary>
    PasswordRejected,

    /// <summary>The printer denied access (HTTP 403).</summary>
    AccessDenied,

    /// <summary>The printer asks for an authentication method the gateway cannot answer.</summary>
    UnsupportedAuthentication,

    /// <summary>The printer is not reachable or did not answer in time.</summary>
    NotReachable,

    /// <summary>Something answered, but not like the expected printer API.</summary>
    UnexpectedResponse,

    /// <summary>The local proxy could not be started (e.g. no free port).</summary>
    ProxyUnavailable,
}
