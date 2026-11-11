using System.Globalization;

using LayerPeek.App.Windows;
using LayerPeek.Core.Settings;

namespace LayerPeek.App.Tray.Menu;

/// <summary>
/// Address of the end-user manual (T-172): <c>manuals/&lt;language&gt;/manual.md</c> on <c>main</c>, which matches the
/// released version. There is one manual per <see cref="AppLanguages.Supported"/> language.
/// </summary>
internal static class UserManual
{
    /// <summary>
    /// The GitHub anchor of the chapter "PrusaLink access security" (T-183) in each manual, linked from the warning about an
    /// unencrypted address outside the local network (T-182). The one place that maps a language to its chapter. Empty until
    /// T-183 is merged (open item in T-182): a language without an anchor shows no link.
    /// </summary>
    private static readonly Dictionary<string, string> SecurityChapterAnchors = new(StringComparer.Ordinal);

    /// <summary>
    /// The manual in the language of <paramref name="textCulture"/> (regional variants count as their language:
    /// <c>de-AT</c> → <c>de</c>); English for any language the app is not translated into.
    /// </summary>
    public static Uri UriFor(CultureInfo textCulture) => ManualUri(LanguageOf(textCulture), anchor: null);

    /// <summary>
    /// The chapter about securing the access to PrusaLink (T-183) in the manual of <paramref name="textCulture"/> (same
    /// language rule as <see cref="UriFor"/>); <see langword="null"/> while that manual has no such chapter.
    /// </summary>
    public static Uri? SecurityChapterUriFor(CultureInfo textCulture)
    {
        string language = LanguageOf(textCulture);
        return SecurityChapterAnchors.TryGetValue(language, out string? anchor) ? ManualUri(language, anchor) : null;
    }

    private static string LanguageOf(CultureInfo textCulture)
    {
        ArgumentNullException.ThrowIfNull(textCulture);
        return AppLanguages.TryNormalize(textCulture.TwoLetterISOLanguageName, out string? code) ? code! : "en";
    }

    private static Uri ManualUri(string language, string? anchor) =>
        new($"{AboutViewModel.RepositoryUri.AbsoluteUri.TrimEnd('/')}/blob/main/manuals/{language}/manual.md{(anchor is null ? null : "#" + anchor)}");
}
