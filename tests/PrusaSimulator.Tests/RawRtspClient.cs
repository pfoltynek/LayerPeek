using System.Buffers.Binary;
using System.Globalization;
using System.Net.Sockets;
using System.Text;

namespace PrusaTrayDiagnostic.Simulator.Tests;

/// <summary>
/// A bare RTSP client (TCP interleaved) that exposes the RTP packets exactly as the camera sends them, independently of
/// FFmpeg: for checking sequence numbers, timestamps and the NAL units at loop boundaries.
/// </summary>
internal sealed class RawRtspClient : IAsyncDisposable
{
    private readonly TcpClient _client;
    private readonly BufferedStream _stream;
    private readonly Uri _uri;
    private int _cseq;

    private RawRtspClient(TcpClient client, Uri uri)
    {
        _client = client;
        _stream = new BufferedStream(client.GetStream(), 64 * 1024);
        _uri = uri;
    }

    public static async Task<RawRtspClient> ConnectAsync(Uri uri, CancellationToken cancellationToken)
    {
        TcpClient client = new();
        await client.ConnectAsync(uri.Host, uri.Port, cancellationToken);
        return new RawRtspClient(client, uri);
    }

    /// <summary>OPTIONS, DESCRIBE, SETUP (TCP) and PLAY; RTP packets follow.</summary>
    public async Task PlayAsync(CancellationToken cancellationToken)
    {
        Assert.StartsWith("RTSP/1.0 200", (await RequestAsync("OPTIONS", _uri.ToString(), "", cancellationToken)).Head, StringComparison.Ordinal);
        RtspResponse describe = await RequestAsync("DESCRIBE", _uri.ToString(), "Accept: application/sdp\r\n", cancellationToken);
        Assert.Contains("a=rtpmap:96 H264/90000", describe.Body, StringComparison.Ordinal);
        RtspResponse setup = await RequestAsync("SETUP", _uri + "/track1", "Transport: RTP/AVP/TCP;unicast;interleaved=0-1\r\n", cancellationToken);
        Assert.StartsWith("RTSP/1.0 200", setup.Head, StringComparison.Ordinal);
        string session = setup.Header("Session")!.Split(';')[0];
        Assert.StartsWith("RTSP/1.0 200", (await RequestAsync("PLAY", _uri.ToString(), $"Session: {session}\r\nRange: npt=0.000-\r\n", cancellationToken)).Head, StringComparison.Ordinal);
    }

    public async Task<RtspResponse> RequestAsync(string method, string url, string headers, CancellationToken cancellationToken)
    {
        await SendRequestAsync(method, url, headers, cancellationToken);
        return await ReadResponseAsync(cancellationToken);
    }

    /// <summary>Sends a request without waiting for the answer (e.g. while RTP packets arrive).</summary>
    public async Task SendRequestAsync(string method, string url, string headers, CancellationToken cancellationToken)
    {
        string request = string.Create(CultureInfo.InvariantCulture, $"{method} {url} RTSP/1.0\r\nCSeq: {++_cseq}\r\n{headers}\r\n");
        await _stream.WriteAsync(Encoding.ASCII.GetBytes(request), cancellationToken);
        await _stream.FlushAsync(cancellationToken);
    }

    /// <summary>The next RTP packet (RTSP responses in between are skipped); null when the camera closed the connection.</summary>
    public async Task<RtpPacket?> ReadPacketAsync(CancellationToken cancellationToken)
    {
        byte[] header = new byte[4];
        while (true)
        {
            if (await _stream.ReadAsync(header.AsMemory(0, 1), cancellationToken) == 0)
            {
                return null;
            }

            if (header[0] != (byte)'$')
            {
                // An RTSP response: read up to its end and continue.
                await ReadResponseAsync(cancellationToken, header[0]);
                continue;
            }

            await _stream.ReadExactlyAsync(header.AsMemory(1, 3), cancellationToken);
            byte[] rtp = new byte[BinaryPrimitives.ReadUInt16BigEndian(header.AsSpan(2))];
            await _stream.ReadExactlyAsync(rtp, cancellationToken);
            return new RtpPacket(
                header[1],
                BinaryPrimitives.ReadUInt16BigEndian(rtp.AsSpan(2)),
                BinaryPrimitives.ReadUInt32BigEndian(rtp.AsSpan(4)),
                (rtp[1] & 0x80) != 0,
                rtp[12..]);
        }
    }

    /// <summary>Packets of the next frame (up to the marker); null at the end of the stream.</summary>
    public async Task<List<RtpPacket>?> ReadFrameAsync(CancellationToken cancellationToken)
    {
        List<RtpPacket> packets = [];
        while (await ReadPacketAsync(cancellationToken) is { } packet)
        {
            packets.Add(packet);
            if (packet.Marker)
            {
                return packets;
            }
        }

        return packets.Count == 0 ? null : packets;
    }

    public async ValueTask DisposeAsync()
    {
        await _stream.DisposeAsync();
        _client.Dispose();
    }

    private async Task<RtspResponse> ReadResponseAsync(CancellationToken cancellationToken, byte? first = null)
    {
        StringBuilder head = new();
        if (first is { } b)
        {
            head.Append((char)b);
        }

        byte[] one = new byte[1];
        while (!head.ToString().EndsWith("\r\n\r\n", StringComparison.Ordinal))
        {
            await _stream.ReadExactlyAsync(one, cancellationToken);
            head.Append((char)one[0]);
        }

        RtspResponse response = new(head.ToString(), "");
        if (int.TryParse(response.Header("Content-Length"), CultureInfo.InvariantCulture, out int length) && length > 0)
        {
            byte[] body = new byte[length];
            await _stream.ReadExactlyAsync(body, cancellationToken);
            response = response with { Body = Encoding.ASCII.GetString(body) };
        }

        return response;
    }
}

internal sealed record RtspResponse(string Head, string Body)
{
    public string? Header(string name)
    {
        foreach (string line in Head.Split("\r\n"))
        {
            if (line.StartsWith(name + ":", StringComparison.OrdinalIgnoreCase))
            {
                return line[(name.Length + 1)..].Trim();
            }
        }

        return null;
    }
}

internal sealed record RtpPacket(byte Channel, ushort Sequence, uint Timestamp, bool Marker, byte[] Payload)
{
    /// <summary>NAL unit type carried by the packet (the original type for FU-A fragments).</summary>
    public int NalType => (Payload[0] & 0x1F) == 28 ? Payload[1] & 0x1F : Payload[0] & 0x1F;

    /// <summary>Starts a NAL unit (single NAL unit packet or first FU-A fragment).</summary>
    public bool StartsNal => (Payload[0] & 0x1F) != 28 || (Payload[1] & 0x80) != 0;
}
