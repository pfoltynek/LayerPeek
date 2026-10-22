using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;

using PrusaTrayDiagnostic.Core.Secrets;

using static PrusaTrayDiagnostic.App.Platform.Windows.CredentialManagerNative;

namespace PrusaTrayDiagnostic.App.Platform.Windows;

/// <summary>
/// <see cref="ISecretStore"/> backed by the Windows Credential Manager (generic credentials of the current user,
/// protected by DPAPI, roamed with the user profile). Each key maps to the credential target <c>{prefix}/{key}</c>.
/// Argument errors are thrown immediately; cancellation and native failures surface through the returned task.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed unsafe class WindowsCredentialSecretStore : ISecretStore
{
    public const string DefaultTargetPrefix = "PrusaTrayDiagnostic";

    /// <summary>Maximum secret size in UTF-8 bytes supported by the Credential Manager.</summary>
    public const int MaxSecretBytes = MaxCredentialBlobSize;

    // Strict: an invalid UTF-16 secret (lone surrogate) must be rejected, not silently altered to U+FFFD.
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    private readonly string _targetPrefix;
    private readonly uint _persist;

    public WindowsCredentialSecretStore(string targetPrefix = DefaultTargetPrefix)
        // Roams together with the settings in %APPDATA%, so printers keep their credentials on other PCs.
        : this(targetPrefix, CredPersistEnterprise)
    {
    }

    /// <param name="persist">A CRED_PERSIST_* value; tests use <see cref="CredPersistSession"/> so nothing reaches disk or roams.</param>
    internal WindowsCredentialSecretStore(string targetPrefix, uint persist)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetPrefix);
        _targetPrefix = targetPrefix;
        _persist = persist;
    }

    public Task<string?> GetAsync(string key, CancellationToken cancellationToken)
    {
        string target = ToTarget(key);
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromCanceled<string?>(cancellationToken);
        }

        try
        {
            return Task.FromResult(Read(target));
        }
        catch (SecretStoreException ex)
        {
            return Task.FromException<string?>(ex);
        }
    }

    public Task SetAsync(string key, string secret, CancellationToken cancellationToken)
    {
        string target = ToTarget(key);
        ArgumentNullException.ThrowIfNull(secret);
        int byteCount = GetSecretByteCount(secret);
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromCanceled(cancellationToken);
        }

        try
        {
            Write(target, secret, byteCount);
            return Task.CompletedTask;
        }
        catch (SecretStoreException ex)
        {
            return Task.FromException(ex);
        }
    }

    public Task<bool> DeleteAsync(string key, CancellationToken cancellationToken)
    {
        string target = ToTarget(key);
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromCanceled<bool>(cancellationToken);
        }

        if (CredDelete(target, CredTypeGeneric, 0))
        {
            return Task.FromResult(true);
        }

        int error = Marshal.GetLastPInvokeError();
        return error == ErrorNotFound ? Task.FromResult(false) : Task.FromException<bool>(Failure("delete", error));
    }

    private static string? Read(string target)
    {
        if (!CredRead(target, CredTypeGeneric, 0, out Credential* credential))
        {
            int error = Marshal.GetLastPInvokeError();
            return error == ErrorNotFound ? null : throw Failure("read", error);
        }

        try
        {
            Span<byte> blob = new(credential->CredentialBlob, checked((int)credential->CredentialBlobSize));
            try
            {
                return StrictUtf8.GetString(blob);
            }
            catch (DecoderFallbackException)
            {
                // Written by something else than this store; do not guess what the secret was. Not chained: its message
                // quotes the offending bytes of the secret, and logged exceptions go to the log file whole (T-024).
                throw new SecretStoreException("The stored credential is not valid UTF-8.");
            }
            finally
            {
                // CredFree does not wipe the buffer; clear the plaintext copy owned by this process.
                CryptographicOperations.ZeroMemory(blob);
            }
        }
        finally
        {
            CredFree(credential);
        }
    }

    private void Write(string target, string secret, int byteCount)
    {
        // MaxSecretBytes (2.5 kB) is small enough for the stack and avoids a heap copy of the plaintext.
        Span<byte> blob = stackalloc byte[byteCount];
        try
        {
            StrictUtf8.GetBytes(secret, blob);
            fixed (char* targetName = target)
            fixed (byte* blobPointer = blob)
            {
                Credential credential = new()
                {
                    Type = CredTypeGeneric,
                    TargetName = targetName,
                    CredentialBlobSize = (uint)byteCount,
                    CredentialBlob = blobPointer,
                    Persist = _persist,
                };

                if (!CredWrite(&credential, 0))
                {
                    throw Failure("write", Marshal.GetLastPInvokeError());
                }
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(blob);
        }
    }

    private static int GetSecretByteCount(string secret)
    {
        int byteCount;
        try
        {
            byteCount = StrictUtf8.GetByteCount(secret);
        }
        catch (EncoderFallbackException)
        {
            // The exception message would contain the offending characters, so it is not chained.
            throw new ArgumentException("The secret is not a valid Unicode string.", nameof(secret));
        }

        if (byteCount > MaxSecretBytes)
        {
            throw new ArgumentException($"The secret exceeds {MaxSecretBytes} bytes in UTF-8.", nameof(secret));
        }

        return byteCount;
    }

    private string ToTarget(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        return $"{_targetPrefix}/{key}";
    }

    private static SecretStoreException Failure(string operation, int error) =>
        new($"Windows Credential Manager failed to {operation} a credential.", new Win32Exception(error));
}
