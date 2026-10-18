using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;

using PrusaTrayDiagnostic.Core.Printers;
using PrusaTrayDiagnostic.Core.Secrets;
using PrusaTrayDiagnostic.Core.Web;
using PrusaTrayDiagnostic.PrusaLink.WebProxy;

namespace PrusaTrayDiagnostic.PrusaLink.Tests.WebProxy;

/// <summary>
/// A gateway with a fake printer (in-memory <see cref="FakePrusaLinkServer"/>, firmware Digest) and a real loopback
/// "browser" client. Disposing stops every proxy and closes every port, also after a failed test.
/// </summary>
internal sealed class ProxyTestContext : IAsyncDisposable
{
    public const string PrinterAddress = "192.168.1.20";

    public ProxyTestContext(PrusaLinkWebProxyOptions? options = null, ISecretStore? secrets = null)
    {
        Printer.IsPublicPath = static path => !path.Contains("/api/", StringComparison.Ordinal);
        Printer.Route("/", HttpStatusCode.OK, "<html>PrusaLink</html>");
        Printer.RouteFixture("/api/version", "version");
        Printer.RouteFixture("/api/v1/status", "status-idle");
        Secrets.SetAsync(SecretKeys.ForPrinter("core-one"), Printer.Password, CancellationToken.None).GetAwaiter().GetResult();
        Gateway = new PrusaLinkWebGateway(new FakeHttpClientFactory(Printer), secrets ?? Secrets, Time, Logger, options ?? PrusaLinkWebProxyOptions.Default);
    }

    public FakePrusaLinkServer Printer { get; } = new();

    public InMemorySecretStore Secrets { get; } = new();

    public FakeTimeProvider Time { get; } = new(new DateTimeOffset(2026, 10, 18, 12, 0, 0, TimeSpan.Zero));

    public CapturingLoggerFactory Logger { get; } = new();

    public PrusaLinkWebGateway Gateway { get; }

    /// <summary>The "browser": real sockets to the proxy, nothing automatic (no redirects, cookies, decompression).</summary>
    public HttpClient Browser { get; } = new(new SocketsHttpHandler
    {
        AllowAutoRedirect = false,
        UseCookies = false,
        UseProxy = false,
        AutomaticDecompression = DecompressionMethods.None,
    })
    {
        Timeout = TimeSpan.FromSeconds(60),
    };

    public static PrinterDefinition Definition(string id = "core-one", string address = PrinterAddress) =>
        new() { Id = id, Name = id, ConnectionType = ConnectionType.PrusaLink, Address = address };

    /// <summary>Opens the proxy and asserts that it is used.</summary>
    public async Task<Uri> OpenAsync(PrinterDefinition? printer = null)
    {
        PrinterWebAccess access = await Gateway.OpenAsync(printer ?? Definition(), TestContext.Current.CancellationToken);
        Assert.True(access.IsProxied, $"Expected a proxy address, got fallback {access.Fallback}.");
        return access.Address!;
    }

    public PrusaLinkWebProxy Proxy(string printerId = "core-one") =>
        Gateway.GetProxyForTests(printerId) ?? throw new InvalidOperationException("No proxy is running.");

    public async ValueTask DisposeAsync()
    {
        Browser.Dispose();
        await Gateway.DisposeAsync();
        Printer.Dispose();
    }

    /// <summary>Addresses on which this machine listens on <paramref name="port"/> (OS listener table, no connection).</summary>
    public static IPEndPoint[] Listeners(int port) =>
        [.. System.Net.NetworkInformation.IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners().Where(e => e.Port == port)];

    /// <summary>Whether anything listens on <paramref name="port"/> (refused connects take seconds on Windows).</summary>
    public static bool IsListening(int port) => Listeners(port).Length != 0;

    /// <summary>Whether something accepts TCP connections on <paramref name="address"/>:<paramref name="port"/>.</summary>
    public static async Task<bool> IsListeningAsync(IPAddress address, int port)
    {
        using Socket socket = new(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
        try
        {
            using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(5));
            await socket.ConnectAsync(address, port, timeout.Token);
            return true;
        }
        catch (SocketException)
        {
            return false;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    /// <summary>Sends a hand-written HTTP/1.1 request (for what HttpClient refuses to send) and returns the status code.</summary>
    public static async Task<int> SendRawAsync(int port, string request)
    {
        using TcpClient client = new();
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(10));
        await client.ConnectAsync(IPAddress.Loopback, port, timeout.Token);
        NetworkStream stream = client.GetStream();
        await stream.WriteAsync(Encoding.ASCII.GetBytes(request), timeout.Token);
        byte[] buffer = new byte[1024];
        StringBuilder received = new();
        while (!received.ToString().Contains("\r\n", StringComparison.Ordinal))
        {
            int count = await stream.ReadAsync(buffer, timeout.Token);
            if (count == 0)
            {
                break;
            }

            received.Append(Encoding.ASCII.GetString(buffer, 0, count));
        }

        // "HTTP/1.1 404 Not Found"
        string statusLine = received.ToString().Split("\r\n")[0];
        return int.Parse(statusLine.Split(' ')[1], System.Globalization.CultureInfo.InvariantCulture);
    }
}

/// <summary>Every client talks to the in-memory printer; the handler outlives the clients.</summary>
internal sealed class FakeHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
{
    public ConcurrentQueue<string> Names { get; } = new();

    public HttpClient CreateClient(string name)
    {
        Names.Enqueue(name);
        return new HttpClient(handler, disposeHandler: false) { Timeout = Timeout.InfiniteTimeSpan };
    }
}

/// <summary>
/// Collects formatted messages, state and exceptions of every category in one queue, so that tests can look for leaked
/// secrets. Entries start with the level; <see cref="Categories"/> records which categories logged.
/// </summary>
internal sealed class CapturingLoggerFactory : ILoggerFactory
{
    public ConcurrentQueue<string> Entries { get; } = new();

    public ConcurrentQueue<string> Categories { get; } = new();

    public string AllText => string.Join("\n", Entries);

    public ILogger CreateLogger(string categoryName) => new Logger(this, categoryName);

    public void AddProvider(ILoggerProvider provider) => throw new NotSupportedException();

    public void Dispose()
    {
    }

    private sealed class Logger(CapturingLoggerFactory owner, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            string stateText = state?.ToString() ?? string.Empty;
            owner.Categories.Enqueue(category);
            owner.Entries.Enqueue($"{logLevel}: {formatter(state, exception)} | {stateText} | {exception}");
        }
    }
}

/// <summary>
/// A body of <paramref name="length"/> generated bytes (value = position mod 251). Before passing
/// <paramref name="gateAt"/> bytes it waits for <paramref name="gate"/>: when the receiver opens the gate only after
/// it got the first part, the body provably flows through without being buffered.
/// </summary>
internal sealed class GeneratedStream(long length, long gateAt = long.MaxValue, Task? gate = null) : Stream
{
    private long _position;

    public override bool CanRead => true;

    public override bool CanSeek => false;

    public override bool CanWrite => false;

    public override long Length => length;

    public override long Position
    {
        get => _position;
        set => throw new NotSupportedException();
    }

    public static byte ValueAt(long position) => (byte)(position % 251);

    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException("Only asynchronous reads are expected.");

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (gate is not null && _position >= gateAt)
        {
            await gate.WaitAsync(cancellationToken);
        }

        long limit = _position < gateAt ? Math.Min(length, gateAt) : length;
        int count = (int)Math.Min(buffer.Length, limit - _position);
        Span<byte> span = buffer.Span[..count];
        for (int i = 0; i < span.Length; i++)
        {
            span[i] = ValueAt(_position + i);
        }

        _position += count;
        return count;
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    public override void Flush()
    {
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}

/// <summary>Write-only sink that verifies <see cref="GeneratedStream"/> content and signals when enough arrived.</summary>
internal sealed class VerifyingSink(long signalAt) : Stream
{
    private readonly TaskCompletionSource _reached = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public long Received { get; private set; }

    public bool ContentMatches { get; private set; } = true;

    public Task Reached => _reached.Task;

    public override bool CanRead => false;

    public override bool CanSeek => false;

    public override bool CanWrite => true;

    public override long Length => Received;

    public override long Position
    {
        get => Received;
        set => throw new NotSupportedException();
    }

    public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));

    public override void Write(ReadOnlySpan<byte> buffer)
    {
        // Spot checks keep the 256 MB tests fast.
        for (int i = 0; i < buffer.Length; i += 4093)
        {
            ContentMatches &= buffer[i] == GeneratedStream.ValueAt(Received + i);
        }

        Received += buffer.Length;
        if (Received >= signalAt)
        {
            _reached.TrySetResult();
        }
    }

    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        Write(buffer.Span);
        return ValueTask.CompletedTask;
    }

    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        Write(buffer.AsSpan(offset, count));
        return Task.CompletedTask;
    }

    public override void Flush()
    {
    }

    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();
}
