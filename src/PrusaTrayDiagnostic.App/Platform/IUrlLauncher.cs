namespace PrusaTrayDiagnostic.App.Platform;

/// <summary>Opens web addresses in the user's default browser.</summary>
internal interface IUrlLauncher
{
    /// <summary>
    /// Opens <paramref name="uri"/> if it is a plain http(s) address. Never throws for launch problems: they are logged
    /// and reported as <see langword="false"/>. Does not block the calling (UI) thread.
    /// </summary>
    Task<bool> OpenAsync(Uri uri);
}
