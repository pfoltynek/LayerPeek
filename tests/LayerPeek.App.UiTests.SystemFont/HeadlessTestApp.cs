using Avalonia;
using Avalonia.Headless;
using Avalonia.Media;

namespace LayerPeek.App.UiTests.Infrastructure;

/// <summary>
/// The headless application of LayerPeek.App.UiTests (see its HeadlessTestApp) with San Francisco instead of Inter as
/// the only font (default family, no fallbacks), read from <see cref="SystemFont.FilePath"/> (T-214). Only built when
/// the font exists (<see cref="HeadlessSession"/> starts no session without it).
/// </summary>
public static class HeadlessTestApp
{
    /// <summary>The only font family of these tests: the family of SFNS.ttf in <see cref="SystemFontCollection"/>.</summary>
    public const string FontFamilyName = "fonts:SystemFont#" + SystemFontCollection.FamilyName;

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UseSkia()
            .ConfigureFonts(static fonts =>
            {
                SystemFontCollection collection = new();
                collection.Load();
                fonts.AddFontCollection(collection);
            })
            .With(new FontManagerOptions { DefaultFamilyName = FontFamilyName, FontFallbacks = [] })
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false, OverlayPopups = false });
}
