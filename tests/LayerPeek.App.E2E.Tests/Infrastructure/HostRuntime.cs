using System.Runtime.InteropServices;

namespace LayerPeek.App.E2E.Tests.Infrastructure;

/// <summary>
/// The runtime identifier of this Windows machine (T-226): <c>win-x64</c> or <c>win-arm64</c>, so that the tests of the
/// installer, the update and the published exe also run on a Windows ARM64 device (T-227). The same rule as
/// <c>Get-HostRuntime</c> of <c>scripts/ReleaseTools/ReleaseTools.psm1</c>, which <c>tools/run-e2e.ps1</c> and
/// <c>tools/windows-verify.ps1</c> use.
/// </summary>
internal static class HostRuntime
{
    /// <summary><c>win-x64</c>, <c>win-arm64</c>, or <see langword="null"/> on any other architecture.</summary>
    public static string? Windows { get; } = ForArchitecture(RuntimeInformation.OSArchitecture);

    /// <summary>The RID of a Windows machine of <paramref name="architecture"/> (<see langword="null"/>: not released).</summary>
    public static string? ForArchitecture(Architecture architecture) => architecture switch
    {
        Architecture.X64 => "win-x64",
        Architecture.Arm64 => "win-arm64",
        _ => null,
    };
}
