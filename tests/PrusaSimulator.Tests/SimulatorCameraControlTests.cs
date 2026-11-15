using System.Net;

using LayerPeek.Simulator.Camera;
using LayerPeek.Simulator.Printers;

namespace LayerPeek.Simulator.Tests;

/// <summary>
/// The control endpoints <c>POST /_simulator/camera/offline</c> and <c>/online</c> (T-210): the same switch as the console
/// command <c>camera offline</c> / <c>camera online</c>, with the login rules of the other control endpoints.
/// </summary>
public sealed class SimulatorCameraControlTests
{
    private const int TimingTestTimeout = 30_000;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact(Timeout = TimingTestTimeout)]
    public async Task CameraEndpointsDropClientsAndAcceptThemAgain()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using SimulatorHost host = await SimulatorHost.StartAsync(new SimulatorOptions { Printers = 2, FirstPort = 0, CameraPort = 0 }, ct);
        RtspCamera camera = host.Camera!;
        using HttpClient client = TestPrinters.CreateHttpClient();
        RawRtspClient playing = await RawRtspClient.ConnectAsync(camera.StreamUri, ct);
        await using (playing)
        {
            await playing.PlayAsync(ct);
            Assert.NotNull(await playing.ReadFrameAsync(ct));

            // Any printer of the simulator switches the one shared camera.
            using HttpResponseMessage offline = await client.PostAsync(new Uri(host.Printers[1].BaseUri, "_simulator/camera/offline"), null, ct);

            Assert.Equal(HttpStatusCode.OK, offline.StatusCode);
            Assert.Contains("\"camera_offline\":true", await offline.Content.ReadAsStringAsync(ct), StringComparison.Ordinal);
            Assert.True(camera.IsOffline);
            await Assert.ThrowsAnyAsync<Exception>(async () =>
            {
                while (await playing.ReadPacketAsync(ct) is not null)
                {
                }

                throw new EndOfStreamException();
            });
        }

        await using (RawRtspClient refused = await RawRtspClient.ConnectAsync(camera.StreamUri, ct))
        {
            await Assert.ThrowsAnyAsync<Exception>(() => refused.PlayAsync(ct));
        }

        string offlineState = await client.GetStringAsync(new Uri(host.Printers[0].BaseUri, "_simulator/state"), ct);
        using HttpResponseMessage online = await client.PostAsync(new Uri(host.Printers[0].BaseUri, "_simulator/camera/online"), null, ct);
        await using RawRtspClient again = await RawRtspClient.ConnectAsync(camera.StreamUri, ct);
        await again.PlayAsync(ct);

        Assert.Contains("\"camera_offline\":true", offlineState, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.OK, online.StatusCode);
        Assert.Contains("\"camera_offline\":false", await online.Content.ReadAsStringAsync(ct), StringComparison.Ordinal);
        Assert.False(camera.IsOffline);
        Assert.NotNull(await again.ReadFrameAsync(ct));
        // The printers are not touched.
        Assert.All(host.Printers, static p => Assert.True(p.Simulation.IsOnline));
    }

    [Fact]
    public async Task CameraEndpointsAnswerNotFoundWithoutACameraOrForOtherMethods()
    {
        await using SimulatorHost host = await SimulatorHost.StartAsync(new SimulatorOptions { FirstPort = 0, Camera = false }, Ct);
        using HttpClient client = TestPrinters.CreateHttpClient();
        Uri baseUri = host.Printers[0].BaseUri;

        using HttpResponseMessage offline = await client.PostAsync(new Uri(baseUri, "_simulator/camera/offline"), null, Ct);
        using HttpResponseMessage get = await client.GetAsync(new Uri(baseUri, "_simulator/camera/offline"), Ct);
        string state = await client.GetStringAsync(new Uri(baseUri, "_simulator/state"), Ct);

        Assert.Equal(HttpStatusCode.NotFound, offline.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, get.StatusCode);
        Assert.DoesNotContain("camera_offline", state, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CameraEndpointsNeedTheLoginWhenRequired()
    {
        await using RtspCamera camera = new(new RtspCameraOptions { Clip = H264Clip.Load(SimulatorHost.DefaultClipPath) });
        await using SimulatedPrinter printer = await SimulatedPrinter.StartAsync(
            new SimulatedPrinterOptions
            {
                Port = 0,
                Password = TestPrinters.Password,
                Timing = TestPrinters.Timing,
                ControlRequiresLogin = true,
                Camera = camera,
            },
            Ct);
        using HttpClient client = TestPrinters.CreateHttpClient();
        Uri uri = new(printer.BaseUri, "_simulator/camera/offline");

        using HttpResponseMessage anonymous = await client.PostAsync(uri, null, Ct);
        bool afterAnonymous = camera.IsOffline;
        using HttpRequestMessage withKey = new(HttpMethod.Post, uri);
        withKey.Headers.Add("X-Api-Key", TestPrinters.Password);
        using HttpResponseMessage authorized = await client.SendAsync(withKey, Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        Assert.False(afterAnonymous);
        Assert.Equal(HttpStatusCode.OK, authorized.StatusCode);
        Assert.True(camera.IsOffline);
    }
}
