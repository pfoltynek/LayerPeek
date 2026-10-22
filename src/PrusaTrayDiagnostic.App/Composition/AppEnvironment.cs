using PrusaTrayDiagnostic.Core.Settings;

namespace PrusaTrayDiagnostic.App.Composition;

/// <summary>
/// Where the app keeps its files and whether it may touch per-user state of the operating system. Release builds always
/// run in <see cref="Production"/>; only Debug builds can be switched into the isolated test mode of the end-to-end tests
/// (T-044), and the switch does not even exist in Release builds.
/// </summary>
internal sealed class AppEnvironment
{
    private readonly string? _dataDirectory;

    private AppEnvironment(string? dataDirectory, bool isolated)
    {
        _dataDirectory = dataDirectory;
        IsIsolated = isolated;
    }

    /// <summary>The user's data folder and the user's OS state (Credential Manager, toasts with their Start menu shortcut).</summary>
    public static AppEnvironment Production { get; } = new(null, isolated: false);

    /// <summary>
    /// Test mode: every file lives in its own data folder, passwords only in memory and no toasts are shown, so that the
    /// Start menu shortcut and its AppUserModelID registration (T-010) are never written. Always <see langword="false"/> in
    /// Release builds.
    /// </summary>
    public bool IsIsolated { get; }

    /// <summary>
    /// The folder of <c>settings.json</c>, <c>camera-windows.json</c> and <c>printer-images/</c>: the per-user data folder,
    /// or the test folder.
    /// </summary>
    /// <exception cref="InvalidOperationException">The OS does not provide a per-user data folder.</exception>
    public string DataDirectory() => _dataDirectory ?? AppDataPaths.GetDefaultDirectory();

    /// <summary>
    /// The AppUserModelID of the process and its windows on Windows: <c>PrusaTrayDiagnostic</c> (T-010, T-043), in the test
    /// mode <c>PrusaTrayDiagnostic.E2E</c>, so that test windows never group with the user's instance on the taskbar.
    /// </summary>
    public string AppUserModelId => IsIsolated ? TestAppUserModelId : ProductionAppUserModelId;

    /// <summary>Before the tray tooltip: empty, in the test mode <c>[E2E] </c> so the tests find their own tray icon (T-044).</summary>
    public string TrayToolTipPrefix => IsIsolated ? "[E2E] " : string.Empty;

    /// <summary>Same value as <c>WindowsAppIdentity.AppUserModelId</c> (a unit test keeps them equal).</summary>
    internal const string ProductionAppUserModelId = "PrusaTrayDiagnostic";

    /// <summary>The AppUserModelID of the test mode (never used by Release builds).</summary>
    internal const string TestAppUserModelId = "PrusaTrayDiagnostic.E2E";

    /// <summary>
    /// The folder of the log files (T-024): the standard log folder of the OS, in the test mode the <c>logs</c> folder in the
    /// test data folder.
    /// </summary>
    /// <exception cref="InvalidOperationException">The OS does not provide a per-user folder for logs.</exception>
    public string LogDirectory() => _dataDirectory is null ? AppDataPaths.GetDefaultLogDirectory() : Path.Combine(_dataDirectory, TestLogFolderName);

    /// <summary>The log folder inside the test data folder.</summary>
    internal const string TestLogFolderName = "logs";

#if DEBUG
    /// <summary>
    /// Debug builds only: the test data folder (absolute path). Setting it switches the app into the isolated test mode;
    /// such a start also needs the instance name suffix (<see cref="SingleInstance.InstanceTransports.SuffixVariable"/>),
    /// so that it can never meet the user's running instance.
    /// </summary>
    public const string DataDirectoryVariable = "PRUSATRAYDIAGNOSTIC_TEST_DATA_DIR";

    /// <summary>Exit code of a test-mode start without an instance name suffix or with a relative data folder.</summary>
    public const int InvalidTestModeExitCode = 4;

    /// <summary>The isolated test mode with its data in <paramref name="dataDirectory"/>.</summary>
    public static AppEnvironment ForTests(string dataDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        if (!Path.IsPathFullyQualified(dataDirectory))
        {
            throw new ArgumentException("The test data folder must be an absolute path.", nameof(dataDirectory));
        }

        return new AppEnvironment(Path.GetFullPath(dataDirectory), isolated: true);
    }
#endif

    /// <summary>
    /// The environment of this process: <see cref="Production"/>, or (Debug builds only) the test mode when
    /// <c>PRUSATRAYDIAGNOSTIC_TEST_DATA_DIR</c> is set.
    /// </summary>
    /// <exception cref="ArgumentException">Debug: the test data folder is not an absolute path.</exception>
    public static AppEnvironment FromProcess()
    {
#if DEBUG
        string? dataDirectory = Environment.GetEnvironmentVariable(DataDirectoryVariable);
        return string.IsNullOrWhiteSpace(dataDirectory) ? Production : ForTests(dataDirectory.Trim());
#else
        return Production;
#endif
    }
}
