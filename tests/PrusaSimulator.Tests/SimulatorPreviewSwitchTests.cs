namespace LayerPeek.Simulator.Tests;

/// <summary>The switch between job files with and without G-code previews (T-086): command line and console.</summary>
public sealed class SimulatorPreviewSwitchTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void PreviewsAreOnByDefaultAndOffWithTheOption()
    {
        Assert.True(SimulatorOptions.TryParse([], out SimulatorOptions defaults, out _));
        Assert.True(SimulatorOptions.TryParse(["--no-previews", "--printers", "2"], out SimulatorOptions options, out string? error));

        Assert.True(defaults.Previews);
        Assert.Null(error);
        Assert.False(options.Previews);
        Assert.Equal(2, options.Printers);
        Assert.Contains("--no-previews", SimulatorOptions.Usage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task HostPassesTheOptionAndConsoleCommandsSwitchIt()
    {
        await using SimulatorHost host = await SimulatorHost.StartAsync(
            new SimulatorOptions { Printers = 2, FirstPort = 0, Camera = false, Previews = false },
            Ct);
        using StringWriter output = new();
        bool initiallyOff = !host.Printers[0].PreviewsEnabled && !host.Printers[1].PreviewsEnabled;

        Assert.True(ConsoleCommands.Execute(host, "all previews", output));
        bool allOn = host.Printers[0].PreviewsEnabled && host.Printers[1].PreviewsEnabled;
        Assert.True(ConsoleCommands.Execute(host, "2 no-previews", output));

        Assert.True(initiallyOff);
        Assert.True(allOn);
        Assert.True(host.Printers[0].PreviewsEnabled);
        Assert.False(host.Printers[1].PreviewsEnabled);
        Assert.Contains("(no previews)", output.ToString(), StringComparison.Ordinal);
    }
}
