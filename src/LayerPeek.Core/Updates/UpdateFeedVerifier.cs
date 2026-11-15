using System.Buffers;
using System.Buffers.Text;
using System.Security.Cryptography;

namespace LayerPeek.Core.Updates;

/// <summary>What <see cref="UpdateFeedVerifier.Verify"/> found.</summary>
public enum UpdateFeedSignatureCheck
{
    /// <summary>A trusted key signed exactly these feed bytes for this channel.</summary>
    Valid,

    /// <summary>The signature file is empty, too long or not one base64 signature of <see cref="UpdateFeedVerifier.SignatureLength"/> bytes.</summary>
    Malformed,

    /// <summary>
    /// A well-formed signature that no trusted key made over these bytes and this channel: the feed was changed, another key
    /// signed it, or it is the feed of another channel. The three are indistinguishable by design.
    /// </summary>
    Untrusted,
}

/// <summary>
/// Verifies the signature of a Velopack update feed (<c>releases.&lt;RID&gt;.json</c>, T-181) against a fixed list of trusted
/// ECDSA P-256 public keys (more than one only while a key is rotated). Pure crypto over bytes: no Velopack, no HTTP.
/// <list type="bullet">
/// <item><b>Signed data:</b> <c>"LayerPeek-feed-v1\n" + channel + "\n" + feed bytes</c> (ASCII prefix, the exact bytes of the
/// feed file), so that the signature of one RID's feed is worthless for another RID or any other purpose.</item>
/// <item><b>Signature file</b> (<c>releases.&lt;RID&gt;.json.sig</c>): the ECDSA P-256 / SHA-256 signature in the IEEE P1363
/// format (r ‖ s, 64 bytes; the default of .NET <c>ECDsa.SignData</c>) as one line of standard base64 (88 characters); ASCII
/// whitespace around it is ignored (a trailing newline of PowerShell), anything else is <see cref="UpdateFeedSignatureCheck.Malformed"/>.</item>
/// </list>
/// Thread-safe and allocation-light: the keys are kept as SubjectPublicKeyInfo bytes and imported per verification (a check
/// runs about once a day), so no native key handle lives between checks.
/// </summary>
public sealed class UpdateFeedVerifier
{
    /// <summary>The domain prefix of the signed data, version 1 of the format.</summary>
    public const string Domain = "LayerPeek-feed-v1";

    /// <summary>The bytes of an IEEE P1363 ECDSA P-256 signature.</summary>
    public const int SignatureLength = 64;

    /// <summary>Longest signature file read at all (88 base64 characters and some whitespace); anything longer is malformed.</summary>
    public const int MaxSignatureFileBytes = 256;

    /// <summary>Longest channel (RID) accepted, e.g. <c>osx-arm64</c>.</summary>
    public const int MaxChannelLength = 64;

    private const string P256Oid = "1.2.840.10045.3.1.7";
    private const int Base64SignatureLength = 88;

    private static readonly SearchValues<char> ChannelCharacters =
        SearchValues.Create("ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-._");

    private readonly byte[][] _keys;

    /// <summary><see cref="Domain"/> and the newline after it.</summary>
    private static ReadOnlySpan<byte> Prefix => "LayerPeek-feed-v1\n"u8;

    /// <param name="trustedPublicKeysPem">
    /// The trusted keys, each a PEM <c>PUBLIC KEY</c> (SubjectPublicKeyInfo) of an ECDSA P-256 key. An empty list trusts nothing.
    /// </param>
    /// <exception cref="ArgumentException">A key is no P-256 public key in PEM.</exception>
    public UpdateFeedVerifier(IEnumerable<string> trustedPublicKeysPem)
    {
        ArgumentNullException.ThrowIfNull(trustedPublicKeysPem);
        List<byte[]> keys = [];
        foreach (string pem in trustedPublicKeysPem)
        {
            keys.Add(SubjectPublicKeyInfoOf(pem));
        }

        _keys = [.. keys];
    }

    /// <summary>How many keys are trusted.</summary>
    public int TrustedKeyCount => _keys.Length;

    /// <summary>
    /// Whether <paramref name="signatureFile"/> is a signature of a trusted key over <paramref name="feed"/> for
    /// <paramref name="channel"/>. Never throws for any content of <paramref name="feed"/> or <paramref name="signatureFile"/>.
    /// </summary>
    /// <param name="feed">The exact bytes of the downloaded feed file.</param>
    /// <param name="channel">The channel (RID) the feed was asked for, e.g. <c>osx-arm64</c>.</param>
    /// <param name="signatureFile">The exact bytes of the downloaded signature file.</param>
    /// <exception cref="ArgumentException"><paramref name="channel"/> is no RID (empty, too long, other than <c>[A-Za-z0-9._-]</c>).</exception>
    public UpdateFeedSignatureCheck Verify(ReadOnlySpan<byte> feed, string channel, ReadOnlySpan<byte> signatureFile)
    {
        ValidateChannel(channel);
        Span<byte> signature = stackalloc byte[SignatureLength];
        if (!TryReadSignature(signatureFile, signature))
        {
            return UpdateFeedSignatureCheck.Malformed;
        }

        Span<byte> hash = stackalloc byte[SHA256.HashSizeInBytes];
        HashSignedData(feed, channel, hash);
        foreach (byte[] key in _keys)
        {
            if (VerifyWith(key, hash, signature))
            {
                return UpdateFeedSignatureCheck.Valid;
            }
        }

        return UpdateFeedSignatureCheck.Untrusted;
    }

    /// <summary>
    /// The data a signature covers (<see cref="Domain"/>, the channel, the feed bytes); for the signing tools and tests. The
    /// verification itself hashes the parts without copying the feed.
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="channel"/> is no RID.</exception>
    public static byte[] SignedDataOf(ReadOnlySpan<byte> feed, string channel)
    {
        ValidateChannel(channel);
        ReadOnlySpan<byte> prefix = Prefix;
        byte[] data = new byte[prefix.Length + channel.Length + 1 + feed.Length];
        prefix.CopyTo(data);
        int at = prefix.Length;
        foreach (char c in channel)
        {
            data[at++] = (byte)c;
        }

        data[at++] = (byte)'\n';
        feed.CopyTo(data.AsSpan(at));
        return data;
    }

    private static void HashSignedData(ReadOnlySpan<byte> feed, string channel, Span<byte> hash)
    {
        using IncrementalHash sha256 = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        sha256.AppendData(Prefix);
        Span<byte> channelBytes = stackalloc byte[MaxChannelLength + 1];
        for (int i = 0; i < channel.Length; i++)
        {
            // ASCII only (ValidateChannel).
            channelBytes[i] = (byte)channel[i];
        }

        channelBytes[channel.Length] = (byte)'\n';
        sha256.AppendData(channelBytes[..(channel.Length + 1)]);
        sha256.AppendData(feed);
        sha256.GetHashAndReset(hash);
    }

    private static bool VerifyWith(byte[] subjectPublicKeyInfo, ReadOnlySpan<byte> hash, ReadOnlySpan<byte> signature)
    {
        try
        {
            using ECDsa key = ECDsa.Create();
            key.ImportSubjectPublicKeyInfo(subjectPublicKeyInfo, out _);
            return key.VerifyHash(hash, signature, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        }
        catch (CryptographicException)
        {
            // Never seen: the key was checked when it was trusted, and the signature has the fixed length.
            return false;
        }
    }

    /// <summary>One base64 signature of <see cref="SignatureLength"/> bytes, ASCII whitespace around it ignored.</summary>
    private static bool TryReadSignature(ReadOnlySpan<byte> file, Span<byte> signature)
    {
        if (file.Length is 0 or > MaxSignatureFileBytes)
        {
            return false;
        }

        ReadOnlySpan<byte> text = file.Trim(" \t\r\n"u8);
        return text.Length == Base64SignatureLength
            && Base64.DecodeFromUtf8(text, signature, out int consumed, out int written) == OperationStatus.Done
            && consumed == Base64SignatureLength
            && written == SignatureLength;
    }

    /// <summary>The SubjectPublicKeyInfo of one PEM <c>PUBLIC KEY</c> of P-256; a private key or another curve is refused.</summary>
    private static byte[] SubjectPublicKeyInfoOf(string pem)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pem);
        try
        {
            if (!PemEncoding.TryFind(pem, out PemFields fields) || !pem.AsSpan()[fields.Label].SequenceEqual("PUBLIC KEY"))
            {
                throw new ArgumentException("A trusted update feed key must be a PEM 'PUBLIC KEY'.", nameof(pem));
            }

            byte[] subjectPublicKeyInfo = Convert.FromBase64String(pem[fields.Base64Data]);
            using ECDsa key = ECDsa.Create();
            key.ImportSubjectPublicKeyInfo(subjectPublicKeyInfo, out int read);
            if (read != subjectPublicKeyInfo.Length || key.ExportParameters(includePrivateParameters: false).Curve.Oid?.Value != P256Oid)
            {
                throw new ArgumentException("A trusted update feed key must be an ECDSA P-256 key.", nameof(pem));
            }

            return subjectPublicKeyInfo;
        }
        catch (CryptographicException ex)
        {
            throw new ArgumentException("A trusted update feed key must be an ECDSA P-256 public key.", nameof(pem), ex);
        }
    }

    private static void ValidateChannel(string channel)
    {
        ArgumentException.ThrowIfNullOrEmpty(channel);
        if (channel.Length > MaxChannelLength || channel.AsSpan().ContainsAnyExcept(ChannelCharacters))
        {
            throw new ArgumentException("The update channel must be a RID such as 'osx-arm64'.", nameof(channel));
        }
    }
}
