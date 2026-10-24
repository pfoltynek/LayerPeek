using LayerPeek.App.Tray.Menu;
using LayerPeek.Core.Printers;

namespace LayerPeek.App.Tests.Tray.Menu;

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
    public void LongNameIsShortenedToAboutThirtyTwoCharactersAndTheStateIsKept()
    {
        string name = new('a', 100);

        string header = TrayMenuFormatter.PrinterHeader(name, new PrinterMenuContent(PrinterState.Attention, 99));

        Assert.Equal(32, TrayMenuFormatter.MaxNameLength);
        Assert.Equal(new string('a', 31) + "…: Needs attention – 99%", header);
        Assert.Equal(name, TrayMenuFormatter.PrinterToolTip(name));
        Assert.Null(TrayMenuFormatter.PrinterToolTip(new string('a', 32)));
    }

    [Fact]
    public void ShorteningNeverSplitsSurrogatePair()
    {
        // The emoji (two UTF-16 units) straddles the cut position.
        string name = new string('a', TrayMenuFormatter.MaxNameLength - 2) + "\U0001F600" + new string('b', 50);

        string header = TrayMenuFormatter.PrinterHeader(name, new PrinterMenuContent(PrinterState.Idle, null));

        Assert.Equal(new string('a', TrayMenuFormatter.MaxNameLength - 2) + "…: Ready", header);
        for (int i = 0; i < header.Length; i++)
        {
            Assert.False(char.IsHighSurrogate(header[i]) && (i + 1 == header.Length || !char.IsLowSurrogate(header[i + 1])), "Broken surrogate pair.");
        }
    }

    [Theory]
    [InlineData("MK4", "MK4")]
    [InlineData("my_printer", "my_|__printer")]
    [InlineData("_a__b_", "_|__a____b__")]
    [InlineData("", "")]
    public void AccessKeyMarkersAreEscaped(string text, string expected)
    {
        // "|" stands for the invisible word joiner that becomes the access key nobody can type (Avalonia 12 workaround).
        Assert.Equal(expected.Replace("|", NativeMenuText.NoAccessKey.ToString(), StringComparison.Ordinal), NativeMenuText.EscapeAccessKeys(text));
        Assert.Equal(0x2060, NativeMenuText.NoAccessKey);
    }

    [Theory]
    [InlineData("_Settings…", 'S', "Settings…")]
    [InlineData("U_končit", 'k', "Ukončit")]
    [InlineData("MK4__dílna", '_', "MK4_dílna")]
    [InlineData("a__b_c_d", '_', "a_bc_d")]
    [InlineData("trailing_", null, "trailing_")]
    [InlineData("Printer", null, "Printer")]
    [InlineData("", null, "")]
    public void AccessKeyAndDisplayTextFollowAvaloniaAccessText(string menuText, char? key, string display)
    {
        Assert.Equal(key, NativeMenuText.AccessKeyOf(menuText));
        Assert.Equal(display, NativeMenuText.DisplayTextOf(menuText));
    }

    [Theory]
    [InlineData("my_printer")]
    [InlineData("_a__b_")]
    [InlineData("__")]
    [InlineData("_")]
    public void EscapedTextHasNoAccessKeyAndShowsUnchanged(string text)
    {
        string escaped = NativeMenuText.EscapeAccessKeys(text);

        Assert.Null(NativeMenuText.AccessKeyOf(escaped));
        Assert.Equal(text, NativeMenuText.DisplayTextOf(escaped));
    }

    [Fact]
    public void HeaderOfEscapesOnlyPlainTexts()
    {
        Assert.Equal("A_" + NativeMenuText.NoAccessKey + "__B", NativeMenuText.HeaderOf(new TrayMenuEntry(TrayMenuCommand.None, "A_B", IsEnabled: true)));
        Assert.Equal("A_B", NativeMenuText.HeaderOf(new TrayMenuEntry(TrayMenuCommand.About, "A_B", IsEnabled: true) { HasAccessKey = true }));
    }

    [Fact]
    public void TextWithoutUnderscoreIsReturnedAsIs()
    {
        string text = "Printer";

        Assert.Same(text, NativeMenuText.EscapeAccessKeys(text));
    }
}
