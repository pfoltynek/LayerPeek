using System.Diagnostics;
using System.Globalization;
using System.Runtime.Versioning;
using System.Text;
using System.Xml.Linq;

namespace LayerPeek.App.E2E.Tests.Infrastructure;

/// <summary>One toast of the test app in Windows' notification history: its tag, group and content (the toast XML).</summary>
internal sealed record HistoryToast(string Tag, string Group, string Xml)
{
    /// <summary>The parsed content.</summary>
    public XElement Content => XElement.Parse(Xml);

    /// <summary>The texts of the toast (title first), as the app wrote them.</summary>
    public IReadOnlyList<string> Texts => [.. Content.Descendants("text").Select(static text => text.Value)];

    /// <summary>
    /// Whether the toast is silent: an <c>&lt;audio silent="true"/&gt;</c> element. Without an <c>&lt;audio&gt;</c> element
    /// Windows plays the default notification sound (the app's convention, <c>ToastContentBuilder</c>).
    /// </summary>
    public bool IsSilent => Content.Elements("audio").Any(static audio => string.Equals((string?)audio.Attribute("silent"), "true", StringComparison.OrdinalIgnoreCase));

    /// <summary>The launch argument a click hands to the app.</summary>
    public string? Launch => (string?)Content.Attribute("launch");
}

/// <summary>
/// The notification history of the test app <see cref="AppSwitches.AppUserModelId"/> (T-128): what Windows keeps of its
/// toasts in the notification center, read as the toasts' own XML (the sound of a toast is nowhere on screen). Through
/// Windows PowerShell 5.1, which has the WinRT projection of <c>ToastNotificationManager</c> (this project targets
/// <c>net11.0-windows</c> without it), like the installer test (T-017). Only the test app id is ever read, never another
/// app's notifications.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class ToastHistory
{
    private const string AppIdVariable = "LAYERPEEK_E2E_HISTORY_APP_ID";

    private const string Script = """
        $ErrorActionPreference = 'Stop'
        $null = [Windows.UI.Notifications.ToastNotificationManager, Windows.UI.Notifications, ContentType = WindowsRuntime]
        $items = @([Windows.UI.Notifications.ToastNotificationManager]::History.GetHistory($env:LAYERPEEK_E2E_HISTORY_APP_ID))
        foreach ($toast in $items) {
            $line = [string]$toast.Tag + "`t" + [string]$toast.Group + "`t" + $toast.Content.GetXml()
            [Console]::Out.WriteLine('TOAST ' + [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($line)))
        }
        [Console]::Out.WriteLine('COUNT ' + $items.Count)
        """;

    /// <summary>The toasts of the test app in the notification history.</summary>
    public static async Task<IReadOnlyList<HistoryToast>> ReadAsync(CancellationToken cancellationToken)
    {
        string appId = AppSwitches.AppUserModelId;
        if (!appId.EndsWith(".E2E", StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"'{appId}' is not the app id of the test app; refusing to read its notifications.");
        }

        string powershell = Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");
        ProcessStartInfo start = new(powershell) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (string argument in new[] { "-NoProfile", "-NonInteractive", "-EncodedCommand", Convert.ToBase64String(Encoding.Unicode.GetBytes(Script)) })
        {
            start.ArgumentList.Add(argument);
        }

        start.Environment[AppIdVariable] = appId;
        using Process process = Process.Start(start) ?? throw new InvalidOperationException("Windows PowerShell did not start.");
        Task<string> error = process.StandardError.ReadToEndAsync(cancellationToken);
        string output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken).WaitAsync(TimeSpan.FromSeconds(60), cancellationToken);
        return Parse(output) ?? throw new InvalidOperationException($"Reading the toast history failed (exit code {process.ExitCode}): {await error}");
    }

    /// <summary>The toasts of the script's output, or <see langword="null"/> when it did not end with its count.</summary>
    internal static IReadOnlyList<HistoryToast>? Parse(string output)
    {
        List<HistoryToast> toasts = [];
        int? count = null;
        foreach (string line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (line.StartsWith("TOAST ", StringComparison.Ordinal))
            {
                string[] parts = Encoding.UTF8.GetString(Convert.FromBase64String(line["TOAST ".Length..])).Split('\t', 3);
                if (parts.Length == 3)
                {
                    toasts.Add(new HistoryToast(parts[0], parts[1], parts[2]));
                }
            }
            else if (line.StartsWith("COUNT ", StringComparison.Ordinal)
                && int.TryParse(line["COUNT ".Length..], NumberStyles.None, CultureInfo.InvariantCulture, out int parsed))
            {
                count = parsed;
            }
        }

        return count == toasts.Count ? toasts : null;
    }
}
