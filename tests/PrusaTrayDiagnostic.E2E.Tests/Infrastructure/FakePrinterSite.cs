using System.Collections.Concurrent;
using System.Net;
using System.Text;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Primitives;

using PrusaTrayDiagnostic.PrusaLink.Tests;

namespace PrusaTrayDiagnostic.E2E.Tests.Infrastructure;

/// <summary>
/// The fake printer of the browser tests: a real HTTP server on 127.0.0.1 in front of the shared firmware imitation
/// <see cref="FakePrusaLinkServer"/> (Buddy Digest: RFC 2069, MD5, no qop, realm "Printer API", stale=true, HA2 with
/// the real method). Web pages and scripts are public and <c>/api/*</c> needs Digest, like on the firmware. It serves
/// the fake Prusa-Link-Web from <c>FakeWeb/</c> and accepts G-code uploads, checking every byte.
/// </summary>
internal sealed class FakePrinterSite : IAsyncDisposable
{
    private const string UploadPrefix = "/api/v1/files/usb/";

    private static readonly string WebDirectory = Path.Combine(AppContext.BaseDirectory, "FakeWeb");

    private readonly HttpMessageInvoker _firmware;
    private readonly ConcurrentDictionary<string, TaskCompletionSource<int>> _answers = new(StringComparer.Ordinal);
    private LoopbackServer? _server;

    private FakePrinterSite()
    {
        Firmware.IsPublicPath = static path => !path.StartsWith("/api/", StringComparison.Ordinal);
        Firmware.Handler = ServeAsync;
        _firmware = new HttpMessageInvoker(Firmware, disposeHandler: false);
    }

    /// <summary>Digest, password, nonces and the log of every request that reached the printer.</summary>
    public FakePrusaLinkServer Firmware { get; } = new();

    public ConcurrentQueue<ReceivedUpload> Uploads { get; } = new();

    /// <summary>Completes with the status of the first answer the printer sent for <paramref name="path"/>.</summary>
    public Task<int> AnsweredAsync(string path) => Answer(path).Task;

    public Uri BaseAddress => _server?.BaseAddress ?? throw new InvalidOperationException("Not started.");

    /// <summary>The address as a user enters it in the settings (host and port).</summary>
    public string Address => BaseAddress.Authority;

    public static async Task<FakePrinterSite> StartAsync(CancellationToken cancellationToken)
    {
        FakePrinterSite site = new();
        try
        {
            site._server = await LoopbackServer.StartAsync(site.BridgeAsync, cancellationToken);
            return site;
        }
        catch
        {
            await site.DisposeAsync();
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (_server is not null)
            {
                await _server.DisposeAsync();
            }
        }
        finally
        {
            _firmware.Dispose();
            Firmware.Dispose();
        }
    }

    /// <summary>Passes the HTTP request to the in-memory firmware unchanged (raw target: Digest covers the exact URI).</summary>
    private async Task BridgeAsync(HttpContext context)
    {
        string target = context.Features.GetRequiredFeature<IHttpRequestFeature>().RawTarget;
        using HttpRequestMessage request = new(new HttpMethod(context.Request.Method), new Uri(BaseAddress, target));
        bool hasBody = context.Request.ContentLength > 0 || context.Request.Headers.TransferEncoding.Count > 0;
        if (hasBody)
        {
            request.Content = new StreamContent(context.Request.Body, 64 * 1024);
        }

        foreach ((string name, StringValues values) in context.Request.Headers)
        {
            if (!request.Headers.TryAddWithoutValidation(name, (IEnumerable<string?>)values))
            {
                request.Content?.Headers.TryAddWithoutValidation(name, (IEnumerable<string?>)values);
            }
        }

        using HttpResponseMessage response = await _firmware.SendAsync(request, context.RequestAborted);
        context.Response.StatusCode = (int)response.StatusCode;
        foreach (KeyValuePair<string, IEnumerable<string>> header in response.Headers)
        {
            context.Response.Headers[header.Key] = header.Value.ToArray();
        }

        if (response.Content is not null)
        {
            foreach (KeyValuePair<string, IEnumerable<string>> header in response.Content.Headers)
            {
                context.Response.Headers[header.Key] = header.Value.ToArray();
            }

            await response.Content.CopyToAsync(context.Response.Body, context.RequestAborted);
        }

        await context.Response.CompleteAsync();
        Answer(context.Request.Path).TrySetResult((int)response.StatusCode);
    }

    private TaskCompletionSource<int> Answer(string path) =>
        _answers.GetOrAdd(path, static _ => new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously));

    /// <summary>Requests that passed the firmware's login check (or need none).</summary>
    private async Task<HttpResponseMessage> ServeAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        string path = request.RequestUri!.AbsolutePath;
        if (request.Method == HttpMethod.Put && path.StartsWith(UploadPrefix, StringComparison.Ordinal))
        {
            Uploads.Enqueue(await ReceiveUploadAsync(request, Uri.UnescapeDataString(path[UploadPrefix.Length..]), cancellationToken));
            return new HttpResponseMessage(HttpStatusCode.Created);
        }

        if (request.Method != HttpMethod.Get && request.Method != HttpMethod.Head)
        {
            return Json(HttpStatusCode.MethodNotAllowed, """{"title": "405: Method Not Allowed","message":""}""");
        }

        return path switch
        {
            "/" or "/index.html" => File("index.html", "text/html; charset=utf-8"),
            "/app.js" => File("app.js", "text/javascript; charset=utf-8"),
            "/api/version" => Json(HttpStatusCode.OK, Fixtures.Read("version")),
            "/api/v1/info" => Json(HttpStatusCode.OK, Fixtures.Read("info")),
            "/api/v1/status" => Json(HttpStatusCode.OK, Fixtures.Read("status-idle")),
            _ => Json(HttpStatusCode.NotFound, """{"title": "404: Not Found","message":""}"""),
        };
    }

    private static async Task<ReceivedUpload> ReceiveUploadAsync(HttpRequestMessage request, string name, CancellationToken cancellationToken)
    {
        long received = 0;
        bool contentMatches = true;
        if (request.Content is not null)
        {
            await using Stream body = await request.Content.ReadAsStreamAsync(cancellationToken);
            byte[] buffer = new byte[64 * 1024];
            int count;
            while ((count = await body.ReadAsync(buffer, cancellationToken)) > 0)
            {
                for (int i = 0; i < count; i++)
                {
                    contentMatches &= buffer[i] == (byte)((received + i) % 251);
                }

                received += count;
            }
        }

        return new ReceivedUpload(
            name,
            received,
            request.Content?.Headers.ContentLength,
            contentMatches,
            Header(request, "Overwrite"),
            Header(request, "Print-After-Upload"));
    }

    private static string? Header(HttpRequestMessage request, string name) =>
        request.Headers.TryGetValues(name, out IEnumerable<string>? values) ? string.Join(",", values) : null;

    private static HttpResponseMessage File(string name, string contentType)
    {
        HttpResponseMessage response = new(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(System.IO.File.ReadAllBytes(Path.Combine(WebDirectory, name))),
        };
        response.Content.Headers.TryAddWithoutValidation("Content-Type", contentType);
        return response;
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
}

/// <summary>A G-code upload as the printer received it.</summary>
internal sealed record ReceivedUpload(string Name, long Length, long? ContentLength, bool ContentMatches, string? Overwrite, string? PrintAfterUpload);
