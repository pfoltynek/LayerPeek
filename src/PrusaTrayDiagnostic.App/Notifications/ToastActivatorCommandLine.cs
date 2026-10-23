using System.Text;

#if DEBUG
using PrusaTrayDiagnostic.App.Composition;
using PrusaTrayDiagnostic.App.SingleInstance;
#endif

namespace PrusaTrayDiagnostic.App.Notifications;

/// <summary>
/// The command line of the COM toast activator (T-068): the <c>LocalServer32</c> value Windows starts the app with when
/// the user clicks a toast in the notification center while the app does not run, and how a start recognizes such a start.
/// COM itself appends <c>-Embedding</c> to a server it starts, so both markers count.
/// <para>
/// Debug builds: the command line of the test app also carries its test mode (<c>--ptd-e2e-env=NAME=value</c>, only the
/// test switches) and its own arguments, so that a start by Windows for a test toast comes up as the same isolated test
/// app and never as the user's production app.
/// </para>
/// </summary>
internal static class ToastActivatorCommandLine
{
    /// <summary>The marker in the <c>LocalServer32</c> command line.</summary>
    public const string ToastActivatedArgument = "-ToastActivated";

    private static readonly string[] ActivationArguments = [ToastActivatedArgument, "-Embedding", "/Embedding"];

    /// <summary>Whether Windows (COM) started this process for a toast activation.</summary>
    public static bool IsActivationStart(IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        return arguments.Any(IsActivationArgument);
    }

    /// <summary>The <c>LocalServer32</c> command line: the quoted exe, the marker and <paramref name="extraArguments"/>.</summary>
    public static string For(string executablePath, IEnumerable<string> extraArguments)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        ArgumentNullException.ThrowIfNull(extraArguments);
        StringBuilder line = new();
        // Always quoted: a path with spaces would otherwise run another program (C:\Program.exe).
        line.Append('"').Append(executablePath).Append('"').Append(' ').Append(ToastActivatedArgument);
        foreach (string argument in extraArguments)
        {
            line.Append(' ').Append(Quote(argument));
        }

        return line.ToString();
    }

    /// <summary>
    /// <paramref name="argument"/> the way Windows (<c>CommandLineToArgvW</c>, the .NET runtime) splits it back: quoted when
    /// it is empty or holds white space or quotes; backslashes before a quote and at the end are doubled.
    /// </summary>
    public static string Quote(string argument)
    {
        ArgumentNullException.ThrowIfNull(argument);
        if (argument.Length > 0 && argument.IndexOfAny([' ', '\t', '\n', '\v', '"']) < 0)
        {
            return argument;
        }

        StringBuilder quoted = new(argument.Length + 2);
        quoted.Append('"');
        int backslashes = 0;
        foreach (char c in argument)
        {
            if (c == '\\')
            {
                backslashes++;
                continue;
            }

            // Backslashes before a quote escape it: double them, then escape the quote itself.
            quoted.Append('\\', c == '"' ? (backslashes * 2) + 1 : backslashes);
            backslashes = 0;
            quoted.Append(c);
        }

        // Before the closing quote, too.
        quoted.Append('\\', backslashes * 2);
        quoted.Append('"');
        return quoted.ToString();
    }

    private static bool IsActivationArgument(string argument) =>
        ActivationArguments.Contains(argument, StringComparer.OrdinalIgnoreCase);

#if DEBUG
    /// <summary>Debug builds only: one switch of the test mode in the command line of the test app's activator.</summary>
    public const string TestEnvironmentPrefix = "--ptd-e2e-env=";

    /// <summary>The variables a test app passes on (never the forward-only switch of the two-process test, nothing else).</summary>
    private static readonly string[] TestVariables =
    [
        AppEnvironment.DataDirectoryVariable,
        InstanceTransports.SuffixVariable,
        AppEnvironment.TestToastsVariable,
        AppEnvironment.TestCredentialPrefixVariable,
        AppEnvironment.TestStartDelayVariable,
    ];

    /// <summary>
    /// The extra arguments of the test app's activator: each set test switch (<paramref name="read"/>), then the arguments
    /// of this process (the test marker) without those of an earlier start by Windows.
    /// </summary>
    public static List<string> TestServerArguments(IReadOnlyList<string> processArguments, Func<string, string?> read)
    {
        ArgumentNullException.ThrowIfNull(processArguments);
        ArgumentNullException.ThrowIfNull(read);
        List<string> arguments = [];
        foreach (string name in TestVariables)
        {
            if (read(name) is { Length: > 0 } value)
            {
                arguments.Add(TestEnvironmentPrefix + name + "=" + value);
            }
        }

        arguments.AddRange(processArguments.Where(static argument => !IsActivationArgument(argument) && !IsTestEnvironmentArgument(argument)));
        return arguments;
    }

    /// <summary>Whether the command line carries a test mode (then it must come up as that test mode or not at all).</summary>
    public static bool HasTestEnvironment(IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        return arguments.Any(IsTestEnvironmentArgument);
    }

    /// <summary>
    /// Sets the test switches of the command line in this process (before anything reads them). Any other variable, or a
    /// malformed switch, refuses the whole command line: nothing is set.
    /// </summary>
    /// <returns><see langword="false"/> when the command line was refused.</returns>
    public static bool TryApplyTestEnvironment(IReadOnlyList<string> arguments, Action<string, string> set)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(set);
        List<(string Name, string Value)> values = [];
        foreach (string argument in arguments.Where(IsTestEnvironmentArgument))
        {
            string assignment = argument[TestEnvironmentPrefix.Length..];
            int equals = assignment.IndexOf('=', StringComparison.Ordinal);
            if (equals <= 0 || !TestVariables.Contains(assignment[..equals], StringComparer.Ordinal))
            {
                return false;
            }

            values.Add((assignment[..equals], assignment[(equals + 1)..]));
        }

        foreach ((string name, string value) in values)
        {
            set(name, value);
        }

        return true;
    }

    private static bool IsTestEnvironmentArgument(string argument) => argument.StartsWith(TestEnvironmentPrefix, StringComparison.Ordinal);
#endif
}
