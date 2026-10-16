using PrusaTrayDiagnostic.PrusaLink.Api;

namespace PrusaTrayDiagnostic.PrusaLink.Tests;

public class DigestParametersTests
{
    [Fact]
    public void FirmwareChallengeIsParsed()
    {
        List<(string Name, string Value)> parameters = DigestParameters.Parse("realm=\"Printer API\", nonce=\"3f2a9c1d00012e4b\", stale=false");

        Assert.Equal([("realm", "Printer API"), ("nonce", "3f2a9c1d00012e4b"), ("stale", "false")], parameters);
    }

    [Fact]
    public void CommaInsideQuotesIsPartOfTheValue()
    {
        Assert.Equal([("realm", "a, b"), ("nonce", "n")], DigestParameters.Parse("realm=\"a, b\",nonce=\"n\""));
    }

    [Fact]
    public void UnquotedValuesAndWhitespaceAreTolerated()
    {
        Assert.Equal([("stale", "TRUE"), ("algorithm", "MD5")], DigestParameters.Parse("  stale = TRUE ,algorithm=MD5"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("garbage")]
    [InlineData("=value")]
    public void InputWithoutParametersYieldsNothing(string input)
    {
        Assert.Empty(DigestParameters.Parse(input));
    }

    [Fact]
    public void UnterminatedQuoteStopsParsing()
    {
        Assert.Equal([("realm", "ok")], DigestParameters.Parse("realm=\"ok\", nonce=\"unterminated"));
    }
}
