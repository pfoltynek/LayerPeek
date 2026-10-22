using System.Reflection;
using System.Text.RegularExpressions;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using PrusaTrayDiagnostic.App.Cameras;
using PrusaTrayDiagnostic.App.Composition;
using PrusaTrayDiagnostic.App.Logging;
using PrusaTrayDiagnostic.App.Platform;
using PrusaTrayDiagnostic.App.Settings;
using PrusaTrayDiagnostic.App.Tests.Logging;
using PrusaTrayDiagnostic.Core.Cameras;
using PrusaTrayDiagnostic.Core.Images;
using PrusaTrayDiagnostic.Core.Notifications;
using PrusaTrayDiagnostic.Core.Printers;
using PrusaTrayDiagnostic.Core.Secrets;
using PrusaTrayDiagnostic.Core.Settings;
using PrusaTrayDiagnostic.Tests.Shared;

namespace PrusaTrayDiagnostic.App.Tests.Composition;

/// <summary>
/// The data folder of the app and the isolated test mode of the end-to-end tests (T-044): every file goes to the folder
/// of the environment, and the test mode never touches the Credential Manager, toasts or the Start menu shortcut.
/// </summary>
public sealed class AppEnvironmentTests
{
    [Fact]
    public void ProductionUsesTheUserDataFolderAndIsNotIsolated()
    {
        Assert.False(AppEnvironment.Production.IsIsolated);
        Assert.Equal(AppDataPaths.GetDefaultDirectory(), AppEnvironment.Production.DataDirectory());
    }

    [Fact]
    public void ProductionKeepsTheAppUserModelIdOfTheStartMenuShortcut()
    {
        Assert.Equal("PrusaTrayDiagnostic", AppEnvironment.Production.AppUserModelId);
        if (OperatingSystem.IsWindows())
        {
            Assert.Equal(PrusaTrayDiagnostic.App.Platform.Windows.WindowsAppIdentity.AppUserModelId, AppEnvironment.Production.AppUserModelId);
        }
    }

#if DEBUG
    [Fact]
    public void WithoutTheVariableTheProcessRunsInProduction()
    {
        Assert.SkipWhen(
            Environment.GetEnvironmentVariable(AppEnvironment.DataDirectoryVariable) is not null,
            "The test process itself runs in the test mode.");

        Assert.Same(AppEnvironment.Production, AppEnvironment.FromProcess());
    }

    [Fact]
    public void TestModeIsIsolatedAndUsesItsOwnFolder()
    {
        string folder = TestDirectories.NewDirectoryPath();

        AppEnvironment environment = AppEnvironment.ForTests(folder);

        Assert.True(environment.IsIsolated);
        Assert.Equal(Path.GetFullPath(folder), environment.DataDirectory());
        // Test windows never group with the user's instance on the taskbar.
        Assert.Equal("PrusaTrayDiagnostic.E2E", environment.AppUserModelId);
        // Nothing is created up front.
        Assert.False(Directory.Exists(folder));
    }

    [Theory]
    [InlineData("relative")]
    [InlineData(@"..\up")]
    [InlineData(" ")]
    public void TestModeNeedsAnAbsoluteFolder(string folder)
    {
        Assert.ThrowsAny<ArgumentException>(() => AppEnvironment.ForTests(folder));
    }

    [Fact]
    public async Task TestModeKeepsEveryFileInItsFolderAndNothingInTheUserState()
    {
        string folder = TestDirectories.NewDirectoryPath();
        try
        {
            await using ServiceProvider services = AppServices.Create(environment: AppEnvironment.ForTests(folder));

            Assert.True(services.GetRequiredService<AppEnvironment>().IsIsolated);
            JsonSettingsStore settings = Assert.IsType<JsonSettingsStore>(services.GetRequiredService<ISettingsStore>());
            Assert.Equal(Path.Combine(folder, JsonSettingsStore.FileName), settings.FilePath);
            FilePrinterImageStore images = Assert.IsType<FilePrinterImageStore>(services.GetRequiredService<IPrinterImageStore>());
            Assert.Equal(Path.Combine(folder, FilePrinterImageStore.DirectoryName), images.DirectoryPath);
            JsonCameraWindowStateStore cameraWindows = Assert.IsType<JsonCameraWindowStateStore>(services.GetRequiredService<ICameraWindowStateStore>());
            Assert.Equal(Path.Combine(folder, JsonCameraWindowStateStore.FileName), cameraWindows.FilePath);

            // No Credential Manager, no toasts (and so no Start menu shortcut with the AppUserModelID).
            Assert.IsType<TestModeSecretStore>(services.GetRequiredService<ISecretStore>());
            // The settings window validates passwords like with the Credential Manager.
            Assert.Equal(new PasswordPolicy(IsSupported: true, MaxBytes: 2560), services.GetRequiredService<PasswordPolicy>());
            using (SettingsViewModel window = AppServices.CreateSettingsViewModel(services))
            {
                Assert.True(window.IsPasswordSupported);
            }

            Assert.Same(NullNotificationService.Instance, services.GetRequiredService<INotificationService>());

            // Really written there: settings, a custom printer image and a camera window state.
            await settings.SaveAsync(new AppSettings { Printers = [new PrinterDefinition { Id = "p1", Name = "P1", ConnectionType = ConnectionType.PrusaLink, Address = "127.0.0.1" }] }, TestContext.Current.CancellationToken);
            await images.SaveAsync("p1", TinyPng, TestContext.Current.CancellationToken);
            await cameraWindows.LoadAsync(TestContext.Current.CancellationToken);
            cameraWindows.Set("p1", CameraWindowState.Default with { Mode = CameraWindowMode.Mini });
            Assert.True(await cameraWindows.WaitForPendingSaveAsync(TimeSpan.FromSeconds(10)));

            Assert.True(File.Exists(settings.FilePath));
            Assert.True(File.Exists(images.GetFilePath("p1")));
            Assert.True(File.Exists(cameraWindows.FilePath));
        }
        finally
        {
            TestDirectories.Delete(folder);
        }
    }

    [Fact]
    public async Task TestModeSecretsLiveInMemoryAndCanBeSeeded()
    {
        string folder = TestDirectories.NewDirectoryPath();
        try
        {
            Directory.CreateDirectory(folder);
            await File.WriteAllTextAsync(
                Path.Combine(folder, TestModeSecretStore.SeedFileName),
                $"{SecretKeys.ForPrinter("p1")}\tRk7mPq2x\nmalformed line\n",
                TestContext.Current.CancellationToken);
            await using ServiceProvider services = AppServices.Create(environment: AppEnvironment.ForTests(folder));
            ISecretStore secrets = services.GetRequiredService<ISecretStore>();
            CancellationToken ct = TestContext.Current.CancellationToken;

            Assert.Equal("Rk7mPq2x", await secrets.GetAsync(SecretKeys.ForPrinter("p1"), ct));
            await secrets.SetAsync("other", "s3cret", ct);
            Assert.Equal("s3cret", await secrets.GetAsync("OTHER", ct));
            Assert.True(await secrets.DeleteAsync("other", ct));
            Assert.Null(await secrets.GetAsync("other", ct));

            // Never written back: the folder holds only the seed file.
            Assert.Equal([TestModeSecretStore.SeedFileName], Directory.GetFiles(folder).Select(Path.GetFileName));
        }
        finally
        {
            TestDirectories.Delete(folder);
        }
    }

    [Fact]
    public void TestModeLogsIntoItsFolderAndProductionIntoTheLogFolderOfTheOs()
    {
        string folder = TestDirectories.NewDirectoryPath();
        try
        {
            AppEnvironment environment = AppEnvironment.ForTests(folder);
            Assert.Equal(Path.Combine(environment.DataDirectory(), "logs"), environment.LogDirectory());
            using (AppLogging logging = AppLogging.Create(environment))
            {
                Assert.Equal(LogLevel.Debug, logging.Level.MinimumLevel);
                logging.Factory.CreateLogger("E2E").Write(LogLevel.Debug, "Window opened.");
            }

            string log = File.ReadAllText(LogFile.PathOf(environment.LogDirectory(), 0));
            Assert.Contains(" DBG [pid ", log, StringComparison.Ordinal);
            Assert.Contains("] E2E: Window opened.", log, StringComparison.Ordinal);

            Assert.Equal(AppDataPaths.GetDefaultLogDirectory(), AppEnvironment.Production.LogDirectory());
        }
        finally
        {
            TestDirectories.Delete(folder);
        }
    }

    /// <summary>The test mode exists only in Debug builds (the Release build of these tests checks the opposite).</summary>
    [Fact]
    public void TestModeIsPartOfDebugBuildsOnly()
    {
        Assert.Equal("Debug", typeof(AppEnvironment).Assembly.GetCustomAttribute<AssemblyConfigurationAttribute>()?.Configuration);
        Assert.NotNull(typeof(AppEnvironment).GetMethod(nameof(AppEnvironment.ForTests), BindingFlags.Public | BindingFlags.Static));
    }
#else
    [Fact]
    public void ReleaseBuildsHaveNoTestMode()
    {
        Assert.Null(typeof(AppEnvironment).GetMethod("ForTests", BindingFlags.Public | BindingFlags.Static | BindingFlags.NonPublic));
        Assert.Null(typeof(AppEnvironment).Assembly.GetType("PrusaTrayDiagnostic.App.Platform.TestModeSecretStore"));
        // T-067: no browser replacement, no toast cleanup of the test app, no switch properties.
        Assert.Null(typeof(AppEnvironment).Assembly.GetType("PrusaTrayDiagnostic.App.Platform.RecordingUrlLauncher"));
        Assert.Null(typeof(AppEnvironment).Assembly.GetType("PrusaTrayDiagnostic.App.Platform.Windows.TestToastCleanup"));
        Assert.Null(typeof(AppEnvironment).GetProperty("TestToasts", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance));
        Assert.Same(AppEnvironment.Production, AppEnvironment.FromProcess());
    }
#endif

    /// <summary>
    /// Guard against a new file location that bypasses <see cref="AppEnvironment"/> (and so the test mode): in the app's
    /// sources, the per-user folders, the default stores, the Credential Manager, the Start menu shortcut and the registry
    /// (Explorer's tray icon settings) appear only where the environment decides about them.
    /// </summary>
    [Fact]
    public void EveryPerUserLocationOfTheAppGoesThroughTheEnvironment()
    {
        string sources = Path.Combine(TestWorktree.Root, "src", "PrusaTrayDiagnostic.App");
        (string Pattern, string[] AllowedFiles)[] rules =
        [
            (@"AppDataPaths\.GetDefaultDirectory", ["Composition/AppEnvironment.cs"]),
            (@"AppDataPaths\.GetDefaultLogDirectory", ["Composition/AppEnvironment.cs"]),
            (@"\.CreateDefault\(", []),
            // The Start menu folder of the toast shortcut: only reached through toasts, which the test mode switches off (or, with
            // the Debug switch of T-067, to the test shortcut of its own).
            // The Windows folder of explorer.exe (T-048): a system folder, not per-user data.
            (@"Environment\.GetFolderPath|SpecialFolder\.", ["Platform/Windows/AppShortcutRegistration.cs", "Platform/FolderLauncher.cs"]),
            // macOS socket fallback next to the instance lock (its name carries the test suffix).
            (@"Path\.GetTemp(Path|FileName)", ["SingleInstance/InstanceTransports.cs"]),
            (@"new WindowsCredentialSecretStore\(", ["Composition/AppServices.cs"]),
            (@"new AppShortcutRegistration\(", ["Composition/AppServices.cs"]),
            // Explorer's tray icon settings (T-061): created only where the test mode switches the promotion off.
            (@"Microsoft\.Win32\.Registry|RegistryKey|\bRegistry\.(CurrentUser|LocalMachine|ClassesRoot|Users)\b", ["Platform/Windows/NotifyIconSettingsRegistry.cs"]),
            (@"new NotifyIconSettingsRegistry\(", ["Composition/TrayHost.cs"]),
        ];

        List<string> violations = [];
        foreach (string file in Directory.EnumerateFiles(sources, "*.cs", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(sources, file).Replace('\\', '/');
            if (relative.StartsWith("obj/", StringComparison.Ordinal) || relative.StartsWith("bin/", StringComparison.Ordinal))
            {
                continue;
            }

            string text = File.ReadAllText(file);
            foreach ((string pattern, string[] allowed) in rules)
            {
                if (Regex.IsMatch(text, pattern) && !allowed.Contains(relative, StringComparer.Ordinal))
                {
                    violations.Add($"{relative}: {pattern}");
                }
            }
        }

        Assert.Empty(violations);
    }

    /// <summary>A 1×1 PNG (the image store accepts PNG only).</summary>
    private static readonly byte[] TinyPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==");
}
