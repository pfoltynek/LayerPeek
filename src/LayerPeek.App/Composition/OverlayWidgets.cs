using LayerPeek.App.Cameras.Overlay;
using LayerPeek.App.Cameras.Overlay.Widgets;
using LayerPeek.App.Resources;
using LayerPeek.Core.Cameras.Overlay;

namespace LayerPeek.App.Composition;

/// <summary>
/// The widgets of the camera overlay (T-081), registered explicitly: Native AOT allows no assembly scanning and no
/// <c>Activator</c>. A new widget is its view model (<see cref="OverlayWidgetViewModel{TContent}"/>), its card template and
/// one <see cref="OverlayWidgetDefinition"/> line in <see cref="CreateRegistry"/>, with its description and icon for the
/// Widgets settings tab and, when it has settings of its own, its settings panel (T-092).
/// </summary>
/// <remarks>
/// Status (T-082), Times (T-083), Temperatures (T-084) and Print name (T-085) are registered. Until the widget of T-088
/// (Cool-down) is, its placement stays in the layout and is only not drawn nor listed on the Widgets tab; T-088 registers
/// it with <see cref="OverlayWidgetDescriptions.Cooldown"/>, <see cref="OverlayWidgetIcons.Cooldown"/> and
/// <c>Settings = Settings.Widgets.CooldownWidgetSettings.Instance</c> (the panel of T-092).
/// </remarks>
internal static class OverlayWidgets
{
    /// <summary>Collision priorities (T-076 1.4 rule 3): the higher one stays when cards collide in a small window.</summary>
    public const int StatusPriority = 50;

    public const int TimesPriority = 40;

    public const int CooldownPriority = 30;

    public const int TemperaturesPriority = 20;

    public const int PrintNamePriority = 10;

    /// <summary>The widgets of this version, in the order of the widget list of the settings.</summary>
    public static OverlayWidgetRegistry CreateRegistry() => new(
    [
        new OverlayWidgetDefinition(OverlayWidgetIds.Status, OverlayWidgetNames.Status, StatusPriority, StatusWidgetTemplate.Card, static _ => new StatusWidgetViewModel(), StatusWidgetViewModel.MinWidths)
        {
            DescriptionKey = OverlayWidgetDescriptions.Status,
            IconKey = OverlayWidgetIcons.Status,
        },
        new OverlayWidgetDefinition(OverlayWidgetIds.Times, OverlayWidgetNames.Times, TimesPriority, TimesWidgetView.CardTemplate, static _ => new TimesWidgetViewModel(), TimesWidgetViewModel.MinWidths)
        {
            DescriptionKey = OverlayWidgetDescriptions.Times,
            IconKey = OverlayWidgetIcons.Times,
        },
        new OverlayWidgetDefinition(OverlayWidgetIds.Temperatures, OverlayWidgetNames.Temperatures, TemperaturesPriority, TemperaturesWidgetView.CardTemplate, static context => new TemperaturesWidgetViewModel(context), TemperaturesWidgetViewModel.MinWidths)
        {
            DescriptionKey = OverlayWidgetDescriptions.Temperatures,
            IconKey = OverlayWidgetIcons.Temperatures,
        },
        new OverlayWidgetDefinition(OverlayWidgetIds.PrintName, OverlayWidgetNames.PrintName, PrintNamePriority, PrintNameWidgetView.CardTemplate, static context => new PrintNameWidgetViewModel(context), PrintNameWidgetViewModel.MinWidths)
        {
            DescriptionKey = OverlayWidgetDescriptions.PrintName,
            IconKey = OverlayWidgetIcons.PrintName,
        },
    ]);
}

/// <summary>Keys of the widget names in <see cref="Strings"/> (settings table, layout mode), one per id of <c>OverlayWidgetIds</c>.</summary>
internal static class OverlayWidgetNames
{
    public const string Status = nameof(Strings.CameraOverlayWidgetStatus);

    public const string Times = nameof(Strings.CameraOverlayWidgetTimes);

    public const string Temperatures = nameof(Strings.CameraOverlayWidgetTemperatures);

    public const string PrintName = nameof(Strings.CameraOverlayWidgetPrintName);

    public const string Cooldown = nameof(Strings.CameraOverlayWidgetCooldown);

    /// <summary>The name key of a widget id of this version; <see langword="null"/> for an unknown id.</summary>
    public static string? KeyFor(string widgetId) => widgetId switch
    {
        OverlayWidgetIds.Status => Status,
        OverlayWidgetIds.Times => Times,
        OverlayWidgetIds.Temperatures => Temperatures,
        OverlayWidgetIds.PrintName => PrintName,
        OverlayWidgetIds.Cooldown => Cooldown,
        _ => null,
    };
}

/// <summary>Keys of the one-sentence widget descriptions in <see cref="Strings"/> (Widgets settings tab, T-092 design 4.1 and 8).</summary>
internal static class OverlayWidgetDescriptions
{
    public const string Status = nameof(Strings.CameraOverlayWidgetStatusDescription);

    public const string Times = nameof(Strings.CameraOverlayWidgetTimesDescription);

    public const string Temperatures = nameof(Strings.CameraOverlayWidgetTemperaturesDescription);

    public const string PrintName = nameof(Strings.CameraOverlayWidgetPrintNameDescription);

    public const string Cooldown = nameof(Strings.CameraOverlayWidgetCooldownDescription);
}

/// <summary>
/// Keys of the widget icons in <c>Cameras/Overlay/OverlayIcons.axaml</c> (Fluent UI System Icons, T-076 6), shown in the
/// widget list of the settings (T-092) and on the placeholder cards of the layout mode (T-091).
/// </summary>
internal static class OverlayWidgetIcons
{
    public const string Status = "IconStatus16Regular";

    public const string Times = "IconTimer16Regular";

    public const string Temperatures = "IconTemperature16Regular";

    public const string PrintName = "IconCube16Regular";

    /// <summary>Only a 20 DIP variant exists; drawn smaller in the 16 DIP list (T-092 design 6).</summary>
    public const string Cooldown = "IconWeatherSnowflake20Regular";
}
