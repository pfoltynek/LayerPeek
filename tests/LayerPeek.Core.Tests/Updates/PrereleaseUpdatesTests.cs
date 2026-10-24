using LayerPeek.Core.Updates;

namespace LayerPeek.Core.Tests.Updates;

/// <summary>The effective "offer pre-release versions" choice (user decision 2026-10-24, T-017).</summary>
public class PrereleaseUpdatesTests
{
    [Theory]
    [InlineData("0.1.0")]
    [InlineData("0.12.3")]
    [InlineData("0.0.1")]
    public void ZeroVersionsOfferPrereleasesByDefault(string version)
    {
        Assert.True(PrereleaseUpdates.IsDefaultFor(version));
        Assert.True(PrereleaseUpdates.Offered(null, version));
    }

    [Theory]
    [InlineData("1.0.0")]
    [InlineData("1.2.3")]
    [InlineData("10.0.0")]
    public void ReleasedVersionsDoNotOfferPrereleasesByDefault(string version)
    {
        Assert.False(PrereleaseUpdates.IsDefaultFor(version));
        Assert.False(PrereleaseUpdates.Offered(null, version));
    }

    [Theory]
    [InlineData(true, "0.1.0")]
    [InlineData(false, "0.1.0")]
    [InlineData(true, "1.0.0")]
    [InlineData(false, "1.0.0")]
    [InlineData(true, "2.5.1")]
    [InlineData(false, "?")]
    public void AnExplicitChoiceAlwaysWins(bool choice, string version)
    {
        Assert.Equal(choice, PrereleaseUpdates.Offered(choice, version));
    }

    [Theory]
    [InlineData("?")]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("-1.0.0")]
    public void AnUnreadableVersionCountsAsAPreview(string version)
    {
        // The app reads its own version from the assembly ("?" when it has none): never a released 1.x.
        Assert.True(PrereleaseUpdates.Offered(null, version));
    }

    [Fact]
    public void VersionMustNotBeNull()
    {
        Assert.Throws<ArgumentNullException>(() => PrereleaseUpdates.Offered(null, null!));
    }
}
