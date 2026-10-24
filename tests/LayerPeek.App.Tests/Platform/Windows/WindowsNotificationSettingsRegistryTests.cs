using System.Runtime.Versioning;

using Microsoft.Win32;

using LayerPeek.App.Platform.Windows;

namespace LayerPeek.App.Tests.Platform.Windows;

/// <summary>
/// Windows' notification settings of one app, removed by the uninstall hook (T-017). The real registry only under a test
/// key of its own (<see cref="TestRegistry"/>), never under Windows' <c>Notifications\Settings</c>.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsNotificationSettingsRegistryTests : IDisposable
{
    private readonly string _subKey = TestRegistry.NewSubKey();

    public WindowsNotificationSettingsRegistryTests()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows registry");
    }

    public void Dispose() => TestRegistry.Delete(_subKey);

    [Fact]
    public void TheKeyOfTheAppIsDeletedWithItsSubkeys()
    {
        using (RegistryKey app = Registry.CurrentUser.CreateSubKey(_subKey + @"\LayerPeek", writable: true))
        {
            app.SetValue("Enabled", 0, RegistryValueKind.DWord);
            app.CreateSubKey("Sub").Dispose();
        }

        using (RegistryKey other = Registry.CurrentUser.CreateSubKey(_subKey + @"\Other.App", writable: true))
        {
            other.SetValue("Enabled", 1, RegistryValueKind.DWord);
        }

        Assert.True(new WindowsNotificationSettingsRegistry(_subKey).Delete("LayerPeek"));

        using RegistryKey parent = Registry.CurrentUser.OpenSubKey(_subKey)!;
        Assert.Equal(["Other.App"], parent.GetSubKeyNames());
    }

    [Fact]
    public void AMissingKeyIsNoError()
    {
        Assert.False(new WindowsNotificationSettingsRegistry(_subKey).Delete("LayerPeek"));
    }

    /// <summary>An empty id or a path would delete the settings of every app (or of another key).</summary>
    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(@"..\LayerPeek")]
    [InlineData(@"LayerPeek\Sub")]
    [InlineData("Layer/Peek")]
    public void AnythingButOneAppIdIsRefused(string appUserModelId)
    {
        Assert.ThrowsAny<ArgumentException>(() => new WindowsNotificationSettingsRegistry(_subKey).Delete(appUserModelId));
    }

    [Fact]
    public void TheDefaultIsTheNotificationSettingsOfWindows()
    {
        Assert.Equal(@"Software\Microsoft\Windows\CurrentVersion\Notifications\Settings", WindowsNotificationSettingsRegistry.DefaultSubKey);
    }
}
