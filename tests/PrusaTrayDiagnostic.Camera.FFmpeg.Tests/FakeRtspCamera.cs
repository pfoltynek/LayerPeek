using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace PrusaTrayDiagnostic.Camera.FFmpeg.Tests;

/// <summary>
/// A minimal RTSP camera on the loopback interface: answers OPTIONS / DESCRIBE / SETUP / PLAY like the Buddy3D
/// (one H.264 track, packetization-mode 1, no sprop-parameter-sets, TCP interleaved) and then sends the recorded
/// fixture as RTP (single NAL units and FU-A fragments). Closes the connection after the last frame.
/// </summary>
internal sealed class FakeRtspCamera : IAsyncDisposable
{
    private const int MaxRtpPayload = 1400;
    private const int TicksPerFrame = 90000 / 25;

    private readonly TcpListener _listener;
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _serve;
    private readonly List<byte[]> _nals;
    private readonly List<string> _requests = [];

    public FakeRtspCamera(string annexBFile)
    {
        _nals = SplitAnnexB(File.ReadAllBytes(annexBFile));
        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Start();
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        _serve = ServeAsync(_stop.Token);
    }

    public int Port { get; }

    public Uri StreamUri => new($"rtsp://127.0.0.1:{Port}/live");

    /// <summary>RTSP methods in the order the client sent them.</summary>
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

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync();
        _listener.Stop();
        try
        {
            await _serve;
        }
        catch (Exception ex) when (ex is OperationCanceledException or SocketException or IOException or ObjectDisposedException)
        {
            // Stopped or the client went away.
        }

        _stop.Dispose();
    }

    private static List<byte[]> SplitAnnexB(byte[] data)
    {
        List<byte[]> nals = [];
        int start = -1;
        int i = 0;
        while (i + 3 <= data.Length)
        {
            bool code3 = data[i] == 0 && data[i + 1] == 0 && data[i + 2] == 1;
            if (code3)
            {
                if (start >= 0)
                {
                    int end = i;
                    while (end > start && data[end - 1] == 0)
                    {
                        end--;
                    }

                    nals.Add(data[start..end]);
                }

                i += 3;
                start = i;
                continue;
            }

            i++;
        }

        if (start >= 0 && start < data.Length)
        {
            nals.Add(data[start..]);
        }

        return nals;
    }

    private static bool IsVcl(byte[] nal) => (nal[0] & 0x1F) is 1 or 5;

    /// <summary>Whether the NAL unit starts a new access unit (frame), given that the current one has a slice.</summary>
    private static bool StartsAccessUnit(byte[] nal) =>
        !IsVcl(nal) || (nal.Length > 1 && (nal[1] & 0x80) != 0); // first_mb_in_slice == 0 (ue(v) "1")

    private async Task ServeAsync(CancellationToken cancellationToken)
    {
        using TcpClient client = await _listener.AcceptTcpClientAsync(cancellationToken);
        NetworkStream stream = client.GetStream();
        StringBuilder pending = new();
        byte[] buffer = new byte[4096];
        while (true)
        {
            int read = await stream.ReadAsync(buffer, cancellationToken);
            if (read == 0)
            {
                return;
            }

            pending.Append(Encoding.ASCII.GetString(buffer, 0, read));
            int end;
            while ((end = pending.ToString().IndexOf("\r\n\r\n", StringComparison.Ordinal)) >= 0)
            {
                string request = pending.ToString(0, end);
                pending.Remove(0, end + 4);
                string[] lines = request.Split("\r\n");
                string method = lines[0].Split(' ')[0];
                lock (_requests)
                {
                    _requests.Add(method);
                }

                string cseq = lines.FirstOrDefault(static l => l.StartsWith("CSeq:", StringComparison.OrdinalIgnoreCase))?["CSeq:".Length..].Trim() ?? "0";
                string response = method switch
                {
                    "OPTIONS" => Ok(cseq, "Public: OPTIONS, DESCRIBE, SETUP, PLAY, TEARDOWN\r\n"),
                    "DESCRIBE" => Describe(cseq),
                    "SETUP" => Ok(cseq, "Transport: RTP/AVP/TCP;unicast;interleaved=0-1\r\nSession: 12345678\r\n"),
                    "PLAY" => Ok(cseq, "Session: 12345678\r\nRange: npt=0.000-\r\n"),
                    _ => Ok(cseq, "Session: 12345678\r\n"),
                };
                await stream.WriteAsync(Encoding.ASCII.GetBytes(response), cancellationToken);
                if (method == "PLAY")
                {
                    await StreamFixtureAsync(stream, cancellationToken);
                    return;
                }
            }
        }
    }

    private static string Ok(string cseq, string headers) => $"RTSP/1.0 200 OK\r\nCSeq: {cseq}\r\n{headers}\r\n";

    private string Describe(string cseq)
    {
        string sdp =
            "v=0\r\n" +
            "o=- 0 0 IN IP4 127.0.0.1\r\n" +
            "s=fake camera\r\n" +
            "c=IN IP4 0.0.0.0\r\n" +
            "t=0 0\r\n" +
            "m=video 0 RTP/AVP 96\r\n" +
            "a=rtpmap:96 H264/90000\r\n" +
            "a=fmtp:96 packetization-mode=1\r\n" +
            "a=control:track1\r\n";
        return $"RTSP/1.0 200 OK\r\nCSeq: {cseq}\r\nContent-Base: rtsp://127.0.0.1:{Port}/live/\r\n" +
            $"Content-Type: application/sdp\r\nContent-Length: {Encoding.ASCII.GetByteCount(sdp)}\r\n\r\n{sdp}";
    }

    private async Task StreamFixtureAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        ushort sequence = 0;
        uint timestamp = 0;
        bool hasSlice = false;
        for (int n = 0; n < _nals.Count; n++)
        {
            byte[] nal = _nals[n];
            if (hasSlice && StartsAccessUnit(nal))
            {
                timestamp += TicksPerFrame;
                hasSlice = false;
            }

            hasSlice |= IsVcl(nal);
            bool lastOfFrame = n + 1 == _nals.Count || (hasSlice && StartsAccessUnit(_nals[n + 1]));
            if (nal.Length <= MaxRtpPayload)
            {
                await SendRtpAsync(stream, sequence++, timestamp, lastOfFrame, nal, cancellationToken);
                continue;
            }

            // FU-A: indicator (F, NRI, type 28), header (start, end, original type), then the payload in pieces.
            for (int offset = 1; offset < nal.Length; offset += MaxRtpPayload)
            {
                int length = Math.Min(MaxRtpPayload, nal.Length - offset);
                bool first = offset == 1;
                bool last = offset + length == nal.Length;
                byte[] payload = new byte[length + 2];
                payload[0] = (byte)((nal[0] & 0xE0) | 28);
                payload[1] = (byte)((first ? 0x80 : 0) | (last ? 0x40 : 0) | (nal[0] & 0x1F));
                Array.Copy(nal, offset, payload, 2, length);
                await SendRtpAsync(stream, sequence++, timestamp, last && lastOfFrame, payload, cancellationToken);
            }
        }

        await stream.FlushAsync(cancellationToken);
    }

    private static async Task SendRtpAsync(NetworkStream stream, ushort sequence, uint timestamp, bool marker, byte[] payload, CancellationToken cancellationToken)
    {
        int rtpLength = 12 + payload.Length;
        byte[] packet = new byte[4 + rtpLength];
        packet[0] = (byte)'$';
        packet[1] = 0; // interleaved channel 0 (RTP)
        BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(2), (ushort)rtpLength);
        packet[4] = 0x80; // version 2
        packet[5] = (byte)((marker ? 0x80 : 0) | 96);
        BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(6), sequence);
        BinaryPrimitives.WriteUInt32BigEndian(packet.AsSpan(8), timestamp);
        BinaryPrimitives.WriteUInt32BigEndian(packet.AsSpan(12), 0x50545344); // SSRC
        payload.CopyTo(packet, 16);
        await stream.WriteAsync(packet, cancellationToken);
    }
}
