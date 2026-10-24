using LayerPeek.App.E2E.Tests.Infrastructure;

namespace LayerPeek.App.E2E.Tests;

/// <summary>
/// The restore of <c>tools/run-e2e.ps1 -RestoreOnly</c> (T-065, layers 3–5: the script's <c>finally</c>, the watchdog
/// and a manual cleanup): the script runs only this method with <see cref="E2EFixture.RestoreVariable"/>=1. It restores
/// every journal entry whose test process is gone, waits a while for test processes that still run, writes what it did
/// to the file in <see cref="ReportVariable"/> (printed by the script) and fails when an entry could not be restored.
/// Entries of a test process that still runs are reported, not restored (it or its watchdog does that). Needs only this
/// test project, not the app.
/// </summary>
public sealed class SystemChangeRestore
{
    /// <summary>Where the report goes (optional).</summary>
    public const string ReportVariable = "LAYERPEEK_E2E_RESTORE_REPORT";

    /// <summary>
    /// A test process that still runs (e.g. ending after Ctrl+C) gets a moment; after that its entries are reported and left
    /// to it or its watchdog (not a failure).
    /// </summary>
    private static readonly TimeSpan OwnerWait = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task RestorePendingSystemChanges()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows() && E2EFixture.IsRestoreProcess, $"Only for tools/run-e2e.ps1 -RestoreOnly ({E2EFixture.RestoreVariable}=1).");

        SystemChangeRestoreReport report = await SystemChanges.Journal.RestorePendingAsync(includeOwn: false, OwnerWait, TestContext.Current.CancellationToken);

        if (Environment.GetEnvironmentVariable(ReportVariable) is { Length: > 0 } file)
        {
            await File.WriteAllTextAsync(file, report.ToString(), TestContext.Current.CancellationToken);
        }

        Assert.True(report.Failed.Count == 0, report.ToString());
    }
}
