using System.Text;

using PrusaTrayDiagnostic.App.SingleInstance;

namespace PrusaTrayDiagnostic.App.Tests.SingleInstance;

/// <summary>The wire format of the instance channel (T-043) and the Debug-only test commands of the end-to-end tests (T-044).</summary>
public sealed class InstanceProtocolTests
{
    [Fact]
    public void ActivateRoundTrips()
    {
        byte[] wire = InstanceProtocol.Encode(InstanceCommand.Activate);

        Assert.Equal("activate\n", Encoding.ASCII.GetString(wire));
        Assert.True(InstanceProtocol.TryDecode(wire, out InstanceCommand decoded));
        Assert.Equal(InstanceCommand.Activate, decoded);
    }

    [Fact]
    public void ActivateTakesNoArgument()
    {
        Assert.False(InstanceProtocol.TryDecode("activate:x"u8, out _));
        Assert.Throws<ArgumentOutOfRangeException>(() => InstanceProtocol.Encode(new InstanceCommand(InstanceCommandKind.Activate, "x")));
    }

#if DEBUG
    [Theory]
    [InlineData("settings", "Settings", null)]
    [InlineData("diagnostics", "Diagnostics", null)]
    [InlineData("diagnostics:e2e-printer-1", "Diagnostics", "e2e-printer-1")]
    [InlineData("about", "About", null)]
    [InlineData("camera:3f1c2b0a9d8e4f6a8b7c6d5e4f3a2b1c", "Camera", "3f1c2b0a9d8e4f6a8b7c6d5e4f3a2b1c")]
    [InlineData("quit", "Quit", null)]
    [InlineData("crash:ui", "Crash", "ui")]
    [InlineData("crash:background", "Crash", "background")]
    [InlineData("crash:task", "Crash", "task")]
    public void TestCommandsRoundTripInDebugBuilds(string text, string kind, string? argument)
    {
        Assert.True(InstanceProtocol.TryDecode(Encoding.ASCII.GetBytes(text + "\n"), out InstanceCommand decoded));

        Assert.Equal(new InstanceCommand(Enum.Parse<InstanceCommandKind>(kind), argument), decoded);
        Assert.Equal(text + "\n", Encoding.ASCII.GetString(InstanceProtocol.Encode(decoded)));
        Assert.Equal(text, decoded.ToString());
    }

    [Theory]
    [InlineData("camera")]
    [InlineData("camera:")]
    [InlineData("camera:a b")]
    [InlineData("camera:a/b")]
    [InlineData("settings:x")]
    [InlineData("about:x")]
    [InlineData("quit:x")]
    [InlineData("crash")]
    [InlineData("crash:")]
    [InlineData("Settings")]
    [InlineData("diagnostics:é")]
    public void MalformedTestCommandsAreRejected(string text)
    {
        Assert.False(InstanceProtocol.TryDecode(Encoding.UTF8.GetBytes(text), out _));
    }

    [Fact]
    public void TooLongCommandCannotBeEncoded()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => InstanceProtocol.Encode(new InstanceCommand(InstanceCommandKind.Camera, new string('a', 64))));
    }
#else
    [Theory]
    [InlineData("settings")]
    [InlineData("camera:x")]
    [InlineData("quit")]
    [InlineData("crash:ui")]
    public void ReleaseBuildsKnowOnlyActivate(string text)
    {
        Assert.False(InstanceProtocol.TryDecode(Encoding.ASCII.GetBytes(text), out _));
    }
#endif
}
