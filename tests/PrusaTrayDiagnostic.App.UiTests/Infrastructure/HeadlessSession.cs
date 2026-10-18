using System.Globalization;
using System.Runtime.ExceptionServices;

using Avalonia;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Threading;

using PrusaTrayDiagnostic.App.Resources;
using PrusaTrayDiagnostic.Tests.Shared;
using PrusaTrayDiagnostic.App.UiTests.Infrastructure;

// One headless Avalonia session (one UI thread) for the whole test assembly.
[assembly: AssemblyFixture(typeof(HeadlessSession))]

// Test bodies run one after another on the single UI thread anyway (each body is one queued job of the session), and
// the language of the texts is process-wide (Strings.Culture). Running the tests serially keeps the per-test timeout a
// measure of the test itself instead of the queue in front of it. Other test assemblies still run in parallel.
[assembly: Xunit.v3.Parallelization(Mode = Xunit.Sdk.ParallelMode.None)]

namespace PrusaTrayDiagnostic.App.UiTests.Infrastructure;

/// <summary>
/// Entry point of the headless application: the real <see cref="App"/> (App.axaml: Fluent theme, the tray icon with its
/// empty menu) on the headless platform. No window is ever shown on screen. Frames are rendered by Skia in memory so that
/// snapshot tests can capture them; the only font is the bundled Inter (default family, no fallbacks), so rendering never
/// depends on fonts installed on the machine (characters outside Inter would fall back to system fonts, which
/// <c>SnapshotTests</c> rules out). <see cref="App.OnFrameworkInitializationCompleted"/> starts nothing: there is no
/// desktop lifetime headless, so no settings file, credential store, printer client or local proxy is touched.
/// </summary>
public static class HeadlessTestApp
{
    /// <summary>The only font family of the UI tests.</summary>
    public const string FontFamilyName = "fonts:Inter#Inter";

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UseSkia()
            .WithInterFont()
            .With(new FontManagerOptions { DefaultFamilyName = FontFamilyName, FontFallbacks = [] })
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}

/// <summary>
/// xUnit v3 assembly fixture owning the <see cref="HeadlessUnitTestSession"/>. Tests run their body on the Avalonia UI
/// thread through <see cref="RunAsync"/>; every body gets a fresh application (<see cref="AvaloniaTestIsolationLevel.PerTest"/>).
/// </summary>
/// <remarks>
/// Why <see cref="HeadlessUnitTestSession.Dispose"/> hung (architect's probe, T-031): the session completes the task of
/// <c>Dispatch</c> with a plain <c>TaskCompletionSource</c> (no <c>RunContinuationsAsynchronously</c>) from its own
/// dispatch thread. The awaiting test method therefore continued <em>on the session thread</em>, inside the session's
/// dispatch loop. Calling the synchronous <c>Dispose</c> there cancels the loop and then blocks in
/// <c>_dispatchTask.Wait()</c>, waiting for the very thread it runs on: a self-deadlock. <see cref="RunAsync"/> therefore
/// always resumes the test off the session thread, and the session is disposed asynchronously, with a bounded wait.
/// </remarks>
public sealed class HeadlessSession : IAsyncLifetime
{
    /// <summary>A broken test fails with a timeout instead of hanging the run.</summary>
    public static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(30);

    /// <summary>Bound for stopping the session's dispatch loop at the end of the run.</summary>
    public static readonly TimeSpan ShutdownTimeout = TimeSpan.FromSeconds(10);

    /// <summary>How long a later test waits for a body that outlived its timeout before failing fast.</summary>
    private static readonly TimeSpan StuckBodyGrace = TimeSpan.FromMilliseconds(250);

    private static readonly CultureInfo SystemCulture = CultureInfo.GetCultureInfo("en-US");

    private HeadlessUnitTestSession? _session;
    private TestRunLock? _runLock;
    private volatile StuckBody? _stuckBody;

    public async ValueTask InitializeAsync()
    {
        // Another run of this assembly in this worktree (IDE and CLI) must not sweep the snapshot output of a running one.
        _runLock = await SnapshotAssert.Reserved.AcquireLockAsync(TimeSpan.FromMinutes(5)).ConfigureAwait(false);
        try
        {
            // Actual and diff images of the previous run's snapshot failures (kept for inspection until now), leftovers of
            // deleted worktrees and of older runs.
            await SnapshotAssert.SweepFailureOutputAsync().ConfigureAwait(false);
            _session = HeadlessUnitTestSession.StartNew(typeof(HeadlessTestApp), AvaloniaTestIsolationLevel.PerTest);
        }
        catch
        {
            _runLock.Dispose();
            _runLock = null;
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            await StopSessionAsync().ConfigureAwait(false);
        }
        finally
        {
            // Nothing stays behind unless a snapshot failed (then its images do, until the next run). Without the lock
            // (not initialized) the folder may belong to another run of this worktree and is not touched.
            if (_runLock is not null)
            {
                SnapshotAssert.RemoveEmptyFailureOutput();
            }

            _runLock?.Dispose();
            _runLock = null;
        }
    }

    private async Task StopSessionAsync()
    {
        if (_session is not { } session)
        {
            return;
        }

        _session = null;

        // Called from an xUnit thread (never the session thread, see RunAsync), so waiting for the dispatch loop to end
        // cannot deadlock. If a timed-out test body is still stuck on the UI thread the loop never ends: give up after the
        // bound (the session thread is a background thread pool thread and ends with the process) and say why.
        Task disposing = session.DisposeAsync().AsTask();
        try
        {
            await disposing.WaitAsync(ShutdownTimeout).ConfigureAwait(false);
        }
        catch (TimeoutException ex)
        {
            throw new TimeoutException(
                $"The headless session did not stop within {ShutdownTimeout}: the body of {_stuckBody?.TestName ?? "a UI test"} that timed out is still running on the UI thread.",
                ex);
        }
    }

    /// <inheritdoc cref="RunAsync{T}"/>
    public Task RunAsync(Func<UiTestScope, Task> body)
    {
        ArgumentNullException.ThrowIfNull(body);
        return RunAsync(async scope =>
        {
            await body(scope);
            return 0;
        });
    }

    /// <summary>
    /// Runs <paramref name="body"/> on the UI thread with a fresh application, with the system language English and the
    /// app language "system" (<see cref="Strings.Culture"/> = <see langword="null"/>). Windows and objects registered in
    /// the <see cref="UiTestScope"/> are closed / disposed afterwards, also when the body fails. The body's token is
    /// cancelled by the xUnit test token or after <see cref="TestTimeout"/>.
    /// </summary>
    public Task<T> RunAsync<T>(Func<UiTestScope, Task<T>> body) => RunAsync(body, TestTimeout);

    /// <summary>
    /// Whether the body that last outlived its timeout or cancellation has finished (in any state) within
    /// <paramref name="bound"/>, i.e. the UI thread is free again; <see langword="true"/> when no body is stuck.
    /// </summary>
    internal Task<bool> UiThreadFreedWithinAsync(TimeSpan bound) =>
        _stuckBody is { } stuck ? HasFinishedAsync(stuck.Body, bound) : Task.FromResult(true);

    /// <summary>Whether <paramref name="body"/> ends (in any state) within <paramref name="bound"/>.</summary>
    private static async Task<bool> HasFinishedAsync(Task body, TimeSpan bound)
    {
        // Neither the body's own failure nor the timeout of the wait is an error here; only the outcome counts.
        await body.WaitAsync(bound).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        return body.IsCompleted;
    }

    /// <inheritdoc cref="RunAsync{T}(Func{UiTestScope, Task{T}})"/>
    /// <param name="body">The test body.</param>
    /// <param name="testTimeout">Bound of the body; <see cref="TestTimeout"/> unless a test of the infrastructure needs less.</param>
    internal async Task<T> RunAsync<T>(Func<UiTestScope, Task<T>> body, TimeSpan testTimeout)
    {
        ArgumentNullException.ThrowIfNull(body);
        HeadlessUnitTestSession session = _session ?? throw new InvalidOperationException("The headless session is not running.");

        // A body that timed out without observing its token keeps the session's only UI thread: everything queued after
        // it would just wait for its own timeout. Fail fast instead and name the culprit.
        if (_stuckBody is { Body.IsCompleted: false } stuck && !await HasFinishedAsync(stuck.Body, StuckBodyGrace).ConfigureAwait(false))
        {
            throw new InvalidOperationException(
                $"UI thread still blocked by {stuck.TestName}: that test body did not finish after its timeout or cancellation, so no other UI test can run.");
        }

        CancellationToken testToken = TestContext.Current.CancellationToken;
        string testName = TestContext.Current.Test?.TestDisplayName ?? "an unknown test";
        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(testToken);
        timeout.CancelAfter(testTimeout);
        CancellationToken token = timeout.Token;

        // The token is deliberately not given to Dispatch: when it fires, Dispatch leaves its dispatcher frame and then
        // blocks the UI thread on the unfinished body (GetAwaiter().GetResult()). The body observes the token instead,
        // and the caller stops waiting for it.
        Task<T> dispatched = session.Dispatch<T>(() => RunOnUiThreadAsync(body, token), CancellationToken.None);
        try
        {
            return await ResumeOffSessionThread(dispatched).WaitAsync(token).ConfigureAwait(false);
        }
        catch (OperationCanceledException ex) when (token.IsCancellationRequested)
        {
            // Timed out or cancelled. A body still running (it may yet finish) is remembered, so that the next tests fail
            // fast while it blocks the UI thread. A body that observed the token is usually done by the next test.
            if (!dispatched.IsCompleted)
            {
                _stuckBody = new StuckBody(dispatched, testName);
            }

            if (testToken.IsCancellationRequested)
            {
                throw;
            }

            throw new TimeoutException($"The UI test body did not finish within {testTimeout}.", ex);
        }
    }

    private static async Task<T> RunOnUiThreadAsync<T>(Func<UiTestScope, Task<T>> body, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        Dispatcher.UIThread.VerifyAccess();
        CultureInfo culture = CultureInfo.CurrentCulture;
        CultureInfo uiCulture = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentCulture = SystemCulture;
        CultureInfo.CurrentUICulture = SystemCulture;
        Strings.Culture = null;
        UiTestScope scope = new(token);
        T result;
        try
        {
            // No ConfigureAwait(false): the body continues on the UI thread (Avalonia synchronization context).
            result = await body(scope);
        }
        catch (Exception failure)
        {
            // A failing cleanup must not hide why the test failed: report both.
            if (CleanUp(scope, culture, uiCulture) is { } cleanupFailure)
            {
                throw new AggregateException("The UI test body failed and so did its cleanup.", failure, cleanupFailure);
            }

            throw;
        }

        if (CleanUp(scope, culture, uiCulture) is { } failedCleanup)
        {
            ExceptionDispatchInfo.Throw(failedCleanup);
        }

        return result;
    }

    /// <summary>Closes and disposes what the body registered and restores the languages; returns what failed.</summary>
    private static Exception? CleanUp(UiTestScope scope, CultureInfo culture, CultureInfo uiCulture)
    {
        try
        {
            scope.Dispose();
            return null;
        }
        catch (Exception ex)
        {
            return ex;
        }
        finally
        {
            // Never leave another language behind for the next test.
            Strings.Culture = null;
            CultureInfo.CurrentCulture = culture;
            CultureInfo.CurrentUICulture = uiCulture;
        }
    }

    /// <summary>
    /// The session completes its task on the session thread with synchronous continuations; this copy runs them on the
    /// thread pool, so the test never continues inside the session's dispatch loop (see the remarks of the class).
    /// </summary>
    private static Task<T> ResumeOffSessionThread<T>(Task<T> task)
    {
        TaskCompletionSource<T> resumed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        _ = task.ContinueWith(
            static (completed, state) =>
            {
                TaskCompletionSource<T> target = (TaskCompletionSource<T>)state!;
                if (completed.IsFaulted)
                {
                    target.TrySetException(completed.Exception!.InnerExceptions);
                }
                else if (completed.IsCanceled)
                {
                    target.TrySetCanceled();
                }
                else
                {
                    target.TrySetResult(completed.Result);
                }
            },
            resumed,
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
        return resumed.Task;
    }

    /// <summary>A test body that outlived its timeout or cancellation and may still occupy the UI thread.</summary>
    private sealed record StuckBody(Task Body, string TestName);
}
