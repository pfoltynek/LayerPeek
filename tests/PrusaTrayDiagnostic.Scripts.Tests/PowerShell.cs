using System.Diagnostics;
using System.Text;

namespace PrusaTrayDiagnostic.Scripts.Tests;

/// <summary>Runs PowerShell 7 (<c>pwsh</c>) as a child process; the whole process tree is killed on timeout.</summary>
internal static class PowerShell
{
    /// <summary>Minimum Pester major version of scripts/ReleaseTools/ReleaseTools.Tests.ps1.</summary>
    public const int MinimumPesterMajor = 5;

    /// <summary>Executable name of PowerShell 7, resolved through PATH.</summary>
    public static string Executable { get; } = OperatingSystem.IsWindows() ? "pwsh.exe" : "pwsh";

    /// <summary>
    /// Why the Pester tests cannot run on this machine (no pwsh, or no Pester 5+), or <see langword="null"/> when they
    /// can. Missing tools are a skip reason, like a missing Edge for the browser tests, not a failure.
    /// </summary>
    public static async Task<string?> PesterSkipReasonAsync(CancellationToken cancellationToken)
    {
        if (!IsOnPath(Executable))
        {
            return $"PowerShell 7 ({Executable}) is not on PATH; see docs/prerequisites.md.";
        }

        const string Probe =
            "$m = Get-Module -ListAvailable Pester | Sort-Object Version -Descending | Select-Object -First 1; " +
            "if ($m) { $m.Version.ToString() } else { 'none' }";
        Result probe = await RunAsync(Probe, environment: null, TimeSpan.FromMinutes(1), cancellationToken);
        string found = probe.Output.Trim();
        if (probe.ExitCode != 0 || !Version.TryParse(found, out Version? version) || version.Major < MinimumPesterMajor)
        {
            return $"Pester {MinimumPesterMajor} or newer is not installed (found: {(found.Length == 0 ? "none" : found)}). " +
                "Install it: Install-Module Pester -Scope CurrentUser -MinimumVersion 5.0.0 (docs/prerequisites.md).";
        }

        return null;
    }

    /// <summary>Runs <paramref name="command"/> with <c>pwsh -NoProfile -NonInteractive -Command</c>.</summary>
    public static async Task<Result> RunAsync(string command, IReadOnlyDictionary<string, string>? environment, TimeSpan timeout, CancellationToken cancellationToken)
    {
        ProcessStartInfo startInfo = new(Executable)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (string argument in new[] { "-NoProfile", "-NonInteractive", "-Command", command })
        {
            startInfo.ArgumentList.Add(argument);
        }

        foreach ((string name, string value) in environment ?? new Dictionary<string, string>())
        {
            startInfo.Environment[name] = value;
        }

        using Process process = Process.Start(startInfo) ?? throw new InvalidOperationException($"{Executable} did not start.");
        Task<string> output = process.StandardOutput.ReadToEndAsync(cancellationToken);
        Task<string> error = process.StandardError.ReadToEndAsync(cancellationToken);
        using CancellationTokenSource timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);
        try
        {
            await process.WaitForExitAsync(timeoutSource.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync(CancellationToken.None);
            throw new TimeoutException($"{Executable} did not finish within {timeout}.{Environment.NewLine}{await output}{await error}");
        }

        return new Result(process.ExitCode, await output, await error);
    }

    private static bool IsOnPath(string executable) =>
        (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(directory => File.Exists(Path.Combine(directory, executable)));

    /// <summary>Exit code and captured output of a pwsh run.</summary>
    internal sealed record Result(int ExitCode, string Output, string Error);
}
