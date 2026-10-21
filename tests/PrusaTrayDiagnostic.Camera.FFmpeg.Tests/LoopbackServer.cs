using System.Net;
using System.Net.Sockets;
using System.Text;

namespace PrusaTrayDiagnostic.Camera.FFmpeg.Tests;

/// <summary>
/// A TCP server on the loopback interface that plays a broken camera (no real network, no real camera). Every accepted
/// connection is handled by <see cref="LoopbackBehavior"/>; connections and the listener are closed on dispose.
/// </summary>
internal sealed class LoopbackServer : IAsyncDisposable
{
    private readonly TcpListener _listener;
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _acceptLoop;
    private readonly List<TcpClient> _clients = [];
    private readonly string? _redirectTo;
    private int _connections;

    /// <param name="redirectTo">Target address of <see cref="LoopbackBehavior.Redirect"/>; ignored otherwise.</param>
    public LoopbackServer(LoopbackBehavior behavior, string? redirectTo = null)
    {
        if (behavior == LoopbackBehavior.Redirect && string.IsNullOrEmpty(redirectTo))
        {
            throw new ArgumentException("A redirect needs a target address.", nameof(redirectTo));
        }

        _redirectTo = redirectTo;
        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Start();
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        _acceptLoop = AcceptAsync(behavior);
    }

    public int Port { get; }

    public Uri StreamUri => new($"rtsp://127.0.0.1:{Port}/live");

    /// <summary>Number of accepted connections.</summary>
    public int Connections => Volatile.Read(ref _connections);

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync();
        _listener.Stop();
        try
        {
            await _acceptLoop;
        }
        catch (Exception ex) when (ex is OperationCanceledException or SocketException or ObjectDisposedException)
        {
            // Stopped.
        }

        lock (_clients)
        {
            foreach (TcpClient client in _clients)
            {
                client.Dispose();
            }

            _clients.Clear();
        }

        _stop.Dispose();
    }

    private async Task AcceptAsync(LoopbackBehavior behavior)
    {
        while (!_stop.IsCancellationRequested)
        {
            TcpClient client = await _listener.AcceptTcpClientAsync(_stop.Token);
            Interlocked.Increment(ref _connections);
            switch (behavior)
            {
                case LoopbackBehavior.Silent:
                    lock (_clients)
                    {
                        _clients.Add(client);
                    }

                    break;
                case LoopbackBehavior.CloseImmediately:
                    client.Dispose();
                    break;
                case LoopbackBehavior.Unauthorized:
                    Answer(client, "401 Unauthorized", "WWW-Authenticate: Basic realm=\"camera\"\r\n");
                    break;
                case LoopbackBehavior.Redirect:
                    Answer(client, "302 Moved Temporarily", $"Location: {_redirectTo}\r\n");
                    break;
            }
        }
    }

    private void Answer(TcpClient client, string status, string headers)
    {
        lock (_clients)
        {
            _clients.Add(client);
        }

        _ = AnswerAsync(client, status, headers, _stop.Token);
    }

    /// <summary>Answers every RTSP request with the same <paramref name="status"/> and <paramref name="headers"/>.</summary>
    private static async Task AnswerAsync(TcpClient client, string status, string headers, CancellationToken cancellationToken)
    {
        try
        {
            NetworkStream stream = client.GetStream();
            byte[] buffer = new byte[4096];
            StringBuilder request = new();
            while (true)
            {
                int read = await stream.ReadAsync(buffer, cancellationToken);
                if (read == 0)
                {
                    return;
                }

                request.Append(Encoding.ASCII.GetString(buffer, 0, read));
                string text = request.ToString();
                int end = text.IndexOf("\r\n\r\n", StringComparison.Ordinal);
                if (end < 0)
                {
                    continue;
                }

                request.Remove(0, end + 4);
                string cseq = "1";
                foreach (string line in text[..end].Split("\r\n"))
                {
                    if (line.StartsWith("CSeq:", StringComparison.OrdinalIgnoreCase))
                    {
                        cseq = line["CSeq:".Length..].Trim();
                    }
                }

                string response = $"RTSP/1.0 {status}\r\nCSeq: {cseq}\r\n{headers}Content-Length: 0\r\n\r\n";
                await stream.WriteAsync(Encoding.ASCII.GetBytes(response), cancellationToken);
            }
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException or ObjectDisposedException or SocketException)
        {
            // Client gone or server stopped.
        }
    }
}

internal enum LoopbackBehavior
{
    /// <summary>Accepts the connection and never answers.</summary>
    Silent,

    /// <summary>Accepts and closes the connection at once.</summary>
    CloseImmediately,

    /// <summary>Answers every RTSP request with <c>401 Unauthorized</c>, like a camera that requires a login.</summary>
    Unauthorized,

    /// <summary>Answers every RTSP request with <c>302 Moved Temporarily</c> to the given address.</summary>
    Redirect,
}

/// <summary>
/// A loopback port that refuses connections for as long as this object lives: the socket is bound but never
/// listens, so no other process can take the port meanwhile (unlike a port that was free a moment ago).
/// </summary>
internal sealed class RefusingEndpoint : IDisposable
{
    private readonly Socket _socket = new(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

    public RefusingEndpoint()
    {
        _socket.ExclusiveAddressUse = true;
        _socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        StreamUri = new Uri($"rtsp://127.0.0.1:{((IPEndPoint)_socket.LocalEndPoint!).Port}/live");
    }

    public Uri StreamUri { get; }

    public void Dispose() => _socket.Dispose();
}
