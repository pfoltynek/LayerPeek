using System.Security.Cryptography;
using System.Text;

using LayerPeek.Core.Updates;
using LayerPeek.Tests.Shared;

namespace LayerPeek.Core.Tests.Updates;

/// <summary>
/// The signature check of the update feed (T-181) on the shared test vector (<see cref="TestUpdateFeed"/>, signed with
/// PowerShell 7): exactly the signed bytes of exactly this RID under a trusted key are valid; anything else is rejected
/// without an exception.
/// </summary>
public sealed class UpdateFeedVerifierTests
{
    private static readonly byte[] Feed = File.ReadAllBytes(TestUpdateFeed.FeedPath);
    private static readonly byte[] Signature = File.ReadAllBytes(TestUpdateFeed.SignaturePath);

    private readonly UpdateFeedVerifier _verifier = new([TestUpdateFeed.PublicKeyPem]);

    [Fact]
    public void TheSignatureOfPowerShellIsValid()
    {
        Assert.Equal(UpdateFeedSignatureCheck.Valid, _verifier.Verify(Feed, TestUpdateFeed.Rid, Signature));
    }

    [Fact]
    public void ASignatureMadeInDotNetIsValidToo()
    {
        Assert.Equal(UpdateFeedSignatureCheck.Valid, _verifier.Verify(Feed, TestUpdateFeed.Rid, TestUpdateFeed.Sign(Feed)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(100)]
    [InlineData(-1)]
    public void AFeedChangedInOneByteIsUntrusted(int index)
    {
        byte[] changed = [.. Feed];
        changed[index < 0 ? changed.Length + index : index] ^= 0x01;

        Assert.Equal(UpdateFeedSignatureCheck.Untrusted, _verifier.Verify(changed, TestUpdateFeed.Rid, Signature));
    }

    [Fact]
    public void AFeedWithOneByteMoreOrLessIsUntrusted()
    {
        Assert.Equal(UpdateFeedSignatureCheck.Untrusted, _verifier.Verify([.. Feed, (byte)'\n'], TestUpdateFeed.Rid, Signature));
        Assert.Equal(UpdateFeedSignatureCheck.Untrusted, _verifier.Verify(Feed.AsSpan(0, Feed.Length - 1), TestUpdateFeed.Rid, Signature));
    }

    [Fact]
    public void ASignatureOfAnotherKeyIsUntrusted()
    {
        using ECDsa other = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        Assert.Equal(UpdateFeedSignatureCheck.Untrusted, _verifier.Verify(Feed, TestUpdateFeed.Rid, TestUpdateFeed.Sign(Feed, key: other)));
    }

    [Theory]
    [InlineData("win-x64")]
    [InlineData("osx-x64")]
    [InlineData("OSX-ARM64")]
    public void TheSignatureOfTheFeedOfAnotherRidIsUntrusted(string rid)
    {
        // The same bytes signed for osx-arm64 never pass as the feed of another RID, and the other way round.
        Assert.Equal(UpdateFeedSignatureCheck.Untrusted, _verifier.Verify(Feed, rid, Signature));
        Assert.Equal(UpdateFeedSignatureCheck.Untrusted, _verifier.Verify(Feed, TestUpdateFeed.Rid, TestUpdateFeed.Sign(Feed, rid)));
    }

    [Fact]
    public void ASignatureOfTheBareFeedWithoutThePrefixIsUntrusted()
    {
        using ECDsa key = ECDsa.Create();
        key.ImportFromPem(File.ReadAllText(Path.Combine(TestUpdateFeed.Folder, "test-feed-key.private.pem")));
        byte[] bare = Encoding.ASCII.GetBytes(Convert.ToBase64String(key.SignData(Feed, HashAlgorithmName.SHA256)));

        Assert.Equal(UpdateFeedSignatureCheck.Untrusted, _verifier.Verify(Feed, TestUpdateFeed.Rid, bare));
    }

    public static TheoryData<string> MalformedSignatures => new()
    {
        "",
        " ",
        "\n",
        "not a signature",
        // One character short, one too many, invalid characters, whitespace inside.
        Encoding.ASCII.GetString(File.ReadAllBytes(TestUpdateFeed.SignaturePath)).Trim()[..87],
        Encoding.ASCII.GetString(File.ReadAllBytes(TestUpdateFeed.SignaturePath)).Trim() + "A",
        new string('*', 88),
        Encoding.ASCII.GetString(File.ReadAllBytes(TestUpdateFeed.SignaturePath)).Trim().Insert(44, " "),
        // Base64 of 63 and 65 bytes.
        Convert.ToBase64String(new byte[63]),
        Convert.ToBase64String(new byte[65]),
        // Twice the signature.
        Encoding.ASCII.GetString(File.ReadAllBytes(TestUpdateFeed.SignaturePath)).Trim() + "\n" + Encoding.ASCII.GetString(File.ReadAllBytes(TestUpdateFeed.SignaturePath)).Trim(),
    };

    [Theory]
    [MemberData(nameof(MalformedSignatures))]
    public void AMalformedSignatureFileIsRejected(string file)
    {
        Assert.Equal(UpdateFeedSignatureCheck.Malformed, _verifier.Verify(Feed, TestUpdateFeed.Rid, Encoding.ASCII.GetBytes(file)));
    }

    [Fact]
    public void ADerSignatureIsRejected()
    {
        using ECDsa key = ECDsa.Create();
        key.ImportFromPem(File.ReadAllText(Path.Combine(TestUpdateFeed.Folder, "test-feed-key.private.pem")));
        byte[] der = key.SignData(TestUpdateFeed.SignedData(Feed, TestUpdateFeed.Rid), HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);

        Assert.NotEqual(UpdateFeedSignatureCheck.Valid, _verifier.Verify(Feed, TestUpdateFeed.Rid, Encoding.ASCII.GetBytes(Convert.ToBase64String(der))));
    }

    [Fact]
    public void AnOversizedSignatureFileIsRejectedEvenWithAValidSignatureInIt()
    {
        byte[] padded = [.. Signature, .. Enumerable.Repeat((byte)' ', UpdateFeedVerifier.MaxSignatureFileBytes - Signature.Length + 1)];

        Assert.Equal(UpdateFeedSignatureCheck.Malformed, _verifier.Verify(Feed, TestUpdateFeed.Rid, padded));
        Assert.Equal(UpdateFeedSignatureCheck.Malformed, _verifier.Verify(Feed, TestUpdateFeed.Rid, new byte[1024 * 1024]));
    }

    [Fact]
    public void WhitespaceAroundTheSignatureIsIgnored()
    {
        string text = Encoding.ASCII.GetString(Signature).Trim();

        foreach (string file in new[] { text, text + "\r\n", "  " + text + "\t\n" })
        {
            Assert.Equal(UpdateFeedSignatureCheck.Valid, _verifier.Verify(Feed, TestUpdateFeed.Rid, Encoding.ASCII.GetBytes(file)));
        }
    }

    [Fact]
    public void RandomSignatureFilesNeverThrow()
    {
        Random random = new(181);
        for (int i = 0; i < 500; i++)
        {
            byte[] file = new byte[random.Next(0, 300)];
            random.NextBytes(file);
            Assert.NotEqual(UpdateFeedSignatureCheck.Valid, _verifier.Verify(Feed, TestUpdateFeed.Rid, file));

            // Well-formed base64 of 64 random bytes: a syntactically valid but wrong signature.
            byte[] signature = new byte[UpdateFeedVerifier.SignatureLength];
            random.NextBytes(signature);
            Assert.Equal(UpdateFeedSignatureCheck.Untrusted, _verifier.Verify(Feed, TestUpdateFeed.Rid, Encoding.ASCII.GetBytes(Convert.ToBase64String(signature))));
        }
    }

    [Fact]
    public void AnEmptyFeedCanBeVerifiedToo()
    {
        Assert.Equal(UpdateFeedSignatureCheck.Valid, _verifier.Verify([], TestUpdateFeed.Rid, TestUpdateFeed.Sign([])));
        Assert.Equal(UpdateFeedSignatureCheck.Untrusted, _verifier.Verify([], TestUpdateFeed.Rid, Signature));
    }

    [Fact]
    public void NoTrustedKeyTrustsNothing()
    {
        UpdateFeedVerifier none = new([]);

        Assert.Equal(0, none.TrustedKeyCount);
        Assert.Equal(UpdateFeedSignatureCheck.Untrusted, none.Verify(Feed, TestUpdateFeed.Rid, Signature));
    }

    [Fact]
    public void AnyOfSeveralTrustedKeysIsEnough()
    {
        // Rotation: the new key is trusted next to the old one.
        using ECDsa next = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        UpdateFeedVerifier both = new([next.ExportSubjectPublicKeyInfoPem(), TestUpdateFeed.PublicKeyPem]);

        Assert.Equal(2, both.TrustedKeyCount);
        Assert.Equal(UpdateFeedSignatureCheck.Valid, both.Verify(Feed, TestUpdateFeed.Rid, Signature));
        Assert.Equal(UpdateFeedSignatureCheck.Valid, both.Verify(Feed, TestUpdateFeed.Rid, TestUpdateFeed.Sign(Feed, key: next)));
    }

    [Fact]
    public void OnlyP256PublicKeysCanBeTrusted()
    {
        using ECDsa p384 = ECDsa.Create(ECCurve.NamedCurves.nistP384);
        using RSA rsa = RSA.Create(2048);
        using ECDsa p256 = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        Assert.Throws<ArgumentException>(() => new UpdateFeedVerifier([p384.ExportSubjectPublicKeyInfoPem()]));
        Assert.Throws<ArgumentException>(() => new UpdateFeedVerifier([rsa.ExportSubjectPublicKeyInfoPem()]));
        // A private key is never pasted into the list by mistake: only a PEM "PUBLIC KEY".
        Assert.Throws<ArgumentException>(() => new UpdateFeedVerifier([p256.ExportPkcs8PrivateKeyPem()]));
        Assert.Throws<ArgumentException>(() => new UpdateFeedVerifier([p256.ExportECPrivateKeyPem()]));
        Assert.Throws<ArgumentException>(() => new UpdateFeedVerifier(["-----BEGIN PUBLIC KEY-----\nAAAA\n-----END PUBLIC KEY-----"]));
        Assert.Throws<ArgumentException>(() => new UpdateFeedVerifier(["no PEM"]));
        Assert.Throws<ArgumentException>(() => new UpdateFeedVerifier([""]));
    }

    [Theory]
    [InlineData("")]
    [InlineData("osx-arm64\n")]
    [InlineData("osx arm64")]
    [InlineData("osx/arm64")]
    [InlineData("osx-ärm64")]
    public void AChannelThatIsNoRidIsAnError(string channel)
    {
        Assert.Throws<ArgumentException>(() => _verifier.Verify(Feed, channel, Signature));
        Assert.Throws<ArgumentException>(() => UpdateFeedVerifier.SignedDataOf(Feed, channel));
    }

    [Fact]
    public void TheSignedDataIsTheDomainTheRidAndTheFeed()
    {
        Assert.Equal(TestUpdateFeed.SignedData(Feed, TestUpdateFeed.Rid), UpdateFeedVerifier.SignedDataOf(Feed, TestUpdateFeed.Rid));
        Assert.StartsWith(UpdateFeedVerifier.Domain + "\nosx-arm64\n{", Encoding.UTF8.GetString(UpdateFeedVerifier.SignedDataOf(Feed, TestUpdateFeed.Rid)), StringComparison.Ordinal);
    }

    [Fact]
    public void TheVectorIsTheFeedOfVpkWithAPowerShellSignature()
    {
        // Byte for byte as committed (.gitattributes -text): no BOM, no trailing newline, one base64 line with LF.
        Assert.Equal((byte)'{', Feed[0]);
        Assert.Equal((byte)'}', Feed[^1]);
        Assert.Equal(89, Signature.Length);
        Assert.Equal((byte)'\n', Signature[^1]);
    }
}
