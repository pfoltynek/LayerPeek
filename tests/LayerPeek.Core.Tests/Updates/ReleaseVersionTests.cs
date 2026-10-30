using LayerPeek.Core.Updates;

namespace LayerPeek.Core.Tests.Updates;

/// <summary>SemVer 2.0.0 versions of the release tags (T-058): parsing, the <c>v</c> prefix, invalid tags and precedence.</summary>
public sealed class ReleaseVersionTests
{
    [Theory]
    [InlineData("0.3.1", 0, 3, 1, "")]
    [InlineData("v0.3.1", 0, 3, 1, "")]
    [InlineData("V1.0.0", 1, 0, 0, "")]
    [InlineData("v1.2.3-beta.2", 1, 2, 3, "beta.2")]
    [InlineData("1.0.0-0.3.7", 1, 0, 0, "0.3.7")]
    [InlineData("1.0.0-x-y-z.--", 1, 0, 0, "x-y-z.--")]
    [InlineData("10.20.30", 10, 20, 30, "")]
    [InlineData("2147483647.0.0", int.MaxValue, 0, 0, "")]
    public void ValidVersionsAreRead(string text, int major, int minor, int patch, string prerelease)
    {
        Assert.True(ReleaseVersion.TryParse(text, out ReleaseVersion? version));
        Assert.Equal(major, version.Major);
        Assert.Equal(minor, version.Minor);
        Assert.Equal(patch, version.Patch);
        Assert.Equal(prerelease, string.Join('.', version.PrereleaseIdentifiers));
        Assert.Equal(prerelease.Length > 0, version.IsPrerelease);
    }

    [Fact]
    public void BuildMetadataIsKeptButNotCompared()
    {
        ReleaseVersion version = ReleaseVersion.Parse("v1.0.0-rc.1+build.5");

        Assert.Equal("build.5", version.Build);
        Assert.Equal("1.0.0-rc.1", version.ToString());
        Assert.Equal(ReleaseVersion.Parse("1.0.0-rc.1"), version);
        Assert.Equal(0, version.CompareTo(ReleaseVersion.Parse("1.0.0-rc.1+other")));
        Assert.Equal(ReleaseVersion.Parse("1.0.0-rc.1").GetHashCode(), version.GetHashCode());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("v")]
    [InlineData("1")]
    [InlineData("1.2")]
    [InlineData("1.2.3.4")]
    [InlineData("01.2.3")]
    [InlineData("1.02.3")]
    [InlineData("1.2.03")]
    [InlineData("1.2.3-")]
    [InlineData("1.2.3-01")]
    [InlineData("1.2.3-beta..1")]
    [InlineData("1.2.3+")]
    [InlineData("1.2.3-be ta")]
    [InlineData("1.2.3-béta")]
    [InlineData(" 1.2.3")]
    [InlineData("1.2.3 ")]
    [InlineData("vv1.2.3")]
    [InlineData("release-1.2.3")]
    [InlineData("-1.2.3")]
    [InlineData("1.-2.3")]
    [InlineData("2147483648.0.0")]
    [InlineData("latest")]
    [InlineData("nightly-2026-10-30")]
    public void InvalidTagsAreRefused(string? text)
    {
        Assert.False(ReleaseVersion.TryParse(text, out ReleaseVersion? version));
        Assert.Null(version);
    }

    [Fact]
    public void ATooLongTextIsRefused()
    {
        Assert.False(ReleaseVersion.TryParse("1.0.0-" + new string('a', ReleaseVersion.MaxLength), out _));
    }

    [Fact]
    public void ParseThrowsForAnInvalidText()
    {
        Assert.Throws<FormatException>(() => ReleaseVersion.Parse("1.2"));
    }

    /// <summary>The example of the SemVer 2.0.0 specification (item 11), in ascending order, plus 0.x versions.</summary>
    [Fact]
    public void PrecedenceFollowsTheSpecification()
    {
        string[] ascending =
        [
            "0.1.0",
            "0.2.0",
            "0.3.0",
            "0.3.1",
            "0.9.0",
            "0.10.0",
            "1.0.0-alpha",
            "1.0.0-alpha.1",
            "1.0.0-alpha.beta",
            "1.0.0-beta",
            "1.0.0-beta.2",
            "1.0.0-beta.11",
            "1.0.0-rc.1",
            "1.0.0",
            "1.0.1",
            "1.1.0",
            "2.0.0",
        ];

        for (int i = 0; i < ascending.Length - 1; i++)
        {
            ReleaseVersion lower = ReleaseVersion.Parse(ascending[i]);
            ReleaseVersion higher = ReleaseVersion.Parse(ascending[i + 1]);
            Assert.True(lower < higher, $"{lower} < {higher}");
            Assert.True(higher > lower, $"{higher} > {lower}");
            Assert.True(lower.CompareTo(higher) < 0);
            Assert.True(higher.CompareTo(lower) > 0);
            Assert.NotEqual(lower, higher);
        }
    }

    [Fact]
    public void ThePrefixMakesNoDifference()
    {
        Assert.Equal(ReleaseVersion.Parse("v0.3.1"), ReleaseVersion.Parse("0.3.1"));
        Assert.True(ReleaseVersion.Parse("v0.3.1") == ReleaseVersion.Parse("0.3.1"));
        Assert.Equal("0.3.1", ReleaseVersion.Parse("v0.3.1").ToString());
    }

    [Fact]
    public void NumericIdentifiersCompareNumericallyWhateverTheirLength()
    {
        Assert.True(ReleaseVersion.Parse("1.0.0-rc.99999999999999999999") > ReleaseVersion.Parse("1.0.0-rc.9"));
        Assert.True(ReleaseVersion.Parse("1.0.0-rc.10") > ReleaseVersion.Parse("1.0.0-rc.9"));
        // Numeric identifiers rank below alphanumeric ones.
        Assert.True(ReleaseVersion.Parse("1.0.0-1") < ReleaseVersion.Parse("1.0.0-a"));
    }

    [Fact]
    public void NullRanksBelowEveryVersion()
    {
        ReleaseVersion version = ReleaseVersion.Parse("0.0.0");

        Assert.Equal(1, version.CompareTo(null));
        Assert.True(version > null);
        Assert.False(version < null);
        Assert.True((ReleaseVersion?)null < version);
        Assert.False(version.Equals(null));
    }
}
