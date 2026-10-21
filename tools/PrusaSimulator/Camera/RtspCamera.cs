using System.Buffers.Binary;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace PrusaTrayDiagnostic.Simulator.Camera;

/// <summary>
/// A minimal RTSP camera that behaves like the Prusa Buddy3D: one H.264 track, packetization-mode 1, no
/// sprop-parameter-sets (SPS/PPS are sent in-band before every IDR frame), RTP over TCP (interleaved). Serves any number
/// of clients at the same time, each in its own session; any path is accepted (e.g. <c>/live</c>).
/// <para>
/// RTP sequence numbers and timestamps keep increasing across loops of the clip (90 kHz clock, one frame per
/// 1/<see cref="RtspCameraOptions.FramesPerSecond"/>), so a decoder sees one continuous live stream. UDP transport is
/// declined with 461, like a camera that streams over TCP only.
/// </para>
/// </summary>
public sealed class RtspCamera : IAsyncDisposable
{
    private const int MaxRtpPayload = 1400;
    private const int PayloadType = 96;
    private const uint Ssrc = 0x50545344;
    private const int MaxRequestBytes = 64 * 1024;

    private readonly RtspCameraOptions _options;
    private readonly TcpListener _listener;
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _acceptLoop;
    private readonly List<Session> _sessions = [];
    private readonly List<string> _requests = [];
    private readonly List<string> _requestLines = [];
    private int _connections;
    private int _sessionCounter;
    private long _framesSent;
    private volatile bool _offline;
    private int _disposed;

    /// <summary>Starts listening at once.</summary>
    /// <exception cref="SocketException">The port cannot be used (e.g. it is taken).</exception>
    public RtspCamera(RtspCameraOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
        _listener = CreateListener(options.Address, options.Port);
        _listener.Start();
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        string host = options.Address.Equals(IPAddress.Any) || options.Address.Equals(IPAddress.IPv6Any)
            ? "127.0.0.1"
            : options.Address.AddressFamily == AddressFamily.InterNetworkV6 ? $"[{options.Address}]" : options.Address.ToString();
        StreamUri = new Uri(string.Create(CultureInfo.InvariantCulture, $"rtsp://{host}:{Port}/live"));
        _acceptLoop = AcceptAsync(_stop.Token);
    }

    /// <summary>
    /// A listener for <paramref name="address"/>; for <c>::</c> (all IPv6 interfaces) it accepts IPv4 too, so that
    /// <c>--host ::</c> is reachable as <c>127.0.0.1</c> like the printers (Kestrel does the same).
    /// </summary>
    internal static TcpListener CreateListener(IPAddress address, int port)
    {
        TcpListener listener = new(address, port);
        if (address.Equals(IPAddress.IPv6Any))
        {
            listener.Server.DualMode = true;
        }

        return listener;
    }

    public int Port { get; }

    /// <summary>Stream address on this machine (<c>rtsp://host:port/live</c>).</summary>
    public Uri StreamUri { get; }

    /// <summary>Accepted TCP connections so far.</summary>
    public int Connections => Volatile.Read(ref _connections);

    /// <summary>Clients currently connected.</summary>
    public int ActiveSessions
    {
        get
        {
            lock (_sessions)
            {
                return _sessions.Count;
            }
        }
    }

    /// <summary>Frames sent to all clients together.</summary>
    public long FramesSent => Interlocked.Read(ref _framesSent);

    /// <summary>
    /// While offline, connected clients are dropped and new connections are closed right after they are accepted, like a
    /// camera that lost power or network; switching back lets clients connect again.
    /// </summary>
    public bool IsOffline => _offline;

    /// <summary>RTSP methods in the order the clients sent them.</summary>
    public IReadOnlyList<string> Requests
    {
        get
        {
            lock (_requests)
            {
                return [.. _requests];
            }
        }
    }

    /// <summary>First lines of the requests ("METHOD url RTSP/1.0").</summary>
    public IReadOnlyList<string> RequestLines
    {
        get
        {
            lock (_requests)
            {
                return [.. _requestLines];
            }
        }
    }

    public void SetOffline(bool offline)
    {
        _offline = offline;
        if (offline)
        {
            foreach (Session session in SnapshotSessions())
            {
                session.Close();
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        await _stop.CancelAsync().ConfigureAwait(false);
        _listener.Stop();
        try
        {
            await _acceptLoop.ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is OperationCanceledException or SocketException or ObjectDisposedException)
        {
            // Stopped.
        }

        Session[] sessions = SnapshotSessions();
        foreach (Session session in sessions)
        {
            session.Close();
        }

        // Session tasks end on the cancellation and never throw.
        await Task.WhenAll(sessions.Select(static s => s.Completion)).ConfigureAwait(false);
        _stop.Dispose();
    }

    private Session[] SnapshotSessions()
    {
        lock (_sessions)
        {
            return [.. _sessions];
        }
    }

    private async Task AcceptAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            TcpClient client = await _listener.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false);
            Interlocked.Increment(ref _connections);
            if (_offline)
            {
                client.Dispose();
                continue;
            }

            client.NoDelay = true;
            int id = Interlocked.Increment(ref _sessionCounter);
            Session session = new(this, client, id, cancellationToken);
            lock (_sessions)
            {
                _sessions.Add(session);
            }

            session.Start();
        }
    }

    private void Remove(Session session)
    {
        lock (_sessions)
        {
            _sessions.Remove(session);
        }
    }

    private void Record(string method, string requestLine)
    {
        lock (_requests)
        {
            _requests.Add(method);
            _requestLines.Add(requestLine);
        }
    }

    private void Log(string message) => _options.Log?.Invoke(message);

    /// <summary>One client connection: RTSP requests in, RTP (interleaved) out.</summary>
    private sealed class Session : IDisposable
    {
        private readonly RtspCamera _camera;
        private readonly TcpClient _client;
        private readonly int _id;
        private readonly CancellationTokenSource _cancellation;
        private readonly SemaphoreSlim _writeLock = new(1, 1);
        private readonly string _sessionId;
        private Task? _streaming;
        private byte _rtpChannel;
        private bool _setUp;
        private TaskCompletionSource? _paused;

        public Session(RtspCamera camera, TcpClient client, int id, CancellationToken stopToken)
        {
            _camera = camera;
            _client = client;
            _id = id;
            _sessionId = string.Create(CultureInfo.InvariantCulture, $"{0x1A2B0000 + id:X8}");
            _cancellation = CancellationTokenSource.CreateLinkedTokenSource(stopToken);
        }

        public Task Completion { get; private set; } = Task.CompletedTask;

        public void Start() => Completion = RunAsync();

        public void Dispose()
        {
            _client.Dispose();
            _cancellation.Dispose();
            _writeLock.Dispose();
        }

        public void Close()
        {
            try
            {
                _cancellation.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // Already finished.
            }
        }

        private async Task RunAsync()
        {
            CancellationToken cancellationToken = _cancellation.Token;
            string? remote = _client.Client.RemoteEndPoint?.ToString();
            _camera.Log(string.Create(CultureInfo.InvariantCulture, $"camera: client {_id} connected ({remote})"));
            try
            {
                // Closing the socket is what unblocks a pending read or write on cancellation.
                using CancellationTokenRegistration closeOnCancel = cancellationToken.Register(static c => ((TcpClient)c!).Dispose(), _client);
                await ReadRequestsAsync(_client.GetStream(), cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or SocketException or OperationCanceledException or ObjectDisposedException or InvalidOperationException)
            {
                // Client gone, camera offline or stopped.
            }
            finally
            {
                await _cancellation.CancelAsync().ConfigureAwait(false);
                if (_streaming is { } streaming)
                {
                    await streaming.ConfigureAwait(false);
                }

                Dispose();
                _camera.Remove(this);
                _camera.Log(string.Create(CultureInfo.InvariantCulture, $"camera: client {_id} disconnected"));
            }
        }

        private async Task ReadRequestsAsync(NetworkStream stream, CancellationToken cancellationToken)
        {
            byte[] buffer = new byte[MaxRequestBytes];
            int count = 0;
            // Bytes of an interleaved packet larger than the buffer that are still to be discarded.
            int skip = 0;
            while (true)
            {
                int read = await stream.ReadAsync(buffer.AsMemory(count), cancellationToken).ConfigureAwait(false);
                if (read == 0)
                {
                    return;
                }

                if (skip > 0)
                {
                    int dropped = Math.Min(skip, read);
                    Buffer.BlockCopy(buffer, count + dropped, buffer, count, read - dropped);
                    skip -= dropped;
                    read -= dropped;
                }

                count += read;
                while (count > 0)
                {
                    int consumed;
                    if (buffer[0] == (byte)'$')
                    {
                        // Interleaved binary data from the client (RTCP receiver reports): skipped.
                        if (count < 4)
                        {
                            break;
                        }

                        int packet = 4 + BinaryPrimitives.ReadUInt16BigEndian(buffer.AsSpan(2));
                        if (count < packet)
                        {
                            if (packet > buffer.Length)
                            {
                                // Never fits: drop what is here and discard the rest as it arrives.
                                skip = packet - count;
                                count = 0;
                            }

                            break;
                        }

                        consumed = packet;
                    }
                    else
                    {
                        int end = buffer.AsSpan(0, count).IndexOf("\r\n\r\n"u8);
                        if (end < 0)
                        {
                            if (count == buffer.Length)
                            {
                                return; // Not RTSP: drop the client.
                            }

                            break;
                        }

                        string head = Encoding.ASCII.GetString(buffer, 0, end);
                        int contentLength = ContentLength(head);
                        consumed = end + 4 + contentLength;
                        if (consumed > buffer.Length)
                        {
                            return;
                        }

                        if (count < consumed)
                        {
                            break;
                        }

                        if (!await HandleRequestAsync(stream, head, cancellationToken).ConfigureAwait(false))
                        {
                            return;
                        }
                    }

                    Buffer.BlockCopy(buffer, consumed, buffer, 0, count - consumed);
                    count -= consumed;
                }
            }
        }

        /// <returns>False when the connection should be closed (TEARDOWN).</returns>
        private async Task<bool> HandleRequestAsync(NetworkStream stream, string head, CancellationToken cancellationToken)
        {
            string[] lines = head.Split("\r\n");
            string[] requestLine = lines[0].Split(' ');
            string method = requestLine[0];
            string url = requestLine.Length > 1 ? requestLine[1] : "";
            _camera.Record(method, lines[0]);
            string cseq = Header(lines, "CSeq") ?? "0";
            string session = string.Create(CultureInfo.InvariantCulture, $"Session: {_sessionId};timeout=60\r\n");
            string? requestSession = Header(lines, "Session")?.Split(';')[0].Trim();
            bool needsSession = method is "PLAY" or "PAUSE" or "TEARDOWN";
            string response;
            bool keepOpen = true;
            bool startStreaming = false;
            if (needsSession && !_setUp)
            {
                response = Status(cseq, "455 Method Not Valid in This State", "");
            }
            else if ((needsSession && requestSession != _sessionId)
                || (method is "GET_PARAMETER" or "SET_PARAMETER" && requestSession is not null && requestSession != _sessionId))
            {
                response = Status(cseq, "454 Session Not Found", "");
            }
            else
            {
                switch (method)
                {
                    case "OPTIONS":
                        response = Ok(cseq, "Public: OPTIONS, DESCRIBE, SETUP, PLAY, PAUSE, TEARDOWN, GET_PARAMETER, SET_PARAMETER\r\n");
                        break;
                    case "DESCRIBE":
                        response = Describe(cseq, url, _camera._options);
                        break;
                    case "SETUP":
                        string transport = Header(lines, "Transport") ?? "";
                        if (!transport.Contains("TCP", StringComparison.OrdinalIgnoreCase))
                        {
                            response = Status(cseq, "461 Unsupported Transport", "");
                            break;
                        }

                        (byte rtp, byte rtcp) = Interleaved(transport);
                        _rtpChannel = rtp;
                        _setUp = true;
                        response = Ok(cseq, string.Create(CultureInfo.InvariantCulture, $"Transport: RTP/AVP/TCP;unicast;interleaved={rtp}-{rtcp};ssrc={Ssrc:X8}\r\n") + session);
                        break;
                    case "PLAY":
                        response = Ok(cseq, session + "Range: npt=0.000-\r\n" + string.Create(CultureInfo.InvariantCulture, $"RTP-Info: url={url};seq=0;rtptime=0\r\n"));
                        startStreaming = true;
                        break;
                    case "PAUSE":
                        // Frames stop until the next PLAY; the stream then continues live (no catching up).
                        Interlocked.CompareExchange(ref _paused, new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously), null);
                        response = Ok(cseq, session);
                        break;
                    case "TEARDOWN":
                        response = Ok(cseq, session);
                        keepOpen = false;
                        break;
                    case "GET_PARAMETER" or "SET_PARAMETER":
                        response = Ok(cseq, session);
                        break;
                    default:
                        response = Status(cseq, "501 Not Implemented", "");
                        break;
                }
            }

            await WriteAsync(stream, Encoding.ASCII.GetBytes(response), cancellationToken).ConfigureAwait(false);
            if (startStreaming)
            {
                Interlocked.Exchange(ref _paused, null)?.TrySetResult();
                if (_streaming is null)
                {
                    _camera.Log(string.Create(CultureInfo.InvariantCulture, $"camera: client {_id} playing"));
                    _streaming = StreamQuietlyAsync(stream, cancellationToken);
                }
            }

            return keepOpen;
        }

        private async Task StreamQuietlyAsync(NetworkStream stream, CancellationToken cancellationToken)
        {
            // Off the reading loop: the client's keep-alive requests are answered while frames are sent.
            await Task.Yield();
            try
            {
                await StreamAsync(stream, cancellationToken).ConfigureAwait(false);
                if (!_camera._options.Loop)
                {
                    // Played once: close like a recording that ended (the reading loop ends too).
                    await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                    _client.Client.Shutdown(SocketShutdown.Both);
                    Close();
                }
            }
            catch (Exception ex) when (ex is IOException or SocketException or OperationCanceledException or ObjectDisposedException or InvalidOperationException)
            {
                // Client gone, camera offline or stopped.
                Close();
            }
        }

        private async Task StreamAsync(NetworkStream stream, CancellationToken cancellationToken)
        {
            RtspCameraOptions options = _camera._options;
            H264Clip clip = options.Clip;
            TimeProvider clock = options.TimeProvider;
            double frameSeconds = 1 / options.FramesPerSecond;
            ushort sequence = 0;
            long frameIndex = 0;
            long paceStart = clock.GetTimestamp();
            long pacedFrames = 0;
            int frame = clip.LoopStart;
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (options.RealTime)
                {
                    TimeSpan due = TimeSpan.FromSeconds(pacedFrames * frameSeconds) - clock.GetElapsedTime(paceStart);
                    if (due > TimeSpan.Zero)
                    {
                        await Task.Delay(due, clock, cancellationToken).ConfigureAwait(false);
                    }
                    else if (due < TimeSpan.FromSeconds(-1))
                    {
                        // A slow client: continue at the normal pace instead of bursting to catch up.
                        paceStart = clock.GetTimestamp();
                        pacedFrames = 0;
                    }

                    pacedFrames++;
                }

                if (Volatile.Read(ref _paused) is { } paused)
                {
                    await paused.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
                    // Live again: the pace restarts now, this frame goes out at once.
                    paceStart = clock.GetTimestamp();
                    pacedFrames = 1;
                }

                uint timestamp = unchecked((uint)(long)Math.Round(frameIndex * 90000 * frameSeconds));
                byte[] packets = Packetize(clip.Frames[frame], ref sequence, timestamp, _rtpChannel);
                await WriteAsync(stream, packets, cancellationToken).ConfigureAwait(false);
                Interlocked.Increment(ref _camera._framesSent);
                frameIndex++;
                if (++frame == clip.Frames.Count)
                {
                    if (!options.Loop)
                    {
                        return;
                    }

                    frame = clip.LoopStart;
                }
            }
        }

        private async Task WriteAsync(NetworkStream stream, byte[] data, CancellationToken cancellationToken)
        {
            await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await stream.WriteAsync(data, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _writeLock.Release();
            }
        }

        /// <summary>All RTP packets of one frame (single NAL units and FU-A fragments), each with the interleaved header.</summary>
        private static byte[] Packetize(IReadOnlyList<byte[]> nals, ref ushort sequence, uint timestamp, byte channel)
        {
            using MemoryStream output = new();
            for (int n = 0; n < nals.Count; n++)
            {
                byte[] nal = nals[n];
                bool lastNal = n + 1 == nals.Count;
                if (nal.Length <= MaxRtpPayload)
                {
                    WriteRtp(output, channel, sequence++, timestamp, lastNal, nal, []);
                    continue;
                }

                // FU-A: indicator (F, NRI, type 28), header (start, end, original type), then the payload in pieces.
                for (int offset = 1; offset < nal.Length; offset += MaxRtpPayload)
                {
                    int length = Math.Min(MaxRtpPayload, nal.Length - offset);
                    bool first = offset == 1;
                    bool last = offset + length == nal.Length;
                    byte[] header =
                    [
                        (byte)((nal[0] & 0xE0) | 28),
                        (byte)((first ? 0x80 : 0) | (last ? 0x40 : 0) | (nal[0] & 0x1F)),
                    ];
                    WriteRtp(output, channel, sequence++, timestamp, last && lastNal, header, nal.AsSpan(offset, length));
                }
            }

            return output.ToArray();
        }

        private static void WriteRtp(MemoryStream output, byte channel, ushort sequence, uint timestamp, bool marker, ReadOnlySpan<byte> prefix, ReadOnlySpan<byte> payload)
        {
            int rtpLength = 12 + prefix.Length + payload.Length;
            Span<byte> header = stackalloc byte[16];
            header[0] = (byte)'$';
            header[1] = channel;
            BinaryPrimitives.WriteUInt16BigEndian(header[2..], (ushort)rtpLength);
            header[4] = 0x80; // version 2
            header[5] = (byte)((marker ? 0x80 : 0) | PayloadType);
            BinaryPrimitives.WriteUInt16BigEndian(header[6..], sequence);
            BinaryPrimitives.WriteUInt32BigEndian(header[8..], timestamp);
            BinaryPrimitives.WriteUInt32BigEndian(header[12..], Ssrc);
            output.Write(header);
            output.Write(prefix);
            output.Write(payload);
        }

        private static string Describe(string cseq, string url, RtspCameraOptions options)
        {
            // The format parameters are H.264 specific; another announced encoding goes without them.
            string formatParameters = options.RtpMap == RtspCameraOptions.H264RtpMap ? "a=fmtp:96 packetization-mode=1\r\n" : "";
            string sdp =
                "v=0\r\n" +
                "o=- 0 0 IN IP4 127.0.0.1\r\n" +
                "s=PrusaSimulator camera\r\n" +
                "c=IN IP4 0.0.0.0\r\n" +
                "t=0 0\r\n" +
                $"m={options.Media} 0 RTP/AVP 96\r\n" +
                $"a=rtpmap:96 {options.RtpMap}\r\n" +
                formatParameters +
                "a=control:track1\r\n";
            string contentBase = url.EndsWith('/') ? url : url + "/";
            return Ok(cseq, string.Create(CultureInfo.InvariantCulture,
                $"Content-Base: {contentBase}\r\nContent-Type: application/sdp\r\nContent-Length: {Encoding.ASCII.GetByteCount(sdp)}\r\n")) + sdp;
        }

        private static string Ok(string cseq, string headers) => Status(cseq, "200 OK", headers);

        private static string Status(string cseq, string status, string headers) => $"RTSP/1.0 {status}\r\nCSeq: {cseq}\r\n{headers}\r\n";

        private static string? Header(string[] lines, string name)
        {
            foreach (string line in lines.AsSpan(1))
            {
                int colon = line.IndexOf(':', StringComparison.Ordinal);
                if (colon > 0 && line.AsSpan(0, colon).Trim().Equals(name, StringComparison.OrdinalIgnoreCase))
                {
                    return line[(colon + 1)..].Trim();
                }
            }

            return null;
        }

        private static int ContentLength(string head)
        {
            string? value = Header(head.Split("\r\n"), "Content-Length");
            return int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int length) && length >= 0 ? length : 0;
        }

        private static (byte Rtp, byte Rtcp) Interleaved(string transport)
        {
            const string key = "interleaved=";
            int start = transport.IndexOf(key, StringComparison.OrdinalIgnoreCase);
            if (start >= 0)
            {
                string[] channels = transport[(start + key.Length)..].Split(';')[0].Split('-');
                if (byte.TryParse(channels[0], NumberStyles.None, CultureInfo.InvariantCulture, out byte rtp))
                {
                    byte rtcp = channels.Length > 1 && byte.TryParse(channels[1], NumberStyles.None, CultureInfo.InvariantCulture, out byte c) ? c : (byte)(rtp + 1);
                    return (rtp, rtcp);
                }
            }

            return (0, 1);
        }
    }
}
