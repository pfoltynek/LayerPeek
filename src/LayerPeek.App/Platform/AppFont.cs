using Avalonia;
using Avalonia.Media;

namespace LayerPeek.App.Platform;

/// <summary>
/// The default font of the app's windows (T-223, user decision 2026-11-15). macOS: the bundled Inter, because Avalonia
/// 12.1 lays the variable system font San Francisco out with the advances of its default instance while it draws the
/// requested weight, so text is 7–20 % narrower than in native apps and semibold / bold letters touch (T-222). Windows:
/// unchanged, the system font Segoe UI (a static font, laid out correctly). Menus, notifications and system dialogs are
/// native and keep the system font on both.
/// </summary>
internal static class AppFont
{
    /// <summary>The family of the bundled Inter (the font collection of <c>WithInterFont</c>).</summary>
    public const string InterFamilyName = "fonts:Inter#Inter";

    /// <summary>Inter as the default font on macOS; elsewhere the builder is returned unchanged.</summary>
    public static AppBuilder UseAppFont(this AppBuilder builder) =>
        OperatingSystem.IsMacOS() ? builder.UseInterFont() : builder;

    /// <summary>
    /// Registers the bundled Inter and makes it the default family. Characters Inter lacks (e.g. CJK in a printer name)
    /// fall back to the system fonts unless <paramref name="fallbacks"/> says otherwise (the UI tests allow none).
    /// </summary>
    internal static AppBuilder UseInterFont(this AppBuilder builder, IReadOnlyList<FontFallback>? fallbacks = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder
            .WithInterFont()
            .With(new FontManagerOptions { DefaultFamilyName = InterFamilyName, FontFallbacks = fallbacks });
    }
}
