using System.Buffers.Text;
using System.Security.Cryptography;

namespace LayerPeek.PrusaLink.WebProxy;

/// <summary>
/// The secret first path segment of a proxy address: 256 random bits, base64url without padding. Compared in constant
/// time. Never log <see cref="Value"/>.
/// </summary>
internal sealed class ProxyToken
{
    private const int RandomBytes = 32;

    private readonly byte[] _ascii;

    private ProxyToken(string value)
    {
        Value = value;
        _ascii = new byte[value.Length];
        for (int i = 0; i < value.Length; i++)
        {
            _ascii[i] = (byte)value[i];
        }
    }

    public string Value { get; }

    public static ProxyToken Create()
    {
        Span<byte> random = stackalloc byte[RandomBytes];
        RandomNumberGenerator.Fill(random);
        try
        {
            return new ProxyToken(Base64Url.EncodeToString(random));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(random);
        }
    }

    /// <summary>Constant-time comparison (for candidates of the right length; the length itself is not secret).</summary>
    public bool Matches(ReadOnlySpan<char> candidate)
    {
        if (candidate.Length != _ascii.Length)
        {
            return false;
        }

        Span<byte> bytes = stackalloc byte[candidate.Length];
        bool ascii = true;
        for (int i = 0; i < candidate.Length; i++)
        {
            char c = candidate[i];
            ascii &= c <= 0x7F;
            bytes[i] = (byte)c;
        }

        return CryptographicOperations.FixedTimeEquals(bytes, _ascii) & ascii;
    }

    /// <summary>Never the token itself.</summary>
    public override string ToString() => nameof(ProxyToken);
}
