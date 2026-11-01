using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Markup.Xaml;

namespace LayerPeek.App.Cameras.Overlay.Widgets;

/// <summary>The card content of the Status widget (<c>StatusWidgetTemplate.axaml</c>, compiled XAML and bindings).</summary>
public sealed class StatusWidgetTemplate : ResourceDictionary
{
    private const string Key = "StatusWidgetCard";

    public StatusWidgetTemplate()
    {
        // Rewritten by the XAML compiler into a call of the generated code (no runtime XAML loading; Native AOT safe).
        AvaloniaXamlLoader.Load(this);
    }

    /// <summary>
    /// The <c>DataTemplate</c> of <see cref="StatusWidgetViewModel"/> (<c>x:DataType</c>), loaded once. The first use may
    /// come from several threads at once (T-131): the template is published atomically, so every caller gets the same
    /// instance even if two of them loaded the dictionary (a plain <c>field ??= …</c> let the loser keep its own copy).
    /// </summary>
    public static IDataTemplate Card => LazyInitializer.EnsureInitialized(ref field, static () => (IDataTemplate)new StatusWidgetTemplate()[Key]!);
}
