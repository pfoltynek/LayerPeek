using LayerPeek.App.Resources;
using LayerPeek.Core.Settings;

namespace LayerPeek.App.Localization;

/// <summary>An item of the language selection: the system language or one of <see cref="AppLanguages.Supported"/>.</summary>
public sealed class LanguageOption : LocalizedOption
{
    private LanguageOption(string? code, Func<string> text)
        : base(text)
    {
        Code = code;
    }

    /// <summary>Language code; <see langword="null"/> = follow the system.</summary>
    public string? Code { get; }

    /// <summary>
    /// "System default" (in the current language) first, then every language under its own name, so that a user finds
    /// it even when the current language is one they do not read.
    /// </summary>
    internal static LanguageOption[] CreateAll()
    {
        LanguageOption[] options = new LanguageOption[1 + NativeNames.Length];
        options[0] = new LanguageOption(null, static () => Strings.SettingsLanguageSystem);
        for (int i = 0; i < NativeNames.Length; i++)
        {
            string name = NativeNames[i].Name;
            options[i + 1] = new LanguageOption(NativeNames[i].Code, () => name);
        }

        return options;
    }

    /// <summary>Names of the languages in the language itself (never translated), in alphabetical order.</summary>
    internal static readonly (string Code, string Name)[] NativeNames =
    [
        ("cs", "Čeština"),
        ("de", "Deutsch"),
        ("en", "English"),
        ("es", "Español"),
        ("fr", "Français"),
        ("it", "Italiano"),
        ("pl", "Polski"),
        ("sk", "Slovenčina"),
    ];
}
