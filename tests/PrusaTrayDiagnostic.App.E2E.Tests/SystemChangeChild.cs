using System.Diagnostics;
using System.Globalization;

using PrusaTrayDiagnostic.App.E2E.Tests.Infrastructure;

namespace PrusaTrayDiagnostic.App.E2E.Tests;

/// <summary>
/// The child side of the self-tests in <see cref="SystemChangeTests"/> (T-065): these methods run only in a child test
/// process (<see cref="E2EFixture.ChildVariable"/>) and are skipped everywhere else, except
/// <see cref="GuardProbeChangesOnlyWithTheConfirmedWarning"/>, which is harmless in a confirmed run.
/// </summary>
public sealed class SystemChangeChild
{
    /// <summary>The kind the child changes before it is killed (<c>Probe</c> or <c>Theme</c>).</summary>
    public const string KindVariable = "PTD_E2E_CRASH_KIND";

    /// <summary>File the child creates once its change is applied.</summary>
    public const string ReadyVariable = "PTD_E2E_CRASH_READY";

    /// <summary>Process id of the parent: the child ends (restoring its change) when the parent is gone.</summary>
    public const string ParentVariable = "PTD_E2E_CRASH_PARENT";

    public const string ProbeValue = "changed-by-the-child";

    private static readonly TimeSpan MaxWait = TimeSpan.FromMinutes(2);

    [Fact]
    public async Task ApplyAChangeAndWaitToBeKilled()
    {
        string? kind = Environment.GetEnvironmentVariable(KindVariable);
        Assert.SkipUnless(E2EFixture.IsChildProcess && kind is { Length: > 0 }, "Only in the child test process of SystemChangeTests.");
        string ready = Environment.GetEnvironmentVariable(ReadyVariable) ?? throw new InvalidOperationException($"{ReadyVariable} is not set.");
        int parent = int.Parse(Environment.GetEnvironmentVariable(ParentVariable) ?? throw new InvalidOperationException($"{ParentVariable} is not set."), CultureInfo.InvariantCulture);

        if (kind == SystemChanges.Probe.Name)
        {
            await using SystemChange<ProbeState> probe = await SystemChanges.ProbeAsync(TestContext.Current.CancellationToken);
            await probe.ApplyAsync(new ProbeState(ProbeValue), TestContext.Current.CancellationToken);
            await SignalAndWaitAsync(ready, parent);
        }
        else if (kind == SystemChanges.Theme.Name)
        {
            SystemChanges.Require();
            await using SystemChange<ThemeState> theme = await SystemChanges.ThemeAsync(TestContext.Current.CancellationToken);
            await theme.ApplyAsync(theme.Original.TaskbarIsLight ? ThemeState.Dark : ThemeState.Light, TestContext.Current.CancellationToken);
            await SignalAndWaitAsync(ready, parent);
        }
        else
        {
            throw new InvalidOperationException($"Unknown kind '{kind}'.");
        }
    }

    /// <summary>
    /// The canary of the guard self-test: marked like every test that changes the system, and its probe is handled like a
    /// system change. Without the confirmed warning it must be skipped before the probe changes.
    /// </summary>
    [Fact]
    [Trait(SystemChanges.Trait, SystemChanges.TraitValue)]
    public async Task GuardProbeChangesOnlyWithTheConfirmedWarning()
    {
        SystemChanges.Require();
        await using SystemChange<ProbeState> probe = await SystemChanges.GuardProbeAsync(TestContext.Current.CancellationToken);

        await probe.ApplyAsync(new ProbeState(ProbeValue), TestContext.Current.CancellationToken);

        Assert.Equal(ProbeValue, (await probe.ReadAsync(TestContext.Current.CancellationToken)).Value);
    }

    /// <summary>Tells the parent the change is applied, then waits to be killed (ending when the parent is gone).</summary>
    private static async Task SignalAndWaitAsync(string ready, int parent)
    {
        await File.WriteAllTextAsync(ready, Environment.ProcessId.ToString(CultureInfo.InvariantCulture));
        using Process parentProcess = Process.GetProcessById(parent);
        try
        {
            await parentProcess.WaitForExitAsync().WaitAsync(MaxWait);
        }
        catch (TimeoutException)
        {
            // Not killed in time: the change is restored by the end of the test.
        }
    }
}
