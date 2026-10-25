using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;

namespace LayerPeek.App.Cameras.Overlay;

/// <summary>
/// A single-colour vector icon (Fluent UI System Icons, T-076 6): <see cref="Data"/> is the path of the SVG in its own
/// <see cref="CanvasSize"/> × <see cref="CanvasSize"/> canvas (16, 20 or 24), scaled to the control's size and filled with
/// <see cref="Foreground"/>. Not a hit-test target.
/// </summary>
public sealed class OverlayIcon : Control
{
    public static readonly StyledProperty<Geometry?> DataProperty =
        AvaloniaProperty.Register<OverlayIcon, Geometry?>(nameof(Data));

    public static readonly StyledProperty<double> CanvasSizeProperty =
        AvaloniaProperty.Register<OverlayIcon, double>(nameof(CanvasSize), 16);

    public static readonly StyledProperty<IBrush?> ForegroundProperty =
        AvaloniaProperty.Register<OverlayIcon, IBrush?>(nameof(Foreground), inherits: true);

    static OverlayIcon()
    {
        AffectsRender<OverlayIcon>(DataProperty, CanvasSizeProperty, ForegroundProperty);
        IsHitTestVisibleProperty.OverrideDefaultValue<OverlayIcon>(false);
    }

    public Geometry? Data
    {
        get => GetValue(DataProperty);
        set => SetValue(DataProperty, value);
    }

    /// <summary>Size of the icon's own canvas (the number in its file name, e.g. 20 for <c>ic_fluent_flag_20_regular</c>).</summary>
    public double CanvasSize
    {
        get => GetValue(CanvasSizeProperty);
        set => SetValue(CanvasSizeProperty, value);
    }

    public IBrush? Foreground
    {
        get => GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        if (Data is not { } data || Foreground is not { } brush || CanvasSize <= 0)
        {
            return;
        }

        double scale = Math.Min(Bounds.Width, Bounds.Height) / CanvasSize;
        using (context.PushTransform(Matrix.CreateScale(scale, scale)))
        {
            context.DrawGeometry(brush, null, data);
        }
    }

    protected override Size MeasureOverride(Size availableSize) =>
        new(double.IsNaN(Width) ? CanvasSize : Width, double.IsNaN(Height) ? CanvasSize : Height);
}

/// <summary>
/// A progress bar of the overlay cards (T-076 2.1): a rounded track (<see cref="TemplatedControl.Background"/>) with the
/// filled part (<see cref="TemplatedControl.Foreground"/>) for <see cref="Value"/> percent. Drawn directly, without
/// animation, so that it is cheap and stable in snapshots.
/// </summary>
public sealed class OverlayProgressBar : TemplatedControl
{
    public static readonly StyledProperty<double> ValueProperty =
        AvaloniaProperty.Register<OverlayProgressBar, double>(nameof(Value));

    static OverlayProgressBar()
    {
        AffectsRender<OverlayProgressBar>(ValueProperty, BackgroundProperty, ForegroundProperty);
        IsHitTestVisibleProperty.OverrideDefaultValue<OverlayProgressBar>(false);
    }

    /// <summary>0–100 (clamped).</summary>
    public double Value
    {
        get => GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        Rect bounds = new(Bounds.Size);
        double radius = bounds.Height / 2;
        if (Background is { } track)
        {
            context.DrawRectangle(track, null, bounds, radius, radius);
        }

        double value = double.IsFinite(Value) ? Math.Clamp(Value, 0, 100) : 0;
        if (Foreground is { } fill && value > 0)
        {
            // At least a round dot, so that 1 % is visible.
            double width = Math.Max(bounds.Width * value / 100, Math.Min(bounds.Height, bounds.Width));
            context.DrawRectangle(fill, null, new Rect(0, 0, width, bounds.Height), radius, radius);
        }
    }
}

/// <summary>The state "pill" of the Status widget (T-076 2.1): a dot in the state colour and the state name, on a light pill.</summary>
public sealed class OverlayStatusPill : TemplatedControl
{
    public static readonly StyledProperty<string?> TextProperty =
        AvaloniaProperty.Register<OverlayStatusPill, string?>(nameof(Text));

    public static readonly StyledProperty<IBrush?> DotBrushProperty =
        AvaloniaProperty.Register<OverlayStatusPill, IBrush?>(nameof(DotBrush));

    public string? Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    /// <summary>The state colour of the dot (the tray icon's colours).</summary>
    public IBrush? DotBrush
    {
        get => GetValue(DotBrushProperty);
        set => SetValue(DotBrushProperty, value);
    }
}

/// <summary>A row of an overlay card: an icon in place of a label and a value (T-076 2.2); the text may wrap to two lines.</summary>
public sealed class OverlayIconText : TemplatedControl
{
    public static readonly StyledProperty<Geometry?> IconProperty =
        AvaloniaProperty.Register<OverlayIconText, Geometry?>(nameof(Icon));

    public static readonly StyledProperty<double> IconCanvasSizeProperty =
        AvaloniaProperty.Register<OverlayIconText, double>(nameof(IconCanvasSize), 16);

    public static readonly StyledProperty<double> IconSizeProperty =
        AvaloniaProperty.Register<OverlayIconText, double>(nameof(IconSize), 16);

    public static readonly StyledProperty<IBrush?> IconBrushProperty =
        AvaloniaProperty.Register<OverlayIconText, IBrush?>(nameof(IconBrush));

    public static readonly StyledProperty<string?> TextProperty =
        AvaloniaProperty.Register<OverlayIconText, string?>(nameof(Text));

    public Geometry? Icon
    {
        get => GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    /// <summary>The icon's own canvas (16, 20 or 24).</summary>
    public double IconCanvasSize
    {
        get => GetValue(IconCanvasSizeProperty);
        set => SetValue(IconCanvasSizeProperty, value);
    }

    /// <summary>Drawn size of the icon (16; 20 in size L).</summary>
    public double IconSize
    {
        get => GetValue(IconSizeProperty);
        set => SetValue(IconSizeProperty, value);
    }

    public IBrush? IconBrush
    {
        get => GetValue(IconBrushProperty);
        set => SetValue(IconBrushProperty, value);
    }

    public string? Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }
}

/// <summary>A chip of the print name widget (T-076 2.4): short text, optionally with an icon, on a light rounded background.</summary>
public sealed class OverlayChip : TemplatedControl
{
    public static readonly StyledProperty<string?> TextProperty =
        AvaloniaProperty.Register<OverlayChip, string?>(nameof(Text));

    public static readonly StyledProperty<Geometry?> IconProperty =
        AvaloniaProperty.Register<OverlayChip, Geometry?>(nameof(Icon));

    public static readonly StyledProperty<double> IconCanvasSizeProperty =
        AvaloniaProperty.Register<OverlayChip, double>(nameof(IconCanvasSize), 20);

    public string? Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    /// <summary>Optional icon before the text (drawn 12 DIP); <see langword="null"/> = text only.</summary>
    public Geometry? Icon
    {
        get => GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    public double IconCanvasSize
    {
        get => GetValue(IconCanvasSizeProperty);
        set => SetValue(IconCanvasSizeProperty, value);
    }

    // Pseudo-class :icon: the template shows the icon only when one is set.
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IconProperty)
        {
            PseudoClasses.Set(":icon", change.NewValue is not null);
        }
    }
}
