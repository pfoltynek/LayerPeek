using PrusaTrayDiagnostic.Core.Secrets;

namespace PrusaTrayDiagnostic.Core.Tests.Secrets;

public class SecretKeysTests
{
    [Fact]
    public void ForPrinterIsStableAndScopedToPrinter()
    {
        Assert.Equal("printer/mk4", SecretKeys.ForPrinter("mk4"));
        Assert.NotEqual(SecretKeys.ForPrinter("mk4"), SecretKeys.ForPrinter("xl"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void ForPrinterRejectsEmptyId(string? printerId)
    {
        Assert.ThrowsAny<ArgumentException>(() => SecretKeys.ForPrinter(printerId!));
    }
}
