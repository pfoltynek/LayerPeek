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

    // Network tests fail instead of hanging the whole run when something regresses.
    private const int TimingTestTimeout = 30_000;

    private static readonly H264Clip Buddy3D = H264Clip.Load(SimulatorHost.DefaultClipPath);


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

    [Fact(Timeout = TimingTestTimeout)]
    public async Task LoopsSeamlesslyWithContinuousSequenceNumbersAndTimestamps()
    {
        await using RtspCamera camera = new(new RtspCameraOptions { Clip = Buddy3D, RealTime = false });
        await using RawRtspClient client = await RawRtspClient.ConnectAsync(camera.StreamUri, TestContext.Current.CancellationToken);
        await client.PlayAsync(TestContext.Current.CancellationToken);
        ushort? lastSequence = null;
        uint? lastTimestamp = null;

        // Three whole loops and the start of a fourth.
        for (int frame = 0; frame < (3 * Buddy3DFrames) + 5; frame++)
        {
            List<RtpPacket> packets = (await client.ReadFrameAsync(TestContext.Current.CancellationToken))!;
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

    [Fact(Timeout = TimingTestTimeout)]
    public async Task FramesArePacedAtTheFrameRate()
    {
        FakeTimeProvider clock = new();
        long origin = clock.GetTimestamp();
        await using RtspCamera camera = new(new RtspCameraOptions { Clip = Buddy3D, TimeProvider = clock });
        await using RawRtspClient client = await RawRtspClient.ConnectAsync(camera.StreamUri, TestContext.Current.CancellationToken);
        await client.PlayAsync(TestContext.Current.CancellationToken);
        List<RtpPacket> first = (await client.ReadFrameAsync(TestContext.Current.CancellationToken))!; // the first frame goes out at once, the clock stands still
        uint previous = first[0].Timestamp;

        for (int k = 1; k <= 25; k++)
        {
            List<RtpPacket> frame = (await AdvanceUntilAsync(clock, client.ReadFrameAsync(TestContext.Current.CancellationToken)))!;
            TimeSpan arrival = clock.GetElapsedTime(origin);

            // Never before its time (frame k is due at k × 40 ms), and the RTP clock says the same.
            Assert.True(arrival >= TimeSpan.FromMilliseconds(40 * k), $"Frame {k} came at {arrival.TotalMilliseconds} ms.");
            Assert.Equal(previous + TicksPerFrame, frame[0].Timestamp);
            previous = frame[0].Timestamp;
        }

        // The pace is absolute: a late timer does not add up, 25 frames take one simulated second.
        Assert.True(clock.GetElapsedTime(origin) < TimeSpan.FromMilliseconds(1200), $"Took {clock.GetElapsedTime(origin)}.");
    }

    [Fact(Timeout = TimingTestTimeout)]
    public async Task PauseStopsTheFramesUntilPlay()
    {
        FakeTimeProvider clock = new();
        await using RtspCamera camera = new(new RtspCameraOptions { Clip = Buddy3D, TimeProvider = clock });
        await using RawRtspClient client = await RawRtspClient.ConnectAsync(camera.StreamUri, TestContext.Current.CancellationToken);
        await client.PlayAsync(TestContext.Current.CancellationToken);
        Assert.NotNull(await client.ReadFrameAsync(TestContext.Current.CancellationToken));

        // The clock stands still, so no frame can come before the answer.
        RtspResponse paused = await client.RequestAsync("PAUSE", camera.StreamUri.ToString(), $"Session: {client.Session}\r\n", TestContext.Current.CancellationToken);
        clock.Advance(TimeSpan.FromSeconds(1));
        Task<List<RtpPacket>?> next = client.ReadFrameAsync(TestContext.Current.CancellationToken);
        await Task.Delay(150, TestContext.Current.CancellationToken);
        bool sentWhilePaused = next.IsCompleted;
        await client.SendRequestAsync("PLAY", camera.StreamUri.ToString(), $"Session: {client.Session}\r\n", TestContext.Current.CancellationToken);

        Assert.StartsWith("RTSP/1.0 200", paused.Head, StringComparison.Ordinal);
        Assert.False(sentWhilePaused, "A frame was sent while paused.");
        Assert.NotNull(await AdvanceUntilAsync(clock, next));
    }

    /// <summary>
    /// Advances the fake clock in 5 ms steps, with a short real pause after each, until <paramref name="task"/> completes.
    /// The camera may register its frame timer after an advance (it is busy sending); a single advance could then leave
    /// the timer forever in the future, stepping cannot.
    /// </summary>
    private static async Task<T> AdvanceUntilAsync<T>(FakeTimeProvider clock, Task<T> task)
    {
        using CancellationTokenSource guard = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        guard.CancelAfter(TimeSpan.FromSeconds(10));
        while (!task.IsCompleted)
        {
            clock.Advance(TimeSpan.FromMilliseconds(5));
            await Task.WhenAny(task, Task.Delay(5, guard.Token));
            guard.Token.ThrowIfCancellationRequested();
        }

        return await task;
    }

    [Fact(Timeout = TimingTestTimeout)]
    public async Task PlayWithoutSetupIsNotValidInThisState()
    {
        await using RtspCamera camera = new(new RtspCameraOptions { Clip = Buddy3D });
        await using RawRtspClient client = await RawRtspClient.ConnectAsync(camera.StreamUri, TestContext.Current.CancellationToken);

        RtspResponse response = await client.RequestAsync("PLAY", camera.StreamUri.ToString(), "Session: 12345678\r\n", TestContext.Current.CancellationToken);

        Assert.StartsWith("RTSP/1.0 455", response.Head, StringComparison.Ordinal);
        Assert.Equal(0, camera.FramesSent);
    }

    [Theory(Timeout = TimingTestTimeout)]
    [InlineData("Session: 0BADF00D\r\n")]
    [InlineData("")]
    public async Task PlayWithAnotherOrNoSessionIsNotFound(string sessionHeader)
    {
        await using RtspCamera camera = new(new RtspCameraOptions { Clip = Buddy3D });
        await using RawRtspClient client = await RawRtspClient.ConnectAsync(camera.StreamUri, TestContext.Current.CancellationToken);
        RtspResponse setup = await client.RequestAsync("SETUP", camera.StreamUri + "/track1", "Transport: RTP/AVP/TCP;unicast;interleaved=0-1\r\n", TestContext.Current.CancellationToken);
        Assert.StartsWith("RTSP/1.0 200", setup.Head, StringComparison.Ordinal);

        RtspResponse response = await client.RequestAsync("PLAY", camera.StreamUri.ToString(), sessionHeader, TestContext.Current.CancellationToken);

        Assert.StartsWith("RTSP/1.0 454", response.Head, StringComparison.Ordinal);
        Assert.Equal(0, camera.FramesSent);
    }

    [Fact(Timeout = TimingTestTimeout)]
    public async Task OversizedInterleavedPacketIsSkipped()
    {
        await using RtspCamera camera = new(new RtspCameraOptions { Clip = Buddy3D });
        await using RawRtspClient client = await RawRtspClient.ConnectAsync(camera.StreamUri, TestContext.Current.CancellationToken);
        byte[] packet = new byte[4 + 65535];
        packet[0] = (byte)'$';
        packet[1] = 1;
        packet[2] = 0xFF;
        packet[3] = 0xFF;
        packet.AsSpan(4).Fill((byte)'R');

        await client.SendRawAsync(packet, TestContext.Current.CancellationToken);
        RtspResponse options = await client.RequestAsync("OPTIONS", camera.StreamUri.ToString(), "", TestContext.Current.CancellationToken);

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

    [Fact(Timeout = TimingTestTimeout)]
    public async Task ServesSeveralClientsAtOnce()
    {
        await using RtspCamera camera = new(new RtspCameraOptions { Clip = Buddy3D, RealTime = false });
        RawRtspClient[] clients = await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => RawRtspClient.ConnectAsync(camera.StreamUri, TestContext.Current.CancellationToken)));
        try
        {
            await Task.WhenAll(clients.Select(c => c.PlayAsync(TestContext.Current.CancellationToken)));
            Assert.Equal(3, camera.ActiveSessions);

            int[] frames = await Task.WhenAll(clients.Select(static async c =>
            {
                int count = 0;
                while (count < 2 * Buddy3DFrames && await c.ReadFrameAsync(TestContext.Current.CancellationToken) is not null)
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

    [Fact(Timeout = TimingTestTimeout)]
    public async Task WithoutLoopTheClipIsPlayedOnceAndTheConnectionCloses()
    {
        await using RtspCamera camera = new(new RtspCameraOptions { Clip = Buddy3D, Loop = false, RealTime = false });
        await using RawRtspClient client = await RawRtspClient.ConnectAsync(camera.StreamUri, TestContext.Current.CancellationToken);
        await client.PlayAsync(TestContext.Current.CancellationToken);
        int frames = 0;

        while (await client.ReadFrameAsync(TestContext.Current.CancellationToken) is not null)
        {
            frames++;
        }

        Assert.Equal(Buddy3DFrames, frames);
        Assert.Equal(["OPTIONS", "DESCRIBE", "SETUP", "PLAY"], camera.Requests);
    }

    [Fact(Timeout = TimingTestTimeout)]
    public async Task CustomClipIsLoopedFromItsIdrFrame()
    {
        await using RtspCamera camera = new(new RtspCameraOptions { Clip = H264Clip.Parse(CraftedClip()), RealTime = false });
        await using RawRtspClient client = await RawRtspClient.ConnectAsync(camera.StreamUri, TestContext.Current.CancellationToken);
        await client.PlayAsync(TestContext.Current.CancellationToken);
        List<int[]> frames = [];

        for (int i = 0; i < 4; i++)
        {
            frames.Add([.. (await client.ReadFrameAsync(TestContext.Current.CancellationToken))!.Select(static p => p.NalType)]);
        }

        // The leading P frame cannot be decoded and is never sent.
        Assert.Equal([[7, 8, 5], [1], [7, 8, 5], [1]], frames);
    }

    [Fact(Timeout = TimingTestTimeout)]
    public async Task UdpTransportIsDeclined()
    {
        await using RtspCamera camera = new(new RtspCameraOptions { Clip = Buddy3D });
        await using RawRtspClient client = await RawRtspClient.ConnectAsync(camera.StreamUri, TestContext.Current.CancellationToken);

        RtspResponse response = await client.RequestAsync("SETUP", camera.StreamUri + "/track1", "Transport: RTP/AVP;unicast;client_port=5000-5001\r\n", TestContext.Current.CancellationToken);

        Assert.StartsWith("RTSP/1.0 461", response.Head, StringComparison.Ordinal);
    }

    [Fact(Timeout = TimingTestTimeout)]
    public async Task TeardownEndsTheSession()
    {
        await using RtspCamera camera = new(new RtspCameraOptions { Clip = Buddy3D });
        await using RawRtspClient client = await RawRtspClient.ConnectAsync(camera.StreamUri, TestContext.Current.CancellationToken);
        await client.PlayAsync(TestContext.Current.CancellationToken);
        await client.ReadFrameAsync(TestContext.Current.CancellationToken);

        // The answer may arrive between RTP packets; the client skips it.
        await client.SendRequestAsync("TEARDOWN", camera.StreamUri.ToString(), $"Session: {client.Session}\r\n", TestContext.Current.CancellationToken);
        while (await client.ReadPacketAsync(TestContext.Current.CancellationToken) is not null)
        {
        }

        await WaitUntilAsync(() => camera.ActiveSessions == 0);
        Assert.Contains("TEARDOWN", camera.Requests);
    }

    [Fact(Timeout = TimingTestTimeout)]
    public async Task OfflineCameraDropsClientsAndAcceptsThemAgainWhenOnline()
    {
        await using RtspCamera camera = new(new RtspCameraOptions { Clip = Buddy3D });
        RawRtspClient playing = await RawRtspClient.ConnectAsync(camera.StreamUri, TestContext.Current.CancellationToken);
        await using (playing)
        {
            await playing.PlayAsync(TestContext.Current.CancellationToken);
            await playing.ReadFrameAsync(TestContext.Current.CancellationToken);

            camera.SetOffline(true);

            await Assert.ThrowsAnyAsync<Exception>(async () =>
            {
                while (await playing.ReadPacketAsync(TestContext.Current.CancellationToken) is not null)
                {
                }

                throw new EndOfStreamException();
            });
        }

        await using (RawRtspClient refused = await RawRtspClient.ConnectAsync(camera.StreamUri, TestContext.Current.CancellationToken))
        {
            await Assert.ThrowsAnyAsync<Exception>(() => refused.PlayAsync(TestContext.Current.CancellationToken));
        }

        camera.SetOffline(false);
        await using RawRtspClient again = await RawRtspClient.ConnectAsync(camera.StreamUri, TestContext.Current.CancellationToken);
        await again.PlayAsync(TestContext.Current.CancellationToken);
        Assert.NotNull(await again.ReadFrameAsync(TestContext.Current.CancellationToken));
        Assert.False(camera.IsOffline);
    }

    [Fact(Timeout = TimingTestTimeout)]
    public async Task DisposingClosesClientsAndReleasesThePort()
    {
        RtspCamera camera = new(new RtspCameraOptions { Clip = Buddy3D });
        await using RawRtspClient client = await RawRtspClient.ConnectAsync(camera.StreamUri, TestContext.Current.CancellationToken);
        await client.PlayAsync(TestContext.Current.CancellationToken);
        await client.ReadFrameAsync(TestContext.Current.CancellationToken);

        await camera.DisposeAsync();

        await Assert.ThrowsAnyAsync<Exception>(async () =>
        {
            while (await client.ReadPacketAsync(TestContext.Current.CancellationToken) is not null)
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
        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        while (!condition())
        {
            await Task.Delay(20, timeout.Token);
        }
    }
}
