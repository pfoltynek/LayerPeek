using Avalonia;
using Avalonia.Headless;
using Avalonia.Themes.Fluent;

using PrusaTrayDiagnostic.App.UiTests;

[assembly: AvaloniaTestApplication(typeof(TestApplication))]

// All headless tests share one UI thread, culture and mouse: running them one after another keeps them independent
// (interleaved tests would swap each other's CultureScope and continue each other's click series).
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace PrusaTrayDiagnostic.App.UiTests;

/// <summary>
/// The headless application of the UI tests: the app's theme, no tray icon and no services (the real <c>App</c> would start
/// monitoring and touch the user's settings). Rendering goes through Skia, so tests can capture the drawn frames.
/// </summary>
public sealed class TestApplication : Application
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<TestApplication>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });

    public override void Initialize() => Styles.Add(new FluentTheme());
}
