using System.Diagnostics;

namespace LayerPeek.App.E2E.Tests.Infrastructure;

/// <summary>
/// The only way these tests start the app (T-074 safety fix): every start must carry the complete test mode, i.e. the run
/// marker <c>--layerpeek-e2e-run=&lt;run&gt;</c> with an absolute run folder, the test data folder <c>&lt;run&gt;\data</c>
/// and an instance name suffix. A start without them would run the app in production mode (the user's data, log folder and
/// instance names), so it is refused before any process exists. The app itself refuses such a start too (exit code 4).
/// </summary>
internal static class TestLaunch
{
    /// <summary>Starts the app under test after <see cref="Verify(ProcessStartInfo)"/>.</summary>
    /// <exception cref="InvalidOperationException">The start is not a complete test-mode start.</exception>
    public static Process Start(ProcessStartInfo start)
    {
        Verify(start);
        return Process.Start(start) ?? throw new InvalidOperationException($"'{start.FileName}' did not start.");
    }

    /// <summary>Refuses a start of the app that does not carry the complete test mode.</summary>
    /// <exception cref="InvalidOperationException">The start is not a complete test-mode start.</exception>
    public static void Verify(ProcessStartInfo start)
    {
        ArgumentNullException.ThrowIfNull(start);
        if (start.UseShellExecute)
        {
            // The shell would not pass start.Environment on; the variables would come from the test process instead.
            throw new InvalidOperationException("The app under test is never started through the shell with a ProcessStartInfo.");
        }

        if (!string.IsNullOrEmpty(start.Arguments))
        {
            throw new InvalidOperationException("The app under test takes its arguments through ArgumentList only.");
        }

        ThrowIfProblem([.. start.ArgumentList], name => start.Environment.TryGetValue(name, out string? value) ? value : null);
    }

    /// <summary>
    /// Refuses a start through the shell (<see cref="ShellStart"/>): <paramref name="arguments"/> of the shortcut, and the
    /// environment the started process gets, i.e. <paramref name="overrides"/> over the variables of the test process.
    /// </summary>
    /// <exception cref="InvalidOperationException">The start is not a complete test-mode start.</exception>
    public static void VerifyShellStart(IReadOnlyList<string> arguments, IReadOnlyDictionary<string, string?> overrides)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(overrides);
        ThrowIfProblem(arguments, name => overrides.TryGetValue(name, out string? value) ? value : Environment.GetEnvironmentVariable(name));
    }

    /// <summary>What is missing for a complete test-mode start, or <see langword="null"/> when nothing is.</summary>
    internal static string? Problem(IReadOnlyList<string> arguments, Func<string, string?> read)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(read);
        string[] markers = [.. arguments.Where(static argument => argument.StartsWith(AppSwitches.MarkerArgument, StringComparison.OrdinalIgnoreCase))];
        if (markers.Length != 1)
        {
            return $"exactly one run marker {AppSwitches.MarkerArgument}<run folder> is required (found {markers.Length})";
        }

        string run = markers[0][AppSwitches.MarkerArgument.Length..];
        if (!Path.IsPathFullyQualified(run))
        {
            return $"the run folder of the marker '{run}' is not an absolute path";
        }

        string? data = read(AppSwitches.DataDirectoryVariable);
        if (string.IsNullOrWhiteSpace(data))
        {
            return $"{AppSwitches.DataDirectoryVariable} is not set (the app would run in production mode)";
        }

        string expected = Path.Combine(Path.TrimEndingDirectorySeparator(Path.GetFullPath(run)), "data");
        if (!Path.IsPathFullyQualified(data) || !string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(data)), expected, StringComparison.OrdinalIgnoreCase))
        {
            return $"{AppSwitches.DataDirectoryVariable} '{data}' is not the data folder of the run '{expected}'";
        }

        if (string.IsNullOrWhiteSpace(read(AppSwitches.SuffixVariable)))
        {
            return $"{AppSwitches.SuffixVariable} is not set (the app would meet the user's instance)";
        }

        return null;
    }

    private static void ThrowIfProblem(IReadOnlyList<string> arguments, Func<string, string?> read)
    {
        if (Problem(arguments, read) is { } problem)
        {
            throw new InvalidOperationException($"Refused to start the app under test outside the test mode: {problem}.");
        }
    }
}
