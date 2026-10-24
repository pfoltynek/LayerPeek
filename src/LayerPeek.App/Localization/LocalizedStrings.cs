using System.ComponentModel;

using LayerPeek.App.Resources;

namespace LayerPeek.App.Localization;

/// <summary>
/// Texts of <see cref="Strings"/> for XAML that follow a language switch at run time:
/// <c>{Binding [SettingsTitle], Source={x:Static loc:LocalizedStrings.Instance}}</c>. A compiled indexer binding (no
/// reflection, Native AOT safe) whose value is read again when <see cref="Refresh"/> raises the indexer change; the
/// binding engine listens weakly, so closed windows are not kept alive by this static instance. <c>{x:Static}</c> would
/// be evaluated only once, when the window is created.
/// </summary>
public sealed class LocalizedStrings : INotifyPropertyChanged
{
    // Avalonia's indexer bindings react to "Item"; "Item[]" is the WPF / older Avalonia convention.
    private static readonly PropertyChangedEventArgs IndexerChanged = new("Item");
    private static readonly PropertyChangedEventArgs IndexerChangedLegacy = new("Item[]");

    private LocalizedStrings()
    {
    }

    public static LocalizedStrings Instance { get; } = new();

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>The text of <paramref name="key"/> in the current language; the key itself when it does not exist (a visible typo).</summary>
    public string this[string key] => Strings.ResourceManager.GetString(key, Strings.Culture) ?? key;

    /// <summary>UI thread: every bound text is read again (after the language changed).</summary>
    internal void Refresh()
    {
        PropertyChanged?.Invoke(this, IndexerChanged);
        PropertyChanged?.Invoke(this, IndexerChangedLegacy);
    }
}
