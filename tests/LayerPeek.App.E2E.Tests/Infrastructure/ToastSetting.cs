using System.Diagnostics;
using System.Runtime.Versioning;
using System.Text;

namespace LayerPeek.App.E2E.Tests.Infrastructure;

/// <summary>
/// The effective notification setting of the test app <see cref="AppSwitches.AppUserModelId"/> (T-133):
/// <c>ToastNotifier.Setting</c>, what Windows' notification service (its database <c>wpndatabase.db</c>) decides, not
/// the registry key of the app's settings. The service takes a value of the key over, but deleting the key does not reset
/// it: a key deleted while <c>Enabled</c> = 0 leaves the app switched off (T-133). Read through Windows PowerShell 5.1 like
/// <see cref="ToastHistory"/>; readable without the Start menu shortcut once Windows knows the app id. Only the test app
/// id is ever read.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class ToastSetting
{
    /// <summary>The value of <c>NotificationSetting</c> when the app's notifications are on.</summary>
    public const string Enabled = "Enabled";

    /// <summary>The value when the app's own switch in Settings &gt; System &gt; Notifications is off.</summary>
    public const string DisabledForApplication = "DisabledForApplication";

    /// <summary>What <see cref="ReadAsync"/> returns for an app id Windows does not know (the property is null then).</summary>
    public const string Unknown = "(unknown)";

    private const string AppIdVariable = "LAYERPEEK_E2E_SETTING_APP_ID";

    private static readonly TimeSpan ReadTimeout = TimeSpan.FromSeconds(30);

    private const string Script = """
        $ErrorActionPreference = 'Stop'
        $null = [Windows.UI.Notifications.ToastNotificationManager, Windows.UI.Notifications, ContentType = WindowsRuntime]
        $setting = [Windows.UI.Notifications.ToastNotificationManager]::CreateToastNotifier($env:LAYERPEEK_E2E_SETTING_APP_ID).Setting
        if ($null -eq $setting) { [Console]::Out.WriteLine('SETTING (unknown)') } else { [Console]::Out.WriteLine('SETTING ' + [string]$setting) }
        """;

    /// <summary>
    /// The effective setting of the test app (<see cref="Enabled"/>, <see cref="DisabledForApplication"/>,
    /// <c>DisabledForUser</c>, …, <see cref="Unknown"/>), or <c>(not readable: …)</c>; never throws but for cancellation.
    /// </summary>
    public static async Task<string> ReadAsync(CancellationToken cancellationToken)
    {
        string appId = AppSwitches.AppUserModelId;
        if (!appId.EndsWith(".E2E", StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"'{appId}' is not the app id of the test app; refusing to read its notification setting.");
        }

        try
        {
            (int exitCode, string output, string error) = await WindowsPowerShell.RunAsync(Script, new Dictionary<string, string> { [AppIdVariable] = appId }, ReadTimeout, cancellationToken).ConfigureAwait(false);
            return Parse(output) ?? $"(not readable: exit code {exitCode}{(error.Length == 0 ? string.Empty : ", " + error)})";
        }
        catch (Exception ex) when (ex is TimeoutException or InvalidOperationException or System.ComponentModel.Win32Exception or IOException)
        {
            return $"(not readable: {ex.GetType().Name}: {ex.Message})";
        }
    }

    /// <summary>The setting of the script's output, or <see langword="null"/> when it wrote none.</summary>
    internal static string? Parse(string output)
    {
        foreach (string line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (line.StartsWith("SETTING ", StringComparison.Ordinal) && line["SETTING ".Length..].Trim() is { Length: > 0 } setting)
            {
                return setting;
            }
        }

        return null;
    }

    /// <summary>Whether <paramref name="setting"/> is a read value that switches the app's notifications off.</summary>
    public static bool IsDisabled(string setting) => setting.StartsWith("Disabled", StringComparison.Ordinal);

    /// <summary>
    /// Reads with <paramref name="read"/> until it returns <paramref name="wanted"/> or <paramref name="timeout"/> is over
    /// (at least once); returns the last value read.
    /// </summary>
    public static async Task<string> WaitForAsync(Func<CancellationToken, Task<string>> read, string wanted, TimeSpan timeout, TimeSpan interval, CancellationToken cancellationToken)
    {
        Stopwatch elapsed = Stopwatch.StartNew();
        while (true)
        {
            string setting = await read(cancellationToken).ConfigureAwait(false);
            if (setting == wanted || elapsed.Elapsed >= timeout)
            {
                return setting;
            }

            await Task.Delay(interval, cancellationToken).ConfigureAwait(false);
        }
    }
}

/// <summary>
/// Runs a script in Windows PowerShell 5.1, which has the WinRT projection of the notification APIs (this project targets
/// <c>net11.0-windows</c> without it).
/// </summary>
[SupportedOSPlatform("windows")]
internal static class WindowsPowerShell
{
    public static string Exe => Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");

    /// <summary>Exit code, standard output and trimmed standard error of <paramref name="script"/>; the process is killed after <paramref name="timeout"/>.</summary>
    public static async Task<(int ExitCode, string Output, string Error)> RunAsync(string script, IReadOnlyDictionary<string, string> environment, TimeSpan timeout, CancellationToken cancellationToken)
    {
        ProcessStartInfo start = new(Exe) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (string argument in new[] { "-NoProfile", "-NonInteractive", "-EncodedCommand", Convert.ToBase64String(Encoding.Unicode.GetBytes(script)) })
        {
            start.ArgumentList.Add(argument);
        }

        foreach ((string name, string value) in environment)
        {
            start.Environment[name] = value;
        }

        using Process process = Process.Start(start) ?? throw new InvalidOperationException("Windows PowerShell did not start.");
        try
        {
            Task<string> error = process.StandardError.ReadToEndAsync(cancellationToken);
            Task<string> output = process.StandardOutput.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken).WaitAsync(timeout, cancellationToken).ConfigureAwait(false);
            return (process.ExitCode, await output.ConfigureAwait(false), (await error.ConfigureAwait(false)).Trim());
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
    }
}
