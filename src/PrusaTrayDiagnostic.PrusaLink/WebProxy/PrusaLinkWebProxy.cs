using System.Globalization;
using System.Net;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;

using PrusaTrayDiagnostic.Core.Printers;
using PrusaTrayDiagnostic.Core.Secrets;
using PrusaTrayDiagnostic.Core.Web;
using PrusaTrayDiagnostic.PrusaLink.Api;

namespace PrusaTrayDiagnostic.PrusaLink.WebProxy;

/// <summary>
/// Local reverse proxy of one PrusaLink printer (T-028): Kestrel on 127.0.0.1 with a random port, every request must
/// carry the secret <see cref="ProxyToken"/> as its first path segment and <c>Host: 127.0.0.1:&lt;port&gt;</c>. The proxy
/// answers the printer's Digest challenges itself, so the password never reaches the browser. Bodies are streamed in
/// both directions. Stops after <see cref="PrusaLinkWebProxyOptions.IdleTimeout"/> without a request.
/// Logs contain only the method, the path after the token, the status code and the printer id.
/// </summary>
internal sealed partial class PrusaLinkWebProxy : IDisposable
{
    private readonly PrinterDefinition _printer;
    private readonly Uri _printerBase;
    private readonly string _printerOrigin;
    private readonly Uri _versionUri;
    private readonly HttpClient _httpClient;
    private readonly DigestAuthenticator _authenticator;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger _logger;
    private readonly PrusaLinkWebProxyOptions _options;
    private readonly Action<PrusaLinkWebProxy> _onIdle;
    private readonly ProxyToken _token = ProxyToken.Create();
    private readonly CancellationTokenSource _stopping = new();
    private readonly TaskCompletionSource _stopped = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Lock _stopLock = new();

    private WebApplication? _app;
    private ITimer? _idleTimer;
    private string _expectedHost = string.Empty;
    private string _expectedOrigin = string.Empty;
    private ProxyLocation? _location;
    private Task? _stopTask;
    private int _activeRequests;
    private long _lastActivity;

    public PrusaLinkWebProxy(
        PrinterDefinition printer,
        Uri printerBase,
        HttpClient httpClient,
        ISecretStore secretStore,
        TimeProvider timeProvider,
        ILogger logger,
        PrusaLinkWebProxyOptions options,
        Action<PrusaLinkWebProxy> onIdle)
    {
        _printer = printer;
        _printerBase = printerBase;
        _printerOrigin = printerBase.GetLeftPart(UriPartial.Authority);
        _versionUri = new Uri(printerBase, "api/version");
        _httpClient = httpClient;
        // Separate from the monitoring client's authenticator; shares only the secret. No per-URI cache: file paths are unbounded.
        _authenticator = new DigestAuthenticator(secretStore, SecretKeys.ForPrinter(printer.Id), printer.UserName, timeProvider, cacheGetAuthorizations: false);
        _timeProvider = timeProvider;
        _logger = logger;
        _options = options;
        _onIdle = onIdle;
        _lastActivity = timeProvider.GetTimestamp();
    }

    public PrinterDefinition Printer => _printer;

    /// <summary>Address for the browser (contains the token: never log it). Valid after <see cref="StartAsync"/>.</summary>
    public Uri? Address { get; private set; }

    /// <summary>Listening port, valid after <see cref="StartAsync"/>.</summary>
    public int Port { get; private set; }

    public bool IsStopping => _stopping.IsCancellationRequested;

    /// <summary>Completes when the proxy has stopped and released its port.</summary>
    public Task Stopped => _stopped.Task;

    /// <summary>The token, for tests only.</summary>
    internal string TokenForTests => _token.Value;

    /// <summary>Whether this proxy was started for the same printer settings (otherwise it must be replaced).</summary>
    public bool Serves(PrinterDefinition printer) =>
        string.Equals(printer.Id, _printer.Id, StringComparison.OrdinalIgnoreCase)
        && printer.ConnectionType == _printer.ConnectionType
        && string.Equals(printer.Address, _printer.Address, StringComparison.Ordinal)
        && string.Equals(printer.UserName, _printer.UserName, StringComparison.Ordinal);

    /// <summary>Marks the proxy as used now (resets the idle timeout).</summary>
    public void Touch() => Interlocked.Exchange(ref _lastActivity, _timeProvider.GetTimestamp());

    /// <summary>Logged-in <c>GET /api/version</c> made before the browser is opened (decided 2026-10-18, T-028 Q3).</summary>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    public async Task<PrinterWebFallback> CheckAsync(CancellationToken cancellationToken)
    {
        Touch();
        using CancellationTokenSource timeout = new(_options.CheckTimeout, _timeProvider);
        using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token, _stopping.Token);
        try
        {
            (HttpResponseMessage? response, AuthOutcome outcome) = await SendAuthenticatedAsync(CreateVersionRequest, canRepeat: true, linked.Token).ConfigureAwait(false);
            if (response is null)
            {
                return ToFallback(outcome);
            }

            using (response)
            {
                return response.StatusCode switch
                {
                    HttpStatusCode.Unauthorized => PrinterWebFallback.PasswordRejected,
                    HttpStatusCode.Forbidden => PrinterWebFallback.AccessDenied,
                    _ when response.IsSuccessStatusCode => PrinterWebFallback.None,
                    _ => PrinterWebFallback.UnexpectedResponse,
                };
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when ((ex is OperationCanceledException or ObjectDisposedException) && _stopping.IsCancellationRequested)
        {
            // The proxy stopped (idle timeout, settings change) while it was checked; its HTTP client may be released.
            return PrinterWebFallback.ProxyUnavailable;
        }
        catch (OperationCanceledException)
        {
            // The check timeout.
            return PrinterWebFallback.NotReachable;
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException)
        {
            LogPrinterNotReachable(ex, _printer.Id);
            return PrinterWebFallback.NotReachable;
        }
        catch (SecretStoreException ex)
        {
            LogPasswordUnreadable(ex, _printer.Id);
            return PrinterWebFallback.PasswordUnreadable;
        }
    }

    /// <summary>Starts listening on 127.0.0.1 with a port chosen by the OS.</summary>
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        WebApplicationBuilder builder = WebApplication.CreateEmptyBuilder(new WebApplicationOptions());
        // The server's own logs would contain request URLs (with the token): it gets no logging provider at all.
        builder.Logging.ClearProviders();
        // The default console lifetime would register Ctrl+C / SIGTERM handlers for every proxy.
        builder.Services.AddSingleton<IHostLifetime, ManualHostLifetime>();
        IPAddress listenAddress = _options.ListenAddress;
        builder.WebHost.UseKestrelCore().ConfigureKestrel(options =>
        {
            options.AddServerHeader = false;
            // G-code uploads have hundreds of MB and the printer may accept them slowly (decided 2026-10-18).
            options.Limits.MaxRequestBodySize = null;
            options.Limits.MinRequestBodyDataRate = null;
            options.Limits.MinResponseDataRate = null;
            // Browsers open at most 6 connections per origin; anything beyond is not a browser tab.
            options.Limits.MaxConcurrentConnections = 64;
            options.Listen(listenAddress, 0, static listen => listen.Protocols = HttpProtocols.Http1);
        });

        WebApplication app = builder.Build();
        app.Run(HandleAsync);
        _app = app;
        await app.StartAsync(cancellationToken).ConfigureAwait(false);

        string bound = app.Services.GetRequiredService<IServer>().Features.GetRequiredFeature<IServerAddressesFeature>().Addresses.Single();
        Port = new Uri(bound).Port;
        _expectedHost = string.Create(CultureInfo.InvariantCulture, $"127.0.0.1:{Port}");
        _expectedOrigin = "http://" + _expectedHost;
        string prefix = "/" + _token.Value + "/";
        _location = new ProxyLocation(_printerBase, prefix);
        Address = new Uri("http://" + _expectedHost + prefix);
        Touch();
        _idleTimer = _timeProvider.CreateTimer(static state => ((PrusaLinkWebProxy)state!).OnIdleTimer(), this, _options.IdleTimeout, Timeout.InfiniteTimeSpan);
        LogStarted(_printer.Id);
    }

    /// <summary>Cancels requests in progress and releases the port. Idempotent, never throws.</summary>
    /// <param name="cancellationToken">Stops waiting for a graceful shutdown.</param>
    public Task StopAsync(CancellationToken cancellationToken)
    {
        lock (_stopLock)
        {
            return _stopTask ??= StopCoreAsync(cancellationToken);
        }
    }

    /// <summary>Synchronous stop for <see cref="IDisposable"/> owners (a disposed DI container). Idempotent, never throws.</summary>
    public void Dispose()
    {
        lock (_stopLock)
        {
            if (_stopTask is not null)
            {
                return;
            }

            _stopTask = _stopped.Task;
        }

        CancelAndStopTimer();
        try
        {
            (_app as IDisposable)?.Dispose();
        }
        catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException or InvalidOperationException or IOException)
        {
            LogStopFailed(ex, _printer.Id);
        }

        Finish();
    }

    private async Task StopCoreAsync(CancellationToken cancellationToken)
    {
        // Started under _stopLock: leave it before doing any work.
        await Task.Yield();
        try
        {
            CancelAndStopTimer();
            if (_app is { } app)
            {
                try
                {
                    // Requests in progress were cancelled above; after the timeout remaining connections are dropped.
                    using CancellationTokenSource timeout = new(_options.StopTimeout);
                    using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
                    await app.StopAsync(linked.Token).ConfigureAwait(false);
                }
                finally
                {
                    await app.DisposeAsync().ConfigureAwait(false);
                }
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException or InvalidOperationException or IOException or AggregateException)
        {
            LogStopFailed(ex, _printer.Id);
        }
        finally
        {
            Finish();
        }
    }

    private void CancelAndStopTimer()
    {
        _idleTimer?.Dispose();
        try
        {
            _stopping.Cancel();
        }
        catch (AggregateException ex)
        {
            LogStopFailed(ex, _printer.Id);
        }
    }

    private void Finish()
    {
        _httpClient.Dispose();
        if (_stopped.TrySetResult() && _app is not null)
        {
            LogStopped(_printer.Id);
        }
    }

    private void OnIdleTimer()
    {
        if (IsStopping)
        {
            return;
        }

        TimeSpan idle = _timeProvider.GetElapsedTime(Interlocked.Read(ref _lastActivity));
        if (Volatile.Read(ref _activeRequests) == 0 && idle >= _options.IdleTimeout)
        {
            LogIdle(_printer.Id);
            _onIdle(this);
            return;
        }

        // A request is running (e.g. a long upload) or one ended recently: check again when the remaining time is over.
        TimeSpan next = Volatile.Read(ref _activeRequests) == 0 ? _options.IdleTimeout - idle : _options.IdleTimeout;
        try
        {
            _idleTimer?.Change(next > TimeSpan.Zero ? next : _options.IdleTimeout, Timeout.InfiniteTimeSpan);
        }
        catch (ObjectDisposedException)
        {
            // Stopped meanwhile.
        }
    }

    private async Task HandleAsync(HttpContext context)
    {
        HttpResponse response = context.Response;
        response.Headers[ProxyHeaders.ReferrerPolicy] = "no-referrer";
        response.Headers[ProxyHeaders.ContentTypeOptions] = "nosniff";
        // Other pages (also other ports on 127.0.0.1) cannot embed or read anything of the proxy as no-cors content.
        response.Headers[ProxyHeaders.ResourcePolicy] = "same-origin";

        // DNS rebinding: only the exact loopback authority of this listener is accepted.
        StringValues host = context.Request.Headers.Host;
        if (host.Count != 1 || !string.Equals(host[0], _expectedHost, StringComparison.Ordinal))
        {
            response.StatusCode = StatusCodes.Status421MisdirectedRequest;
            return;
        }

        string rawTarget = context.Features.GetRequiredFeature<IHttpRequestFeature>().RawTarget;
        if (!TrySplitTarget(rawTarget, out string? rest))
        {
            // Same answer for a missing token, a wrong token and anything else unknown.
            response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        // Only after the token check, so that a rejection never tells whether a token is valid. Not an activity: the
        // request is not forwarded and does not postpone the idle stop.
        if (ForeignWriteReason(context.Request) is { } reason)
        {
            LogForeignWriteRejected(context.Request.Method, reason, _printer.Id);
            await WriteErrorAsync(context, StatusCodes.Status403Forbidden, ProxyErrors.ForeignPage).ConfigureAwait(false);
            return;
        }

        Interlocked.Increment(ref _activeRequests);
        Touch();
        try
        {
            await HandleProxiedAsync(context, rest).ConfigureAwait(false);
        }
        finally
        {
            Touch();
            Interlocked.Decrement(ref _activeRequests);
        }
    }

    /// <summary>
    /// Why a request that may change something on the printer comes from another web page (security finding L1), or
    /// null. A page that learned the token could otherwise send blind <c>POST</c>/<c>PUT</c>/<c>DELETE</c> requests the
    /// proxy would log in. Browsers mark every such request with <c>Sec-Fetch-Site</c> and <c>Origin</c>; another port on
    /// 127.0.0.1 is <c>same-site</c>, not <c>same-origin</c>. A request with neither header is not from a browser page.
    /// The PrusaLink web uses fetch/XMLHttpRequest, which send its exact origin; an HTML form under
    /// <c>Referrer-Policy: no-referrer</c> would send <c>Origin: null</c> and be refused.
    /// </summary>
    private string? ForeignWriteReason(HttpRequest request)
    {
        string method = request.Method;
        if (HttpMethods.IsGet(method) || HttpMethods.IsHead(method) || HttpMethods.IsOptions(method))
        {
            return null;
        }

        IHeaderDictionary headers = request.Headers;
        if (headers.TryGetValue(ProxyHeaders.SecFetchSite, out StringValues site)
            && (site.Count != 1 || site[0] is not ("same-origin" or "none")))
        {
            return ProxyHeaders.SecFetchSite;
        }

        if (headers.TryGetValue(ProxyHeaders.Origin, out StringValues origin)
            && (origin.Count != 1 || !string.Equals(origin[0], _expectedOrigin, StringComparison.Ordinal)))
        {
            return ProxyHeaders.Origin;
        }

        return null;
    }

    /// <summary>Splits <c>/&lt;token&gt;&lt;rest&gt;</c>; false when the token is missing or wrong.</summary>
    private bool TrySplitTarget(string rawTarget, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out string? rest)
    {
        rest = null;
        if (!rawTarget.StartsWith('/'))
        {
            // Absolute-form, authority-form (CONNECT) or asterisk-form targets.
            return false;
        }

        ReadOnlySpan<char> afterSlash = rawTarget.AsSpan(1);
        int end = afterSlash.IndexOfAny('/', '?');
        ReadOnlySpan<char> segment = end < 0 ? afterSlash : afterSlash[..end];
        if (!_token.Matches(segment))
        {
            return false;
        }

        rest = end < 0 ? string.Empty : afterSlash[end..].ToString();
        return true;
    }

    private async Task HandleProxiedAsync(HttpContext context, string rest)
    {
        HttpRequest request = context.Request;
        HttpResponse response = context.Response;
        if (rest.Length == 0 || rest[0] == '?')
        {
            // The web derives its API root from the page path; it needs the trailing slash.
            response.StatusCode = StatusCodes.Status302Found;
            response.Headers.Location = "/" + _token.Value + "/" + rest;
            return;
        }

        string logPath = PathForLog(rest);
        if (HttpMethods.IsConnect(request.Method) || HttpMethods.IsTrace(request.Method))
        {
            response.StatusCode = StatusCodes.Status405MethodNotAllowed;
            LogRequest(request.Method, logPath, response.StatusCode, _printer.Id);
            return;
        }

        if (context.Features.Get<IHttpUpgradeFeature>() is { IsUpgradableRequest: true })
        {
            // The PrusaLink web uses neither WebSockets nor other upgrades.
            response.StatusCode = StatusCodes.Status501NotImplemented;
            LogRequest(request.Method, logPath, response.StatusCode, _printer.Id);
            return;
        }

        if (!TryCreatePrinterUri(rest, out Uri? target))
        {
            response.StatusCode = StatusCodes.Status404NotFound;
            LogRequest(request.Method, logPath, response.StatusCode, _printer.Id);
            return;
        }

        using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted, _stopping.Token);
        CancellationToken cancellationToken = linked.Token;
        try
        {
            await ForwardAsync(context, target, cancellationToken).ConfigureAwait(false);
            LogRequest(request.Method, logPath, response.StatusCode, _printer.Id);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            // The browser went away; the request to the printer was cancelled with it.
        }
        catch (Exception ex) when ((ex is OperationCanceledException or ObjectDisposedException) && _stopping.IsCancellationRequested)
        {
            // Stopping: the request was cancelled, or it outlived StopTimeout and the HTTP client was released under it.
            // Expected, so no error is logged.
            LogAbortedByStop(request.Method, logPath, _printer.Id);
            context.Abort();
        }
        catch (SecretStoreException ex)
        {
            LogPasswordUnreadable(ex, _printer.Id);
            await WriteErrorAsync(context, StatusCodes.Status403Forbidden, ProxyErrors.PasswordUnreadable).ConfigureAwait(false);
        }
        catch (OperationCanceledException ex)
        {
            // Connect timeout of the printer client.
            LogPrinterNotReachable(ex, _printer.Id);
            await WriteErrorAsync(context, StatusCodes.Status504GatewayTimeout, ProxyErrors.Timeout).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException)
        {
            // Printer unreachable or connection dropped (also a browser that stopped sending an upload).
            LogPrinterNotReachable(ex, _printer.Id);
            await WriteErrorAsync(context, StatusCodes.Status502BadGateway, ProxyErrors.NotReachable).ConfigureAwait(false);
        }
#pragma warning disable CA1031 // Last resort of the request: anything else becomes a 500 and a log entry, never a crash.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            LogUnexpected(ex, _printer.Id);
            if (response.HasStarted)
            {
                context.Abort();
            }
            else
            {
                response.StatusCode = StatusCodes.Status500InternalServerError;
            }
        }
    }

    private async Task ForwardAsync(HttpContext context, Uri target, CancellationToken cancellationToken)
    {
        HttpRequest request = context.Request;
        HttpMethod method = HttpMethod.Parse(request.Method);
        bool hasBody = context.Features.Get<IHttpRequestBodyDetectionFeature>()?.CanHaveBody ?? false;
        long? length = request.ContentLength;

        byte[]? bufferedBody = null;
        bool streamed = false;
        if (hasBody)
        {
            if (length is { } known && known <= _options.MaxBufferedBodySize)
            {
                bufferedBody = await ReadBodyAsync(request.Body, (int)known, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                streamed = true;
            }
        }

        if (streamed && (_authenticator.GetNonceAge() is not { } age || age >= _options.NonceRefreshAge))
        {
            // The streamed body cannot be sent twice: renew the nonce first, so that it is not rejected as stale.
            await RefreshNonceAsync(cancellationToken).ConfigureAwait(false);
        }

        HttpRequestMessage CreateRequest()
        {
            HttpRequestMessage message = new(method, target);
            if (bufferedBody is not null)
            {
                message.Content = new ByteArrayContent(bufferedBody);
            }
            else if (streamed)
            {
                message.Content = new RequestBodyContent(request.Body, length);
            }

            ProxyHeaders.CopyRequestHeaders(request.Headers, message);
            return message;
        }

        (HttpResponseMessage? printerResponse, AuthOutcome outcome) = await SendAuthenticatedAsync(CreateRequest, canRepeat: !streamed, cancellationToken).ConfigureAwait(false);
        if (printerResponse is null)
        {
            await WriteErrorAsync(context, StatusCodes.Status403Forbidden, ErrorBody(outcome)).ConfigureAwait(false);
            return;
        }

        using (printerResponse)
        {
            if (printerResponse.StatusCode == HttpStatusCode.Unauthorized)
            {
                // The password was re-read and still rejected. Never a 401 for the browser.
                await WriteErrorAsync(context, StatusCodes.Status403Forbidden, ProxyErrors.PasswordRejected).ConfigureAwait(false);
                return;
            }

            await CopyResponseAsync(context, printerResponse, method, target, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task CopyResponseAsync(HttpContext context, HttpResponseMessage printerResponse, HttpMethod method, Uri target, CancellationToken cancellationToken)
    {
        HttpResponse response = context.Response;
        int status = (int)printerResponse.StatusCode;
        response.StatusCode = status;
        ProxyHeaders.CopyResponseHeaders(printerResponse, response, _location!);
        if (target.AbsolutePath.AsSpan(_printerBase.AbsolutePath.Length).StartsWith("api/", StringComparison.Ordinal))
        {
            response.Headers.CacheControl = "no-store";
        }

        bool bodyless = status < 200 || status is StatusCodes.Status204NoContent or StatusCodes.Status304NotModified;
        if (!bodyless && printerResponse.Content.Headers.ContentLength is { } contentLength)
        {
            response.ContentLength = contentLength;
        }

        if (bodyless || method == HttpMethod.Head)
        {
            return;
        }

        Stream body = await printerResponse.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using (body.ConfigureAwait(false))
        {
            await body.CopyToAsync(response.Body, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Sends with the current Digest credentials and answers 401 challenges like the monitoring client: at most twice,
    /// the second time only after a stale nonce. A request that cannot be repeated (streamed body) is sent once; its 401
    /// still renews the nonce for the next request. Response null: the challenge cannot be answered (see outcome).
    /// </summary>
    private async Task<(HttpResponseMessage? Response, AuthOutcome Outcome)> SendAuthenticatedAsync(
        Func<HttpRequestMessage> createRequest,
        bool canRepeat,
        CancellationToken cancellationToken)
    {
        HttpResponseMessage response = await SendOnceAsync(createRequest(), cancellationToken).ConfigureAwait(false);
        for (int round = 0; round < 2 && response.StatusCode == HttpStatusCode.Unauthorized; round++)
        {
            ChallengeResult challenge;
            try
            {
                challenge = await _authenticator.AcceptChallengeAsync(response.Headers.WwwAuthenticate, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                response.Dispose();
            }

            if (challenge.Error is not null)
            {
                return (null, challenge.Reason switch
                {
                    PrusaLinkFailure.PasswordMissing => AuthOutcome.PasswordMissing,
                    _ => AuthOutcome.UnsupportedAuthentication,
                });
            }

            if (!canRepeat)
            {
                return (null, challenge.PasswordReloaded ? AuthOutcome.PasswordRejected : AuthOutcome.LoginExpired);
            }

            response = await SendOnceAsync(createRequest(), cancellationToken).ConfigureAwait(false);
            if (challenge.PasswordReloaded)
            {
                break;
            }
        }

        return (response, AuthOutcome.None);
    }

    private async Task<HttpResponseMessage> SendOnceAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        using (request)
        {
            if (_authenticator.GetAuthorization(request.Method, request.RequestUri!.PathAndQuery) is { } authorization)
            {
                request.Headers.TryAddWithoutValidation("Authorization", authorization);
            }

            return await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task RefreshNonceAsync(CancellationToken cancellationToken)
    {
        (HttpResponseMessage? response, _) = await SendAuthenticatedAsync(CreateVersionRequest, canRepeat: true, cancellationToken).ConfigureAwait(false);
        // Whatever the result, the upload itself reports the problem.
        response?.Dispose();
    }

    private HttpRequestMessage CreateVersionRequest()
    {
        HttpRequestMessage request = new(HttpMethod.Get, _versionUri);
        // The firmware answers errors as JSON only when application/json is the first accepted type.
        request.Headers.TryAddWithoutValidation("Accept", "application/json");
        return request;
    }

    /// <summary>
    /// The printer address for <c>/&lt;rest&gt;</c>: the raw path and query are appended unchanged, without the
    /// canonicalization of <see cref="Uri"/> (it would e.g. drop a trailing <c>%20</c> or unescape characters, and the
    /// firmware hashes the exact request target). Browsers resolve dot segments before sending; a request that still has
    /// them (or backslashes) could climb above the printer's base path and is refused.
    /// </summary>
    private bool TryCreatePrinterUri(string rest, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out Uri? target)
    {
        target = null;
        int query = rest.IndexOf('?', StringComparison.Ordinal);
        ReadOnlySpan<char> path = query < 0 ? rest : rest.AsSpan(0, query);
        if (path.ContainsAny('\\', '#') || HasDotSegment(path))
        {
            return false;
        }

        UriCreationOptions raw = new() { DangerousDisablePathAndQueryCanonicalization = true };
        if (Uri.TryCreate(string.Concat(_printerOrigin, _printerBase.AbsolutePath, rest.AsSpan(1)), in raw, out Uri? uri)
            && string.Equals(uri.GetLeftPart(UriPartial.Authority), _printerOrigin, StringComparison.OrdinalIgnoreCase))
        {
            target = uri;
            return true;
        }

        return false;
    }

    /// <summary>A <c>.</c> or <c>..</c> segment, also percent-encoded (<c>%2e</c>).</summary>
    private static bool HasDotSegment(ReadOnlySpan<char> path)
    {
        foreach (Range range in path.Split('/'))
        {
            ReadOnlySpan<char> segment = path[range];
            int dots = 0;
            bool onlyDots = true;
            for (int i = 0; i < segment.Length && onlyDots; i++)
            {
                if (segment[i] == '.')
                {
                    dots++;
                }
                else if (segment[i] == '%' && segment[(i + 1)..].StartsWith("2e", StringComparison.OrdinalIgnoreCase))
                {
                    dots++;
                    i += 2;
                }
                else
                {
                    onlyDots = false;
                }
            }

            if (onlyDots && dots is 1 or 2)
            {
                return true;
            }
        }

        return false;
    }

    private static async Task<byte[]> ReadBodyAsync(Stream body, int length, CancellationToken cancellationToken)
    {
        byte[] buffer = new byte[length];
        int read = 0;
        while (read < length)
        {
            int count = await body.ReadAsync(buffer.AsMemory(read), cancellationToken).ConfigureAwait(false);
            if (count == 0)
            {
                throw new IOException("The request body ended before its Content-Length.");
            }

            read += count;
        }

        return buffer;
    }

    private static async Task WriteErrorAsync(HttpContext context, int statusCode, byte[] body)
    {
        HttpResponse response = context.Response;
        if (response.HasStarted)
        {
            context.Abort();
            return;
        }

        response.StatusCode = statusCode;
        response.ContentType = ProxyErrors.JsonContentType;
        response.ContentLength = body.Length;
        response.Headers.CacheControl = "no-store";
        try
        {
            await response.Body.WriteAsync(body, context.RequestAborted).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is OperationCanceledException or IOException)
        {
            // The browser is gone.
        }
    }

    private static byte[] ErrorBody(AuthOutcome outcome) => outcome switch
    {
        AuthOutcome.PasswordMissing => ProxyErrors.PasswordMissing,
        AuthOutcome.UnsupportedAuthentication => ProxyErrors.UnsupportedAuthentication,
        AuthOutcome.LoginExpired => ProxyErrors.LoginExpired,
        _ => ProxyErrors.PasswordRejected,
    };

    private static PrinterWebFallback ToFallback(AuthOutcome outcome) => outcome switch
    {
        AuthOutcome.PasswordMissing => PrinterWebFallback.PasswordMissing,
        AuthOutcome.UnsupportedAuthentication => PrinterWebFallback.UnsupportedAuthentication,
        _ => PrinterWebFallback.PasswordRejected,
    };

    /// <summary>The path after the token, without the query (which may carry arbitrary values).</summary>
    private static string PathForLog(string rest)
    {
        int query = rest.IndexOf('?', StringComparison.Ordinal);
        return query < 0 ? rest : rest[..query];
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "The web proxy of printer '{PrinterId}' started.")]
    private partial void LogStarted(string printerId);

    [LoggerMessage(Level = LogLevel.Information, Message = "The web proxy of printer '{PrinterId}' stopped.")]
    private partial void LogStopped(string printerId);

    [LoggerMessage(Level = LogLevel.Information, Message = "The web proxy of printer '{PrinterId}' is stopping after a period without requests.")]
    private partial void LogIdle(string printerId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Stopping the web proxy of printer '{PrinterId}' failed.")]
    private partial void LogStopFailed(Exception exception, string printerId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Web proxy: {Method} {Path} answered {StatusCode} (printer '{PrinterId}').")]
    private partial void LogRequest(string method, string path, int statusCode, string printerId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Web proxy: a {Method} request from another web page was rejected (header {Header}) (printer '{PrinterId}').")]
    private partial void LogForeignWriteRejected(string method, string header, string printerId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Web proxy: {Method} {Path} was aborted because the proxy stopped (printer '{PrinterId}').")]
    private partial void LogAbortedByStop(string method, string path, string printerId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Web proxy: printer '{PrinterId}' is not reachable.")]
    private partial void LogPrinterNotReachable(Exception exception, string printerId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Web proxy: a request to printer '{PrinterId}' failed unexpectedly.")]
    private partial void LogUnexpected(Exception exception, string printerId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Web proxy: the password of printer '{PrinterId}' could not be read from the secret store.")]
    private partial void LogPasswordUnreadable(Exception exception, string printerId);

    private enum AuthOutcome
    {
        None,
        PasswordMissing,
        PasswordRejected,
        UnsupportedAuthentication,
        LoginExpired,
    }

    /// <summary>The proxy's host is started and stopped explicitly; it reacts to no console signal.</summary>
    private sealed class ManualHostLifetime : IHostLifetime
    {
        public Task WaitForStartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
