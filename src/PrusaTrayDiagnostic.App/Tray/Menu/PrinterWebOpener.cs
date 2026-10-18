using Microsoft.Extensions.Logging;

using PrusaTrayDiagnostic.App.Platform;
using PrusaTrayDiagnostic.Core.Printers;
using PrusaTrayDiagnostic.Core.Secrets;

namespace PrusaTrayDiagnostic.App.Tray.Menu;

/// <summary>
/// Opens the web interface of a printer from the tray menu (T-028). A PrusaLink printer with a stored password is
/// opened logged in (<c>http://user:password@host/</c>); the password is read from <see cref="ISecretStore"/> only when
/// the user clicks, so it is never kept in the menu. Without a password, when the secret store fails, for Prusa Connect
/// or for an unknown printer the plain address is opened as before. Neither the password nor the address with
/// credentials is ever logged.
/// </summary>
internal sealed partial class PrinterWebOpener
{
    private readonly ISecretStore _secrets;
    private readonly IUrlLauncher _launcher;
    private readonly ILogger _logger;

    public PrinterWebOpener(ISecretStore secrets, IUrlLauncher launcher, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(secrets);
        ArgumentNullException.ThrowIfNull(launcher);
        ArgumentNullException.ThrowIfNull(logger);
        _secrets = secrets;
        _launcher = launcher;
        _logger = logger;
    }

    /// <summary>Opens <paramref name="webUri"/>; never throws. Cancelled before launching: opens nothing.</summary>
    /// <param name="webUri">The validated address of the menu entry (<see cref="PrinterWebAddress.For"/>).</param>
    /// <param name="printer">
    /// The printer the address belongs to, or <see langword="null"/> when it is not known (never sends credentials).
    /// </param>
    /// <returns><see langword="true"/> when the browser was started.</returns>
    public async Task<bool> OpenAsync(Uri webUri, PrinterDefinition? printer, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(webUri);
        if (printer is { ConnectionType: ConnectionType.PrusaLink })
        {
            string? password;
            try
            {
                // Called from the menu click on the UI thread: the OS credential store (CredRead, Keychain) may block.
                string key = SecretKeys.ForPrinter(printer.Id);
                password = await Task.Run(() => _secrets.GetAsync(key, cancellationToken), cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return false;
            }
            catch (Exception ex)
            {
                // Secret store unavailable (locked keychain, no backend...): the browser asks for the login instead.
                // The exception comes from the store, which never puts the secret into its messages.
                LogPasswordUnavailable(ex, webUri.Host);
                password = null;
            }

            if (cancellationToken.IsCancellationRequested)
            {
                return false;
            }

            if (!string.IsNullOrEmpty(password))
            {
                // Only the address without credentials is logged.
                LogOpeningLoggedIn(webUri.AbsoluteUri);
                return await _launcher.OpenWithCredentialsAsync(webUri, printer.UserName, password, cancellationToken).ConfigureAwait(false);
            }
        }

        return !cancellationToken.IsCancellationRequested && await _launcher.OpenAsync(webUri).ConfigureAwait(false);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "The stored password for {Host} could not be read; opening its web interface without logging in.")]
    private partial void LogPasswordUnavailable(Exception exception, string host);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Opening the web interface {Address} logged in with the stored credentials.")]
    private partial void LogOpeningLoggedIn(string address);
}
