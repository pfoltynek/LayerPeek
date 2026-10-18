namespace PrusaTrayDiagnostic.App.Platform;

/// <summary>Opens web addresses in the user's default browser.</summary>
internal interface IUrlLauncher
{
    /// <summary>
    /// Opens <paramref name="uri"/> if it is a plain http(s) address (no credentials). Never throws for launch problems:
    /// they are logged and reported as <see langword="false"/>. Does not block the calling (UI) thread.
    /// </summary>
    Task<bool> OpenAsync(Uri uri);

    /// <summary>
    /// Opens <paramref name="baseUri"/> logged in as <paramref name="userName"/> (<c>http://user:password@host/</c>,
    /// T-028). The userinfo is built only from these credentials, which the app read from its own storage:
    /// <paramref name="baseUri"/> must be a plain http(s) address without credentials of its own (a user-entered address
    /// with userinfo is rejected like in <see cref="OpenAsync"/>). The password and the address with credentials are
    /// never logged. Never throws for launch problems; a cancelled request starts nothing and returns
    /// <see langword="false"/>. Does not block the calling (UI) thread.
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="userName"/> or <paramref name="password"/> is empty.</exception>
    Task<bool> OpenWithCredentialsAsync(Uri baseUri, string userName, string password, CancellationToken cancellationToken);
}
