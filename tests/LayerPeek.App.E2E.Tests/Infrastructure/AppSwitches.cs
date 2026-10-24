namespace LayerPeek.App.E2E.Tests.Infrastructure;

/// <summary>
/// The Debug-only switches of the app the tests use (the app is not referenced; the values must match
/// <c>AppEnvironment</c> and <c>InstanceTransports</c> in src/LayerPeek.App, and
/// <see cref="E2EFixture"/> refuses an exe without them).
/// </summary>
internal static class AppSwitches
{
    /// <summary>Test data folder; switches the app into the isolated test mode (T-044).</summary>
    public const string DataDirectoryVariable = "LAYERPEEK_TEST_DATA_DIR";

    /// <summary>Suffix of the instance mutex and pipe names (T-043).</summary>
    public const string SuffixVariable = "LAYERPEEK_INSTANCE_SUFFIX";

    /// <summary>A start that would become the primary instance exits with 3 (T-043).</summary>
    public const string ForwardOnlyVariable = "LAYERPEEK_INSTANCE_FORWARD_ONLY";

    /// <summary>Native library search probe of T-052.</summary>
    public const string NativeLibraryProbeVariable = "LAYERPEEK_NATIVE_LIBRARY_PROBE";

    /// <summary>Base of the instance names.</summary>
    public const string BaseName = "LayerPeek";

    /// <summary>Marker argument of every app process these tests start (ignored by the app).</summary>
    public const string MarkerArgument = "--layerpeek-e2e-run=";

    /// <summary>AppUserModelID of the app in the test mode (T-044); production uses "LayerPeek".</summary>
    public const string AppUserModelId = "LayerPeek.E2E";

    /// <summary>Secrets file of the test mode: key, tab, secret per line.</summary>
    public const string SecretsFileName = "test-secrets.txt";

    /// <summary>Text of the Debug-only crash test command (T-046); must not appear in a Release build.</summary>
    public const string CrashTestMessage = "E2E test crash on the UI thread.";

    /// <summary>Real toasts of the test app with its own Start menu shortcut (T-067).</summary>
    public const string TestToastsVariable = "LAYERPEEK_TEST_TOASTS";

    /// <summary>The real Credential Manager under this prefix, which must start with <see cref="TestCredentialPrefixRoot"/> (T-067).</summary>
    public const string TestCredentialPrefixVariable = "LAYERPEEK_TEST_CREDENTIAL_PREFIX";

    /// <summary>Every credential target of the test mode starts with this (T-067).</summary>
    public const string TestCredentialPrefixRoot = "LayerPeek.E2E/";

    /// <summary>Monitoring starts this many milliseconds later, 0 to 60000 (T-067).</summary>
    public const string TestStartDelayVariable = "LAYERPEEK_TEST_START_DELAY_MS";

    /// <summary>
    /// Command line mode of the exe (T-067): removes the toasts of the test app and its Start menu shortcut, starts nothing
    /// else, exits with 0 (otherwise 1 and the reason on stderr).
    /// </summary>
    public const string ClearToastsArgument = "--layerpeek-e2e-clear-toasts";

    /// <summary>The Start menu shortcut of the test app (T-067); the production one is "LayerPeek.lnk".</summary>
    public const string TestShortcutFileName = "LayerPeek E2E.lnk";

    /// <summary>
    /// The COM toast activator of the test app (T-068): registered under <c>HKCU\Software\Classes\CLSID</c> by a test app
    /// with toasts, removed when it quits and by <see cref="ClearToastsArgument"/>.
    /// </summary>
    public const string TestToastActivatorClsid = "EDD3ED5E-8363-4CF4-977A-64A623E0A5A2";

    /// <summary>The COM toast activator of the production app (T-068); the tests never change its registration.</summary>
    public const string ProductionToastActivatorClsid = "EC47413C-984D-447A-866A-5F780E408DE7";

    /// <summary>
    /// One test switch in the command line of the test app's toast activator (T-068), so that a start by Windows for a test
    /// toast comes up in the same test mode.
    /// </summary>
    public const string TestEnvironmentArgument = "--layerpeek-e2e-env=";

    /// <summary>The production Start menu shortcut (T-010); the tests never change it.</summary>
    public const string ProductionShortcutFileName = "LayerPeek.lnk";

    /// <summary>The addresses the test mode would have opened in the browser, one per line, in the data folder (T-067).</summary>
    public const string OpenedUrlsFileName = "opened-urls.txt";

    /// <summary>The current log file of the test mode, relative to the data folder (T-024).</summary>
    public static readonly string LogFile = Path.Combine("logs", "LayerPeek.log");
}
