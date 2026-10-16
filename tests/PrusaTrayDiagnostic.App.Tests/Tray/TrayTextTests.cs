using PrusaTrayDiagnostic.App.Tray;

namespace PrusaTrayDiagnostic.App.Tests.Tray;

public sealed class TrayTextTests
{
    [Theory]
    [InlineData("abc", 3, "abc")]
    [InlineData("abcd", 3, "ab…")]
    [InlineData("abcd", 1, "…")]
    [InlineData("abcd", 0, "")]
    public void EllipsizeRespectsMaximumLength(string text, int maxLength, string expected)
    {
        Assert.Equal(expected, TrayText.Ellipsize(text, maxLength));
    }
}
