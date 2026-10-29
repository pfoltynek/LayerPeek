using LayerPeek.Core.Settings;

namespace LayerPeek.App.Composition;

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
    /// Test mode: every file lives in its own data folder, passwords only in memory, no toasts are shown (so that the
    /// Start menu shortcut and its AppUserModelID registration of T-010 are never written) and no browser is started.
    /// Debug switches (T-067) turn toasts and the Credential Manager on in isolation (test app id, test shortcut, test
    /// prefix). Always <see langword="false"/> in Release builds.
    /// </summary>
    public bool IsIsolated { get; }

    /// <summary>
    /// The folder of <c>settings.json</c>, <c>camera-windows.json</c>, <c>windows.json</c> and <c>printer-images/</c>: the per-user data folder,
    /// or the test folder.
    /// </summary>
    /// <exception cref="InvalidOperationException">The OS does not provide a per-user data folder.</exception>
    public string DataDirectory() => _dataDirectory ?? AppDataPaths.GetDefaultDirectory();

    /// <summary>
    /// The AppUserModelID of the process and its windows on Windows: <c>LayerPeek</c> (T-010, T-043), in the test
    /// mode <c>LayerPeek.E2E</c>, so that test windows never group with the user's instance on the taskbar.
    /// </summary>
    public string AppUserModelId => IsIsolated ? TestAppUserModelId : ProductionAppUserModelId;

    /// <summary>Before the tray tooltip: empty, in the test mode <c>[E2E] </c> so the tests find their own tray icon (T-044).</summary>
    public string TrayToolTipPrefix => IsIsolated ? "[E2E] " : string.Empty;

    /// <summary>Same value as <c>WindowsAppIdentity.AppUserModelId</c> (a unit test keeps them equal).</summary>
    internal const string ProductionAppUserModelId = "LayerPeek";

    /// <summary>The AppUserModelID of the test mode (never used by Release builds).</summary>
    internal const string TestAppUserModelId = "LayerPeek.E2E";

    /// <summary>
    /// The CLSID of the COM toast activator on Windows (T-068), carried by the Start menu shortcut and registered under
    /// <c>HKCU\Software\Classes\CLSID</c>, through which Windows reports clicks on toasts in the notification center and
    /// starts the app for them. Fixed for good: the installer (T-017) removes its registration on uninstall. New with the
    /// name LayerPeek (T-074): the activator of the app's former name is removed by the one-time migration script in tools/.
    /// </summary>
    internal const string ProductionToastActivatorClsid = "EC47413C-984D-447A-866A-5F780E408DE7";

    /// <summary>
    /// The COM toast activator of this run: the production one, in the test mode with toasts that of the test app (T-068),
    /// otherwise none (a run without toasts registers nothing with Windows).
    /// </summary>
    public Guid? ToastActivatorClsid
    {
        get
        {
            if (!IsIsolated)
            {
                return new Guid(ProductionToastActivatorClsid);
            }

#if DEBUG
            return TestToasts ? new Guid(TestToastActivatorClsid) : null;
#else
            return null;
#endif
        }
    }

    /// <summary>
    /// The folder of the log files (T-024): the standard log folder of the OS, in the test mode the <c>logs</c> folder in the
    /// test data folder.
    /// </summary>
    /// <exception cref="InvalidOperationException">The OS does not provide a per-user folder for logs.</exception>
    public string LogDirectory() => _dataDirectory is null ? AppDataPaths.GetDefaultLogDirectory() : Path.Combine(_dataDirectory, TestLogFolderName);

    /// <summary>The log folder inside the test data folder.</summary>
    internal const string TestLogFolderName = "logs";

    /// <summary>
    /// The autostart shortcut (T-017, Windows): <c>LayerPeek.lnk</c> in the user's Startup folder, in the test mode in the
    /// <see cref="TestStartupFolderName"/> folder of the test data folder, so that a test never makes the user's login start
    /// a test build.
    /// </summary>
    public string AutostartShortcutPath() => _dataDirectory is null
        ? Platform.Windows.AppShortcutRegistration.StartupShortcutPath()
        : Path.Combine(_dataDirectory, TestStartupFolderName, Platform.Windows.AppShortcutRegistration.ShortcutFileName);

    /// <summary>The stand-in for the Startup folder inside the test data folder.</summary>
    internal const string TestStartupFolderName = "Startup";

    /// <summary>
    /// The bundle identifier of <c>LayerPeek.app</c> on macOS (T-019): <c>CFBundleIdentifier</c> of build/macos/Info.plist (a
    /// unit test keeps them equal). The macOS identity of the app: its Preferences, Saved Application State, Caches, Login
    /// Item and (T-020) notifications.
    /// </summary>
    internal const string ProductionBundleIdentifier = "io.github.pfoltynek.LayerPeek";

    /// <summary>
    /// The only bundle identifier whose notifications this run may use on macOS (T-020): <see cref="ProductionBundleIdentifier"/>;
    /// in the Debug test mode with notifications (<c>LAYERPEEK_TEST_TOASTS=1</c>) the test bundle <c>…LayerPeek.E2E</c>, otherwise
    /// none. The notification center opens only when the running bundle has exactly this identifier, so the test mode never
    /// posts notifications or asks for permission as the production app, and the production app never as the test bundle.
    /// </summary>
    public string? NotificationBundleIdentifier
    {
        get
        {
            if (!IsIsolated)
            {
                return ProductionBundleIdentifier;
            }

#if DEBUG
            return TestToasts ? TestBundleIdentifier : null;
#else
            return null;
#endif
        }
    }

    /// <summary>
    /// The command line switch of the AOT smoke test (<c>scripts/aot-smoke.ps1</c>, user decision 2026-10-29, T-020): this run
    /// does not open the notification center of macOS at all (not even to set its delegate), so the smoke
    /// run of the release app leaves nothing in the notification settings of the developer. Exists in Release builds (the
    /// smoke test runs them); it only takes notifications away from the one start that carries it, never changes the
    /// bundle identifier, and is ignored on Windows. Deliberately not under the <c>--layerpeek-e2e-</c> prefix of the test
    /// starts (those need the test mode): a start with it is a normal production start without notifications.
    /// </summary>
    internal const string SkipNotificationCenterArgument = "--layerpeek-smoke-skip-notification-center";

    /// <summary>
    /// The bundle identifier whose notification center this start may open on macOS: <see cref="NotificationBundleIdentifier"/>,
    /// or none when the start carries <see cref="SkipNotificationCenterArgument"/> (exact, case-sensitive).
    /// </summary>
    internal string? NotificationBundleIdentifierFor(IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        return SkipsNotificationCenter(arguments) ? null : NotificationBundleIdentifier;
    }

    /// <summary>Whether the start carries <see cref="SkipNotificationCenterArgument"/>.</summary>
    internal static bool SkipsNotificationCenter(IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        return arguments.Contains(SkipNotificationCenterArgument, StringComparer.Ordinal);
    }

    /// <summary>
    /// The stand-in Login Item of the test mode on macOS (T-019): <c>LoginItems/LayerPeek.loginitem</c> in the test data
    /// folder, <see langword="null"/> outside the test mode (the real Login Item of <c>SMAppService</c>).
    /// </summary>
    public string? TestLoginItemPath() => _dataDirectory is null ? null : TestLoginItemPathIn(_dataDirectory);

    /// <summary>The stand-in Login Item inside a test data folder.</summary>
    internal static string TestLoginItemPathIn(string dataDirectory) => Path.Combine(dataDirectory, TestLoginItemFolderName, TestLoginItemFileName);

    /// <summary>The folder of the stand-in Login Item inside the test data folder.</summary>
    internal const string TestLoginItemFolderName = "LoginItems";

    internal const string TestLoginItemFileName = "LayerPeek.loginitem";

#if DEBUG
    /// <summary>
    /// Debug builds only: the test data folder (absolute path). Setting it switches the app into the isolated test mode;
    /// such a start also needs the instance name suffix (<see cref="SingleInstance.InstanceTransports.SuffixVariable"/>),
    /// so that it can never meet the user's running instance.
    /// </summary>
    public const string DataDirectoryVariable = "LAYERPEEK_TEST_DATA_DIR";

    /// <summary>
    /// Debug builds only (T-067): <c>1</c> shows real toasts in the test mode, as the test app
    /// <see cref="TestAppUserModelId"/> with its own Start menu shortcut (never the production ones).
    /// </summary>
    public const string TestToastsVariable = "LAYERPEEK_TEST_TOASTS";

    /// <summary>
    /// Debug builds only (T-067): the test mode keeps passwords in the real Windows Credential Manager under this target
    /// prefix, which must start with <see cref="TestCredentialPrefixRoot"/>.
    /// </summary>
    public const string TestCredentialPrefixVariable = "LAYERPEEK_TEST_CREDENTIAL_PREFIX";

    /// <summary>
    /// Debug builds only (T-067): monitoring starts this many milliseconds (0 to <see cref="MaxTestStartDelayMilliseconds"/>)
    /// later, so that a test can watch the tray menu in its "Starting…" state.
    /// </summary>
    public const string TestStartDelayVariable = "LAYERPEEK_TEST_START_DELAY_MS";

    /// <summary>
    /// Debug builds only (T-067): the command line mode that removes the toasts of the test app and its Start menu shortcut
    /// after a crashed test run, and nothing else: no app, no single-instance guard, no log; exit code 0, otherwise 1 with
    /// the reason on stderr. macOS (T-020): run as the executable of the test bundle, it removes the delivered and pending
    /// notifications of <see cref="TestBundleIdentifier"/> (refused in any other bundle).
    /// </summary>
    public const string ClearToastsArgument = "--layerpeek-e2e-clear-toasts";

    /// <summary>
    /// Debug builds only (T-019): the command line mode that removes the stand-ins of the test mode's autostart from the test
    /// data folder of <see cref="DataDirectoryVariable"/> after a crashed test run (the Login Item of macOS and the Startup
    /// shortcut of Windows), and nothing else: never <c>SMAppService</c>, the user's Login Items or Startup folder, no app, no
    /// single-instance guard, no log; exit code 0, otherwise 1 with the reason on stderr.
    /// </summary>
    public const string ClearAutostartArgument = "--layerpeek-e2e-clear-autostart";

    /// <summary>
    /// Debug builds only (T-020): the bundle identifier of the test bundle <c>LayerPeek E2E.app</c> the end-to-end tests of
    /// macOS notifications build (production identifier + <c>.E2E</c>); its notifications and permission are the test's own.
    /// </summary>
    public const string TestBundleIdentifier = ProductionBundleIdentifier + ".E2E";

    /// <summary>
    /// Debug builds only (T-020): the command line mode of the test bundle that prints the notification permission and the
    /// number of delivered notifications of the test bundle (<c>permission=&lt;…&gt; delivered=&lt;n&gt;</c>) and nothing else: asks
    /// nothing, no app, no single-instance guard, no log; exit code 0, otherwise 1 with the reason on stderr. Refused
    /// outside the test bundle.
    /// </summary>
    public const string NotificationStatusArgument = "--layerpeek-e2e-notification-status";

    /// <summary>The Start menu shortcut of the test app; also the app name Windows shows in its toasts (T-067).</summary>
    public const string TestShortcutFileName = "LayerPeek E2E.lnk";

    /// <summary>
    /// The COM toast activator of the test app (T-068): never the production one, removed when the test app quits and by
    /// <see cref="ClearToastsArgument"/>.
    /// </summary>
    public const string TestToastActivatorClsid = "EDD3ED5E-8363-4CF4-977A-64A623E0A5A2";

    /// <summary>Every credential target of the test mode starts with this; the production prefix never does.</summary>
    public const string TestCredentialPrefixRoot = "LayerPeek.E2E/";

    /// <summary>The longest start delay of the test mode.</summary>
    public const int MaxTestStartDelayMilliseconds = 60_000;

    /// <summary>
    /// Exit code of a test-mode start without an instance name suffix, with a relative data folder or an invalid test switch,
    /// and of a test start (<see cref="IsTestStart"/>) without the test data folder.
    /// </summary>
    public const int InvalidTestModeExitCode = 4;

    /// <summary>
    /// Every command line argument of the test infrastructure starts with this: the run marker of the end-to-end tests, the
    /// test switches in the command line of the test app's toast activator (T-068), the toast cleanup mode (T-067).
    /// </summary>
    public const string TestArgumentPrefix = "--layerpeek-e2e-";

    /// <summary>
    /// Whether a test made this start: an argument with <see cref="TestArgumentPrefix"/> (any case) or the forward-only
    /// switch of the two-process tests (<see cref="SingleInstance.InstanceTransports.ForwardOnlyVariable"/>). Such a start
    /// runs in the test mode or not at all: without the test data folder it would log into the user's log folder and use the
    /// user's data, so <c>Program.Main</c> ends it with <see cref="InvalidTestModeExitCode"/> before anything is written.
    /// </summary>
    internal static bool IsTestStart(IReadOnlyList<string> arguments, Func<string, string?> read)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(read);
        return arguments.Any(static argument => argument.StartsWith(TestArgumentPrefix, StringComparison.OrdinalIgnoreCase))
            || !string.IsNullOrEmpty(read(SingleInstance.InstanceTransports.ForwardOnlyVariable));
    }

    /// <summary>Test mode with <see cref="TestToastsVariable"/>: real toasts as the test app.</summary>
    public bool TestToasts { get; private init; }

    /// <summary>Test mode with <see cref="TestCredentialPrefixVariable"/>: the Credential Manager prefix; otherwise <see langword="null"/>.</summary>
    public string? TestCredentialPrefix { get; private init; }

    /// <summary>Test mode with <see cref="TestStartDelayVariable"/>: how much later monitoring starts.</summary>
    public TimeSpan TestStartDelay { get; private init; }

    /// <summary>Test switches set outside the test mode: they are ignored, and <c>Program.Main</c> logs them.</summary>
    public IReadOnlyList<string> IgnoredTestSwitches { get; private init; } = [];

    /// <summary>The isolated test mode with its data in <paramref name="dataDirectory"/>.</summary>
    /// <param name="toasts">Real toasts as the test app (T-067).</param>
    /// <param name="credentialPrefix">The Credential Manager instead of the in-memory store, under this prefix (T-067).</param>
    /// <param name="startDelay">How much later monitoring starts (T-067).</param>
    /// <exception cref="ArgumentException">The folder is not absolute, or a switch value is invalid.</exception>
    public static AppEnvironment ForTests(string dataDirectory, bool toasts = false, string? credentialPrefix = null, TimeSpan startDelay = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        if (!Path.IsPathFullyQualified(dataDirectory))
        {
            throw new ArgumentException("The test data folder must be an absolute path.", nameof(dataDirectory));
        }

        if (credentialPrefix is not null && !IsTestCredentialPrefix(credentialPrefix))
        {
            throw new ArgumentException($"The credential prefix of the test mode must start with '{TestCredentialPrefixRoot}'.", nameof(credentialPrefix));
        }

        if (startDelay < TimeSpan.Zero || startDelay > TimeSpan.FromMilliseconds(MaxTestStartDelayMilliseconds))
        {
            throw new ArgumentOutOfRangeException(nameof(startDelay), startDelay, "The start delay of the test mode is out of range.");
        }

        return new AppEnvironment(Path.GetFullPath(dataDirectory), isolated: true)
        {
            TestToasts = toasts,
            TestCredentialPrefix = credentialPrefix,
            TestStartDelay = startDelay,
        };
    }

    /// <summary>
    /// The environment described by the variables <paramref name="read"/> returns; <see cref="FromProcess"/> reads those
    /// of the process. Without the data folder the other test switches are ignored (<see cref="IgnoredTestSwitches"/>).
    /// </summary>
    /// <exception cref="ArgumentException">Test mode: the data folder is not absolute, or a switch value is invalid.</exception>
    internal static AppEnvironment FromVariables(Func<string, string?> read)
    {
        ArgumentNullException.ThrowIfNull(read);
        string? dataDirectory = read(DataDirectoryVariable);
        string? toasts = read(TestToastsVariable);
        string? credentialPrefix = read(TestCredentialPrefixVariable);
        string? startDelay = read(TestStartDelayVariable);
        if (string.IsNullOrWhiteSpace(dataDirectory))
        {
            List<string> ignored = [];
            foreach ((string name, string? value) in new[] { (TestToastsVariable, toasts), (TestCredentialPrefixVariable, credentialPrefix), (TestStartDelayVariable, startDelay) })
            {
                if (!string.IsNullOrEmpty(value))
                {
                    ignored.Add(name);
                }
            }

            return ignored.Count == 0 ? Production : new AppEnvironment(null, isolated: false) { IgnoredTestSwitches = ignored };
        }

        return ForTests(
            dataDirectory.Trim(),
            toasts: toasts == "1",
            credentialPrefix: string.IsNullOrEmpty(credentialPrefix) ? null : credentialPrefix,
            startDelay: ParseStartDelay(startDelay));
    }

    /// <summary>Under the test root, with a name of its own after it; never the production prefix.</summary>
    private static bool IsTestCredentialPrefix(string prefix) =>
        prefix.Length > TestCredentialPrefixRoot.Length
        && prefix.StartsWith(TestCredentialPrefixRoot, StringComparison.Ordinal)
        && !prefix.Any(char.IsWhiteSpace);

    private static TimeSpan ParseStartDelay(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return TimeSpan.Zero;
        }

        if (!int.TryParse(value, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int milliseconds)
            || milliseconds > MaxTestStartDelayMilliseconds)
        {
            throw new ArgumentException($"{TestStartDelayVariable} must be a whole number of milliseconds from 0 to {MaxTestStartDelayMilliseconds}.", nameof(value));
        }

        return TimeSpan.FromMilliseconds(milliseconds);
    }
#endif

    /// <summary>
    /// The environment of this process: <see cref="Production"/>, or (Debug builds only) the test mode when
    /// <c>LAYERPEEK_TEST_DATA_DIR</c> is set.
    /// </summary>
    /// <exception cref="ArgumentException">Debug: the test data folder is not an absolute path, or a test switch is invalid.</exception>
    public static AppEnvironment FromProcess()
    {
#if DEBUG
        return FromVariables(Environment.GetEnvironmentVariable);
#else
        return Production;
#endif
    }
}
