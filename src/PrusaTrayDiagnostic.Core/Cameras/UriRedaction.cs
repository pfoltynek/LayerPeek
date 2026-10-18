namespace PrusaTrayDiagnostic.Core.Cameras;

/// <summary>Makes addresses safe for logs and display.</summary>
public static class UriRedaction
{
    private const string Mask = "***";

    /// <summary>
    /// <paramref name="uri"/> with its user info (user name and password) replaced by <c>***</c>; addresses without
    /// user info are returned unchanged (escaped form).
    /// </summary>
    public static string Redact(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);
        if (!uri.IsAbsoluteUri)
        {
            throw new ArgumentException("Only absolute addresses can be redacted.", nameof(uri));
        }

        // Rebuilt from the components without user info, so that no parsing quirk can let a password through.
        string redacted = uri.GetComponents(
            UriComponents.SchemeAndServer | UriComponents.PathAndQuery | UriComponents.Fragment,
            UriFormat.UriEscaped);
        int authority = uri.Scheme.Length + Uri.SchemeDelimiter.Length;
        // An "@" left after removing the user info: e.g. "rtsp://admin:1234/x@host", where Uri sees host "admin" and
        // no user info. Everything up to the last "@" is masked, as when such an address is loaded from the settings.
        int at = redacted.LastIndexOf('@');
        if (at >= authority && redacted.AsSpan(0, authority).EndsWith(Uri.SchemeDelimiter, StringComparison.Ordinal))
        {
            return string.Concat(redacted.AsSpan(0, authority), Mask + "@", redacted.AsSpan(at + 1));
        }

        if (string.IsNullOrEmpty(uri.UserInfo))
        {
            return redacted;
        }

        return string.Concat(redacted.AsSpan(0, authority), Mask + "@", redacted.AsSpan(authority));
    }
}
