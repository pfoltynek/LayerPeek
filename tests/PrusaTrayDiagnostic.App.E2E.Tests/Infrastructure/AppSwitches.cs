namespace PrusaTrayDiagnostic.App.E2E.Tests.Infrastructure;

/// <summary>
/// The Debug-only switches of the app the tests use (the app is not referenced; the values must match
/// <c>AppEnvironment</c> and <c>InstanceTransports</c> in src/PrusaTrayDiagnostic.App, and
/// <see cref="E2EFixture"/> refuses an exe without them).
/// </summary>
internal static class AppSwitches
{
    /// <summary>Test data folder; switches the app into the isolated test mode (T-044).</summary>
    public const string DataDirectoryVariable = "PRUSATRAYDIAGNOSTIC_TEST_DATA_DIR";

    /// <summary>Suffix of the instance mutex and pipe names (T-043).</summary>
    public const string SuffixVariable = "PRUSATRAYDIAGNOSTIC_INSTANCE_SUFFIX";

    /// <summary>A start that would become the primary instance exits with 3 (T-043).</summary>
    public const string ForwardOnlyVariable = "PRUSATRAYDIAGNOSTIC_INSTANCE_FORWARD_ONLY";

    /// <summary>Native library search probe of T-052.</summary>
    public const string NativeLibraryProbeVariable = "PRUSATRAYDIAGNOSTIC_NATIVE_LIBRARY_PROBE";

    /// <summary>Base of the instance names.</summary>
    public const string BaseName = "PrusaTrayDiagnostic";

    /// <summary>Marker argument of every app process these tests start (ignored by the app).</summary>
    public const string MarkerArgument = "--ptd-e2e-run=";

    /// <summary>AppUserModelID of the app in the test mode (T-044); production uses "PrusaTrayDiagnostic".</summary>
    public const string AppUserModelId = "PrusaTrayDiagnostic.E2E";

    /// <summary>Secrets file of the test mode: key, tab, secret per line.</summary>
    public const string SecretsFileName = "test-secrets.txt";

    /// <summary>The current log file of the test mode, relative to the data folder (T-024).</summary>
    public static readonly string LogFile = Path.Combine("logs", "PrusaTrayDiagnostic.log");
}
