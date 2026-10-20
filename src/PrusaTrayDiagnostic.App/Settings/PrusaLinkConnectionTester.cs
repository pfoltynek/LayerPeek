using Microsoft.Extensions.Logging;

using PrusaTrayDiagnostic.Core.Printers;
using PrusaTrayDiagnostic.Core.Secrets;
using PrusaTrayDiagnostic.PrusaLink;

namespace PrusaTrayDiagnostic.App.Settings;

/// <summary>
/// "Test connection" for PrusaLink: one <see cref="PrusaLinkClient.GetInfoAsync"/> (<c>/api/version</c> + <c>/api/v1/info</c>)
/// with the values from the form, on the shared "PrusaLink" HTTP configuration (3 s per request), within
/// <see cref="DefaultTimeout"/> overall. An entered password is handed to the client in memory only; it is never
/// written to the secret store and never logged (the client logs only the printer id).
/// </summary>
internal sealed class PrusaLinkConnectionTester : IConnectionTester
{
    /// <summary>Covers the challenge round trip and both requests on a slow network.</summary>
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(10);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ISecretStore _secretStore;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<PrusaLinkClient> _logger;

    public PrusaLinkConnectionTester(IHttpClientFactory httpClientFactory, ISecretStore secretStore, TimeProvider timeProvider, ILogger<PrusaLinkClient> logger)
    {
        ArgumentNullException.ThrowIfNull(httpClientFactory);
        ArgumentNullException.ThrowIfNull(secretStore);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(logger);
        _httpClientFactory = httpClientFactory;
        _secretStore = secretStore;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<ConnectionTestResult> TestAsync(PrinterDefinition printer, string? password, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(printer);
        if (printer.ConnectionType != ConnectionType.PrusaLink)
        {
            return new ConnectionTestResult(ConnectionTestOutcome.NotSupported);
        }

        if (!PrusaLinkAddress.TryCreateBaseUri(printer.Address, out _))
        {
            return new ConnectionTestResult(ConnectionTestOutcome.InvalidAddress);
        }

        ISecretStore secrets = password is null ? _secretStore : new EnteredPassword(SecretKeys.ForPrinter(printer.Id), password);
        using CancellationTokenSource timeout = new(DefaultTimeout, _timeProvider);
        using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        using HttpClient httpClient = _httpClientFactory.CreateClient(PrusaLinkClientFactory.HttpClientName);
        PrusaLinkClient client = new(httpClient, printer, secrets, _timeProvider, _logger);
        PrusaLinkInfoResult result;
        try
        {
            result = await client.GetInfoAsync(linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && timeout.IsCancellationRequested)
        {
            return new ConnectionTestResult(ConnectionTestOutcome.Timeout);
        }

        if (result.Info is { } info && result.Failure == PrusaLinkFailure.None)
        {
            return new ConnectionTestResult(ConnectionTestOutcome.Success, info.PrinterModel, info.Firmware, info.Hostname, info.ModelCode);
        }

        return new ConnectionTestResult(result.Failure switch
        {
            PrusaLinkFailure.InvalidAddress => ConnectionTestOutcome.InvalidAddress,
            PrusaLinkFailure.NotReachable => ConnectionTestOutcome.NotReachable,
            PrusaLinkFailure.Timeout => ConnectionTestOutcome.Timeout,
            PrusaLinkFailure.SecureConnectionFailed => ConnectionTestOutcome.SecureConnectionFailed,
            PrusaLinkFailure.PasswordRejected => ConnectionTestOutcome.PasswordRejected,
            PrusaLinkFailure.AccessDenied => ConnectionTestOutcome.AccessDenied,
            PrusaLinkFailure.PasswordMissing => ConnectionTestOutcome.PasswordMissing,
            PrusaLinkFailure.PasswordUnreadable => ConnectionTestOutcome.PasswordUnavailable,
            PrusaLinkFailure.UnsupportedAuthentication => ConnectionTestOutcome.UnsupportedAuthentication,
            _ => ConnectionTestOutcome.UnexpectedResponse,
        });
    }

    /// <summary>Serves the password typed into the form to the client; read-only, nothing is stored.</summary>
    private sealed class EnteredPassword(string key, string password) : ISecretStore
    {
        public Task<string?> GetAsync(string key1, CancellationToken cancellationToken) =>
            Task.FromResult(string.Equals(key1, key, StringComparison.OrdinalIgnoreCase) ? password : null);

        public Task SetAsync(string key1, string secret, CancellationToken cancellationToken) =>
            throw new NotSupportedException("Testing a connection never stores a password.");

        public Task<bool> DeleteAsync(string key1, CancellationToken cancellationToken) =>
            throw new NotSupportedException("Testing a connection never deletes a password.");
    }
}
