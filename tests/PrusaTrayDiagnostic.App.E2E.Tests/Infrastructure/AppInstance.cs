using System.Diagnostics;
using System.IO.Pipes;
using System.Security.Principal;
using System.Text;

using FlaUI.Core.AutomationElements;
using FlaUI.UIA3;

namespace PrusaTrayDiagnostic.App.E2E.Tests.Infrastructure;

/// <summary>Which auxiliary window a top-level window of the app is.</summary>
internal enum AppWindowKind
{
    Other,
    Settings,
    Diagnostics,
    Camera,
}

/// <summary>
/// One real app process in the isolated test mode: its own data folder (with the given <c>settings.json</c>), its own
/// instance names and the marker argument. Windows are opened through the Debug-only test commands of the instance
/// channel (T-043/T-044). Disposing quits the app through the channel and kills it if it does not exit; the data folder
/// is deleted.
/// </summary>
internal sealed class AppInstance : IAsyncDisposable
{
    private static readonly TimeSpan StartTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan ExitTimeout = TimeSpan.FromSeconds(20);

    private readonly UIA3Automation _automation;
    private readonly Dictionary<nint, AppWindowKind> _kinds = [];
    private bool _quit;

    private AppInstance(UIA3Automation automation, string runDirectory, string suffix, Process process)
    {
        _automation = automation;
        RunDirectory = runDirectory;
        Suffix = suffix;
        Process = process;
    }

    /// <summary>Label of the run (log file name).</summary>
    public required string Name { get; init; }

    /// <summary>Data folder and marker folder of this app.</summary>
    public string RunDirectory { get; }

    public string DataDirectory => Path.Combine(RunDirectory, "data");

    /// <summary>The instance name suffix (without the leading dash the app adds).</summary>
    public string Suffix { get; }

    public Process Process { get; }

    public UIA3Automation Automation => _automation;

    /// <summary>Starts the app with <paramref name="settingsJson"/> and optional seeded secrets (key, secret).</summary>
    /// <param name="name">Label of the run: its log is kept as <c>log-&lt;name&gt;.txt</c> in the results folder.</param>
    public static async Task<AppInstance> StartAsync(E2EFixture fixture, string name, string settingsJson, IReadOnlyDictionary<string, string>? secrets = null)
    {
        string run = Path.Combine(E2EFixture.Root, "run-" + Guid.NewGuid().ToString("N")[..12]);
        string data = Path.Combine(run, "data");
        Directory.CreateDirectory(data);
        await File.WriteAllTextAsync(Path.Combine(data, "settings.json"), settingsJson);
        if (secrets is not null)
        {
            await File.WriteAllLinesAsync(Path.Combine(data, AppSwitches.SecretsFileName), secrets.Select(static pair => $"{pair.Key}\t{pair.Value}"));
        }

        string suffix = "e2e-" + Guid.NewGuid().ToString("N")[..12];
        Process process = Process.Start(StartInfo(run, suffix)) ?? throw new InvalidOperationException("The app did not start.");
        AppInstance app = new(fixture.Automation, run, suffix, process) { Name = name };
        try
        {
            // The channel exists once the single-instance guard runs (before Avalonia); commands wait for the host.
            await Wait.UntilAsync(async () => !process.HasExited && await app.TryConnectAsync(), StartTimeout, "the instance channel of the started app");
            return app;
        }
        catch
        {
            await app.DisposeAsync();
            throw;
        }
    }

    /// <summary>How a (first or second) start of the app under test is launched.</summary>
    public static ProcessStartInfo StartInfo(string runDirectory, string suffix)
    {
        ProcessStartInfo start = new(E2EFixture.AppPath)
        {
            UseShellExecute = false,
            WorkingDirectory = runDirectory,
        };
        start.ArgumentList.Add(AppSwitches.MarkerArgument + runDirectory);
        start.Environment[AppSwitches.DataDirectoryVariable] = Path.Combine(runDirectory, "data");
        start.Environment[AppSwitches.SuffixVariable] = suffix;
        start.Environment.Remove(AppSwitches.ForwardOnlyVariable);
        return start;
    }

    /// <summary>A second start of the same app (same data folder and instance names).</summary>
    public ProcessStartInfo SecondStartInfo() => StartInfo(RunDirectory, Suffix);

    /// <summary>Sends a test command (<c>settings</c>, <c>diagnostics[:id]</c>, <c>about</c>, <c>camera:id</c>, <c>quit</c>).</summary>
    public async Task SendAsync(string command)
    {
        Exception? last = null;
        for (int attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                await SendOnceAsync(command);
                return;
            }
            catch (Exception ex) when (ex is IOException or TimeoutException or OperationCanceledException)
            {
                last = ex;
                await Task.Delay(200);
            }
        }

        throw new InvalidOperationException($"The app did not take the command '{command}'.", last);
    }

    /// <summary>Visible top-level windows of the app, from the top of the z-order down.</summary>
    public List<nint> Windows() => Native.TopLevelWindows(Process.Id);

    public AppWindowKind KindOf(nint hwnd)
    {
        if (_kinds.TryGetValue(hwnd, out AppWindowKind kind))
        {
            return kind;
        }

        AutomationElement element = Element(hwnd);
        kind = element.FindFirstDescendant(cf => cf.ByAutomationId("SettingsTabs")) is not null ? AppWindowKind.Settings
            : element.FindFirstDescendant(cf => cf.ByAutomationId("DiagnosticsClose")) is not null ? AppWindowKind.Diagnostics
            // Every camera mode shows the mini preview or the normal window button (full screen shows both).
            : element.FindFirstDescendant(cf => cf.ByAutomationId("CameraMiniButton").Or(cf.ByAutomationId("CameraNormalButton"))) is not null ? AppWindowKind.Camera
            : AppWindowKind.Other;
        if (kind != AppWindowKind.Other)
        {
            _kinds[hwnd] = kind;
        }

        return kind;
    }

    public AutomationElement Element(nint hwnd) => _automation.FromHandle(hwnd);

    /// <summary>Waits for a visible window of <paramref name="kind"/> that is not in <paramref name="except"/>.</summary>
    public async Task<nint> WaitForWindowAsync(AppWindowKind kind, IReadOnlyCollection<nint>? except = null, TimeSpan? timeout = null)
    {
        nint found = 0;
        await Wait.UntilAsync(
            () =>
            {
                if (Process.HasExited)
                {
                    throw new InvalidOperationException($"The app exited with {Process.ExitCode}.");
                }

                foreach (nint hwnd in Windows())
                {
                    if ((except is null || !except.Contains(hwnd)) && KindOf(hwnd) == kind)
                    {
                        found = hwnd;
                        return true;
                    }
                }

                return false;
            },
            timeout ?? TimeSpan.FromSeconds(20),
            $"a {kind} window");
        return found;
    }

    /// <summary>
    /// Opens a window by command and waits for it. A camera window needs its printer among the monitored ones, which the
    /// app knows a moment after its start (like the tray menu, which shows the item only then): until a camera window
    /// appears, the command is sent again.
    /// </summary>
    public async Task<nint> OpenAsync(string command, AppWindowKind kind)
    {
        List<nint> before = Windows().Where(hwnd => KindOf(hwnd) == kind).ToList();
        int attempts = kind == AppWindowKind.Camera ? 5 : 1;
        for (int attempt = 1; ; attempt++)
        {
            await SendAsync(command);
            try
            {
                return await WaitForWindowAsync(kind, before, attempt < attempts ? TimeSpan.FromSeconds(3) : null);
            }
            catch (TimeoutException) when (attempt < attempts)
            {
                // Not monitored yet: nothing opened, so the toggle is still "open".
            }
        }
    }

    /// <summary>Closes a window through its system menu close (WM_CLOSE via UIA WindowPattern) and waits until it is gone.</summary>
    public async Task CloseWindowAsync(nint hwnd)
    {
        Element(hwnd).Patterns.Window.Pattern.Close();
        await Wait.UntilAsync(() => !Native.IsWindowVisible(hwnd), TimeSpan.FromSeconds(10), "the window to close");
        _kinds.Remove(hwnd);
    }

    /// <summary>Quits the app (tray Quit through the channel) and waits for the exit.</summary>
    public async Task QuitAsync()
    {
        if (_quit || Process.HasExited)
        {
            _quit = true;
            return;
        }

        _quit = true;
        await SendAsync("quit");
        using CancellationTokenSource timeout = new(ExitTimeout);
        await Process.WaitForExitAsync(timeout.Token);
    }

    public async ValueTask DisposeAsync()
    {
        Exception? failure = null;
        try
        {
            if (!Process.HasExited)
            {
                await QuitAsync();
            }
        }
        catch (Exception ex)
        {
            failure = ex;
        }
        finally
        {
            if (!Process.HasExited)
            {
                Process.Kill(entireProcessTree: true);
                await Process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
            }

            Process.Dispose();
            KeepLog();
            await DeleteRunDirectoryAsync();
        }

        if (failure is not null)
        {
            throw new InvalidOperationException("The app did not quit cleanly; it was killed.", failure);
        }
    }

    /// <summary>The names of the instance channel of this app (see InstanceTransports.WindowsNames in the app).</summary>
    public string PipeName()
    {
        string sid = WindowsIdentity.GetCurrent().User?.Value ?? throw new InvalidOperationException("No SID.");
        using Process current = Process.GetCurrentProcess();
        return $"{AppSwitches.BaseName}-{sid}-{current.SessionId.ToString(System.Globalization.CultureInfo.InvariantCulture)}-{Suffix}";
    }

    private async Task<bool> TryConnectAsync()
    {
        try
        {
            await using NamedPipeClientStream pipe = new(".", PipeName(), PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            await pipe.ConnectAsync(200);
            // An unknown word: the server logs and drops it at once (an empty connection would hold it for its read timeout).
            await pipe.WriteAsync("e2e-ping\n"u8.ToArray());
            await pipe.FlushAsync();
            return true;
        }
        catch (Exception ex) when (ex is TimeoutException or IOException)
        {
            return false;
        }
    }

    private async Task SendOnceAsync(string command)
    {
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(5));
        await using NamedPipeClientStream pipe = new(".", PipeName(), PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        await pipe.ConnectAsync(timeout.Token);
        await pipe.WriteAsync(Encoding.ASCII.GetBytes(command + "\n"), timeout.Token);
        await pipe.FlushAsync(timeout.Token);
        byte[] reply = new byte[1];
        int read = await pipe.ReadAsync(reply, timeout.Token);
        if (read != 1 || reply[0] != (byte)'\n')
        {
            throw new IOException($"The app did not acknowledge '{command}'.");
        }
    }

    /// <summary>The app's test log (what it did, any errors) goes to the results next to the screenshots.</summary>
    private void KeepLog()
    {
        string log = Path.Combine(DataDirectory, AppSwitches.LogFile);
        if (File.Exists(log))
        {
            File.Copy(log, Path.Combine(E2EFixture.ResultsDirectory, $"log-{Name}.txt"), overwrite: true);
        }
    }

    /// <summary>The app's test log so far (for assertion messages).</summary>
    public string ReadLog()
    {
        string log = Path.Combine(DataDirectory, AppSwitches.LogFile);
        try
        {
            using FileStream stream = new(log, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using StreamReader reader = new(stream);
            return reader.ReadToEnd();
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return string.Empty;
        }
    }

    private async Task DeleteRunDirectoryAsync()
    {
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                if (Directory.Exists(RunDirectory))
                {
                    Directory.Delete(RunDirectory, recursive: true);
                }

                return;
            }
            catch (Exception) when (attempt < 10)
            {
                await Task.Delay(200);
            }
        }
    }
}
