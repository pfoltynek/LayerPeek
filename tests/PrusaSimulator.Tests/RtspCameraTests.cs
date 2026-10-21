using System.Net;
using System.Net.Sockets;

using Microsoft.Extensions.Time.Testing;

using PrusaTrayDiagnostic.Simulator.Camera;

namespace PrusaTrayDiagnostic.Simulator.Tests;

/// <summary>The simulated RTSP camera, seen through a bare RTSP client (no FFmpeg; ephemeral loopback ports).</summary>
public sealed class RtspCameraTests
{
    private const int Buddy3DFrames = 71;
    private const int TicksPerFrame = 90000 / 25;

    private static readonly H264Clip Buddy3D = H264Clip.Load(SimulatorHost.DefaultClipPath);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void Buddy3DClipLoopsFromItsFirstFrame()
    {
        Assert.Equal(Buddy3DFrames, Buddy3D.Frames.Count);
        Assert.Equal(0, Buddy3D.LoopStart);
        Assert.Equal(Buddy3DFrames, Buddy3D.LoopLength);
        Assert.Equal([7, 8, 5], Buddy3D.Frames[0].Select(static n => n[0] & 0x1F));
        Assert.All(Buddy3D.Frames.Skip(1), static f => Assert.True((f[^1][0] & 0x1F) is 1 or 5));
    }

    [Fact]
    public void ClipWithParameterSetsOnlyOnceGetsThemAtTheLoopStart()
    {
        H264Clip clip = H264Clip.Parse(CraftedClip());

        Assert.Equal(1, clip.LoopStart);
        Assert.Equal(3, clip.Frames.Count);
        Assert.Equal([7, 8, 5], clip.Frames[1].Select(static n => n[0] & 0x1F));
        Assert.Equal([1], clip.Frames[2].Select(static n => n[0] & 0x1F));
    }

    [Fact]
    public void DataWithoutAnIdrFrameIsRejected()
    {
        Assert.Throws<InvalidDataException>(() => H264Clip.Parse([0, 0, 1, 0x41, 0x80, 0, 0, 1, 0x41, 0x80]));
        Assert.Throws<InvalidDataException>(() => H264Clip.Parse("not a video"u8));
    }

    [Fact]
    public async Task LoopsSeamlesslyWithContinuousSequenceNumbersAndTimestamps()
    {
        await using RtspCamera camera = new(new RtspCameraOptions { Clip = Buddy3D, RealTime = false });
        await using RawRtspClient client = await RawRtspClient.ConnectAsync(camera.StreamUri, Ct);
        await client.PlayAsync(Ct);
        ushort? lastSequence = null;
        uint? lastTimestamp = null;

        // Three whole loops and the start of a fourth.
        for (int frame = 0; frame < (3 * Buddy3DFrames) + 5; frame++)
        {
            List<RtpPacket> packets = (await client.ReadFrameAsync(Ct))!;
            Assert.All(packets, p => Assert.Equal(packets[0].Timestamp, p.Timestamp));
            Assert.All(packets, static p => Assert.Equal(0, p.Channel));
            foreach (RtpPacket packet in packets)
            {
                if (lastSequence is { } previous)
                {
                    Assert.Equal((ushort)(previous + 1), packet.Sequence);
                }

                lastSequence = packet.Sequence;
            }

            if (lastTimestamp is { } before)
            {
                Assert.Equal(before + TicksPerFrame, packets[0].Timestamp);
            }

            lastTimestamp = packets[0].Timestamp;
            int[] nalTypes = [.. packets.Where(static p => p.StartsNal).Select(static p => p.NalType)];
            if (frame % Buddy3DFrames == 0)
            {
                // Every loop starts with SPS, PPS and an IDR frame, so a decoder restarts without a gap.
                Assert.Equal([7, 8, 5], nalTypes);
            }
        }
    }

    [Fact]
    public async Task FramesArePacedAtTheFrameRate()
    {
        FakeTimeProvider clock = new();
        await using RtspCamera camera = new(new RtspCameraOptions { Clip = Buddy3D, TimeProvider = clock });
        await using RawRtspClient client = await RawRtspClient.ConnectAsync(camera.StreamUri, Ct);
        await client.PlayAsync(Ct);
        Assert.NotNull(await client.ReadFrameAsync(Ct)); // the first frame goes out at once

        Task<List<RtpPacket>?> second = client.ReadFrameAsync(Ct);
        clock.Advance(TimeSpan.FromMilliseconds(39));
        await Task.Delay(100, Ct);
        Assert.False(second.IsCompleted, "A frame came before its time.");
        clock.Advance(TimeSpan.FromMilliseconds(1));
        Assert.NotNull(await second);

        // Exactly one frame per 40 ms (25 fps), nothing in between.
        for (int i = 0; i < 24; i++)
        {
            Task<List<RtpPacket>?> next = client.ReadFrameAsync(Ct);
            clock.Advance(TimeSpan.FromMilliseconds(40));
            Assert.NotNull(await next);
        }

        Task<List<RtpPacket>?> early = client.ReadFrameAsync(Ct);
        await Task.Delay(100, Ct);
        Assert.False(early.IsCompleted, "More frames than 25 per simulated second.");
        Assert.Equal(26, camera.FramesSent);
    }

    [Fact]
    public async Task PauseStopsTheFramesUntilPlay()
    {
        FakeTimeProvider clock = new();
        await using RtspCamera camera = new(new RtspCameraOptions { Clip = Buddy3D, TimeProvider = clock });
        await using RawRtspClient client = await RawRtspClient.ConnectAsync(camera.StreamUri, Ct);
        await client.PlayAsync(Ct);
        Assert.NotNull(await client.ReadFrameAsync(Ct));

        // The clock stands still, so no frame can come before the answer.
        RtspResponse paused = await client.RequestAsync("PAUSE", camera.StreamUri.ToString(), $"Session: {client.Session}\r\n", Ct);
        clock.Advance(TimeSpan.FromSeconds(1));
        Task<List<RtpPacket>?> next = client.ReadFrameAsync(Ct);
        await Task.Delay(150, Ct);
        bool sentWhilePaused = next.IsCompleted;
        await client.SendRequestAsync("PLAY", camera.StreamUri.ToString(), $"Session: {client.Session}\r\n", Ct);

        Assert.StartsWith("RTSP/1.0 200", paused.Head, StringComparison.Ordinal);
        Assert.False(sentWhilePaused, "A frame was sent while paused.");
        Assert.NotNull(await next);
    }

    [Fact]
    public async Task PlayWithoutSetupIsNotValidInThisState()
    {
        await using RtspCamera camera = new(new RtspCameraOptions { Clip = Buddy3D });
        await using RawRtspClient client = await RawRtspClient.ConnectAsync(camera.StreamUri, Ct);

        RtspResponse response = await client.RequestAsync("PLAY", camera.StreamUri.ToString(), "Session: 12345678\r\n", Ct);

        Assert.StartsWith("RTSP/1.0 455", response.Head, StringComparison.Ordinal);
        Assert.Equal(0, camera.FramesSent);
    }

    [Theory]
    [InlineData("Session: 0BADF00D\r\n")]
    [InlineData("")]
    public async Task PlayWithAnotherOrNoSessionIsNotFound(string sessionHeader)
    {
        await using RtspCamera camera = new(new RtspCameraOptions { Clip = Buddy3D });
        await using RawRtspClient client = await RawRtspClient.ConnectAsync(camera.StreamUri, Ct);
        RtspResponse setup = await client.RequestAsync("SETUP", camera.StreamUri + "/track1", "Transport: RTP/AVP/TCP;unicast;interleaved=0-1\r\n", Ct);
        Assert.StartsWith("RTSP/1.0 200", setup.Head, StringComparison.Ordinal);

        RtspResponse response = await client.RequestAsync("PLAY", camera.StreamUri.ToString(), sessionHeader, Ct);

        Assert.StartsWith("RTSP/1.0 454", response.Head, StringComparison.Ordinal);
        Assert.Equal(0, camera.FramesSent);
    }

    [Fact]
    public async Task OversizedInterleavedPacketIsSkipped()
    {
        await using RtspCamera camera = new(new RtspCameraOptions { Clip = Buddy3D });
        await using RawRtspClient client = await RawRtspClient.ConnectAsync(camera.StreamUri, Ct);
        byte[] packet = new byte[4 + 65535];
        packet[0] = (byte)'$';
        packet[1] = 1;
        packet[2] = 0xFF;
        packet[3] = 0xFF;
        packet.AsSpan(4).Fill((byte)'R');

        await client.SendRawAsync(packet, Ct);
        RtspResponse options = await client.RequestAsync("OPTIONS", camera.StreamUri.ToString(), "", Ct);

        Assert.StartsWith("RTSP/1.0 200", options.Head, StringComparison.Ordinal);
        Assert.Equal(["OPTIONS"], camera.Requests);
    }

    [Fact]
    public void ListenerOnAllIpv6InterfacesAcceptsIpv4Too()
    {
        using TcpListener any = RtspCamera.CreateListener(IPAddress.IPv6Any, 0);
        using TcpListener loopback = RtspCamera.CreateListener(IPAddress.IPv6Loopback, 0);

        Assert.True(any.Server.DualMode);
        Assert.False(loopback.Server.DualMode);
    }

    [Fact]
    public async Task ServesSeveralClientsAtOnce()
    {
        await using RtspCamera camera = new(new RtspCameraOptions { Clip = Buddy3D, RealTime = false });
        RawRtspClient[] clients = await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => RawRtspClient.ConnectAsync(camera.StreamUri, Ct)));
        try
        {
            await Task.WhenAll(clients.Select(c => c.PlayAsync(Ct)));
            Assert.Equal(3, camera.ActiveSessions);

            int[] frames = await Task.WhenAll(clients.Select(static async c =>
            {
                int count = 0;
                while (count < 2 * Buddy3DFrames && await c.ReadFrameAsync(Ct) is not null)
                {
                    count++;
                }

                return count;
            }));

            Assert.All(frames, static f => Assert.Equal(2 * Buddy3DFrames, f));
            Assert.Equal(3, camera.Connections);
        }
        finally
        {
            foreach (RawRtspClient client in clients)
            {
                await client.DisposeAsync();
            }
        }

        await WaitUntilAsync(() => camera.ActiveSessions == 0);
    }

    [Fact]
    public async Task WithoutLoopTheClipIsPlayedOnceAndTheConnectionCloses()
    {
        await using RtspCamera camera = new(new RtspCameraOptions { Clip = Buddy3D, Loop = false, RealTime = false });
        await using RawRtspClient client = await RawRtspClient.ConnectAsync(camera.StreamUri, Ct);
        await client.PlayAsync(Ct);
        int frames = 0;

        while (await client.ReadFrameAsync(Ct) is not null)
        {
            frames++;
        }

        Assert.Equal(Buddy3DFrames, frames);
        Assert.Equal(["OPTIONS", "DESCRIBE", "SETUP", "PLAY"], camera.Requests);
    }

    [Fact]
    public async Task CustomClipIsLoopedFromItsIdrFrame()
    {
        await using RtspCamera camera = new(new RtspCameraOptions { Clip = H264Clip.Parse(CraftedClip()), RealTime = false });
        await using RawRtspClient client = await RawRtspClient.ConnectAsync(camera.StreamUri, Ct);
        await client.PlayAsync(Ct);
        List<int[]> frames = [];

        for (int i = 0; i < 4; i++)
        {
            frames.Add([.. (await client.ReadFrameAsync(Ct))!.Select(static p => p.NalType)]);
        }

        // The leading P frame cannot be decoded and is never sent.
        Assert.Equal([[7, 8, 5], [1], [7, 8, 5], [1]], frames);
    }

    [Fact]
    public async Task UdpTransportIsDeclined()
    {
        await using RtspCamera camera = new(new RtspCameraOptions { Clip = Buddy3D });
        await using RawRtspClient client = await RawRtspClient.ConnectAsync(camera.StreamUri, Ct);

        RtspResponse response = await client.RequestAsync("SETUP", camera.StreamUri + "/track1", "Transport: RTP/AVP;unicast;client_port=5000-5001\r\n", Ct);

        Assert.StartsWith("RTSP/1.0 461", response.Head, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DescribeAnnouncesH264ByDefault()
    {
        await using RtspCamera camera = new(new RtspCameraOptions { Clip = Buddy3D });
        await using RawRtspClient client = await RawRtspClient.ConnectAsync(camera.StreamUri, Ct);

        RtspResponse describe = await client.RequestAsync("DESCRIBE", camera.StreamUri.ToString(), "Accept: application/sdp\r\n", Ct);

        Assert.Contains("a=rtpmap:96 H264/90000\r\n", describe.Body, StringComparison.Ordinal);
        Assert.Contains("a=fmtp:96 packetization-mode=1\r\n", describe.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DescribeAnnouncesTheConfiguredEncoding()
    {
        await using RtspCamera camera = new(new RtspCameraOptions { Clip = Buddy3D, RtpMap = "H265/90000" });
        await using RawRtspClient client = await RawRtspClient.ConnectAsync(camera.StreamUri, Ct);

        RtspResponse describe = await client.RequestAsync("DESCRIBE", camera.StreamUri.ToString(), "Accept: application/sdp\r\n", Ct);

        Assert.Contains("a=rtpmap:96 H265/90000\r\n", describe.Body, StringComparison.Ordinal);
        // The H.264 format parameters do not belong to another encoding.
        Assert.DoesNotContain("a=fmtp:", describe.Body, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("H264/90000\r\na=evil")]
    public void InvalidRtpMapIsRejected(string rtpMap)
    {
        Assert.Throws<ArgumentException>(() => new RtspCameraOptions { Clip = Buddy3D, RtpMap = rtpMap });
    }

    [Fact]
    public async Task DescribeAnnouncesTheConfiguredMediaType()
    {
        await using RtspCamera camera = new(new RtspCameraOptions { Clip = Buddy3D, Media = "audio", RtpMap = "PCMU/8000" });
        await using RawRtspClient client = await RawRtspClient.ConnectAsync(camera.StreamUri, Ct);

        RtspResponse describe = await client.RequestAsync("DESCRIBE", camera.StreamUri.ToString(), "Accept: application/sdp\r\n", Ct);

        Assert.Contains("m=audio 0 RTP/AVP 96\r\n", describe.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("m=video", describe.Body, StringComparison.Ordinal);
        Assert.Contains("a=rtpmap:96 PCMU/8000\r\n", describe.Body, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("audio video")]
    [InlineData("audio\r\nm=video")]
    public void InvalidMediaIsRejected(string media)
    {
        Assert.Throws<ArgumentException>(() => new RtspCameraOptions { Clip = Buddy3D, Media = media });
    }

    [Fact]
    public async Task TeardownEndsTheSession()
    {
        await using RtspCamera camera = new(new RtspCameraOptions { Clip = Buddy3D });
        await using RawRtspClient client = await RawRtspClient.ConnectAsync(camera.StreamUri, Ct);
        await client.PlayAsync(Ct);
        await client.ReadFrameAsync(Ct);

        // The answer may arrive between RTP packets; the client skips it.
        await client.SendRequestAsync("TEARDOWN", camera.StreamUri.ToString(), $"Session: {client.Session}\r\n", Ct);
        while (await client.ReadPacketAsync(Ct) is not null)
        {
        }

        await WaitUntilAsync(() => camera.ActiveSessions == 0);
        Assert.Contains("TEARDOWN", camera.Requests);
    }

    [Fact]
    public async Task OfflineCameraDropsClientsAndAcceptsThemAgainWhenOnline()
    {
        await using RtspCamera camera = new(new RtspCameraOptions { Clip = Buddy3D });
        RawRtspClient playing = await RawRtspClient.ConnectAsync(camera.StreamUri, Ct);
        await using (playing)
        {
            await playing.PlayAsync(Ct);
            await playing.ReadFrameAsync(Ct);

            camera.SetOffline(true);

            await Assert.ThrowsAnyAsync<Exception>(async () =>
            {
                while (await playing.ReadPacketAsync(Ct) is not null)
                {
                }

                throw new EndOfStreamException();
            });
        }

        await using (RawRtspClient refused = await RawRtspClient.ConnectAsync(camera.StreamUri, Ct))
        {
            await Assert.ThrowsAnyAsync<Exception>(() => refused.PlayAsync(Ct));
        }

        camera.SetOffline(false);
        await using RawRtspClient again = await RawRtspClient.ConnectAsync(camera.StreamUri, Ct);
        await again.PlayAsync(Ct);
        Assert.NotNull(await again.ReadFrameAsync(Ct));
        Assert.False(camera.IsOffline);
    }

    [Fact]
    public async Task DisposingClosesClientsAndReleasesThePort()
    {
        RtspCamera camera = new(new RtspCameraOptions { Clip = Buddy3D });
        await using RawRtspClient client = await RawRtspClient.ConnectAsync(camera.StreamUri, Ct);
        await client.PlayAsync(Ct);
        await client.ReadFrameAsync(Ct);

        await camera.DisposeAsync();

        await Assert.ThrowsAnyAsync<Exception>(async () =>
        {
            while (await client.ReadPacketAsync(Ct) is not null)
            {
            }

            throw new EndOfStreamException();
        });
        using TcpListener listener = new(IPAddress.Loopback, camera.Port);
        listener.Start();
        listener.Stop();
    }

    /// <summary>A P frame, an IDR frame, a P frame, then SPS and PPS once at the end (invented bytes, valid NAL headers).</summary>
    private static byte[] CraftedClip() =>
    [
        0, 0, 0, 1, 0x41, 0x80, 0x11,
        0, 0, 0, 1, 0x65, 0x80, 0x22, 0x33,
        0, 0, 0, 1, 0x41, 0x80, 0x44,
        0, 0, 0, 1, 0x67, 0x42, 0xC0, 0x1F,
        0, 0, 0, 1, 0x68, 0xCE, 0x3C, 0x80,
    ];

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        while (!condition())
        {
            await Task.Delay(20, timeout.Token);
        }
    }
}
