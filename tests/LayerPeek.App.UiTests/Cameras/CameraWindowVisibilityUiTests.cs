using Avalonia.Controls;
using Avalonia.Threading;

using LayerPeek.App.Cameras;
using LayerPeek.App.Platform;
using LayerPeek.App.Tests.Cameras;
using LayerPeek.App.Tests.Tray;
using LayerPeek.App.UiTests.Infrastructure;
using LayerPeek.App.Windows;
using LayerPeek.Core.Display;

namespace LayerPeek.App.UiTests.Cameras;

/// <summary>
/// T-186 (user decision 2026-11-11): the real camera window pauses its stream while it is cloaked (Windows: another
/// virtual desktop) or the screen cannot be seen (locked, display off), in every mode including the always-on-top mini
/// preview, and resumes afterwards. Fake cloaking and screen; the stream is the harness's fake.
/// </summary>
public sealed class CameraWindowVisibilityUiTests(HeadlessSession session)
{
    private const uint Red = 0xFFFF0000;

    private static CameraViewModel ViewModel(CameraWindow window) => (CameraViewModel)window.DataContext!;

    [Fact]
    public Task CloakedWindowPausesTheStreamAndResumesWhenShownAgain() =>
        session.RunAsync(async scope =>
        {
            FakeWindowCloaking cloaking = new();
            await using CameraHarness harness = new(cloaking: cloaking);
            (CameraWindow window, FakeCameraFeed first) = await harness.OpenStreamingAsync(160, 90, Red);
            Assert.Equal(1, cloaking.Watches);

            cloaking.Set(window, cloaked: true);
            await first.Disposed.WaitAsync(TestTimeouts.Wait);
            Assert.False(ViewModel(window).IsStreaming);

            cloaking.Set(window, cloaked: false);
            FakeCameraFeed second = await harness.Factory.WaitForFeedAsync(2);
            Assert.False(second.IsDisposed);
            Assert.True(ViewModel(window).IsStreaming);
        });

    [Fact]
    public Task WindowOpenedCloakedDoesNotStreamUntilShown() =>
        session.RunAsync(async scope =>
        {
            FakeWindowCloaking cloaking = new() { CloakedAtStart = true };
            await using CameraHarness harness = new(cloaking: cloaking);

            CameraWindow window = harness.Open();
            Dispatcher.UIThread.RunJobs();

            Assert.False(ViewModel(window).IsStreaming);
            Assert.Empty(harness.Factory.Feeds);

            cloaking.Set(window, cloaked: false);
            await harness.Factory.WaitForFeedAsync(1);
            Assert.True(ViewModel(window).IsStreaming);
        });

    [Fact]
    public Task ClosedWindowStopsWatchingItsCloaking() =>
        session.RunAsync(async scope =>
        {
            FakeWindowCloaking cloaking = new();
            await using CameraHarness harness = new(cloaking: cloaking);
            CameraWindow window = harness.Open();
            Assert.Equal(1, cloaking.Watches);

            window.Close();

            Assert.Equal(0, cloaking.Watches);
        });

    [Fact]
    public Task LockedScreenPausesTheMiniPreviewAndUnlockResumesIt() =>
        session.RunAsync(async scope =>
        {
            FakeScreen screen = new();
            await using CameraHarness harness = new(display: screen);
            (CameraWindow window, FakeCameraFeed first) = await harness.OpenStreamingAsync(160, 90, Red);
            ViewModel(window).ShowMiniCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();
            Assert.True(window.Topmost);
            Assert.True(ViewModel(window).IsStreaming);

            await Task.Run(() => screen.Set(false));
            Dispatcher.UIThread.RunJobs();
            await first.Disposed.WaitAsync(TestTimeouts.Wait);
            Assert.False(ViewModel(window).IsStreaming);
            Assert.Equal(WindowState.Normal, window.WindowState);

            await Task.Run(() => screen.Set(true));
            Dispatcher.UIThread.RunJobs();
            FakeCameraFeed second = await harness.Factory.WaitForFeedAsync(2);
            Assert.False(second.IsDisposed);
            Assert.True(ViewModel(window).IsStreaming);
        });

    /// <summary>Cloaking the test switches per window; a watch reports the state at once, like the real one.</summary>
    private sealed class FakeWindowCloaking : IWindowCloaking
    {
        private readonly Dictionary<TopLevel, Action<bool>> _watches = [];

        public bool CloakedAtStart { get; init; }

        public int Watches => _watches.Count;

        public IDisposable Watch(TopLevel window, Action<bool> cloakedChanged)
        {
            _watches[window] = cloakedChanged;
            cloakedChanged(CloakedAtStart);
            return new Unwatch(() => _watches.Remove(window));
        }

        public void Set(TopLevel window, bool cloaked) => _watches[window](cloaked);

        private sealed class Unwatch(Action remove) : IDisposable
        {
            public void Dispose() => remove();
        }
    }

    private sealed class FakeScreen : IDisplayActivityService
    {
        private volatile bool _visible = true;

        public bool IsScreenVisible => _visible;

        public event EventHandler? ScreenVisibilityChanged;

        public void Set(bool visible)
        {
            _visible = visible;
            ScreenVisibilityChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
