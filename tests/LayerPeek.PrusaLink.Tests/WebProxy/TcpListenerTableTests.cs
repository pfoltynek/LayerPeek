using System.Net;
using System.Net.Sockets;

using LayerPeek.Tests.Shared;

namespace LayerPeek.PrusaLink.Tests.WebProxy;

/// <summary>
/// The OS listener table the proxy and browser tests rely on (T-227): it must see a listening socket and nothing once it
/// is closed, also with the garbled row states of Windows 11 build 26300 (listeners reported as state 0, connections
/// and TIME_WAIT sockets as LISTEN).
/// </summary>
public sealed class TcpListenerTableTests
{
    [Fact]
    public void ALoopbackListenerIsListedWithItsAddress()
    {
        TcpListener listener = new(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;

            IPEndPoint listed = Assert.Single(TcpListenerTable.Listeners(port));
            Assert.Equal(new IPEndPoint(IPAddress.Loopback, port), listed);
            Assert.True(TcpListenerTable.IsListening(port));
        }
        finally
        {
            listener.Stop();
        }
    }

    [Fact]
    public void AnIPv6ListenerIsListed()
    {
        Assert.SkipUnless(Socket.OSSupportsIPv6, "IPv6 is not available on this machine.");
        TcpListener listener = new(IPAddress.IPv6Loopback, 0);
        listener.Start();
        try
        {
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;

            IPEndPoint listed = Assert.Single(TcpListenerTable.Listeners(port));
            Assert.Equal(IPAddress.IPv6Loopback, listed.Address);
        }
        finally
        {
            listener.Stop();
        }
    }

    [Fact]
    public async Task AClosedListenerIsNotListedEvenWithConnectionsLeftOnItsPort()
    {
        TcpListener listener = new(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        using (TcpClient client = new())
        {
            await client.ConnectAsync(IPAddress.Loopback, port, TestContext.Current.CancellationToken);
            // The server side closes first, so its socket on the listener's port stays in TIME_WAIT.
            using TcpClient accepted = await listener.AcceptTcpClientAsync(TestContext.Current.CancellationToken);
            accepted.Close();
            listener.Stop();
        }

        Assert.Empty(TcpListenerTable.Listeners(port));
        Assert.False(TcpListenerTable.IsListening(port));
    }
}
