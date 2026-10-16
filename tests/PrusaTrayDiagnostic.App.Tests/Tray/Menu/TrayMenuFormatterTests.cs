using PrusaTrayDiagnostic.App.Tray.Menu;
using PrusaTrayDiagnostic.Core.Printers;

namespace PrusaTrayDiagnostic.App.Tests.Tray.Menu;

public sealed class TrayMenuFormatterTests : IDisposable
{
    private readonly CultureScope _culture = new("en-US");

    public void Dispose() => _culture.Dispose();

    [Theory]
    [InlineData(null, null, "MK4: Connecting…")]
    [InlineData(PrinterState.Idle, null, "MK4: Ready")]
    [InlineData(PrinterState.Printing, 0, "MK4: Printing – 0%")]
    [InlineData(PrinterState.Printing, 100, "MK4: Printing – 100%")]
    [InlineData(PrinterState.Unauthorized, null, "MK4: Invalid credentials")]
    [InlineData((PrinterState)999, null, "MK4: Unknown state")]
    public void PrinterHeaderShowsNameStateAndProgress(PrinterState? state, int? progress, string expected)
    {
        Assert.Equal(expected, TrayMenuFormatter.PrinterHeader("MK4", new PrinterMenuContent(state, progress)));
    }

    [Fact]
    public void ShorteningNeverSplitsSurrogatePair()
    {
        // The emoji (two UTF-16 units) straddles the cut position.
        string name = new string('a', TrayMenuFormatter.MaxHeaderLength - 20) + "\U0001F600" + new string('b', 50);

        string header = TrayMenuFormatter.PrinterHeader(name, new PrinterMenuContent(PrinterState.Idle, null));

        Assert.True(header.Length <= TrayMenuFormatter.MaxHeaderLength);
        Assert.EndsWith(": Ready", header, StringComparison.Ordinal);
        for (int i = 0; i < header.Length; i++)
        {
            Assert.False(char.IsHighSurrogate(header[i]) && (i + 1 == header.Length || !char.IsLowSurrogate(header[i + 1])), "Broken surrogate pair.");
        }
    }

    [Theory]
    [InlineData("MK4", "MK4")]
    [InlineData("my_printer", "my__printer")]
    [InlineData("_a__b_", "__a____b__")]
    [InlineData("", "")]
    public void AccessKeyMarkersAreEscaped(string text, string expected)
    {
        Assert.Equal(expected, NativeMenuText.EscapeAccessKeys(text));
    }

    [Fact]
    public void TextWithoutUnderscoreIsReturnedAsIs()
    {
        string text = "Printer";

        Assert.Same(text, NativeMenuText.EscapeAccessKeys(text));
    }
}
