#if DEBUG
using System.Runtime.Versioning;

using Microsoft.Extensions.DependencyInjection;

using PrusaTrayDiagnostic.App.Composition;
using PrusaTrayDiagnostic.App.Notifications;
using PrusaTrayDiagnostic.App.Platform;
using PrusaTrayDiagnostic.App.Platform.Windows;
using PrusaTrayDiagnostic.App.Settings;
using PrusaTrayDiagnostic.Core.Notifications;
using PrusaTrayDiagnostic.Core.Secrets;

namespace PrusaTrayDiagnostic.App.Tests.Composition;

/// <summary>
/// The opt-in switches of the Debug test mode (T-067): real toasts and the real Credential Manager, isolated from the
/// production app id, shortcut and credential prefix; the browser replaced by a file of addresses; a delayed monitoring
/// start; the image dialog in the test folder. Outside the test mode the switches are ignored and reported.
/// </summary>
public sealed class TestModeSwitchesTests
{
    private static readonly string Folder = Path.Combine(Path.GetTempPath(), "ptd-switches-not-created");

    private static AppEnvironment Read(params (string Name, string Value)[] variables)
    {
        Dictionary<string, string> values = variables.ToDictionary(static pair => pair.Name, static pair => pair.Value, StringComparer.Ordinal);
        return AppEnvironment.FromVariables(name => values.GetValueOrDefault(name));
    }

    [Fact]
    public void SwitchNamesAreTheDocumentedOnes()
    {
        Assert.Equal("PRUSATRAYDIAGNOSTIC_TEST_TOASTS", AppEnvironment.TestToastsVariable);
        Assert.Equal("PRUSATRAYDIAGNOSTIC_TEST_CREDENTIAL_PREFIX", AppEnvironment.TestCredentialPrefixVariable);
        Assert.Equal("PRUSATRAYDIAGNOSTIC_TEST_START_DELAY_MS", AppEnvironment.TestStartDelayVariable);
        Assert.Equal("--ptd-e2e-clear-toasts", AppEnvironment.ClearToastsArgument);
        Assert.Equal("Prusa Tray Diagnostic E2E.lnk", AppEnvironment.TestShortcutFileName);
    }

    [Fact]
    public void TestModeWithoutSwitchesKeepsEverythingOff()
    {
        AppEnvironment environment = Read((AppEnvironment.DataDirectoryVariable, Folder));

        Assert.True(environment.IsIsolated);
        Assert.False(environment.TestToasts);
        Assert.Null(environment.TestCredentialPrefix);
        Assert.Equal(TimeSpan.Zero, environment.TestStartDelay);
        Assert.Empty(environment.IgnoredTestSwitches);
    }

    [Theory]
    [InlineData("1", true)]
    [InlineData("0", false)]
    [InlineData("true", false)]
    [InlineData("", false)]
    public void ToastsNeedTheValueOne(string value, bool expected)
    {
        AppEnvironment environment = Read((AppEnvironment.DataDirectoryVariable, Folder), (AppEnvironment.TestToastsVariable, value));

        Assert.Equal(expected, environment.TestToasts);
    }

    [Theory]
    [InlineData("PrusaTrayDiagnostic.E2E/run-1")]
    [InlineData("PrusaTrayDiagnostic.E2E/0123456789abcdef/nested")]
    public void CredentialPrefixUnderTheTestRootIsAccepted(string prefix)
    {
        AppEnvironment environment = Read((AppEnvironment.DataDirectoryVariable, Folder), (AppEnvironment.TestCredentialPrefixVariable, prefix));

        Assert.Equal(prefix, environment.TestCredentialPrefix);
    }

    [Theory]
    [InlineData("PrusaTrayDiagnostic")]
    [InlineData("PrusaTrayDiagnostic/")]
    [InlineData("PrusaTrayDiagnostic/printer")]
    [InlineData("PrusaTrayDiagnostic.E2E")]
    [InlineData("PrusaTrayDiagnostic.E2E/")]
    [InlineData("prusatraydiagnostic.e2e/run")]
    [InlineData("PrusaTrayDiagnostic.E2E/run 1")]
    [InlineData("Other/PrusaTrayDiagnostic.E2E/run")]
    [InlineData(" ")]
    public void AnyOtherCredentialPrefixIsAnInvalidTestMode(string prefix)
    {
        Assert.ThrowsAny<ArgumentException>(() => Read((AppEnvironment.DataDirectoryVariable, Folder), (AppEnvironment.TestCredentialPrefixVariable, prefix)));
    }

    [Theory]
    [InlineData("0", 0)]
    [InlineData("1500", 1500)]
    [InlineData("60000", 60000)]
    public void StartDelayInMilliseconds(string value, int milliseconds)
    {
        AppEnvironment environment = Read((AppEnvironment.DataDirectoryVariable, Folder), (AppEnvironment.TestStartDelayVariable, value));

        Assert.Equal(TimeSpan.FromMilliseconds(milliseconds), environment.TestStartDelay);
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("60001")]
    [InlineData("1.5")]
    [InlineData("5s")]
    [InlineData(" 10")]
    [InlineData("99999999999")]
    public void InvalidStartDelayIsAnInvalidTestMode(string value)
    {
        Assert.ThrowsAny<ArgumentException>(() => Read((AppEnvironment.DataDirectoryVariable, Folder), (AppEnvironment.TestStartDelayVariable, value)));
    }

    [Fact]
    public void SwitchesOutsideTheTestModeAreIgnoredAndReported()
    {
        AppEnvironment environment = Read(
            (AppEnvironment.TestToastsVariable, "1"),
            (AppEnvironment.TestCredentialPrefixVariable, "PrusaTrayDiagnostic.E2E/x"),
            (AppEnvironment.TestStartDelayVariable, "not even a number"));

        Assert.False(environment.IsIsolated);
        Assert.False(environment.TestToasts);
        Assert.Null(environment.TestCredentialPrefix);
        Assert.Equal(TimeSpan.Zero, environment.TestStartDelay);
        Assert.Equal(AppEnvironment.Production.DataDirectory(), environment.DataDirectory());
        Assert.Equal(AppEnvironment.ProductionAppUserModelId, environment.AppUserModelId);
        Assert.Equal(
            [AppEnvironment.TestToastsVariable, AppEnvironment.TestCredentialPrefixVariable, AppEnvironment.TestStartDelayVariable],
            environment.IgnoredTestSwitches);
    }

    [Fact]
    public void WithoutAnyVariableTheProcessIsProduction()
    {
        Assert.Same(AppEnvironment.Production, Read());
        Assert.Empty(AppEnvironment.Production.IgnoredTestSwitches);
    }

    [Fact]
    [SupportedOSPlatform("windows")]
    public async Task ToastsOfTheTestModeBelongToTheTestAppAndItsOwnShortcut()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Toasts exist only on Windows.");
        string folder = TestDirectories.NewDirectoryPath();
        try
        {
            await using ServiceProvider services = AppServices.Create(environment: AppEnvironment.ForTests(folder, toasts: true));

            WindowsToastPlatform platform = ToastPlatform(services);
            Assert.Equal("PrusaTrayDiagnostic.E2E", platform.AppUserModelId);
            Assert.Equal(TestToastCleanup.DefaultShortcutPath(), platform.ShortcutPath());
            Assert.Equal("Prusa Tray Diagnostic E2E.lnk", Path.GetFileName(platform.ShortcutPath()));
            // Quitting removes exactly that shortcut and the toasts of the test app.
            Assert.Equal(TestToastCleanup.DefaultShortcutPath(), AppServices.CreateTestToastCleanup(services)?.ShortcutPath);
            // Creating the services showed nothing and wrote nothing.
            Assert.False(Directory.Exists(folder));
        }
        finally
        {
            TestDirectories.Delete(folder);
        }
    }

    [Fact]
    [SupportedOSPlatform("windows")]
    public async Task ProductionToastsKeepTheProductionAppAndShortcutAndNeverCleanUp()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Toasts exist only on Windows.");
        await using ServiceProvider services = AppServices.Create(environment: AppEnvironment.Production);

        WindowsToastPlatform platform = ToastPlatform(services);
        Assert.Equal("PrusaTrayDiagnostic", platform.AppUserModelId);
        Assert.Equal(AppShortcutRegistration.DefaultShortcutPath(), platform.ShortcutPath());
        Assert.Null(AppServices.CreateTestToastCleanup(services));
    }

    [Fact]
    public async Task TestModeWithoutTheToastSwitchShowsNoToastsAndCleansNothing()
    {
        string folder = TestDirectories.NewDirectoryPath();
        try
        {
            await using ServiceProvider services = AppServices.Create(environment: AppEnvironment.ForTests(folder));

            Assert.Same(NullNotificationService.Instance, services.GetRequiredService<INotificationService>());
            Assert.Null(AppServices.CreateTestToastCleanup(services));
        }
        finally
        {
            TestDirectories.Delete(folder);
        }
    }

    [Fact]
    [SupportedOSPlatform("windows")]
    public async Task CredentialPrefixSwitchesTheTestModeToTheCredentialManager()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "The Credential Manager exists only on Windows.");
        string folder = TestDirectories.NewDirectoryPath();
        try
        {
            await using ServiceProvider services = AppServices.Create(environment: AppEnvironment.ForTests(folder, credentialPrefix: "PrusaTrayDiagnostic.E2E/run-1"));

            WindowsCredentialSecretStore store = Assert.IsType<WindowsCredentialSecretStore>(services.GetRequiredService<ISecretStore>());
            Assert.Equal("PrusaTrayDiagnostic.E2E/run-1", store.TargetPrefix);
            // Session only (user decision 2026-10-22): a crashed run leaves nothing on disk and nothing roams.
            Assert.Equal(CredentialManagerNative.CredPersistSession, store.Persist);
        }
        finally
        {
            TestDirectories.Delete(folder);
        }
    }

    [Fact]
    [SupportedOSPlatform("windows")]
    public async Task ProductionCredentialsKeepTheProductionPrefixAndRoam()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "The Credential Manager exists only on Windows.");
        await using ServiceProvider services = AppServices.Create(environment: AppEnvironment.Production);

        WindowsCredentialSecretStore store = Assert.IsType<WindowsCredentialSecretStore>(services.GetRequiredService<ISecretStore>());
        Assert.Equal(WindowsCredentialSecretStore.DefaultTargetPrefix, store.TargetPrefix);
        Assert.Equal(CredentialManagerNative.CredPersistEnterprise, store.Persist);
    }

    [Fact]
    public async Task TestModeRecordsAddressesInsteadOfStartingTheBrowser()
    {
        string folder = TestDirectories.NewDirectoryPath();
        try
        {
            await using (ServiceProvider services = AppServices.Create(environment: AppEnvironment.ForTests(folder)))
            {
                RecordingUrlLauncher launcher = Assert.IsType<RecordingUrlLauncher>(services.GetRequiredService<IUrlLauncher>());
                Assert.Equal(Path.Combine(folder, "opened-urls.txt"), launcher.FilePath);
            }

            await using (ServiceProvider services = AppServices.Create(environment: AppEnvironment.Production))
            {
                Assert.IsType<ProcessUrlLauncher>(services.GetRequiredService<IUrlLauncher>());
            }
        }
        finally
        {
            TestDirectories.Delete(folder);
        }
    }

    [Fact]
    public async Task ImageDialogStartsInTheTestFolderOnlyInTheTestMode()
    {
        string folder = TestDirectories.NewDirectoryPath();
        try
        {
            await using (ServiceProvider services = AppServices.Create(environment: AppEnvironment.ForTests(folder)))
            {
                using SettingsViewModel window = AppServices.CreateSettingsViewModel(services);
                Assert.Equal(Path.GetFullPath(folder), window.ImageStartDirectory);
            }

            await using (ServiceProvider services = AppServices.Create(environment: AppEnvironment.Production))
            {
                using SettingsViewModel window = AppServices.CreateSettingsViewModel(services);
                Assert.Null(window.ImageStartDirectory);
            }
        }
        finally
        {
            TestDirectories.Delete(folder);
        }
    }

    [SupportedOSPlatform("windows")]
    private static WindowsToastPlatform ToastPlatform(IServiceProvider services) =>
        Assert.IsType<WindowsToastPlatform>(Assert.IsType<ToastNotificationService>(services.GetRequiredService<INotificationService>()).Platform);
}
#endif
