using System.Text;

using LayerPeek.App.Notifications;
using LayerPeek.App.SingleInstance;

namespace LayerPeek.App.Tests.SingleInstance;

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

    [Theory]
    [InlineData("printer=xl")]
    [InlineData("printer=3f1c2b0a9d8e4f6a8b7c6d5e4f3a2b1c")]
    [InlineData("printer=MK4%20%C5%BElut%C3%BD%20%26%20%22co%22")]
    [InlineData("printer=a-b_c.d~e")]
    public void ToastActivationsRoundTripInEveryBuild(string launchArgument)
    {
        Assert.True(InstanceCommand.TryForToast(launchArgument, out InstanceCommand command));
        byte[] wire = InstanceProtocol.Encode(command);

        Assert.Equal("toast:" + launchArgument + "\n", Encoding.ASCII.GetString(wire));
        Assert.True(InstanceProtocol.TryDecode(wire, out InstanceCommand decoded));
        Assert.Equal(new InstanceCommand(InstanceCommandKind.Toast, launchArgument), decoded);
    }

    [Fact]
    public void TheLongestToastArgumentFitsIntoOneMessage()
    {
        Assert.True(ToastLaunchArgument.TryForPrinter(new string('%', 168), out string? longest));
        Assert.True(longest.Length > 500);

        Assert.True(InstanceCommand.TryForToast(longest, out InstanceCommand command));
        byte[] wire = InstanceProtocol.Encode(command);

        Assert.True(wire.Length <= InstanceProtocol.MaxMessageLength);
        Assert.True(InstanceProtocol.TryDecode(wire, out InstanceCommand decoded));
        Assert.Equal(longest, decoded.Argument);
    }

    [Theory]
    [InlineData("toast")]
    [InlineData("toast:")]
    [InlineData("toast:xl")]
    [InlineData("toast:printer=")]
    [InlineData("toast:printer=a b")]
    [InlineData("toast:printer=a&b")]
    [InlineData("toast:printer=a/b")]
    [InlineData("toast:printer=a:b")]
    [InlineData("toast:printer=https://evil.example")]
    [InlineData("toast:printer=é")]
    [InlineData("toast:url=http%3A%2F%2Fevil")]
    [InlineData("Toast:printer=xl")]
    public void MalformedToastActivationsAreRejected(string text)
    {
        Assert.False(InstanceProtocol.TryDecode(Encoding.UTF8.GetBytes(text), out _));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("xl")]
    [InlineData("printer=")]
    [InlineData("printer=%20")]
    [InlineData("printer=a b")]
    [InlineData("printer=https://evil.example")]
    public void OnlyWellFormedLaunchArgumentsBecomeToastCommands(string? launchArgument)
    {
        Assert.False(InstanceCommand.TryForToast(launchArgument, out _));
        Assert.Throws<ArgumentOutOfRangeException>(() => InstanceProtocol.Encode(new InstanceCommand(InstanceCommandKind.Toast, launchArgument)));
    }

    [Fact]
    public void ToastArgumentLongerThanTheLaunchArgumentLimitIsRejected()
    {
        string tooLong = "printer=" + new string('a', ToastLaunchArgument.MaxLength);

        Assert.False(InstanceCommand.TryForToast(tooLong, out _));
        Assert.False(InstanceProtocol.TryDecode(Encoding.ASCII.GetBytes("toast:" + tooLong + "\n"), out _));
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
        Assert.Throws<ArgumentOutOfRangeException>(() => InstanceProtocol.Encode(new InstanceCommand(InstanceCommandKind.Camera, new string('a', InstanceProtocol.MaxMessageLength))));
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
