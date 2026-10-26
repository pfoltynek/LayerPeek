using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;

using LayerPeek.App.Cameras.Overlay;

namespace LayerPeek.App.Settings.Widgets;

/// <summary>
/// The icon of a widget by its resource key (<see cref="IOverlayWidgetDefinition.IconKey"/>, T-092): the
/// <c>StreamGeometry</c> of <c>OverlayIcons.axaml</c>, drawn by an <see cref="OverlayIcon"/> scaled from its own canvas
/// (the size in the key, e.g. 20 for <c>IconWeatherSnowflake20Regular</c>) to the size of this control, in the text colour
/// (<see cref="Foreground"/>, inherited). The key is resolved like <c>{DynamicResource}</c> when the control is attached;
/// nothing is drawn for an unknown key. No reflection (Native AOT).
/// </summary>
public sealed class WidgetIcon : Decorator
{
    public static readonly StyledProperty<string?> IconKeyProperty =
        AvaloniaProperty.Register<WidgetIcon, string?>(nameof(IconKey));

    public static readonly StyledProperty<IBrush?> ForegroundProperty =
        TextElement.ForegroundProperty.AddOwner<WidgetIcon>();

    private readonly OverlayIcon _icon = new();

    public WidgetIcon()
    {
        Child = _icon;
        _icon.Bind(OverlayIcon.ForegroundProperty, this.GetObservable(ForegroundProperty));
    }

    public string? IconKey
    {
        get => GetValue(IconKeyProperty);
        set => SetValue(IconKeyProperty, value);
    }

    public IBrush? Foreground
    {
        get => GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    /// <summary>The geometry drawn (for tests); <see langword="null"/> while the key is unknown or the control is detached.</summary>
    internal Geometry? Data => _icon.Data;

    /// <summary>
    /// The canvas size in an icon key <c>Icon&lt;Name&gt;&lt;Size&gt;&lt;Style&gt;</c> (T-076 6): the digits before the
    /// style ("Regular", "Filled"); 16 when the key has none.
    /// </summary>
    public static double CanvasSizeOf(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        ReadOnlySpan<char> name = key.AsSpan();
        int end = name.Length;
        while (end > 0 && char.IsAsciiLetter(name[end - 1]))
        {
            end--;
        }

        int start = end;
        while (start > 0 && char.IsAsciiDigit(name[start - 1]))
        {
            start--;
        }

        return start < end && int.TryParse(name[start..end], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int size) && size > 0
            ? size
            : 16;
    }

    protected override void OnAttachedToLogicalTree(Avalonia.LogicalTree.LogicalTreeAttachmentEventArgs e)
    {
        base.OnAttachedToLogicalTree(e);
        UpdateIcon();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IconKeyProperty)
        {
            UpdateIcon();
        }
    }

    private void UpdateIcon()
    {
        if (IconKey is { } key && this.TryFindResource(key, ActualThemeVariant, out object? resource) && resource is Geometry geometry)
        {
            _icon.Data = geometry;
            _icon.CanvasSize = CanvasSizeOf(key);
        }
        else
        {
            _icon.Data = null;
        }
    }
}
