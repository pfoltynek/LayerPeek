using LayerPeek.App.Cameras.Overlay;
using LayerPeek.App.Resources;

namespace LayerPeek.App.Composition;

/// <summary>
/// The widgets of the camera overlay (T-081), registered explicitly: Native AOT allows no assembly scanning and no
/// <c>Activator</c>. A new widget is its view model (<see cref="OverlayWidgetViewModel{TContent}"/>), its card template and
/// one <see cref="OverlayWidgetDefinition"/> line in <see cref="CreateRegistry"/>.
/// </summary>
/// <remarks>
/// Until the widgets of T-082 (Status), T-083 (Times), T-084 (Temperatures), T-085 (Print name) and T-088 (Cool-down)
/// are registered, the overlay draws no cards: their placements stay in the layout and are only not drawn.
/// </remarks>
internal static class OverlayWidgets
{
    /// <summary>Collision priorities (T-076 1.4 rule 3): the higher one stays when cards collide in a small window.</summary>
    public const int StatusPriority = 50;

    public const int TimesPriority = 40;

    public const int CooldownPriority = 30;

    public const int TemperaturesPriority = 20;

    public const int PrintNamePriority = 10;

    /// <summary>The widgets of this version, in the order of the settings table.</summary>
    public static OverlayWidgetRegistry CreateRegistry() => new([]);
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
        Core.Cameras.Overlay.OverlayWidgetIds.Status => Status,
        Core.Cameras.Overlay.OverlayWidgetIds.Times => Times,
        Core.Cameras.Overlay.OverlayWidgetIds.Temperatures => Temperatures,
        Core.Cameras.Overlay.OverlayWidgetIds.PrintName => PrintName,
        Core.Cameras.Overlay.OverlayWidgetIds.Cooldown => Cooldown,
        _ => null,
    };
}
