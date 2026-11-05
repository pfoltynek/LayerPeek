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
    /// The manual in the language of <paramref name="textCulture"/> (regional variants count as their language:
    /// <c>de-AT</c> → <c>de</c>); English for any language the app is not translated into.
    /// </summary>
    public static Uri UriFor(CultureInfo textCulture)
    {
        ArgumentNullException.ThrowIfNull(textCulture);
        string language = AppLanguages.TryNormalize(textCulture.TwoLetterISOLanguageName, out string? code) ? code! : "en";
        return new Uri($"{AboutViewModel.RepositoryUri.AbsoluteUri.TrimEnd('/')}/blob/main/manuals/{language}/manual.md");
    }
}
