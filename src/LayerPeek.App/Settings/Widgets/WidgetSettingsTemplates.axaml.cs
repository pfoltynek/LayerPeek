using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Markup.Xaml;

namespace LayerPeek.App.Settings.Widgets;

/// <summary>
/// The views of the widget settings panels (<c>WidgetSettingsTemplates.axaml</c>, T-092 design 6): compiled XAML with
/// compiled bindings, one <c>DataTemplate</c> per panel view model type.
/// </summary>
public sealed class WidgetSettingsTemplates : ResourceDictionary
{
    public WidgetSettingsTemplates()
    {
        // Rewritten by the XAML compiler into a call of the generated code (no runtime XAML loading; Native AOT safe).
        AvaloniaXamlLoader.Load(this);
    }

    /// <summary>
    /// Every template of the dictionary, for <c>SettingsWindow.DataTemplates</c>: each matches its <c>x:DataType</c>, so the
    /// content control of the panel picks the one of the panel's view model.
    /// </summary>
    public static IReadOnlyList<IDataTemplate> All()
    {
        WidgetSettingsTemplates templates = new();
        List<IDataTemplate> all = [];
        foreach (object key in templates.Keys.ToArray())
        {
            if (templates.TryGetResource(key, null, out object? value) && value is IDataTemplate template)
            {
                all.Add(template);
            }
        }

        return all;
    }
}
