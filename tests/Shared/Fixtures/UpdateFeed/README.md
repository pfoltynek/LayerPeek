# Signed update feed test vector (T-181)

Shared by the C# tests (`UpdateFeedVerifierTests`, `SignedUpdateSourceTests`, the update E2E tests) and the Pester tests of
the release scripts, so that both sides agree on the format. The files are kept byte for byte (`.gitattributes`: `-text`).

| File | Content |
|---|---|
| `releases.osx-arm64.json` | a feed as `vpk pack` writes it (UTF-8, no BOM, no trailing newline) |
| `releases.osx-arm64.json.sig` | its signature for the RID `osx-arm64`, made with PowerShell 7 (`ECDsa.SignData`, SHA-256) |
| `test-feed-key.public.pem` | the test public key (SubjectPublicKeyInfo PEM), the same as `UpdateFeedKeys.Test` in the app |
| `test-feed-key.private.pem` | the test private key (PKCS#8 PEM) |
| `test-feed-key.private.encrypted.pem` | the same key, encrypted PKCS#8 PEM, password `layerpeek-test-only` |

**The test key is not a secret.** It is trusted only by the test update source of Debug builds
(`LAYERPEEK_TEST_UPDATE_SOURCE`), never by the GitHub feed; never sign a release with it.

Format:

- signed data: ASCII `LayerPeek-feed-v1` + LF + RID + LF + the exact bytes of the feed file;
- signature: ECDSA P-256 with SHA-256, IEEE P1363 (r ‖ s, 64 bytes, the default of .NET `ECDsa.SignData`), standard
  base64 (88 characters) in the `.sig` file; ASCII whitespace around it (the trailing LF here) is ignored, at most
  256 bytes in total.

Signing in PowerShell 7:

```powershell
$key = [System.Security.Cryptography.ECDsa]::Create()
$key.ImportFromPem([IO.File]::ReadAllText('test-feed-key.private.pem'))
$data = [Text.Encoding]::ASCII.GetBytes("LayerPeek-feed-v1`nosx-arm64`n") + [IO.File]::ReadAllBytes('releases.osx-arm64.json')
[Convert]::ToBase64String($key.SignData([byte[]]$data, [System.Security.Cryptography.HashAlgorithmName]::SHA256))
```

ECDSA signatures are randomized: signing again gives another valid signature, so tests compare by verifying, not by bytes.
