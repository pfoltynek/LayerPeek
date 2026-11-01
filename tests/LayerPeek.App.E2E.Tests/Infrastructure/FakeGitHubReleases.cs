using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json.Nodes;

namespace LayerPeek.App.E2E.Tests.Infrastructure;

/// <summary>
/// The GitHub REST answer of the release list (<c>GET /repos/pfoltynek/LayerPeek/releases</c>) on the loopback, for the
/// update check of the <c>.zip</c> and development builds in the test mode (T-058 Q10 a, T-128): the app gets its address
/// through the update feed switch and never asks GitHub. A minimal HTTP/1.1 server on <c>127.0.0.1</c> (one request per
/// connection); it lists no release until <see cref="Publish"/>.
/// </summary>
internal sealed class FakeGitHubReleases : IAsyncDisposable
{
    /// <summary>The path the app asks for (<c>GitHubReleaseChecker</c>).</summary>
    public const string ReleasesPath = "/repos/pfoltynek/LayerPeek/releases";

    private readonly TcpListener _listener;
    private readonly CancellationTokenSource _stopping = new();
    private readonly Task _serving;
    private readonly Lock _gate = new();
    private readonly List<string> _requests = [];
    private string _body = "[]";

    private FakeGitHubReleases()
    {
        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Start();
        BaseUri = new Uri(string.Create(CultureInfo.InvariantCulture, $"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}/"));
        _serving = Task.Run(ServeAsync);
    }

    /// <summary>The API base the app gets (ends with '/').</summary>
    public Uri BaseUri { get; }

    /// <summary>The request lines received so far (method and path with query).</summary>
    public IReadOnlyList<string> Requests
    {
        get
        {
            lock (_gate)
            {
                return [.. _requests];
            }
        }
    }

    public static FakeGitHubReleases Start() => new();

    /// <summary>
    /// From now on the list holds the release <paramref name="version"/> (no draft, no pre-release) with the checksum file of
    /// Windows, which the app needs before it offers a release (T-113).
    /// </summary>
    public void Publish(string version)
    {
        JsonArray releases =
        [
            new JsonObject
            {
                ["tag_name"] = "v" + version,
                ["html_url"] = $"https://github.com/pfoltynek/LayerPeek/releases/tag/v{version}",
                ["draft"] = false,
                ["prerelease"] = false,
                ["assets"] = new JsonArray(
                    new JsonObject { ["name"] = "SHA256SUMS-windows.txt", ["state"] = "uploaded" },
                    new JsonObject { ["name"] = $"LayerPeek-{version}-win-x64.zip", ["state"] = "uploaded" }),
            },
        ];
        lock (_gate)
        {
            _body = releases.ToJsonString();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _stopping.CancelAsync();
        _listener.Stop();
        try
        {
            await _serving;
        }
        catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException or SocketException)
        {
        }

        _stopping.Dispose();
    }

    private async Task ServeAsync()
    {
        while (!_stopping.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await _listener.AcceptTcpClientAsync(_stopping.Token);
            }
            catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException or SocketException)
            {
                return;
            }

            _ = Task.Run(() => AnswerAsync(client));
        }
    }

    private async Task AnswerAsync(TcpClient client)
    {
        using (client)
        {
            try
            {
                using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(_stopping.Token);
                timeout.CancelAfter(TimeSpan.FromSeconds(10));
                NetworkStream stream = client.GetStream();
                string head = await ReadHeadAsync(stream, timeout.Token);
                string requestLine = head.Split("\r\n", 2)[0];
                string[] parts = requestLine.Split(' ');
                string path = parts.Length > 1 ? parts[1] : string.Empty;
                string body;
                lock (_gate)
                {
                    _requests.Add(requestLine);
                    body = _body;
                }

                bool known = parts.Length > 1 && parts[0] == "GET" && path.Split('?', 2)[0] == ReleasesPath;
                byte[] content = Encoding.UTF8.GetBytes(known ? body : "{\"message\":\"Not Found\"}");
                string status = known ? "200 OK" : "404 Not Found";
                byte[] header = Encoding.ASCII.GetBytes(
                    $"HTTP/1.1 {status}\r\nContent-Type: application/json; charset=utf-8\r\nContent-Length: {content.Length.ToString(CultureInfo.InvariantCulture)}\r\nConnection: close\r\n\r\n");
                await stream.WriteAsync(header, timeout.Token);
                await stream.WriteAsync(content, timeout.Token);
                await stream.FlushAsync(timeout.Token);
            }
            catch (Exception ex) when (ex is IOException or OperationCanceledException or SocketException or ObjectDisposedException)
            {
                // The client went away or the server stops.
            }
        }
    }

    /// <summary>The request line and headers (up to the empty line); a GET has no body.</summary>
    private static async Task<string> ReadHeadAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        byte[] buffer = new byte[4096];
        using MemoryStream head = new();
        while (head.Length < 64 * 1024)
        {
            int read = await stream.ReadAsync(buffer, cancellationToken);
            if (read == 0)
            {
                break;
            }

            head.Write(buffer, 0, read);
            if (Encoding.ASCII.GetString(head.GetBuffer(), 0, (int)head.Length).Contains("\r\n\r\n", StringComparison.Ordinal))
            {
                break;
            }
        }

        return Encoding.ASCII.GetString(head.GetBuffer(), 0, (int)head.Length);
    }
}
