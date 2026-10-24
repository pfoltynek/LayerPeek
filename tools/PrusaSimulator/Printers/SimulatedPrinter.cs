using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;

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

namespace LayerPeek.Simulator.Printers;

/// <summary>
/// One simulated printer: a Kestrel server with the PrusaLink endpoints the app uses (<c>/api/version</c>,
/// <c>/api/v1/info</c>, <c>/api/v1/status</c>, <c>/api/v1/job</c>), Digest login like the firmware, and a small status
/// page at <c>/</c> (read-only, served without login as the firmware serves its web). G-code previews of the job files at
/// <c>/thumb/s/usb/…</c> and <c>/thumb/l/usb/…</c> (T-086) with login, ETag and 304 like the firmware; 404 while
/// <see cref="PreviewsEnabled"/> is off. Control endpoints for scripts and
/// tests, answered also while offline, without login on loopback and with the PrusaLink login otherwise
/// (<see cref="SimulatedPrinterOptions.ControlRequiresLogin"/>): <c>GET /_simulator/state</c>, <c>POST /_simulator/scenario/{name}</c>,
/// <c>POST /_simulator/offline</c>, <c>POST /_simulator/online</c>, <c>POST /_simulator/previews/on</c>,
/// <c>POST /_simulator/previews/off</c>.
/// </summary>
public sealed class SimulatedPrinter : IAsyncDisposable
{
    private const string ControlPrefix = "/_simulator/";
    private const string PreviewPrefix = "/thumb/";
    private const string JsonType = "application/json";

    private readonly SimulatedPrinterOptions _options;
    private readonly DigestVerifier _digest;
    private readonly CancellationTokenSource _stopping = new();
    private readonly string _hostname;
    private readonly string _serial;
    private WebApplication? _app;
    private int _requests;
    private int _previewRequests;
    private int _disposed;
    private volatile bool _previewsEnabled;

    private SimulatedPrinter(SimulatedPrinterOptions options)
    {
        _options = options;
        _previewsEnabled = options.Previews;
        _digest = new DigestVerifier(options.UserName, options.Password, options.TimeProvider);
        _hostname = options.Hostname ?? options.Model.Hostname;
        _serial = "SN-SIM-" + options.Model.Key.ToUpperInvariant();
        Simulation = new PrinterSimulation(options.Model, options.Scenario, options.TimeProvider, options.Speed, options.Timing);
        Simulation.SetOffline(options.StartOffline);
    }

    public PrinterSimulation Simulation { get; }

    /// <summary>
    /// Whether the job files contain PNG previews; off, <c>/thumb/</c> answers 404 "File doesn't contain preview" as the
    /// firmware does for a file without one (e.g. sliced with QOI previews only).
    /// </summary>
    public bool PreviewsEnabled
    {
        get => _previewsEnabled;
        set => _previewsEnabled = value;
    }

    public SimulatedModel Model => _options.Model;

    public string UserName => _options.UserName;

    public string Password => _options.Password;

    /// <summary>The control endpoints need the PrusaLink login (listening beyond loopback).</summary>
    public bool ControlLoginRequired => _options.ControlLoginRequired;

    public int Port { get; private set; }

    /// <summary>Address to enter in the app (<c>http://host:port/</c>).</summary>
    public Uri BaseUri { get; private set; } = null!;

    /// <summary>Requests received (including control and offline ones).</summary>
    public int RequestCount => Volatile.Read(ref _requests);

    /// <summary>Logged-in <c>/thumb/</c> requests (answered with a preview, 304 or 404).</summary>
    public int PreviewRequestCount => Volatile.Read(ref _previewRequests);

    /// <summary>Starts listening.</summary>
    /// <exception cref="IOException">The port cannot be used (e.g. it is taken).</exception>
    public static async Task<SimulatedPrinter> StartAsync(SimulatedPrinterOptions options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        SimulatedPrinter printer = new(options);
        try
        {
            await printer.StartCoreAsync(cancellationToken).ConfigureAwait(false);
            return printer;
        }
        catch
        {
            await printer.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        await _stopping.CancelAsync().ConfigureAwait(false);
        if (_app is { } app)
        {
            try
            {
                using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(5));
                await app.StopAsync(timeout.Token).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException or InvalidOperationException or IOException)
            {
                // Stopping anyway.
            }
            finally
            {
                await app.DisposeAsync().ConfigureAwait(false);
            }
        }

        _stopping.Dispose();
    }

    private async Task StartCoreAsync(CancellationToken cancellationToken)
    {
        WebApplicationBuilder builder = WebApplication.CreateEmptyBuilder(new WebApplicationOptions());
        builder.Logging.ClearProviders();
        // The program handles Ctrl+C itself; several printers must not each register console handlers.
        builder.Services.AddSingleton<IHostLifetime, NoHostLifetime>();
        builder.WebHost.UseKestrelCore().ConfigureKestrel(kestrel =>
        {
            kestrel.AddServerHeader = false;
            // The firmware closes idle connections after 60 s.
            kestrel.Limits.KeepAliveTimeout = TimeSpan.FromSeconds(60);
            kestrel.Listen(_options.Address, _options.Port, static listen => listen.Protocols = HttpProtocols.Http1);
        });

        WebApplication app = builder.Build();
        app.Run(HandleAsync);
        _app = app;
        try
        {
            await app.StartAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not IOException && (ex is SocketException || ex.InnerException is SocketException))
        {
            throw new IOException(ex.Message, ex);
        }

        string bound = app.Services.GetRequiredService<IServer>().Features.GetRequiredFeature<IServerAddressesFeature>().Addresses.First();
        Port = new Uri(bound).Port;
        string host = _options.Address.Equals(IPAddress.Any) || _options.Address.Equals(IPAddress.IPv6Any)
            ? "127.0.0.1"
            : _options.Address.AddressFamily == AddressFamily.InterNetworkV6 ? $"[{_options.Address}]" : _options.Address.ToString();
        BaseUri = new Uri(string.Create(CultureInfo.InvariantCulture, $"http://{host}:{Port}/"));
    }

    private async Task HandleAsync(HttpContext context)
    {
        Interlocked.Increment(ref _requests);
        string path = context.Request.Path.Value ?? "/";
        if (path.StartsWith(ControlPrefix, StringComparison.Ordinal))
        {
            await HandleControlAsync(context, path[ControlPrefix.Length..]).ConfigureAwait(false);
            return;
        }

        if (!Simulation.IsOnline && !await WaitOnlineAsync(context).ConfigureAwait(false))
        {
            context.Abort();
            return;
        }

        if (path.StartsWith(PreviewPrefix, StringComparison.Ordinal))
        {
            // Like the firmware: the /thumb/ namespace needs the login before anything else is checked.
            if (await AuthorizeAsync(context).ConfigureAwait(false))
            {
                await HandlePreviewAsync(context, path[PreviewPrefix.Length..]).ConfigureAwait(false);
            }

            return;
        }

        if (!path.StartsWith("/api/", StringComparison.Ordinal))
        {
            await HandleWebAsync(context, path).ConfigureAwait(false);
            return;
        }

        if (!await AuthorizeAsync(context).ConfigureAwait(false))
        {
            return;
        }

        if (!HttpMethods.IsGet(context.Request.Method))
        {
            await WriteErrorAsync(context, StatusCodes.Status405MethodNotAllowed, "405: Method Not Allowed").ConfigureAwait(false);
            return;
        }

        switch (path)
        {
            case "/api/version":
                await WriteJsonAsync(context, PrusaLinkJson.Version(_options.Model, _hostname)).ConfigureAwait(false);
                break;
            case "/api/v1/info":
                await WriteJsonAsync(context, PrusaLinkJson.Info(_hostname, _serial)).ConfigureAwait(false);
                break;
            case "/api/v1/status" when Simulation.Scenario == PrinterScenario.Malformed:
                // Cut off in the middle of an object, as a firmware bug or a broken connection might.
                await WriteJsonAsync(context, MalformedStatus).ConfigureAwait(false);
                break;
            case "/api/v1/status":
                await WriteJsonAsync(context, PrusaLinkJson.Status(Simulation.GetStatus())).ConfigureAwait(false);
                break;
            case "/api/v1/job":
                SimulatedStatus status = Simulation.GetStatus();
                if (status.Job is { } job)
                {
                    await WriteJsonAsync(context, PrusaLinkJson.Job(job, status.State)).ConfigureAwait(false);
                }
                else
                {
                    context.Response.StatusCode = StatusCodes.Status204NoContent;
                }

                break;
            default:
                await WriteErrorAsync(context, StatusCodes.Status404NotFound, "404: Not Found").ConfigureAwait(false);
                break;
        }
    }

    /// <summary>
    /// <c>/thumb/{s|l}/usb/{file}</c> as Buddy's <c>link_content/previews.cpp</c> and <c>nhttp/gcode_preview.cpp</c>:
    /// GET only, unknown size letter or file 404, file without preview 404 "File doesn't contain preview",
    /// matching If-None-Match 304, otherwise the PNG with its ETag. <paramref name="rest"/> is the decoded path after
    /// <c>/thumb/</c>.
    /// </summary>
    private async Task HandlePreviewAsync(HttpContext context, string rest)
    {
        Interlocked.Increment(ref _previewRequests);
        bool large = rest.StartsWith("l/", StringComparison.Ordinal);
        if (!large && !rest.StartsWith("s/", StringComparison.Ordinal))
        {
            await WriteErrorAsync(context, StatusCodes.Status404NotFound, "404: Not Found", "Thumbnail size specification not recognized").ConfigureAwait(false);
            return;
        }

        if (!HttpMethods.IsGet(context.Request.Method))
        {
            await WriteErrorAsync(context, StatusCodes.Status405MethodNotAllowed, "405: Method Not Allowed").ConfigureAwait(false);
            return;
        }

        string filePath = rest[1..];
        SimulatedFile? file = PrinterSimulation.AllFiles.FirstOrDefault(f => filePath == "/usb/" + f.Name);
        if (file is null)
        {
            await WriteErrorAsync(context, StatusCodes.Status404NotFound, "404: Not Found").ConfigureAwait(false);
            return;
        }

        if (!PreviewsEnabled)
        {
            await WriteErrorAsync(context, StatusCodes.Status404NotFound, "404: Not Found", "File doesn't contain preview").ConfigureAwait(false);
            return;
        }

        string etag = PreviewImages.ETag(file);
        context.Response.Headers.ETag = etag;
        if (context.Request.Headers.IfNoneMatch.Contains(etag))
        {
            context.Response.StatusCode = StatusCodes.Status304NotModified;
            return;
        }

        byte[] png = PreviewImages.For(file, large);
        context.Response.StatusCode = StatusCodes.Status200OK;
        context.Response.ContentType = "image/png";
        await context.Response.Body.WriteAsync(png, context.RequestAborted).ConfigureAwait(false);
    }

    /// <summary>Digest (or X-Api-Key) like the firmware; answers 401 with a challenge and returns false when missing or wrong.</summary>
    private async Task<bool> AuthorizeAsync(HttpContext context)
    {
        string target = context.Features.GetRequiredFeature<IHttpRequestFeature>().RawTarget;
        string? authorization = context.Request.Headers.Authorization;
        string? apiKey = context.Request.Headers["X-Api-Key"];
        if (_digest.Check(context.Request.Method, target, authorization, apiKey) is not { } challenge)
        {
            return true;
        }

        context.Response.Headers.WWWAuthenticate = challenge;
        await WriteErrorAsync(context, StatusCodes.Status401Unauthorized, "401: Unauthorized").ConfigureAwait(false);
        return false;
    }

    /// <returns>True when the printer came back online meanwhile (hang mode), false when the request should be dropped.</returns>
    private async Task<bool> WaitOnlineAsync(HttpContext context)
    {
        if (_options.OfflineMode == OfflineMode.Drop)
        {
            return false;
        }

        try
        {
            using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted, _stopping.Token);
            while (!Simulation.IsOnline)
            {
                // Real time: offline windows are measured on the system clock even when the simulation uses a fake one.
                await Task.Delay(TimeSpan.FromMilliseconds(100), linked.Token).ConfigureAwait(false);
            }

            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    private async Task HandleControlAsync(HttpContext context, string command)
    {
        if (_options.ControlLoginRequired && !await AuthorizeAsync(context).ConfigureAwait(false))
        {
            return;
        }

        bool post = HttpMethods.IsPost(context.Request.Method);
        if (command == "state" && HttpMethods.IsGet(context.Request.Method))
        {
            await WriteJsonAsync(context, StateJson()).ConfigureAwait(false);
            return;
        }

        if (post && command == "offline")
        {
            Simulation.SetOffline(true);
        }
        else if (post && command == "online")
        {
            Simulation.SetOffline(false);
        }
        else if (post && command is "previews/on" or "previews/off")
        {
            PreviewsEnabled = command == "previews/on";
        }
        else if (post && command.StartsWith("scenario/", StringComparison.Ordinal)
            && PrinterScenarios.TryParse(command["scenario/".Length..], out PrinterScenario scenario, out bool offline))
        {
            if (offline)
            {
                Simulation.SetOffline(true);
            }
            else
            {
                Simulation.SetScenario(scenario);
            }
        }
        else
        {
            await WriteErrorAsync(context, StatusCodes.Status404NotFound, "404: Not Found").ConfigureAwait(false);
            return;
        }

        await WriteJsonAsync(context, StateJson()).ConfigureAwait(false);
    }

    /// <summary>The status of the <see cref="PrinterScenario.Malformed"/> scenario: valid JSON up to a cut-off value.</summary>
    internal const string MalformedStatus = "{\"printer\":{\"state\":\"IDLE\",\"temp_nozzle\":";

    private string StateJson() => string.Create(
        CultureInfo.InvariantCulture,
        $"{{\"model\":\"{_options.Model.Code}\",\"scenario\":\"{PrinterScenarios.Format(Simulation.Scenario)}\",\"online\":{(Simulation.IsOnline ? "true" : "false")},\"forced_offline\":{(Simulation.IsForcedOffline ? "true" : "false")},\"previews\":{(PreviewsEnabled ? "true" : "false")}}}");

    private static Task WriteJsonAsync(HttpContext context, string json)
    {
        context.Response.StatusCode = StatusCodes.Status200OK;
        context.Response.ContentType = JsonType;
        return context.Response.WriteAsync(json, Encoding.UTF8, context.RequestAborted);
    }

    /// <summary>Like the firmware: JSON only when application/json is the first accepted type, otherwise a short HTML page.</summary>
    private static Task WriteErrorAsync(HttpContext context, int status, string title, string message = "")
    {
        context.Response.StatusCode = status;
        string accept = context.Request.Headers.Accept.ToString();
        if (accept.TrimStart().StartsWith(JsonType, StringComparison.OrdinalIgnoreCase))
        {
            context.Response.ContentType = JsonType;
            return context.Response.WriteAsync(PrusaLinkJson.Error(title, message), Encoding.UTF8, context.RequestAborted);
        }

        context.Response.ContentType = "text/html";
        return context.Response.WriteAsync($"<html><body><h1>{WebUtility.HtmlEncode(title)}</h1></body></html>", Encoding.UTF8, context.RequestAborted);
    }

    private Task HandleWebAsync(HttpContext context, string path)
    {
        if (path is not "/" and not "/index.html")
        {
            return WriteErrorAsync(context, StatusCodes.Status404NotFound, "404: Not Found");
        }

        context.Response.StatusCode = StatusCodes.Status200OK;
        context.Response.ContentType = "text/html; charset=utf-8";
        return context.Response.WriteAsync(StatusPage.Render(_options.Model, _hostname), Encoding.UTF8, context.RequestAborted);
    }

    /// <summary>The program owns Ctrl+C and shutdown; the hosts only start and stop.</summary>
    private sealed class NoHostLifetime : IHostLifetime
    {
        public Task WaitForStartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
