using System.Net;

using PrusaTrayDiagnostic.Simulator.Printers;

namespace PrusaTrayDiagnostic.Simulator.Tests;

/// <summary>Command line, console commands and the whole simulator (ports chosen by the OS).</summary>
public sealed class SimulatorHostTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void DefaultsAreOnePrintingCoreOneWithCamera()
    {
        Assert.True(SimulatorOptions.TryParse([], out SimulatorOptions options, out string? error));

        Assert.Null(error);
        Assert.Equal(1, options.Printers);
        Assert.Equal(8080, options.PortOf(0));
        Assert.Equal(SimulatedModel.CoreOne, options.ModelOf(0));
        Assert.Equal((PrinterScenario.Printing, false), options.ScenarioOf(0));
        Assert.True(options.Camera);
        Assert.Equal(8554, options.CameraPort);
        Assert.Equal("maker", options.UserName);
        Assert.Equal("simulator", options.Password);
        Assert.Equal(IPAddress.Loopback, options.Address);
    }

    [Fact]
    public void EveryScenarioNameRoundTrips()
    {
        // T-064 added "malformed"; "offline" is an idle printer that starts offline.
        Assert.Contains("malformed", PrinterScenarios.Names);
        foreach (string name in PrinterScenarios.Names)
        {
            Assert.True(PrinterScenarios.TryParse(name, out PrinterScenario scenario, out bool offline), name);
            Assert.Equal(offline ? "idle" : name, PrinterScenarios.Format(scenario));
            Assert.Equal(name == PrinterScenarios.OfflineName, offline);
        }

        Assert.True(SimulatorOptions.TryParse(["--scenario", "malformed"], out SimulatorOptions options, out string? error), error);
        Assert.Equal((PrinterScenario.Malformed, false), options.ScenarioOf(0));
    }

    [Fact]
    public void AllOptionsAreParsed()
    {
        Assert.True(SimulatorOptions.TryParse(
            [
                "--printers", "4", "--port", "9000", "--models", "mini,xl", "--scenario", "idle,offline,flaky",
                "--speed", "10", "--print-minutes", "2.5", "--user", "operator", "--password", "secret", "--host", "0.0.0.0",
                "--offline-mode", "hang", "--camera-port", "9554", "--clip", "my.h264", "--fps", "30",
            ],
            out SimulatorOptions options,
            out string? error), error);

        Assert.Equal(4, options.Printers);
        Assert.Equal([9000, 9001, 9002, 9003], Enumerable.Range(0, 4).Select(options.PortOf));
        Assert.Equal([SimulatedModel.Mini, SimulatedModel.Xl, SimulatedModel.Xl, SimulatedModel.Xl], Enumerable.Range(0, 4).Select(options.ModelOf));
        Assert.Equal(
            [(PrinterScenario.Idle, false), (PrinterScenario.Idle, true), (PrinterScenario.Flaky, false), (PrinterScenario.Flaky, false)],
            Enumerable.Range(0, 4).Select(options.ScenarioOf));
        Assert.Equal(10, options.Speed);
        Assert.Equal(TimeSpan.FromMinutes(2.5), options.PrintDuration);
        Assert.Equal("operator", options.UserName);
        Assert.Equal("secret", options.Password);
        Assert.Equal(IPAddress.Any, options.Address);
        Assert.Equal(OfflineMode.Hang, options.OfflineMode);
        Assert.Equal(9554, options.CameraPort);
        Assert.Equal("my.h264", options.ClipPath);
        Assert.Equal(30, options.FramesPerSecond);
    }

    [Fact]
    public void ModelsRepeatInDefaultOrder()
    {
        Assert.True(SimulatorOptions.TryParse(["--printers", "5"], out SimulatorOptions options, out _));

        Assert.Equal(
            ["7.1.0", "1.4.1", "3.1.0", "2.1.0", "7.1.0"],
            Enumerable.Range(0, 5).Select(i => options.ModelOf(i).Code));
    }

    [Theory]
    [InlineData("--printers", "0")]
    [InlineData("--printers", "17")]
    [InlineData("--port", "70000")]
    [InlineData("--models", "mk3")]
    [InlineData("--scenario", "sleeping")]
    [InlineData("--speed", "0")]
    [InlineData("--offline-mode", "1")]
    [InlineData("--host", "printer.local")]
    [InlineData("--unknown", "x")]
    [InlineData("--port")]
    [InlineData("stray")]
    public void InvalidArgumentsAreRejected(params string[] args)
    {
        Assert.False(SimulatorOptions.TryParse(args, out _, out string? error));
        Assert.False(string.IsNullOrEmpty(error));
    }

    [Fact]
    public void PortsPastTheRangeOrCollidingWithTheCameraAreRejected()
    {
        Assert.False(SimulatorOptions.TryParse(["--port", "65535", "--printers", "2"], out _, out _));
        Assert.False(SimulatorOptions.TryParse(["--port", "8550", "--printers", "5"], out _, out _));
        Assert.True(SimulatorOptions.TryParse(["--port", "8550", "--printers", "5", "--no-camera"], out _, out _));
    }

    [Fact]
    public void HelpIsRecognized()
    {
        Assert.True(SimulatorOptions.TryParse(["--help"], out SimulatorOptions options, out _));
        Assert.True(options.ShowHelp);
        Assert.Contains("--scenario", SimulatorOptions.Usage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task HostStartsPrintersAndCameraAndReleasesEverything()
    {
        SimulatorOptions options = new()
        {
            Printers = 3,
            FirstPort = 0,
            CameraPort = 0,
            Scenarios = ["printing", "offline"],
            ClipPath = SimulatorHost.DefaultClipPath,
        };
        SimulatorHost host = await SimulatorHost.StartAsync(options, Ct);
        int[] ports;
        try
        {
            ports = [.. host.Printers.Select(static p => p.Port), host.Camera!.Port];

            Assert.Equal(4, ports.Distinct().Count());
            Assert.Equal(["7.1.0", "1.4.1", "3.1.0"], host.Printers.Select(static p => p.Model.Code));
            Assert.True(host.Printers[0].Simulation.IsOnline);
            Assert.False(host.Printers[1].Simulation.IsOnline);
            Assert.False(host.Printers[2].Simulation.IsOnline);
            using HttpClient client = TestPrinters.CreateHttpClient();
            using HttpResponseMessage response = await client.GetAsync(host.Printers[0].BaseUri, Ct);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
        finally
        {
            await host.DisposeAsync();
        }

        foreach (int port in ports)
        {
            using System.Net.Sockets.TcpListener listener = new(IPAddress.Loopback, port);
            listener.Start();
            listener.Stop();
        }
    }

    [Fact]
    public async Task MissingClipFailsTheStart()
    {
        SimulatorOptions options = new() { FirstPort = 0, CameraPort = 0, ClipPath = Path.Combine(AppContext.BaseDirectory, "Clips", "missing.h264") };

        await Assert.ThrowsAsync<FileNotFoundException>(() => SimulatorHost.StartAsync(options, Ct));
    }

    [Fact]
    public async Task FailedStartReleasesWhatWasAlreadyOpen()
    {
        // The printer port is taken; the camera (opened first) must be closed again.
        using System.Net.Sockets.TcpListener blocker = new(IPAddress.Loopback, 0);
        blocker.Start();
        int taken = ((IPEndPoint)blocker.LocalEndpoint).Port;
        int cameraPort = FreePort();
        SimulatorOptions options = new() { FirstPort = taken, CameraPort = cameraPort };

        await Assert.ThrowsAsync<IOException>(() => SimulatorHost.StartAsync(options, Ct));

        using System.Net.Sockets.TcpListener camera = new(IPAddress.Loopback, cameraPort);
        camera.Start();
        camera.Stop();
    }

    [Theory]
    [InlineData("localhost", "127.0.0.1")]
    [InlineData("::", "::")]
    [InlineData("::1", "::1")]
    public void HostAcceptsLocalhostAndIpv6(string value, string expected)
    {
        Assert.True(SimulatorOptions.TryParse(["--host", value], out SimulatorOptions options, out string? error), error);
        Assert.Equal(IPAddress.Parse(expected), options.Address);
    }

    private static int FreePort()
    {
        using System.Net.Sockets.TcpListener probe = new(IPAddress.Loopback, 0);
        probe.Start();
        int port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        return port;
    }

    [Fact]
    public async Task ConsoleCommandsSwitchPrintersAndCamera()
    {
        await using SimulatorHost host = await SimulatorHost.StartAsync(new SimulatorOptions { Printers = 2, FirstPort = 0, CameraPort = 0 }, Ct);
        using StringWriter output = new();

        Assert.True(ConsoleCommands.Execute(host, "2 paused", output));
        Assert.True(ConsoleCommands.Execute(host, "all offline", output));
        Assert.True(ConsoleCommands.Execute(host, "1 online", output));
        Assert.True(ConsoleCommands.Execute(host, "camera offline", output));
        Assert.True(ConsoleCommands.Execute(host, "3 idle", output));
        Assert.True(ConsoleCommands.Execute(host, "1 sleeping", output));
        Assert.True(ConsoleCommands.Execute(host, "list", output));
        Assert.False(ConsoleCommands.Execute(host, "quit", output));

        Assert.Equal(PrinterScenario.Printing, host.Printers[0].Simulation.Scenario);
        Assert.Equal(PrinterScenario.Paused, host.Printers[1].Simulation.Scenario);
        Assert.True(host.Printers[0].Simulation.IsOnline);
        Assert.False(host.Printers[1].Simulation.IsOnline);
        Assert.True(host.Camera!.IsOffline);
        string text = output.ToString();
        Assert.Contains("Unknown command", text, StringComparison.Ordinal);
        Assert.Contains("Unknown scenario 'sleeping'", text, StringComparison.Ordinal);
        Assert.Contains("camera: offline", text, StringComparison.Ordinal);
        Assert.Contains("PAUSED", text, StringComparison.Ordinal);
    }
}
