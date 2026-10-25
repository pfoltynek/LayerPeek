using LayerPeek.App.Cameras.Layout;
using LayerPeek.App.Cameras.Overlay;
using LayerPeek.App.Composition;
using LayerPeek.App.Settings.Widgets;
using LayerPeek.App.Tests.Cameras.Overlay;
using LayerPeek.Core.Cameras.Overlay;
using LayerPeek.Core.Settings;

namespace LayerPeek.App.Tests.Settings;

/// <summary>Widget registrations and fakes of the Widgets settings tab (T-092). Linked into the UI tests.</summary>
internal static class WidgetSettingsTestData
{
    /// <summary>
    /// The Cool-down widget registered with its settings panel, as T-088 will register it (until then only in tests); the
    /// card is the test widget's.
    /// </summary>
    public static OverlayWidgetDefinition Cooldown { get; } = new(
        OverlayWidgetIds.Cooldown,
        OverlayWidgetNames.Cooldown,
        OverlayWidgets.CooldownPriority,
        TestOverlayWidgets.Template,
        static _ => new TestOverlayWidget())
    {
        DescriptionKey = OverlayWidgetDescriptions.Cooldown,
        IconKey = OverlayWidgetIcons.Cooldown,
        Settings = CooldownWidgetSettings.Instance,
    };

    /// <summary>The widgets of this version plus the Cool-down widget with its panel (the order of the design).</summary>
    public static OverlayWidgetRegistry WithCooldown() => new([.. OverlayWidgets.CreateRegistry().Definitions, Cooldown]);
}

/// <summary>Records the starts of the layout mode (T-091) by "Edit layout in camera window…".</summary>
internal sealed class FakeLayoutModeLauncher : ICameraLayoutModeLauncher
{
    public List<(string PrinterId, CameraOverlayLayout? Layout)> Starts { get; } = [];

    public bool Result { get; set; } = true;

    public bool Start(string printerId, CameraOverlayLayout? startingLayout)
    {
        Starts.Add((printerId, startingLayout));
        return Result;
    }
}

/// <summary>Announces layouts like the layout mode of a camera window would after "Done" (T-077 Q3).</summary>
internal sealed class FakeLayoutAnnouncer : ICameraOverlayLayoutWriter
{
    private EventHandler<CameraOverlayLayoutSavedEventArgs>? _layoutSaved;

    public event EventHandler<CameraOverlayLayoutSavedEventArgs>? LayoutSaved
    {
        add
        {
            _layoutSaved += value;
            Subscribers++;
        }

        remove
        {
            _layoutSaved -= value;
            Subscribers--;
        }
    }

    public int Subscribers { get; private set; }

    /// <summary>Raises <see cref="LayoutSaved"/> on the calling thread (the writer's thread in the app).</summary>
    public void Announce(string printerId, CameraOverlayLayout layout) => _layoutSaved?.Invoke(this, new CameraOverlayLayoutSavedEventArgs(printerId, layout));

    public Task<bool> SaveAsync(string printerId, CameraOverlayLayout layout, CancellationToken cancellationToken)
    {
        Announce(printerId, layout);
        return Task.FromResult(true);
    }
}
