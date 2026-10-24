using LayerPeek.App.Platform;
using LayerPeek.App.Platform.Windows;
using LayerPeek.App.Settings;

namespace LayerPeek.App.Tests.Settings;

public sealed class PasswordPolicyTests
{
    [Fact]
    public void UnsupportedStoreMeansNoPasswords() =>
        Assert.Same(PasswordPolicy.Unsupported, PasswordPolicy.For(new UnsupportedSecretStore()));

    [Fact]
    public void WindowsStoreAcceptsWhatTheCredentialManagerStores()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Skip("Windows only.");
            return;
        }

        // Only constructed; the Credential Manager is not touched.
        Assert.Equal(new PasswordPolicy(true, WindowsCredentialSecretStore.MaxSecretBytes), PasswordPolicy.For(new WindowsCredentialSecretStore()));
        Assert.Equal(2560, WindowsCredentialSecretStore.MaxSecretBytes);
    }

    [Fact]
    public void UnknownStoreIsRejectedSoThatANewPlatformCannotBeForgotten() =>
        Assert.Throws<NotSupportedException>(() => PasswordPolicy.For(new FakeSecretStore()));

    [Theory]
    [InlineData("", PasswordProblem.None)]
    [InlineData("Rk7mPq2x", PasswordProblem.None)]
    [InlineData("heslo-žluťoučký", PasswordProblem.None)]
    public void ChecksCharacters(string password, PasswordProblem expected) =>
        Assert.Equal(expected, new PasswordPolicy(true, 2560).Check(password));

    [Fact]
    public void LoneSurrogateIsRejected() =>
        // Not theory data: serializing it would replace the lone surrogate.
        Assert.Equal(PasswordProblem.InvalidCharacters, new PasswordPolicy(true, 2560).Check("bad\uD800"));

    [Fact]
    public void LimitIsInUtf8Bytes()
    {
        PasswordPolicy policy = new(true, 4);

        Assert.Equal(PasswordProblem.None, policy.Check("abcd"));
        Assert.Equal(PasswordProblem.TooLong, policy.Check("abcde"));
        // "č" is two bytes in UTF-8.
        Assert.Equal(PasswordProblem.None, policy.Check("čč"));
        Assert.Equal(PasswordProblem.TooLong, policy.Check("ččx"));
    }
}
