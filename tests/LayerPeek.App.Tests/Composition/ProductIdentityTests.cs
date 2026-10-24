using System.Runtime.Versioning;

using LayerPeek.App.Composition;
using LayerPeek.App.Logging;
using LayerPeek.App.Platform.Windows;
using LayerPeek.App.SingleInstance;
using LayerPeek.App.Support;
using LayerPeek.App.Tray;
using LayerPeek.App.Windows;
using LayerPeek.Core.Settings;

namespace LayerPeek.App.Tests.Composition;

/// <summary>
/// The names under which the app meets the user and the OS (T-074, renamed to LayerPeek before the first release): a
/// change of any of them strands the user's settings, passwords, logs, toasts or Start menu shortcut, so each is pinned here.
/// </summary>
public sealed class ProductIdentityTests
{
    [Fact]
    public void ProductionNamesAreLayerPeek()
    {
        Assert.Equal("LayerPeek", TrayToolTipFormatter.AppName);
        Assert.Equal("LayerPeek", AppDataPaths.AppFolderName);
        Assert.Equal("LayerPeek", AppEnvironment.ProductionAppUserModelId);
        Assert.Equal("LayerPeek", InstanceTransports.BaseName);
        Assert.Equal("LayerPeek.exe", TrayIconPromotion.ExecutableName);
        Assert.Equal("LayerPeek.log", LogFile.FileName);
        Assert.Equal(new Uri("https://github.com/pfoltynek/LayerPeek"), AboutViewModel.RepositoryUri);
    }

    [Fact]
    [SupportedOSPlatform("windows")]
    public void WindowsNamesAreLayerPeek()
    {
        // Constants only: readable on every OS, used by the app only on Windows.
        Assert.Equal(AppEnvironment.ProductionAppUserModelId, WindowsAppIdentity.AppUserModelId);
        Assert.Equal("LayerPeek", WindowsCredentialSecretStore.DefaultTargetPrefix);
    }

    [Fact]
    public void DataAndLogFoldersCarryTheNewName()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Equal(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "LayerPeek"), AppDataPaths.GetDefaultDirectory());
            Assert.Equal(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LayerPeek", "logs"), AppDataPaths.GetDefaultLogDirectory());
        }
        else if (OperatingSystem.IsMacOS())
        {
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            Assert.Equal(Path.Combine(home, "Library", "Application Support", "LayerPeek"), AppDataPaths.GetDefaultDirectory());
            Assert.Equal(Path.Combine(home, "Library", "Logs", "LayerPeek"), AppDataPaths.GetDefaultLogDirectory());
        }
    }

    [Fact]
    public void SingleInstanceNamesCarryTheNewName()
    {
        (string mutex, string pipe) = InstanceTransports.WindowsNames("S-1-5-21-1", 2, string.Empty);

        Assert.Equal(@"Local\LayerPeek-S-1-5-21-1", mutex);
        Assert.Equal("LayerPeek-S-1-5-21-1-2", pipe);
    }

    [Fact]
    public void SupportBundleCarriesTheNewName()
    {
        DateTimeOffset now = new(2026, 10, 24, 14, 3, 12, TimeSpan.Zero);

        Assert.Equal("LayerPeek-diagnostics-20261024-140312.zip", SupportBundle.DefaultFileName(now));
    }

    [Fact]
    public void ToastActivatorsAreNewForTheNewName()
    {
        // The CLSIDs of the former name belong to its shortcut and registration; the one-time migration script in tools/
        // removes the old production registration. Reusing them would bind toasts of the new app to the old exe path.
        string[] formerName = ["E8389DCA-851B-498C-8082-69B1AB263C14", "1708A774-C00D-4A8B-B9E5-1BEB8527DD24"];
        Guid production = new(AppEnvironment.ProductionToastActivatorClsid);

        Assert.Equal(new Guid("EC47413C-984D-447A-866A-5F780E408DE7"), production);
        Assert.All(formerName, old => Assert.NotEqual(new Guid(old), production));
#if DEBUG
        Guid test = new(AppEnvironment.TestToastActivatorClsid);
        Assert.Equal(new Guid("EDD3ED5E-8363-4CF4-977A-64A623E0A5A2"), test);
        Assert.All(formerName, old => Assert.NotEqual(new Guid(old), test));
#endif
    }

#if DEBUG
    [Fact]
    [SupportedOSPlatform("windows")]
    public void TestModeNamesAreSeparateFromProduction()
    {
        Assert.Equal("LayerPeek.E2E", AppEnvironment.TestAppUserModelId);
        Assert.Equal("LayerPeek.E2E/", AppEnvironment.TestCredentialPrefixRoot);
        Assert.Equal("LayerPeek E2E.lnk", AppEnvironment.TestShortcutFileName);
        Assert.StartsWith("--layerpeek-e2e-", AppEnvironment.ClearToastsArgument, StringComparison.Ordinal);
        Assert.StartsWith("LAYERPEEK_TEST_", AppEnvironment.DataDirectoryVariable, StringComparison.Ordinal);
        // The production credential targets ("LayerPeek/<key>") never start with the test root.
        Assert.False((WindowsCredentialSecretStore.DefaultTargetPrefix + "/").StartsWith(AppEnvironment.TestCredentialPrefixRoot, StringComparison.Ordinal));
    }
#endif
}
