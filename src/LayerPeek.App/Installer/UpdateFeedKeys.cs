namespace LayerPeek.App.Installer;

/// <summary>
/// The public keys that may sign an update feed (T-181, ECDSA P-256 SubjectPublicKeyInfo PEM; format in
/// <see cref="LayerPeek.Core.Updates.UpdateFeedVerifier"/>). Constants in the binary on purpose: never read from a file or the
/// settings. Only <see cref="SignedUpdateSource"/> uses them, each list for its own source.
/// </summary>
internal static class UpdateFeedKeys
{
    /// <summary>
    /// The keys of the GitHub feed (the private keys stay offline, <c>docs/release.md</c>). More than one only while a key is
    /// rotated: the new key is added in a release still signed with the old one, the old key is removed in a later release.
    /// </summary>
    /// <remarks>Production key generated 2026-11-11 (public key SHA-256 4cb047d19a472427db721e607c76c15ba0fad96e59d564114a62a7e5e7b91e26).</remarks>
    public static readonly IReadOnlyList<string> Production =
    [
        """
        -----BEGIN PUBLIC KEY-----
        MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEPfcPQ7kbae10mvkth5SbOuNaR6BH
        Q7blH5KVm5CoBORcbPCqN97sFlnYE0wHZmIuvQ9y02THANFtVM3UE5AE1A==
        -----END PUBLIC KEY-----
        """,
    ];

#if DEBUG
    /// <summary>
    /// The test key of the end-to-end tests (<c>tests/Shared/Fixtures/UpdateFeed</c>; its private key is committed and no
    /// secret). Trusted only by the test source of <see cref="UpdateSource.TryCreateForTest"/>, never by the GitHub feed, and
    /// compiled into Debug builds only, like the test mode itself.
    /// </summary>
    public static readonly IReadOnlyList<string> Test =
    [
        """
        -----BEGIN PUBLIC KEY-----
        MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAE9IJrCr/nGVeQ8DDwzCvRO3fRhl/6
        g4mBWUmsE2yefdlVvXK94UDdQ8FnBlIDBrS4oA1991uNyudoPLLYSlUonQ==
        -----END PUBLIC KEY-----
        """,
    ];
#else
    /// <summary>A Release build has no test mode and trusts no test key: the test source rejects every feed.</summary>
    public static readonly IReadOnlyList<string> Test = [];
#endif
}
