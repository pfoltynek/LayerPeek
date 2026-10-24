using CommunityToolkit.Mvvm.ComponentModel;

namespace LayerPeek.App.Localization;

/// <summary>
/// An item of a selection whose text follows a language switch: the item instance stays (so the selection is kept) and
/// only <see cref="Text"/> is read again (<see cref="RefreshText"/>).
/// </summary>
public abstract class LocalizedOption : ObservableObject
{
    private readonly Func<string> _text;

    /// <param name="text">Reads the text in the current language (e.g. <c>static () => Strings.SettingsSoundOff</c>).</param>
    protected LocalizedOption(Func<string> text)
    {
        ArgumentNullException.ThrowIfNull(text);
        _text = text;
    }

    public string Text => _text();

    /// <summary>UI thread: the language changed.</summary>
    internal void RefreshText() => OnPropertyChanged(nameof(Text));

    public override string ToString() => Text;
}
