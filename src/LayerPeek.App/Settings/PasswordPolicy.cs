using System.Text;

using LayerPeek.App.Platform;
using LayerPeek.App.Platform.MacOS;
using LayerPeek.App.Platform.Windows;
using LayerPeek.Core.Secrets;

namespace LayerPeek.App.Settings;

/// <summary>Why a password cannot be stored. Public only because xUnit theory data needs it.</summary>
public enum PasswordProblem
{
    None,

    /// <summary>Longer than <see cref="PasswordPolicy.MaxBytes"/> in UTF-8.</summary>
    TooLong,

    /// <summary>Not valid Unicode (a lone surrogate), which the secret store rejects instead of altering it.</summary>
    InvalidCharacters,
}

/// <summary>
/// What the platform <see cref="Core.Secrets.ISecretStore"/> of this OS accepts, so that the settings window validates a
/// password before saving and disables the password field where no store exists (an OS other than Windows and macOS).
/// </summary>
/// <param name="IsSupported">Passwords can be stored; <see langword="false"/> with <see cref="UnsupportedSecretStore"/>.</param>
/// <param name="MaxBytes">Maximum password size in UTF-8 bytes.</param>
internal sealed record PasswordPolicy(bool IsSupported, int MaxBytes)
{
    // Same rule as the stores: invalid UTF-16 is rejected, never silently replaced.
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    /// <summary>No secret store on this OS: passwords cannot be saved.</summary>
    public static PasswordPolicy Unsupported { get; } = new(IsSupported: false, MaxBytes: 0);

    /// <summary>
    /// What <paramref name="store"/> (the one registered by the composition root) accepts, so that the window and the store
    /// can never disagree about the OS.
    /// </summary>
    /// <exception cref="NotSupportedException">
    /// A store without a known policy: a new platform store must be added here.
    /// </exception>
    public static PasswordPolicy For(ISecretStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        if (store is UnsupportedSecretStore)
        {
            return Unsupported;
        }

        if (OperatingSystem.IsWindows() && store is WindowsCredentialSecretStore)
        {
            return new PasswordPolicy(IsSupported: true, WindowsCredentialSecretStore.MaxSecretBytes);
        }

        if (OperatingSystem.IsMacOS() && store is KeychainSecretStore)
        {
            return new PasswordPolicy(IsSupported: true, KeychainSecretStore.MaxSecretBytes);
        }

#if DEBUG
        if (store is TestModeSecretStore)
        {
            // The end-to-end tests (T-044) see the settings window like on Windows.
            return new PasswordPolicy(IsSupported: true, TestModeSecretStore.MaxSecretBytes);
        }
#endif

        throw new NotSupportedException($"No password policy is known for {store.GetType().Name}.");
    }

    /// <summary>Whether <paramref name="password"/> can be stored (an empty password is never stored, so it is fine).</summary>
    public PasswordProblem Check(string password)
    {
        ArgumentNullException.ThrowIfNull(password);
        int byteCount;
        try
        {
            byteCount = StrictUtf8.GetByteCount(password);
        }
        catch (EncoderFallbackException)
        {
            return PasswordProblem.InvalidCharacters;
        }

        return byteCount > MaxBytes ? PasswordProblem.TooLong : PasswordProblem.None;
    }
}
