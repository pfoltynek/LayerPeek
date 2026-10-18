using PrusaTrayDiagnostic.PrusaLink.WebProxy;

namespace PrusaTrayDiagnostic.PrusaLink.Tests.WebProxy;

public class ProxyTokenTests
{
    [Fact]
    public void TokenHas256BitsInBase64UrlWithoutPadding()
    {
        string value = ProxyToken.Create().Value;

        // 32 bytes -> 43 base64url characters.
        Assert.Equal(43, value.Length);
        Assert.All(value, static c => Assert.True(char.IsAsciiLetterOrDigit(c) || c is '-' or '_', $"Unexpected character '{c}'."));
    }

    [Fact]
    public void TokensAreUnique()
    {
        HashSet<string> values = [];
        for (int i = 0; i < 100; i++)
        {
            Assert.True(values.Add(ProxyToken.Create().Value));
        }
    }

    [Fact]
    public void OnlyTheExactTokenMatches()
    {
        ProxyToken token = ProxyToken.Create();
        string value = token.Value;
        char last = value[^1] == 'A' ? 'B' : 'A';

        Assert.True(token.Matches(value));
        Assert.False(token.Matches(string.Concat(value.AsSpan(0, value.Length - 1), [last])));
        Assert.False(token.Matches(value.AsSpan(0, value.Length - 1)));
        Assert.False(token.Matches(value + "A"));
        string upper = value.ToUpperInvariant();
        Assert.False(token.Matches(string.Equals(upper, value, StringComparison.Ordinal) ? value.ToLowerInvariant() : upper));
        Assert.False(token.Matches(string.Empty));
    }

    [Fact]
    public void NonAsciiCharactersNeverMatch()
    {
        ProxyToken token = ProxyToken.Create();
        // U+0141 truncated to a byte is 0x41 ('A'): without the ASCII check it would compare equal to 'A'.
        string value = token.Value;
        int index = value.IndexOf('A', StringComparison.Ordinal);
        string candidate = index >= 0
            ? string.Concat(value.AsSpan(0, index), "Ł", value.AsSpan(index + 1))
            : "Ł" + value[1..];

        Assert.False(token.Matches(candidate));
    }

    [Fact]
    public void TextDoesNotRevealTheToken()
    {
        ProxyToken token = ProxyToken.Create();

        Assert.DoesNotContain(token.Value, token.ToString(), StringComparison.Ordinal);
    }
}
