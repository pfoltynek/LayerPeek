using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;

using LayerPeek.App.Cameras;
using LayerPeek.App.Localization;
using LayerPeek.App.Resources;
using LayerPeek.App.Tests.Cameras;
using LayerPeek.App.Tests.Platform;
using LayerPeek.App.UiTests.Infrastructure;
using LayerPeek.App.Windows;
using LayerPeek.Core.Cameras;
using LayerPeek.Core.Settings;

namespace LayerPeek.App.UiTests.Cameras;

/// <summary>
/// T-097 (macOS 15+): "no route to host" to a camera in the local network shows the Local Network hint in the status over
/// the image. In every language the whole hint is readable (not cut, inside its status box and the image area) in the
/// normal window down to 320 × 180 (the full text with the System Settings path) and in the mini preview from its default
/// size down to its smallest 240 × 135 (the short form, ui-designer 2026-10-29). Only fakes: no network, nothing opens.
/// </summary>
public sealed class CameraWindowLocalNetworkUiTests(HeadlessSession session)
{
    public static TheoryData<string> Languages => [.. AppLanguages.Supported];

    [Theory]
    [MemberData(nameof(Languages))]
    public Task LocalNetworkHintFitsTheNormalWindowAndTheMiniPreview(string language) =>
        session.RunAsync(async scope =>
        {
            Assert.True(new UiLanguage().Apply(language));
            List<string> problems = [];
            FakeLocalNetworkPrivacy privacy = new();
            await using CameraHarness harness = new(localNetwork: privacy);
            CameraWindow window = harness.Open();
            CameraViewModel viewModel = (CameraViewModel)window.DataContext!;
            FakeCameraFeed feed = await harness.Factory.WaitForFeedAsync(1);
            feed.Fail(new CameraFeedException("Opening the stream: No route to host.") { IsHostUnreachable = true });
            await Ui.UntilAsync(() => viewModel.StatusDetail == Strings.LocalNetworkAccessMayBeOff, "hint shown");
            Assert.Contains(("192.168.0.227", "the camera of printer 'a'"), privacy.Calls);

            // The normal window from its default size down to 320 × 180; the mini preview from its default size down to its
            // smallest 240 × 135 (the full text ran past the status box there, T-097 question 1), and back to normal.
            foreach ((string name, Action setUp) in new (string, Action)[]
            {
                ("normal 960 × 540", () => Size(window, 960, 540)),
                ("normal 640 × 360", () => Size(window, 640, 360)),
                ("normal 480 × 270", () => Size(window, 480, 270)),
                ("normal 320 × 180", () => Size(window, 320, 180)),
                ("mini 320 × 180", () =>
                {
                    viewModel.ShowMiniCommand.Execute(null);
                    Dispatcher.UIThread.RunJobs();
                    window.Settle();
                }),
                ("mini 280 × 158", () => CameraHarness.ResizeMiniWithGrip(window, 280, 191)),
                ("mini 260 × 146", () => CameraHarness.ResizeMiniWithGrip(window, 260, 400)),
                ("mini 240 × 135", () => CameraHarness.ResizeMiniWithGrip(window, 240, 400)),
                ("normal again", () =>
                {
                    viewModel.ShowNormalCommand.Execute(null);
                    Dispatcher.UIThread.RunJobs();
                    window.Settle();
                }),
            })
            {
                setUp();
                window.Settle();
                Assert.True(viewModel.IsStatusVisible);
                string hint = viewModel.IsMini ? Strings.CameraMiniLocalNetworkAccessMayBeOff : Strings.LocalNetworkAccessMayBeOff;
                problems.AddRange(CameraHarness.StatusProblems(window, $"{language} / {name}", hint));
            }

            Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
        });

    private static void Size(CameraWindow window, double width, double height)
    {
        window.Width = width;
        window.Height = height;
        Dispatcher.UIThread.RunJobs();
        window.Settle();
    }
}
