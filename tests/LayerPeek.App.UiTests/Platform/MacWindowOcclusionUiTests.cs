using System.Runtime.Versioning;

using Avalonia.Controls;

using LayerPeek.App.Platform;
using LayerPeek.App.Platform.MacOS;
using LayerPeek.App.UiTests.Infrastructure;

namespace LayerPeek.App.UiTests.Platform;

/// <summary>
/// T-187: a window without a native <c>NSWindow</c> (the headless platform) is never reported occluded by the macOS
/// watcher, so its camera stream is never paused by it. The occlusion of a real window (covered, another Space): T-127.
/// </summary>
[SupportedOSPlatform("macos")]
public sealed class MacWindowOcclusionUiTests(HeadlessSession session)
{
    [Fact]
    public Task HeadlessWindowIsReportedVisibleOnceAndNeverOccluded()
    {
        Assert.SkipUnless(OperatingSystem.IsMacOS(), "The occlusion watcher exists only on macOS.");
        return session.RunAsync(static _ =>
        {
            Window window = new();
            window.Show();
            List<bool> reported = [];

            IDisposable watch = ((IWindowCloaking)MacWindowOcclusion.Shared).Watch(window, reported.Add);
            watch.Dispose();
            watch.Dispose();
            window.Close();

            Assert.Equal([false], reported);
            return Task.CompletedTask;
        });
    }
}
