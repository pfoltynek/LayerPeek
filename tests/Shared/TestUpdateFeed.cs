using System.Security.Cryptography;
using System.Text;

namespace LayerPeek.Tests.Shared;

/// <summary>
/// The signed update feed test vector (T-181, <c>tests/Shared/Fixtures/UpdateFeed</c>, shared with the Pester tests) and
/// signing with its test key, which is no secret. The signed data is written out here on purpose instead of calling the app's
/// code, so that the tests check the format, not a copy of it.
/// </summary>
internal static class TestUpdateFeed
{
    /// <summary>The RID the vector is signed for.</summary>
    public const string Rid = "osx-arm64";

    /// <summary>The folder of the vector.</summary>
    public static string Folder { get; } = Path.Combine(TestWorktree.Root, "tests", "Shared", "Fixtures", "UpdateFeed");

    /// <summary>The feed of the vector, as <c>vpk pack</c> writes it.</summary>
    public static string FeedPath { get; } = Path.Combine(Folder, $"releases.{Rid}.json");

    /// <summary>Its signature, made with PowerShell 7.</summary>
    public static string SignaturePath { get; } = FeedPath + ".sig";

    /// <summary>The test public key (PEM).</summary>
    public static string PublicKeyPem => File.ReadAllText(Path.Combine(Folder, "test-feed-key.public.pem"));

    /// <summary>The data a signature covers: <c>"LayerPeek-feed-v1\n" + rid + "\n" + feed</c>.</summary>
    public static byte[] SignedData(ReadOnlySpan<byte> feed, string rid) =>
        [.. Encoding.ASCII.GetBytes($"LayerPeek-feed-v1\n{rid}\n"), .. feed];

    /// <summary>The content of a <c>.sig</c> file for <paramref name="feed"/>, signed with the test key (or <paramref name="key"/>).</summary>
    public static byte[] Sign(ReadOnlySpan<byte> feed, string rid = Rid, ECDsa? key = null)
    {
        using ECDsa testKey = LoadPrivateKey();
        byte[] signature = (key ?? testKey).SignData(SignedData(feed, rid), HashAlgorithmName.SHA256);
        return Encoding.ASCII.GetBytes(Convert.ToBase64String(signature) + "\n");
    }

    /// <summary>
    /// Writes the signature of the feed file at <paramref name="feedPath"/>, signed with the test key, to
    /// <paramref name="signaturePath"/> (default <c>&lt;feed&gt;.sig</c> next to it).
    /// </summary>
    public static void SignFile(string feedPath, string rid, string? signaturePath = null)
    {
        File.WriteAllBytes(signaturePath ?? feedPath + ".sig", Sign(File.ReadAllBytes(feedPath), rid));
    }

    private static ECDsa LoadPrivateKey()
    {
        ECDsa key = ECDsa.Create();
        key.ImportFromPem(File.ReadAllText(Path.Combine(Folder, "test-feed-key.private.pem")));
        return key;
    }
}
