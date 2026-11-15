namespace LayerPeek.Tests.Shared;

/// <summary>
/// The CI workflow (.github/workflows/ci.yml, T-218) sets <see cref="Variable"/>=1. Only for tests that are known to fail
/// on a GitHub runner for a tracked reason (each skip names its task); everywhere else, a developer's machine included,
/// they run as before.
/// </summary>
internal static class ContinuousIntegration
{
    public const string Variable = "LAYERPEEK_CI";

    public static bool IsWindowsRunner => OperatingSystem.IsWindows() && Environment.GetEnvironmentVariable(Variable) == "1";
}
