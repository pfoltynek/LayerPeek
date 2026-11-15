using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;

using LayerPeek.App.Cameras;
using LayerPeek.App.Localization;
using LayerPeek.App.Tests.Cameras;
using LayerPeek.App.UiTests.Infrastructure;
using LayerPeek.App.Windows;
using LayerPeek.Core.Cameras;
using LayerPeek.Core.Settings;

namespace LayerPeek.App.UiTests.Cameras;

/// <summary>
/// T-188 (user decision 2026-11-11): while the camera connection waits between reconnect attempts, the status over the
/// image says roughly when the next one starts. In every language the line is readable (not cut, inside its status box
/// and the image area) in the normal window down to 320 × 180, with the longest seconds ("40 s") and the minutes of the
/// backoff's cap ("1 min"); the mini preview does not show it. Only fakes on the harness's fake clock: no network.
/// </summary>
public sealed class CameraWindowNextAttemptUiTests(HeadlessSession session)
{
    public static TheoryData<string> Languages => [.. AppLanguages.Supported];

    [Theory]
    [MemberData(nameof(Languages))]
    public Task NextAttemptFitsTheNormalWindowAndIsHiddenInTheMiniPreview(string language) =>
        session.RunAsync(async scope =>
        {
            Assert.True(new UiLanguage().Apply(language));
            List<string> problems = [];
            await using CameraHarness harness = new();
            CameraWindow window = harness.Open();
            CameraViewModel viewModel = (CameraViewModel)window.DataContext!;

            // The default backoff: 5, 10, 20, 40 s, then the cap of 1 min. Checked at the two longest texts.
            int[] delays = [5, 10, 20, 40, 60];
            for (int attempt = 1; attempt <= delays.Length; attempt++)
            {
                TimeSpan delay = TimeSpan.FromSeconds(delays[attempt - 1]);
                string expected = CameraViewModel.FormatNextAttempt(delay);
                FakeCameraFeed feed = await harness.Factory.WaitForFeedAsync(attempt);
                feed.Fail(new CameraFeedException("The camera sent no image within 10 s."));
                await Ui.UntilAsync(() => viewModel.NextAttemptText == expected, $"attempt {attempt} failed");
                if (delay >= TimeSpan.FromSeconds(40))
                {
                    problems.AddRange(Check(window, viewModel, $"{language} / {expected}"));
                }

                // The wait is over: the next attempt follows.
                harness.Time.Advance(delay);
            }

            Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
        });

    private static List<string> Check(CameraWindow window, CameraViewModel viewModel, string context)
    {
        List<string> problems = [];
        string text = viewModel.NextAttemptText!;
        foreach ((string name, double width, double height) in new (string, double, double)[] { ("960 × 540", 960, 540), ("480 × 270", 480, 270), ("320 × 180", 320, 180) })
        {
            Size(window, width, height);
            Assert.True(viewModel.IsStatusVisible);
            problems.AddRange(CameraHarness.StatusProblems(window, $"{context} / normal {name}", viewModel.StatusText, viewModel.StatusDetail!, text));
        }

        viewModel.ShowMiniCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        window.Settle();
        if (window.GetVisualDescendants().OfType<TextBlock>().Any(t => t.IsEffectivelyVisible && t.Text == text))
        {
            problems.Add($"{context} / mini: '{text}' is shown.");
        }

        viewModel.ShowNormalCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        Size(window, 960, 540);
        return problems;
    }

    private static void Size(CameraWindow window, double width, double height)
    {
        window.Width = width;
        window.Height = height;
        Dispatcher.UIThread.RunJobs();
        window.Settle();
    }
}
