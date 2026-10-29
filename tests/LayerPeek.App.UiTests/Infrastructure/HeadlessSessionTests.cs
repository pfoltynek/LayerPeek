using System.Globalization;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;

using LayerPeek.App.Resources;

namespace LayerPeek.App.UiTests.Infrastructure;

/// <summary>The test infrastructure itself: the real app on the headless platform, the UI thread, cleanup.</summary>
public sealed class HeadlessSessionTests(HeadlessSession session)
{
    [Fact]
    public Task BodyRunsOnTheUiThreadWithTheRealApplication() =>
        session.RunAsync(static async scope =>
        {
            Assert.True(Dispatcher.UIThread.CheckAccess());
            App app = Assert.IsType<App>(Application.Current);
            Assert.Contains(app.Styles, static style => style is FluentTheme);
            Assert.Null(app.ApplicationLifetime);

            // Awaiting keeps the body on the UI thread.
            await Task.Run(static () => { }, scope.CancellationToken);
            Assert.True(Dispatcher.UIThread.CheckAccess());
        });

    [Fact]
    public Task WindowsAreHeadlessAndNeverOnScreen() =>
        session.RunAsync(static scope =>
        {
            Window window = scope.Track(new Window { Content = new TextBlock { Text = "x" } });
            window.Show();

            // The window implementation comes from the headless platform, not from Win32 / macOS.
            Assert.Same(typeof(HeadlessUnitTestSession).Assembly, window.PlatformImpl?.GetType().Assembly);
            return Task.CompletedTask;
        });

    [Fact]
    public Task TrayIconOfTheAppHasItsEmptyMenu() =>
        session.RunAsync(static scope =>
        {
            TrayIcons? icons = TrayIcon.GetIcons(Application.Current!);

            TrayIcon icon = Assert.Single(icons!);
            Assert.NotNull(icon.Menu);
            Assert.Empty(icon.Menu.Items);
            return Task.CompletedTask;
        });

    [Fact]
    public Task EveryTestStartsInEnglishWithTheSystemLanguage() =>
        session.RunAsync(static scope =>
        {
            Assert.Null(Strings.Culture);
            Assert.Equal("_Quit", Strings.MenuQuit);
            Assert.Equal(ThemeVariant.Default, Application.Current!.RequestedThemeVariant);
            return Task.CompletedTask;
        });

    /// <summary>
    /// Work posted to the UI thread from the thread pool (as camera statuses are) runs without the body's execution
    /// context; it must see English too, not the language of the machine (T-095).
    /// </summary>
    [Fact]
    public Task WorkPostedFromAnotherThreadIsInEnglishToo() =>
        session.RunAsync(static async scope =>
        {
            TaskCompletionSource<string> text = new(TaskCreationOptions.RunContinuationsAsynchronously);
            using (ExecutionContext.SuppressFlow())
            {
                _ = Task.Run(static () => { }).ContinueWith(
                    _ => Dispatcher.UIThread.Post(() => text.SetResult($"{CultureInfo.CurrentUICulture.Name} {Strings.MenuQuit}")),
                    CancellationToken.None,
                    TaskContinuationOptions.None,
                    TaskScheduler.Default);
            }

            Assert.Equal("en-US _Quit", await text.Task.WaitAsync(scope.CancellationToken));
        });

    [Fact]
    public async Task FailureInTheBodyReachesTheTestAndTheSessionKeepsWorking()
    {
        InvalidOperationException failure = await Assert.ThrowsAsync<InvalidOperationException>(
            () => session.RunAsync(static _ => throw new InvalidOperationException("boom")));
        Assert.Equal("boom", failure.Message);

        bool onUiThread = await session.RunAsync(static _ => Task.FromResult(Dispatcher.UIThread.CheckAccess()));
        Assert.True(onUiThread);
    }

    [Fact]
    public async Task FailingCleanupAfterAFailingBodyReportsBothFailures()
    {
        AggregateException failure = await Assert.ThrowsAsync<AggregateException>(() => session.RunAsync(static scope =>
        {
            scope.Defer(new ThrowingDisposable());
            throw new InvalidOperationException("body failed");
        }));

        Assert.Equal(2, failure.InnerExceptions.Count);
        Assert.Equal("body failed", Assert.IsType<InvalidOperationException>(failure.InnerExceptions[0]).Message);
        AggregateException cleanup = Assert.IsType<AggregateException>(failure.InnerExceptions[1]);
        Assert.IsType<ThrowingDisposable.CleanupException>(Assert.Single(cleanup.InnerExceptions));
    }

    [Fact]
    public async Task FailingCleanupAfterAPassingBodyFailsTheTest()
    {
        AggregateException cleanup = await Assert.ThrowsAsync<AggregateException>(() => session.RunAsync(static scope =>
        {
            scope.Defer(new ThrowingDisposable());
            return Task.CompletedTask;
        }));

        Assert.IsType<ThrowingDisposable.CleanupException>(Assert.Single(cleanup.InnerExceptions));
    }

    [Fact]
    public async Task BodyThatObservesItsTokenTimesOutAndTheSessionKeepsWorking()
    {
        await Assert.ThrowsAsync<TimeoutException>(() => session.RunAsync(
            static async scope =>
            {
                await Task.Delay(Timeout.Infinite, scope.CancellationToken);
                return 0;
            },
            TimeSpan.FromMilliseconds(200)));

        Assert.True(await session.UiThreadFreedWithinAsync(TimeSpan.FromSeconds(10)));
        Assert.True(await session.RunAsync(static _ => Task.FromResult(Dispatcher.UIThread.CheckAccess())));
    }

    [Fact]
    public async Task BodyStillBlockingTheUiThreadAfterItsTimeoutMakesLaterTestsFailFast()
    {
        // A body that ignores its token: it keeps the UI thread until released.
        TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            await Assert.ThrowsAsync<TimeoutException>(() => session.RunAsync(
                async _ =>
                {
                    await release.Task;
                    return 0;
                },
                TimeSpan.FromMilliseconds(500)));

            InvalidOperationException blocked = await Assert.ThrowsAsync<InvalidOperationException>(
                () => session.RunAsync(static _ => Task.CompletedTask));
            Assert.StartsWith("UI thread still blocked by ", blocked.Message, StringComparison.Ordinal);
            Assert.Contains(nameof(BodyStillBlockingTheUiThreadAfterItsTimeoutMakesLaterTestsFailFast), blocked.Message, StringComparison.Ordinal);
        }
        finally
        {
            // Never leave the UI thread blocked for the other tests.
            release.TrySetResult();
            Assert.True(await session.UiThreadFreedWithinAsync(TimeSpan.FromSeconds(10)), "The released body did not finish.");
        }

        // Once the body has finished, the session runs tests again.
        Assert.True(await session.RunAsync(static _ => Task.FromResult(Dispatcher.UIThread.CheckAccess())));
    }

    [Fact]
    public async Task TestContinuesOffTheSessionThread()
    {
        int uiThread = await session.RunAsync(static _ => Task.FromResult(Environment.CurrentManagedThreadId));

        // Continuing inside the session's dispatch loop is what made a synchronous session Dispose deadlock.
        Assert.NotEqual(uiThread, Environment.CurrentManagedThreadId);
    }

    [Fact]
    public async Task OpenWindowIsClosedAfterTheTestEvenWhenItFails()
    {
        bool shown = false;
        bool closed = false;
        await Assert.ThrowsAsync<InvalidOperationException>(() => session.RunAsync(scope =>
        {
            Window window = scope.Track(new Window());
            window.Closed += (_, _) => closed = true;
            window.Show();
            shown = window.IsVisible;
            throw new InvalidOperationException("fails with an open window");
        }));

        Assert.True(shown);
        Assert.True(closed);
    }

    private sealed class ThrowingDisposable : IDisposable
    {
        public void Dispose() => throw new CleanupException();

        public sealed class CleanupException : Exception;
    }
}
