using System.Globalization;

using PrusaTrayDiagnostic.App.Resources;
using PrusaTrayDiagnostic.Core.Settings;

namespace PrusaTrayDiagnostic.App.Localization;

/// <summary>
/// The language of the application's texts (<see cref="AppSettings.Language"/>). Only the texts change
/// (<see cref="Strings.Culture"/>); number, date and time formats keep following the system (<c>CurrentCulture</c> is
/// never touched). <see langword="null"/> = the system language: resources then follow the UI culture of the system,
/// including regional variants (<c>de-AT</c> → German) and English for anything untranslated. UI thread only.
/// </summary>
internal sealed class UiLanguage
{
    private readonly Action<CultureInfo?> _applyCulture;

    public UiLanguage()
        : this(ApplyToResources)
    {
    }

    /// <param name="applyCulture">Switches the texts; <see langword="null"/> = system language. Tests replace the global switch.</param>
    internal UiLanguage(Action<CultureInfo?> applyCulture)
    {
        ArgumentNullException.ThrowIfNull(applyCulture);
        _applyCulture = applyCulture;
    }

    /// <summary>
    /// The texts are in another language now: everything that formatted texts before has to format them again. Raised
    /// on the UI thread after the switch; XAML bound through <see cref="LocalizedStrings"/> has already been updated.
    /// </summary>
    public event EventHandler? Changed;

    /// <summary>The applied language code; <see langword="null"/> = system language (the initial state).</summary>
    public string? Code { get; private set; }

    /// <summary>
    /// Switches the texts to <paramref name="code"/> (one of <see cref="AppLanguages.Supported"/>; <see langword="null"/>
    /// or an unknown code = system language) and raises <see cref="Changed"/>; nothing happens when it is already applied.
    /// </summary>
    /// <returns>Whether the language changed.</returns>
    public bool Apply(string? code)
    {
        string? normalized = AppLanguages.TryNormalize(code, out string? supported) ? supported : null;
        if (string.Equals(normalized, Code, StringComparison.Ordinal))
        {
            return false;
        }

        Code = normalized;
        _applyCulture(normalized is null ? null : CultureInfo.GetCultureInfo(normalized));
        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    private static void ApplyToResources(CultureInfo? culture)
    {
        // Read by every generated Strings property on any thread; null = the calling thread's UI culture (the system's).
        Strings.Culture = culture;
        LocalizedStrings.Instance.Refresh();
    }
}
