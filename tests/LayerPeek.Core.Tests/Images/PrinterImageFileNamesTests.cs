using LayerPeek.Core.Images;

namespace LayerPeek.Core.Tests.Images;

public class PrinterImageFileNamesTests
{
    [Theory]
    [InlineData("3f2a9c0d4b1e4e6f8a7b6c5d4e3f2a1b", "3f2a9c0d4b1e4e6f8a7b6c5d4e3f2a1b.png")]
    [InlineData("MK4-Workshop", "mk4-workshop.png")]
    [InlineData("a", "a.png")]
    public void PlainIdsAreUsedInLowerCase(string id, string expected)
    {
        Assert.Equal(expected, PrinterImageFileNames.For(id));
    }

    [Theory]
    [InlineData("..")]
    [InlineData("../settings")]
    [InlineData("..\\..\\Windows")]
    [InlineData("/etc/passwd")]
    [InlineData("C:\\temp\\x")]
    [InlineData("a:b")]
    [InlineData("a*b?")]
    [InlineData("name.png")]
    [InlineData("with space")]
    [InlineData("tiskárna")]
    [InlineData("under_score")]
    [InlineData("con")]
    [InlineData("NUL")]
    [InlineData("Com1")]
    [InlineData("lpt9")]
    [InlineData("a\0b")]
    public void OtherIdsAreHashed(string id)
    {
        string name = PrinterImageFileNames.For(id);

        Assert.Matches("^h_[0-9a-f]{32}\\.png$", name);
    }

    [Fact]
    public void TooLongIdIsHashed()
    {
        Assert.Equal("x".PadRight(64, 'x') + ".png", PrinterImageFileNames.For(new string('x', 64)));
        Assert.Matches("^h_[0-9a-f]{32}\\.png$", PrinterImageFileNames.For(new string('x', 65)));
    }

    [Theory]
    [InlineData("MK4", "mk4")]
    [InlineData("Tiskárna", "TISKÁRNA")]
    [InlineData("a/B", "A/b")]
    public void IdsThatAreEqualIgnoringCaseShareTheName(string first, string second)
    {
        Assert.Equal(PrinterImageFileNames.For(first), PrinterImageFileNames.For(second));
    }

    [Fact]
    public void DifferentIdsGetDifferentNames()
    {
        string[] ids = ["a", "b", "a b", "a_b", "a/b", "a\\b", "..", ".", "con", "con1"];

        Assert.Equal(ids.Length, ids.Select(PrinterImageFileNames.For).Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void NamesNeverContainPathCharacters()
    {
        foreach (string id in new[] { "..", "a/../b", "\\\\server\\share", "x:", "\u2215fake-slash" })
        {
            string name = PrinterImageFileNames.For(id);

            Assert.Equal(name, Path.GetFileName(name));
            Assert.True(name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0);
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void EmptyIdIsRejected(string id)
    {
        Assert.Throws<ArgumentException>(() => PrinterImageFileNames.For(id));
    }
}
