using System.Runtime.InteropServices;

using Microsoft.Win32.SafeHandles;

namespace LayerPeek.App.E2E.Tests.Infrastructure;

/// <summary>
/// Starts a shortcut the way a double-click does (T-072): a <c>.lnk</c> in a test folder (never in the Start menu)
/// opened with <c>ShellExecuteEx</c> from the test process – the shell resolves the shortcut and creates the process,
/// only without File Explorer. The started process inherits the environment of the test process, so the variables of
/// the test mode are set on the test process for the duration of the call (one start at a time) and restored after it.
/// Before the app itself is started this way, <see cref="ExpectInheritedEnvironmentAsync"/> proves with <c>cmd.exe</c>
/// that the variables really arrive: a start without them would run the app in production mode on the user's data.
/// </summary>
internal static partial class ShellStart
{
    private const uint SeeMaskNoCloseProcess = 0x0000_0040;
    private const uint SeeMaskNoAsync = 0x0000_0100;
    private const uint SeeMaskFlagNoUi = 0x0000_0400;
    private const int SwHide = 0;
    private const int SwShowNormal = 1;
    private const uint WaitObject0 = 0;

    private static readonly SemaphoreSlim OneAtATime = new(1, 1);

    /// <summary>Creates the shortcut <paramref name="shortcut"/> to <paramref name="target"/> (folder created when missing).</summary>
    public static void CreateShortcut(string shortcut, string target, string arguments, string workingDirectory)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(shortcut)!);
        Type shellType = Type.GetTypeFromProgID("WScript.Shell") ?? throw new InvalidOperationException("WScript.Shell is not available.");
        object shell = Activator.CreateInstance(shellType)!;
        object? link = null;
        try
        {
            link = ((dynamic)shell).CreateShortcut(shortcut);
            dynamic dynamicLink = link!;
            dynamicLink.TargetPath = target;
            dynamicLink.Arguments = arguments;
            dynamicLink.WorkingDirectory = workingDirectory;
            dynamicLink.Save();
        }
        finally
        {
            if (link is not null)
            {
                Marshal.FinalReleaseComObject(link);
            }

            Marshal.FinalReleaseComObject(shell);
        }
    }

    /// <summary>
    /// Proves that a process started through a shortcut with <see cref="StartAsync"/> gets the variables of the test process:
    /// a shortcut to <c>cmd.exe</c> (hidden) exits with 0 only when it sees <paramref name="variable"/> = <paramref name="value"/>.
    /// </summary>
    public static async Task ExpectInheritedEnvironmentAsync(string folder, string variable, string value)
    {
        Assert.Matches("^[A-Za-z0-9_.-]+$", value);
        string shortcut = Path.Combine(folder, "environment-probe.lnk");
        string cmd = Path.Combine(Environment.SystemDirectory, "cmd.exe");
        CreateShortcut(shortcut, cmd, $"/d /v:on /c if \"!{variable}!\"==\"{value}\" (exit 0) else (exit 9)", folder);
        try
        {
            using StartedProcess probe = await StartAsync(shortcut, new Dictionary<string, string?> { [variable] = value }, SwHide);
            int? exitCode = await probe.WaitForExitAsync(TimeSpan.FromSeconds(10));
            Assert.True(exitCode == 0, $"A process started through a shortcut did not see the variables of the test process (exit code {exitCode?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "none"}); the app would run in production mode.");
        }
        finally
        {
            File.Delete(shortcut);
        }
    }

    /// <summary>
    /// Opens <paramref name="shortcut"/> through the shell (<c>ShellExecuteEx</c>, no error UI) with <paramref name="environment"/>
    /// set on the test process during the call (<see langword="null"/> removes a variable).
    /// </summary>
    public static async Task<StartedProcess> StartAsync(string shortcut, IReadOnlyDictionary<string, string?> environment, int show = SwShowNormal)
    {
        if (!await OneAtATime.WaitAsync(TimeSpan.FromSeconds(60)))
        {
            throw new TimeoutException("An earlier shell start still runs.");
        }

        // The variables are set, the shell called and the variables restored all on the thread that calls the shell, and
        // the next start may begin only after that: a caller that stops waiting (timeout) must never restore the variables
        // while ShellExecuteEx may still create the process, which would then run in production mode on the user's data.
        Dictionary<string, string?> previous = environment.Keys.ToDictionary(static name => name, Environment.GetEnvironmentVariable);
        TaskCompletionSource<StartedProcess> started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Thread thread = new(() =>
        {
            try
            {
                foreach ((string name, string? value) in environment)
                {
                    Environment.SetEnvironmentVariable(name, value);
                }

                started.SetResult(Execute(shortcut, show));
            }
            catch (Exception ex)
            {
                started.SetException(ex);
            }
            finally
            {
                foreach ((string name, string? value) in previous)
                {
                    Environment.SetEnvironmentVariable(name, value);
                }

                OneAtATime.Release();
            }
        })
        {
            IsBackground = true,
            Name = "E2E shell start",
        };

        // The shell resolves shortcuts through COM: a single-threaded apartment, as ShellExecuteEx asks for.
        thread.SetApartmentState(ApartmentState.STA);
        try
        {
            thread.Start();
        }
        catch
        {
            OneAtATime.Release();
            throw;
        }

        try
        {
            return await started.Task.WaitAsync(TimeSpan.FromSeconds(30));
        }
        catch (TimeoutException)
        {
            // A process the shell still creates after the timeout is ended right away, not left behind.
            _ = started.Task.ContinueWith(
                static late => late.Result.Dispose(),
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnRanToCompletion | TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
            throw;
        }
    }

    private static unsafe StartedProcess Execute(string shortcut, int show)
    {
        fixed (char* file = shortcut)
        fixed (char* verb = "open")
        {
            ShellExecuteInfo info = new()
            {
                Size = (uint)sizeof(ShellExecuteInfo),
                Mask = SeeMaskNoCloseProcess | SeeMaskNoAsync | SeeMaskFlagNoUi,
                Verb = verb,
                File = file,
                Show = show,
            };
            if (!ShellExecuteEx(&info))
            {
                throw new InvalidOperationException($"ShellExecuteEx of '{shortcut}' failed ({Marshal.GetLastPInvokeError()}).");
            }

            if (info.Process == 0)
            {
                throw new InvalidOperationException($"ShellExecuteEx of '{shortcut}' started no process the test could wait for.");
            }

            SafeProcessHandle handle = new(info.Process, ownsHandle: true);
            return new StartedProcess(handle, (int)GetProcessId(handle));
        }
    }

    /// <summary>A process started by the shell, by its handle (it may exit before anyone looks it up by id).</summary>
    internal sealed class StartedProcess(SafeProcessHandle handle, int id) : IDisposable
    {
        public int Id { get; } = id;

        /// <summary>The exit code, or <see langword="null"/> when the process still runs after <paramref name="timeout"/>.</summary>
        public async Task<int?> WaitForExitAsync(TimeSpan timeout)
        {
            uint result = await Task.Run(() => WaitForSingleObject(handle, (uint)timeout.TotalMilliseconds));
            if (result != WaitObject0)
            {
                return null;
            }

            return GetExitCodeProcess(handle, out uint exitCode) ? (int)exitCode : throw new InvalidOperationException("GetExitCodeProcess failed.");
        }

        /// <summary>Ends the process when it still runs (a failed test must not leave it behind).</summary>
        public void Kill()
        {
            if (WaitForSingleObject(handle, 0) != WaitObject0)
            {
                TerminateProcess(handle, 1);
                WaitForSingleObject(handle, 5000);
            }
        }

        public void Dispose()
        {
            Kill();
            handle.Dispose();
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private unsafe struct ShellExecuteInfo
    {
        public uint Size;
        public uint Mask;
        public nint Window;
        public char* Verb;
        public char* File;
        public char* Parameters;
        public char* Directory;
        public int Show;
        public nint InstanceApp;
        public nint IdList;
        public char* Class;
        public nint ClassKey;
        public uint HotKey;
        public nint IconOrMonitor;
        public nint Process;
    }

    [LibraryImport("shell32.dll", EntryPoint = "ShellExecuteExW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static unsafe partial bool ShellExecuteEx(ShellExecuteInfo* info);

    [LibraryImport("kernel32.dll")]
    private static partial uint GetProcessId(SafeProcessHandle process);

    [LibraryImport("kernel32.dll")]
    private static partial uint WaitForSingleObject(SafeProcessHandle handle, uint milliseconds);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetExitCodeProcess(SafeProcessHandle process, out uint exitCode);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool TerminateProcess(SafeProcessHandle process, uint exitCode);
}
