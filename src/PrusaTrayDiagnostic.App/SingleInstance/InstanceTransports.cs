using System.Diagnostics;
using System.Security.Principal;
using System.Text;

using PrusaTrayDiagnostic.App.Composition;

namespace PrusaTrayDiagnostic.App.SingleInstance;

/// <summary>
/// Names of the instance lock and channel per user and session (T-043), and the transport of the current OS.
/// </summary>
internal static class InstanceTransports
{
    public const string BaseName = "PrusaTrayDiagnostic";

#if DEBUG
    /// <summary>
    /// Debug builds only: appended to every name, so that tests (and a developer's second build) never meet the user's
    /// running instance. Release builds always use the production names.
    /// </summary>
    public const string SuffixVariable = "PRUSATRAYDIAGNOSTIC_INSTANCE_SUFFIX";

    /// <summary>
    /// Debug builds only: a start that would become the primary instance exits with <see cref="ForwardOnlyExitCode"/>
    /// instead of starting the app (the two-process test, so that a failure never starts a real app).
    /// </summary>
    public const string ForwardOnlyVariable = "PRUSATRAYDIAGNOSTIC_INSTANCE_FORWARD_ONLY";

    public const int ForwardOnlyExitCode = 3;
#endif

    /// <summary>Longest socket path macOS accepts (<c>sun_path</c> is 104 bytes including the terminator).</summary>
    private const int MaxUnixSocketPathBytes = 103;

    /// <summary>The suffix of the names for this process: always empty in Release builds.</summary>
    public static string NameSuffix
    {
        get
        {
#if DEBUG
            string? suffix = Environment.GetEnvironmentVariable(SuffixVariable);
            return string.IsNullOrWhiteSpace(suffix) ? string.Empty : "-" + suffix.Trim();
#else
            return string.Empty;
#endif
        }
    }

    /// <summary>
    /// <see cref="ForCurrentPlatform"/>, or <see langword="null"/> when the user's SID or data folder is unavailable (the
    /// app then runs unguarded rather than not at all).
    /// </summary>
    /// <param name="environment">Where the macOS lock file lives (the test data folder in test mode); <see langword="null"/>: the user data folder.</param>
    public static IInstanceTransport? TryCreateForCurrentPlatform(string suffix, AppEnvironment? environment = null)
    {
        try
        {
            return ForCurrentPlatform(suffix, environment);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.Security.SecurityException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>The transport of the current OS for the current user and session.</summary>
    /// <param name="environment">Where the macOS lock file lives; <see langword="null"/>: the user data folder.</param>
    public static IInstanceTransport ForCurrentPlatform(string suffix, AppEnvironment? environment = null)
    {
        if (OperatingSystem.IsWindows())
        {
            string sid = WindowsIdentity.GetCurrent().User?.Value
                ?? throw new InvalidOperationException("The current user has no SID.");
            using Process current = Process.GetCurrentProcess();
            (string mutexName, string pipeName) = WindowsNames(sid, current.SessionId, suffix);
            return new NamedPipeInstanceTransport(mutexName, pipeName);
        }

        (string lockPath, string socketPath) = UnixPaths((environment ?? AppEnvironment.Production).DataDirectory(), Path.GetTempPath(), suffix);
        return new UnixSocketInstanceTransport(lockPath, socketPath);
    }

    /// <summary>
    /// <c>Local\PrusaTrayDiagnostic-&lt;SID&gt;</c> (the <c>Local\</c> namespace is per session) and
    /// <c>PrusaTrayDiagnostic-&lt;SID&gt;-&lt;session&gt;</c> (pipe names are machine-wide).
    /// </summary>
    public static (string MutexName, string PipeName) WindowsNames(string sid, int sessionId, string suffix)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sid);
        ArgumentNullException.ThrowIfNull(suffix);
        return ($@"Local\{BaseName}-{sid}{suffix}", $"{BaseName}-{sid}-{sessionId.ToString(System.Globalization.CultureInfo.InvariantCulture)}{suffix}");
    }

    /// <summary>
    /// <c>instance.lock</c> and <c>instance.sock</c> in the app's data folder; the socket goes to the (per-user on macOS)
    /// temp folder when its path would be too long for a Unix socket.
    /// </summary>
    public static (string LockPath, string SocketPath) UnixPaths(string dataDirectory, string tempDirectory, string suffix)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(tempDirectory);
        ArgumentNullException.ThrowIfNull(suffix);
        string lockPath = Path.Combine(dataDirectory, $"instance{suffix}.lock");
        string socketPath = Path.Combine(dataDirectory, $"instance{suffix}.sock");
        if (Encoding.UTF8.GetByteCount(socketPath) > MaxUnixSocketPathBytes)
        {
            socketPath = Path.Combine(tempDirectory, $"{BaseName}-instance{suffix}.sock");
        }

        return (lockPath, socketPath);
    }
}
