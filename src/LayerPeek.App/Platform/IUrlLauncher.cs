namespace LayerPeek.App.Platform;

/// <summary>Opens web addresses in the user's default browser.</summary>
internal interface IUrlLauncher
{
    /// <summary>
    /// Opens <paramref name="uri"/> if it is a plain http(s) address (no credentials). Never throws for launch problems:
    /// they are logged and reported as <see langword="false"/>. Does not block the calling (UI) thread.
    /// </summary>
    Task<bool> OpenAsync(Uri uri);

    /// <summary>
    /// Opens <paramref name="uri"/>, an address that itself is a secret (the local web proxy with its access token,
    /// T-028), with the same safety rules as <see cref="OpenAsync"/>. Nothing of the address except its host, and no
    /// exception text (<c>Process.Start</c> puts the whole address into it), ever reaches the log. Never throws for
    /// launch problems; a cancelled request starts nothing and returns <see langword="false"/>. Does not block the
    /// calling (UI) thread.
    /// </summary>
    Task<bool> OpenSecretAsync(Uri uri, CancellationToken cancellationToken);
}
