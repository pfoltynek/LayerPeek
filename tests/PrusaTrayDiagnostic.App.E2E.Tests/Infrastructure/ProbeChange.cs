using System.Runtime.Versioning;

using Microsoft.Win32;

namespace PrusaTrayDiagnostic.App.E2E.Tests.Infrastructure;

/// <summary>State of a probe: the value, or <see langword="null"/> when it does not exist.</summary>
internal sealed record ProbeState(string? Value);

/// <summary>
/// A change of a registry value of the tests' own (<c>HKCU\Software\PrusaTrayDiagnostic.E2E.Tests</c>), for the
/// self-tests of the journal machinery (crash, watchdog, stale entries, guard): the same path through the journal as a
/// real change, without touching any setting of Windows. The key is removed once it holds nothing.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class ProbeKind(string name, bool changesSystem) : SystemChangeKind<ProbeState>(name, changesSystem, SystemChangeJson.Default.ProbeState)
{
    public const string SubKey = @"Software\PrusaTrayDiagnostic.E2E.Tests";

    public override Task<ProbeState> ReadStateAsync(string? key, CancellationToken cancellationToken)
    {
        using RegistryKey? probes = Registry.CurrentUser.OpenSubKey(SubKey);
        return Task.FromResult(new ProbeState(probes?.GetValue(Name) as string));
    }

    public override string Describe(string? key, ProbeState original) =>
        $"Test probe HKCU\\{SubKey}\\{Name} = {original.Value ?? "(missing)"}; by hand: delete the key HKCU\\{SubKey}.";

    protected override Task ApplyCoreAsync(string? key, ProbeState state, CancellationToken cancellationToken)
    {
        if (state.Value is not null)
        {
            using RegistryKey probes = Registry.CurrentUser.CreateSubKey(SubKey, writable: true);
            probes.SetValue(Name, state.Value, RegistryValueKind.String);
            return Task.CompletedTask;
        }

        bool empty;
        using (RegistryKey? probes = Registry.CurrentUser.OpenSubKey(SubKey, writable: true))
        {
            if (probes is null)
            {
                return Task.CompletedTask;
            }

            probes.DeleteValue(Name, throwOnMissingValue: false);
            empty = probes.ValueCount == 0 && probes.SubKeyCount == 0;
        }

        if (empty)
        {
            Registry.CurrentUser.DeleteSubKey(SubKey, throwOnMissingSubKey: false);
        }

        return Task.CompletedTask;
    }
}
