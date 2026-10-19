using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

using Microsoft.Extensions.Logging;

using PrusaTrayDiagnostic.Core.Printers;
using PrusaTrayDiagnostic.Core.Secrets;
using PrusaTrayDiagnostic.PrusaLink.Api;

namespace PrusaTrayDiagnostic.PrusaLink;

/// <summary>
/// <see cref="IPrinterClient"/> for the PrusaLink API built into Buddy firmware 6.x (Core One, MK4/S, MK3.9, XL, MINI).
/// A poll costs one request (<c>/api/v1/status</c>); job details are fetched only when the job changes.
/// Calls on one instance must not overlap (see <see cref="IPrinterClient"/>); <see cref="GetInfoAsync"/> may run concurrently.
/// </summary>
public sealed partial class PrusaLinkClient : IPrinterClient
{
    private readonly HttpClient _httpClient;
    private readonly PrinterDefinition _printer;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<PrusaLinkClient> _logger;
    private readonly DigestAuthenticator _authenticator;
    private readonly ApiUris? _uris;

    // Poll state, touched only by GetSnapshotAsync.
    private JobFileName? _jobFileName;
    private PrintJob? _lastJob;

    public PrusaLinkClient(HttpClient httpClient, PrinterDefinition printer, ISecretStore secretStore, TimeProvider timeProvider, ILogger<PrusaLinkClient> logger)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(printer);
        ArgumentNullException.ThrowIfNull(secretStore);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(logger);
        _httpClient = httpClient;
        _printer = printer;
        _timeProvider = timeProvider;
        _logger = logger;
        _authenticator = new DigestAuthenticator(secretStore, SecretKeys.ForPrinter(printer.Id), printer.UserName, timeProvider);
        _uris = PrusaLinkAddress.TryCreateBaseUri(printer.Address, out Uri? baseUri) ? new ApiUris(baseUri) : null;
    }

    public async Task<PrinterSnapshot> GetSnapshotAsync(CancellationToken cancellationToken)
    {
        if (_uris is null)
        {
            return Unavailable(PrinterState.Unknown, Messages.InvalidAddress);
        }

        try
        {
            ApiResult<StatusResponse> status = await GetAsync(_uris.Status, PrusaLinkJsonContext.Default.StatusResponse, cancellationToken).ConfigureAwait(false);
            if (status.Failure is { } failure)
            {
                return Unavailable(failure.State, failure.Message);
            }

            if (status.Value?.Printer is not { } printer)
            {
                return Unavailable(PrinterState.Unknown, Messages.UnexpectedResponse);
            }

            PrinterState state = PrusaLinkStates.Parse(printer.State);
            PrintJob? job = await GetJobAsync(state, status.Value.Job, cancellationToken).ConfigureAwait(false);
            return new PrinterSnapshot
            {
                PrinterId = _printer.Id,
                State = state,
                Timestamp = _timeProvider.GetUtcNow(),
                Job = job,
                Telemetry = CreateTelemetry(printer),
            };
        }
        catch (Exception ex) when (ToFailure(ex, cancellationToken) is { } failure)
        {
            return Unavailable(failure.State, failure.Message);
        }
    }

    /// <summary>Reads version and printer information, e.g. to test the connection from the settings window.</summary>
    public async Task<PrusaLinkInfoResult> GetInfoAsync(CancellationToken cancellationToken)
    {
        if (_uris is null)
        {
            return new PrusaLinkInfoResult(PrinterState.Unknown, Messages.InvalidAddress, null) { Failure = PrusaLinkFailure.InvalidAddress };
        }

        try
        {
            ApiResult<VersionResponse> version = await GetAsync(_uris.Version, PrusaLinkJsonContext.Default.VersionResponse, cancellationToken).ConfigureAwait(false);
            if (version.Failure is { } failure)
            {
                return InfoFailure(failure);
            }

            if (version.Value is not { } versionValue)
            {
                return InfoFailure(new Failure(PrinterState.Unknown, Messages.UnexpectedResponse, PrusaLinkFailure.UnexpectedResponse));
            }

            // /api/v1/info is optional for the result: version alone proves the connection works.
            ApiResult<InfoResponse> info = await GetAsync(_uris.Info, PrusaLinkJsonContext.Default.InfoResponse, cancellationToken).ConfigureAwait(false);
            InfoResponse? infoValue = info.Value;
            return new PrusaLinkInfoResult(
                PrinterState.Idle,
                null,
                new PrusaLinkPrinterInfo(
                    Hostname: versionValue.Hostname ?? infoValue?.Hostname,
                    Firmware: versionValue.Firmware,
                    ApiVersion: versionValue.Api,
                    PrinterModel: versionValue.Printer,
                    NozzleDiameter: infoValue?.NozzleDiameter ?? versionValue.NozzleDiameter,
                    HasMmu: infoValue?.Mmu ?? false));
        }
        catch (Exception ex) when (ToFailure(ex, cancellationToken) is { } failure)
        {
            return InfoFailure(failure);
        }
    }

    /// <summary>
    /// The job object exists only while printing or paused. Finished and stopped prints report the last known job,
    /// so that observers can tell which print ended.
    /// </summary>
    private async Task<PrintJob?> GetJobAsync(PrinterState state, StatusJob? statusJob, CancellationToken cancellationToken)
    {
        if (statusJob is not null)
        {
            string? fileName = await GetFileNameAsync(statusJob.Id, cancellationToken).ConfigureAwait(false);
            return _lastJob = new PrintJob
            {
                Id = statusJob.Id,
                FileName = fileName,
                ProgressPercent = statusJob.Progress ?? 0,
                Elapsed = Seconds(statusJob.TimePrinting),
                Remaining = Seconds(statusJob.TimeRemaining),
            };
        }

        switch (state)
        {
            // The adjusted copy replaces _lastJob, so that the hours a printer may stay finished cost no allocations.
            case PrinterState.Finished:
                return _lastJob = _lastJob is null or { ProgressPercent: 100, Remaining.Ticks: 0 }
                    ? _lastJob
                    : _lastJob with { ProgressPercent = 100, Remaining = TimeSpan.Zero };
            case PrinterState.Stopped:
                // A cancelled print has no remaining time (and therefore no estimated completion).
                return _lastJob = _lastJob is null or { Remaining: null } ? _lastJob : _lastJob with { Remaining = null };
            default:
                _lastJob = null;
                _jobFileName = null;
                return null;
        }
    }

    /// <summary>File name of the job; requested once per job id and cached. Failures leave it unknown and are retried next poll.</summary>
    private async Task<string?> GetFileNameAsync(long? jobId, CancellationToken cancellationToken)
    {
        if (jobId is null)
        {
            return null;
        }

        if (_jobFileName is { } cached && cached.JobId == jobId)
        {
            return cached.FileName;
        }

        try
        {
            ApiResult<JobResponse> job = await GetAsync(_uris!.Job, PrusaLinkJsonContext.Default.JobResponse, cancellationToken).ConfigureAwait(false);
            if (job.Failure is null && job.Value is { } value && (value.Id is null || value.Id == jobId))
            {
                string? fileName = value.File?.DisplayName ?? value.File?.Name;
                _jobFileName = new JobFileName(jobId.Value, fileName);
                return fileName;
            }
        }
        catch (Exception ex) when (ToFailure(ex, cancellationToken) is not null)
        {
            // The status itself was read; a missing file name must not turn the whole poll into a failure.
        }

        return null;
    }

    private async Task<ApiResult<T>> GetAsync<T>(Uri uri, JsonTypeInfo<T> typeInfo, CancellationToken cancellationToken)
        where T : class
    {
        (HttpResponseMessage? response, AuthenticationError authenticationError) = await SendAuthenticatedAsync(uri, cancellationToken).ConfigureAwait(false);
        if (response is null)
        {
            return ApiResult<T>.Fail(PrinterState.Unauthorized, authenticationError.Message, authenticationError.Reason);
        }

        using (response)
        {
            switch (response.StatusCode)
            {
                case HttpStatusCode.OK:
                    // Content is already buffered (bodies are below 1 kB); this is a read-only view over that buffer.
                    Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                    await using (stream.ConfigureAwait(false))
                    {
                        return ApiResult<T>.Ok(await JsonSerializer.DeserializeAsync(stream, typeInfo, cancellationToken).ConfigureAwait(false));
                    }

                case HttpStatusCode.Unauthorized:
                    return ApiResult<T>.Fail(PrinterState.Unauthorized, Messages.PasswordRejected, PrusaLinkFailure.PasswordRejected);

                case HttpStatusCode.Forbidden:
                    return ApiResult<T>.Fail(PrinterState.Unauthorized, Messages.Forbidden, PrusaLinkFailure.AccessDenied);

                default:
                    int code = (int)response.StatusCode;
                    LogUnexpectedStatus(_printer.Id, uri.AbsolutePath, code);
                    return ApiResult<T>.Fail(PrinterState.Unknown, string.Create(CultureInfo.InvariantCulture, $"The printer answered with HTTP {code}."), PrusaLinkFailure.UnexpectedResponse);
            }
        }
    }

    /// <summary>
    /// Sends a GET with cached digest credentials and answers 401 challenges: at most twice, and the second time only when
    /// the first one reused the cached password (stale nonce), so that a password changed at the same moment is re-read.
    /// A final 401 is returned as is. When a challenge cannot be answered, the response is null and the error says why
    /// (the error is only meaningful then).
    /// </summary>
    private async Task<(HttpResponseMessage? Response, AuthenticationError AuthenticationError)> SendAuthenticatedAsync(Uri uri, CancellationToken cancellationToken)
    {
        HttpResponseMessage response = await SendOnceAsync(uri, cancellationToken).ConfigureAwait(false);
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
                return (null, new AuthenticationError(challenge.Error, challenge.Reason));
            }

            response = await SendOnceAsync(uri, cancellationToken).ConfigureAwait(false);
            if (challenge.PasswordReloaded)
            {
                break;
            }
        }

        return (response, default);
    }

    private async Task<HttpResponseMessage> SendOnceAsync(Uri uri, CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = new(HttpMethod.Get, uri);
        // Firmware answers errors as JSON only when application/json is the first accepted type.
        request.Headers.TryAddWithoutValidation("Accept", "application/json");
        if (_authenticator.GetAuthorization(HttpMethod.Get, uri.PathAndQuery) is { } authorization)
        {
            request.Headers.TryAddWithoutValidation("Authorization", authorization);
        }

        // ResponseContentRead: HttpClient.Timeout then covers the body too, and a connection dropped mid-body is reported
        // as HttpRequestException. Buffering a sub-kilobyte body costs nothing worth streaming for.
        return await _httpClient.SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Maps expected exceptions to a failure; returns null (rethrow) for caller cancellation and anything unexpected.</summary>
    private Failure? ToFailure(Exception exception, CancellationToken cancellationToken)
    {
        switch (exception)
        {
            case OperationCanceledException when cancellationToken.IsCancellationRequested:
                return null;
            case OperationCanceledException:
                // HttpClient.Timeout elapsed.
                return new Failure(PrinterState.Offline, Messages.Timeout, PrusaLinkFailure.Timeout);
            case HttpRequestException { HttpRequestError: HttpRequestError.ConfigurationLimitExceeded } tooLarge:
                // The response exceeded the buffer limit: whatever answered, it is not a printer.
                LogMalformedResponse(tooLarge, _printer.Id);
                return new Failure(PrinterState.Unknown, Messages.UnexpectedResponse, PrusaLinkFailure.UnexpectedResponse);
            case HttpRequestException { HttpRequestError: HttpRequestError.SecureConnectionError } tlsException:
                // E.g. an untrusted certificate, or https against a printer that serves plain http only.
                LogNotReachable(tlsException, _printer.Id);
                return new Failure(PrinterState.Offline, Messages.SecureConnectionFailed, PrusaLinkFailure.SecureConnectionFailed);
            case HttpRequestException httpException:
                LogNotReachable(httpException, _printer.Id);
                return new Failure(PrinterState.Offline, Messages.NotReachable, PrusaLinkFailure.NotReachable);
            case IOException ioException:
                LogNotReachable(ioException, _printer.Id);
                return new Failure(PrinterState.Offline, Messages.NotReachable, PrusaLinkFailure.NotReachable);
            case JsonException jsonException:
                LogMalformedResponse(jsonException, _printer.Id);
                return new Failure(PrinterState.Unknown, Messages.UnexpectedResponse, PrusaLinkFailure.UnexpectedResponse);
            case SecretStoreException secretStoreException:
                LogPasswordUnreadable(secretStoreException, _printer.Id);
                return new Failure(PrinterState.Unknown, Messages.PasswordUnreadable, PrusaLinkFailure.PasswordUnreadable);
            default:
                return null;
        }
    }

    private static PrusaLinkInfoResult InfoFailure(Failure failure) =>
        new(failure.State, failure.Message, null) { Failure = failure.Reason };

    private PrinterSnapshot Unavailable(PrinterState state, string message) =>
        PrinterSnapshot.Unavailable(_printer.Id, state, _timeProvider.GetUtcNow(), message);

    private static Telemetry CreateTelemetry(StatusPrinter printer) => new()
    {
        Nozzle = printer.TempNozzle is { } nozzle ? new Temperature(nozzle, printer.TargetNozzle) : null,
        Bed = printer.TempBed is { } bed ? new Temperature(bed, printer.TargetBed) : null,
        HotendFanRpm = ToInt(printer.FanHotend),
        PrintFanRpm = ToInt(printer.FanPrint),
        AxisZ = printer.AxisZ,
        SpeedPercent = ToInt(printer.Speed),
        FlowPercent = ToInt(printer.Flow),
    };

    private static int? ToInt(double? value) =>
        value is { } v && double.IsFinite(v) ? (int)Math.Clamp(Math.Round(v), int.MinValue, int.MaxValue) : null;

    private static TimeSpan? Seconds(double? value) =>
        value is { } v && double.IsFinite(v) && v >= 0 && v < TimeSpan.MaxValue.TotalSeconds ? TimeSpan.FromSeconds(v) : null;

    [LoggerMessage(Level = LogLevel.Debug, Message = "Printer '{PrinterId}' is not reachable.")]
    private partial void LogNotReachable(Exception exception, string printerId);

    [LoggerMessage(Level = LogLevel.Error, Message = "The PrusaLink password of printer '{PrinterId}' could not be read from the secret store.")]
    private partial void LogPasswordUnreadable(Exception exception, string printerId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Printer '{PrinterId}' sent a malformed response.")]
    private partial void LogMalformedResponse(Exception exception, string printerId);

    // Debug: a persistent error status would otherwise log a warning on every poll.
    [LoggerMessage(Level = LogLevel.Debug, Message = "Printer '{PrinterId}' answered {Path} with HTTP {StatusCode}.")]
    private partial void LogUnexpectedStatus(string printerId, string path, int statusCode);

    private sealed class ApiUris(Uri baseUri)
    {
        public Uri Status { get; } = new(baseUri, "api/v1/status");

        public Uri Job { get; } = new(baseUri, "api/v1/job");

        public Uri Version { get; } = new(baseUri, "api/version");

        public Uri Info { get; } = new(baseUri, "api/v1/info");
    }

    private sealed record JobFileName(long JobId, string? FileName);

    private readonly record struct Failure(PrinterState State, string Message, PrusaLinkFailure Reason);

    private readonly record struct AuthenticationError(string Message, PrusaLinkFailure Reason);

    private readonly record struct ApiResult<T>(T? Value, Failure? Failure)
        where T : class
    {
        public static ApiResult<T> Ok(T? value) => new(value, null);

        public static ApiResult<T> Fail(PrinterState state, string message, PrusaLinkFailure reason) => new(null, new Failure(state, message, reason));
    }

    /// <summary>Technical English texts; they never contain addresses, credentials or exception details.</summary>
    private static class Messages
    {
        public const string InvalidAddress = "The printer address is not valid.";
        public const string NotReachable = "The printer is not reachable.";
        public const string Timeout = "The printer did not respond in time.";
        public const string SecureConnectionFailed = "The secure (https) connection to the printer failed.";
        public const string PasswordRejected = "The printer rejected the user name or password.";
        public const string Forbidden = "The printer denied access (HTTP 403).";
        public const string UnexpectedResponse = "The printer sent an unexpected response.";
        public const string PasswordUnreadable = "The stored password could not be read.";
    }
}
