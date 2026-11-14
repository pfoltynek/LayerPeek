using Avalonia;
using Avalonia.Headless;
using Avalonia.Media;

namespace LayerPeek.App.UiTests.Infrastructure;

/// <summary>
/// Entry point of the headless application: the real <see cref="App"/> (App.axaml: Fluent theme, the tray icon with its
/// empty menu) on the headless platform. No window is ever shown on screen. Frames are rendered by Skia in memory so that
/// snapshot tests can capture them; the only font is the bundled Inter (default family, no fallbacks), so rendering never
/// depends on fonts installed on the machine (characters outside Inter would fall back to system fonts, which
/// <c>SnapshotTests</c> rules out). <see cref="App.OnFrameworkInitializationCompleted"/> starts nothing: there is no
/// desktop lifetime headless, so no settings file, credential store, printer client or local proxy is touched.
/// Popups (context menus, flyouts) open in their own top level (<see cref="Avalonia.Controls.Primitives.PopupRoot"/>), as on
/// Windows and macOS, not in the overlay layer of the window that headless uses by default (T-121): keys for an open popup
/// go to its top level, and the key routing and access keys of a popup behave as in the app.
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
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false, OverlayPopups = false });
}
