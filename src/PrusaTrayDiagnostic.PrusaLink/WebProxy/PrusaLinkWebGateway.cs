using Microsoft.Extensions.Logging;

using PrusaTrayDiagnostic.Core.Printers;
using PrusaTrayDiagnostic.Core.Secrets;
using PrusaTrayDiagnostic.Core.Web;

namespace PrusaTrayDiagnostic.PrusaLink.WebProxy;

/// <summary>
/// <see cref="IPrinterWebGateway"/> for PrusaLink printers (T-028): one local reverse proxy per printer, with its own
/// random port and token, started lazily on the first <see cref="OpenAsync"/> and reused afterwards (decided 2026-10-18).
/// Before an address is returned, a logged-in request checks the password; when it fails the plain printer address is
/// returned. Prusa Connect printers are not handled (<see cref="PrinterWebFallback.NotSupported"/>).
/// </summary>
public sealed partial class PrusaLinkWebGateway : IPrinterWebGateway, IDisposable
{
    /// <summary>Name of the <see cref="HttpClient"/> configuration the proxies use towards the printers.</summary>
    public const string HttpClientName = "PrusaLink.WebProxy";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ISecretStore _secretStore;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<PrusaLinkWebGateway> _logger;
    private readonly ILoggerFactory _loggerFactory;
    private readonly PrusaLinkWebProxyOptions _options;
    private readonly Dictionary<string, PrusaLinkWebProxy> _proxies = new(StringComparer.OrdinalIgnoreCase);
    private readonly Lock _gate = new();
    // Serializes opening, so that two clicks never start two proxies for one printer. Never disposed: a racing
    // OpenAsync may still wait on it when the gateway is disposed.
    private readonly SemaphoreSlim _openLock = new(1, 1);
    private bool _disposed;
    // The printer an OpenAsync (serialized by _openLock) is opening, and whether StopAsync/StopAllAsync asked for its
    // proxy meanwhile: a proxy created during the open is not in _proxies yet, so the stop must not miss it.
    private string? _openingId;
    private bool _openingStopRequested;

    public PrusaLinkWebGateway(IHttpClientFactory httpClientFactory, ISecretStore secretStore, TimeProvider timeProvider, ILoggerFactory loggerFactory)
        : this(httpClientFactory, secretStore, timeProvider, loggerFactory, PrusaLinkWebProxyOptions.Default)
    {
    }

    internal PrusaLinkWebGateway(
        IHttpClientFactory httpClientFactory,
        ISecretStore secretStore,
        TimeProvider timeProvider,
        ILoggerFactory loggerFactory,
        PrusaLinkWebProxyOptions options)
    {
        ArgumentNullException.ThrowIfNull(httpClientFactory);
        ArgumentNullException.ThrowIfNull(secretStore);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(loggerFactory);
        ArgumentNullException.ThrowIfNull(options);
        _httpClientFactory = httpClientFactory;
        _secretStore = secretStore;
        _timeProvider = timeProvider;
        _logger = loggerFactory.CreateLogger<PrusaLinkWebGateway>();
        // The proxies log under their own category (every request at Debug), so it can be filtered separately.
        _loggerFactory = loggerFactory;
        _options = options;
    }

    public async Task<PrinterWebAccess> OpenAsync(PrinterDefinition printer, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(printer);
        ThrowIfDisposed();
        if (printer.ConnectionType != ConnectionType.PrusaLink)
        {
            return PrinterWebAccess.Plain(null, PrinterWebFallback.NotSupported);
        }

        if (!PrusaLinkAddress.TryCreateBaseUri(printer.Address, out Uri? printerBase))
        {
            return PrinterWebAccess.Plain(null, PrinterWebFallback.InvalidAddress);
        }

        await _openLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            PrusaLinkWebProxy? stale = null;
            PrusaLinkWebProxy? proxy;
            lock (_gate)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                _openingId = printer.Id;
                _openingStopRequested = false;
                if (_proxies.TryGetValue(printer.Id, out proxy) && (!proxy.Serves(printer) || proxy.IsStopping))
                {
                    // Settings changed without StopAsync: the old address must not keep working.
                    _proxies.Remove(printer.Id);
                    stale = proxy;
                    proxy = null;
                }
            }

            if (stale is not null)
            {
                await stale.StopAsync(CancellationToken.None).ConfigureAwait(false);
            }

            bool created = proxy is null;
            proxy ??= CreateProxy(printer, printerBase);

            // The secret store (CredRead, Keychain) may block: never on the caller's (UI) thread.
            PrusaLinkWebProxy checkedProxy = proxy;
            PrinterWebFallback check;
            try
            {
                check = await Task.Run(() => checkedProxy.CheckAsync(cancellationToken), cancellationToken).ConfigureAwait(false);
            }
            catch (Exception) when (created)
            {
                // Cancelled (or failed unexpectedly): a proxy nobody will ever use releases its HTTP client.
                await proxy.StopAsync(CancellationToken.None).ConfigureAwait(false);
                throw;
            }

            if (check != PrinterWebFallback.None)
            {
                LogFallback(printer.Id, check);
                if (created)
                {
                    await proxy.StopAsync(CancellationToken.None).ConfigureAwait(false);
                }

                return PrinterWebAccess.Plain(printerBase, check);
            }

            if (created && !await TryStartAsync(proxy, cancellationToken).ConfigureAwait(false))
            {
                return PrinterWebAccess.Plain(printerBase, PrinterWebFallback.ProxyUnavailable);
            }

            lock (_gate)
            {
                if (!_disposed && !proxy.IsStopping && !_openingStopRequested)
                {
                    _proxies[printer.Id] = proxy;
                    return PrinterWebAccess.Proxied(proxy.Address!);
                }
            }

            // Disposed, stopped by the idle timeout during the check, or the printer's settings changed meanwhile
            // (StopAsync): the proxy may have been started with the old login.
            await proxy.StopAsync(CancellationToken.None).ConfigureAwait(false);
            ThrowIfDisposed();
            return PrinterWebAccess.Plain(printerBase, PrinterWebFallback.ProxyUnavailable);
        }
        finally
        {
            lock (_gate)
            {
                _openingId = null;
                _openingStopRequested = false;
            }

            _openLock.Release();
        }
    }

    public Task StopAsync(string printerId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(printerId);
        PrusaLinkWebProxy? proxy;
        lock (_gate)
        {
            _proxies.Remove(printerId, out proxy);
            if (string.Equals(_openingId, printerId, StringComparison.OrdinalIgnoreCase))
            {
                _openingStopRequested = true;
            }
        }

        return proxy?.StopAsync(cancellationToken) ?? Task.CompletedTask;
    }

    public Task StopAllAsync(CancellationToken cancellationToken) => Task.WhenAll(TakeAll().Select(p => p.StopAsync(cancellationToken)));

    public async ValueTask DisposeAsync()
    {
        lock (_gate)
        {
            _disposed = true;
        }

        await StopAllAsync(CancellationToken.None).ConfigureAwait(false);
    }

    /// <summary>Synchronous variant for a container disposed with <see cref="IDisposable.Dispose"/>.</summary>
    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
        }

        foreach (PrusaLinkWebProxy proxy in TakeAll())
        {
            proxy.Dispose();
        }
    }

    /// <summary>The running proxy of a printer, for tests.</summary>
    internal PrusaLinkWebProxy? GetProxyForTests(string printerId)
    {
        lock (_gate)
        {
            return _proxies.GetValueOrDefault(printerId);
        }
    }

    private PrusaLinkWebProxy CreateProxy(PrinterDefinition printer, Uri printerBase) =>
        new(printer, printerBase, _httpClientFactory.CreateClient(HttpClientName), _secretStore, _timeProvider, _loggerFactory.CreateLogger<PrusaLinkWebProxy>(), _options, OnIdle);

    private async Task<bool> TryStartAsync(PrusaLinkWebProxy proxy, CancellationToken cancellationToken)
    {
        try
        {
            await proxy.StartAsync(cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await proxy.StopAsync(CancellationToken.None).ConfigureAwait(false);
            throw;
        }
        catch (Exception ex)
        {
            // E.g. no free port or a socket permission problem: the app goes on with the plain address.
            LogStartFailed(ex, proxy.Printer.Id);
            await proxy.StopAsync(CancellationToken.None).ConfigureAwait(false);
            return false;
        }
    }

    private void OnIdle(PrusaLinkWebProxy proxy)
    {
        lock (_gate)
        {
            if (_proxies.TryGetValue(proxy.Printer.Id, out PrusaLinkWebProxy? current) && ReferenceEquals(current, proxy))
            {
                _proxies.Remove(proxy.Printer.Id);
            }
        }

        // StopAsync never throws; nothing waits for it here.
        _ = proxy.StopAsync(CancellationToken.None);
    }

    private List<PrusaLinkWebProxy> TakeAll()
    {
        lock (_gate)
        {
            List<PrusaLinkWebProxy> all = [.. _proxies.Values];
            _proxies.Clear();
            _openingStopRequested |= _openingId is not null;
            return all;
        }
    }

    private void ThrowIfDisposed()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "The web proxy of printer '{PrinterId}' could not be started; opening the plain printer address.")]
    private partial void LogStartFailed(Exception exception, string printerId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Printer '{PrinterId}' cannot be opened logged in ({Reason}); opening the plain printer address.")]
    private partial void LogFallback(string printerId, PrinterWebFallback reason);
}
