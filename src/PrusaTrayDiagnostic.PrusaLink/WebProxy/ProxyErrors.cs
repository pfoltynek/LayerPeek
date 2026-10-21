namespace PrusaTrayDiagnostic.PrusaLink.WebProxy;

/// <summary>
/// Error bodies the proxy itself answers with, in the firmware's error format (<c>{"title": ..., "message": ...}</c>), so
/// that the PrusaLink web shows them as an error message. Technical English texts (decided 2026-10-18); constant, so
/// no serializer is needed. A login problem is always 403, never 401: the web would loop on a 401 and the browser
/// would show its own login dialog.
/// </summary>
internal static class ProxyErrors
{
    public const string JsonContentType = "application/json";

    public static readonly byte[] PasswordRejected =
        """{"title":"403: Forbidden","message":"The printer rejected the user name or password stored in PrusaTrayDiagnostic."}"""u8.ToArray();

    public static readonly byte[] PasswordMissing =
        """{"title":"403: Forbidden","message":"No password for this printer is stored in PrusaTrayDiagnostic."}"""u8.ToArray();

    public static readonly byte[] PasswordUnreadable =
        """{"title":"403: Forbidden","message":"The password stored in PrusaTrayDiagnostic could not be read."}"""u8.ToArray();

    public static readonly byte[] UnsupportedAuthentication =
        """{"title":"403: Forbidden","message":"The printer requires an authentication method PrusaTrayDiagnostic does not support."}"""u8.ToArray();

    public static readonly byte[] LoginExpired =
        """{"title":"403: Forbidden","message":"The login to the printer expired while the request was sent. Please try again."}"""u8.ToArray();

    public static readonly byte[] ForeignPage =
        """{"title":"403: Forbidden","message":"PrusaTrayDiagnostic does not forward changes requested by another web page."}"""u8.ToArray();

    public static readonly byte[] NotReachable =
        """{"title":"502: Bad Gateway","message":"The printer is not reachable."}"""u8.ToArray();

    public static readonly byte[] Timeout =
        """{"title":"504: Gateway Timeout","message":"The printer did not respond in time."}"""u8.ToArray();
}
