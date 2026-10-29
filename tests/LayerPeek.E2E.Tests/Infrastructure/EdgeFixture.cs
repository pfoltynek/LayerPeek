using Microsoft.Playwright;

using LayerPeek.E2E.Tests.Infrastructure;
using LayerPeek.Tests.Shared;

[assembly: AssemblyFixture(typeof(EdgeFixture))]

namespace LayerPeek.E2E.Tests.Infrastructure;

/// <summary>
/// The installed Microsoft Edge (Playwright <c>Channel = "msedge"</c>, headless) for the whole test assembly. Nothing is
/// ever downloaded: without Edge every browser test is skipped (<see cref="Assert.Skip(string)"/>).
/// <para>
/// Everything the driver and the browser write (profiles, artifacts, temporary files) lies under
/// <see cref="Root"/>, which is removed before the first test (leftovers of a crashed run, including still running
/// browser processes of that run) and after the last one. <see cref="Root"/> is reserved per worktree: concurrent runs
/// in the same worktree are serialized by its lock, runs in other worktrees use their own folder, lock and processes.
/// </para>
/// </summary>
public sealed class EdgeFixture : IAsyncLifetime
{
    /// <summary>
    /// The reservation of this assembly in %TEMP% for this worktree. Before a folder of it is deleted (the own one, one of
    /// a deleted worktree or the un-hashed one of older runs, each only under its lock) the browser processes whose
    /// profile lies in that folder are killed; processes of another existing worktree never match.
    /// </summary>
    private static readonly ReservedTempDirectory Reserved = ReservedTempDirectory.ForCurrentWorktree(
        "LayerPeek.E2E.Tests",
        legacyDirectoryName: "LayerPeek.E2E.Tests",
        beforeDelete: static directory => OwnProcesses.WaitForExitOrKillAsync(directory + Path.DirectorySeparatorChar, TimeSpan.Zero));

    /// <summary>Reserved for this assembly and worktree: every browser process whose profile lies here was started by these tests.</summary>
    public static readonly string Root = Reserved.Path;

    /// <summary>How the <c>--user-data-dir</c> of a process started by these tests begins (no match for a sibling name).</summary>
    public static readonly string ProcessMarker = Root + Path.DirectorySeparatorChar;

    /// <summary>Playwright's name of the installed stable Edge.</summary>
    public const string Channel = "msedge";

    private static readonly TimeSpan ProcessExitTimeout = TimeSpan.FromSeconds(15);

    private TestRunLock? _runLock;
    private IPlaywright? _playwright;
    private IBrowser? _browser;

    /// <summary>Why the browser tests cannot run (Edge not installed), or <see langword="null"/>.</summary>
    public string? SkipReason { get; private set; }

    /// <summary>The driver, for tests that launch a browser of their own.</summary>
    internal IPlaywright Playwright => _playwright ?? throw new InvalidOperationException(SkipReason ?? "Not initialized.");

    internal IBrowser Browser => _browser ?? throw new InvalidOperationException(SkipReason ?? "Not initialized.");

    public async ValueTask InitializeAsync()
    {
        _runLock = await Reserved.AcquireLockAsync(TimeSpan.FromMinutes(5));
        try
        {
            await Reserved.SweepAsync();
            await Reserved.CreateAsync();
            string temp = Directory.CreateDirectory(Path.Combine(Root, "tmp")).FullName;
            _playwright = await CreatePlaywrightAsync(temp);
            (_browser, SkipReason) = await EdgeLauncher.LaunchAsync(_playwright.Chromium, new BrowserTypeLaunchOptions());
        }
        catch (Exception startError)
        {
            // Clean up what was started; a cleanup failure must not hide why the start failed.
            try
            {
                await DisposeAsync();
            }
            catch (Exception cleanupError)
            {
                throw new AggregateException("Starting the shared browser failed, and so did the cleanup.", startError, cleanupError);
            }

            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_runLock is null)
        {
            // Not initialized (e.g. the lock timed out while another run holds it) or already disposed: without the lock
            // the processes and files under Root may belong to another run and must not be touched.
            return;
        }

        try
        {
            // Also when closing fails (a crashed browser, a dead driver pipe, any exception) the driver is stopped and
            // the processes and files are cleaned up before the lock is released; the errors are reported afterwards.
            List<Exception> errors = [];
            IBrowser? browser = _browser;
            _browser = null;
            if (browser is not null)
            {
                await CaptureAsync(errors, async () =>
                {
                    await browser.CloseAsync();
                    await browser.DisposeAsync();
                });
            }

            IPlaywright? playwright = _playwright;
            _playwright = null;
            await CaptureAsync(errors, () =>
            {
                playwright?.Dispose();
                return Task.CompletedTask;
            });

            // Closing the browser ends its processes; anything still running after the timeout is a leak. A failure of
            // the sweep itself is left for the start sweep of the next run (RetryAsync says so).
            List<string> leaked = [];
            await CaptureAsync(errors, async () => leaked = await OwnProcesses.WaitForExitOrKillAsync(ProcessMarker, ProcessExitTimeout));
            await CaptureAsync(errors, Reserved.DeleteAsync);
            if (leaked.Count != 0)
            {
                errors.Add(new InvalidOperationException($"Browser processes outlived the tests and were killed: {string.Join(", ", leaked)}."));
            }

            if (errors.Count != 0)
            {
                throw errors.Count == 1 ? errors[0] : new AggregateException("Shutting down the shared browser failed.", errors);
            }
        }
        finally
        {
            _runLock.Dispose();
            _runLock = null;
        }
    }

    private static async Task CaptureAsync(List<Exception> errors, Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            errors.Add(ex);
        }
    }

    /// <summary>A new, isolated browser context (own storage) of the shared Edge; skips the test when Edge is missing.</summary>
    internal async Task<BrowserSession> NewSessionAsync()
    {
        if (SkipReason is not null)
        {
            Assert.Skip(SkipReason);
        }

        return await BrowserSession.StartAsync(Browser);
    }

    /// <summary>
    /// Starts the Playwright driver with <paramref name="temp"/> as its temporary directory. The driver creates the
    /// browser profiles there (<c>os.tmpdir()</c>) and the browser inherits it, so every file and every process command
    /// line of the run lies under <see cref="Root"/>.
    /// </summary>
    private static async Task<IPlaywright> CreatePlaywrightAsync(string temp)
    {
        // The driver copies the environment when it starts; nothing else runs before the first test.
        string[] names = ["TMP", "TEMP", "TMPDIR"];
        string?[] saved = [.. names.Select(Environment.GetEnvironmentVariable)];
        try
        {
            foreach (string name in names)
            {
                Environment.SetEnvironmentVariable(name, temp);
            }

            return await Microsoft.Playwright.Playwright.CreateAsync();
        }
        finally
        {
            for (int i = 0; i < names.Length; i++)
            {
                Environment.SetEnvironmentVariable(names[i], saved[i]);
            }
        }
    }
}
