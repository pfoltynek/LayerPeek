using System.Globalization;

using LayerPeek.App.Cameras.Overlay;
using LayerPeek.App.Composition;
using LayerPeek.App.Resources;
using LayerPeek.Core.Cameras.Overlay;
using LayerPeek.Core.Settings;

namespace LayerPeek.App.Tests.Cameras.Overlay;

/// <summary>The explicit widget registry (T-081, Native AOT: no scanning) and the registration of this version.</summary>
public sealed class OverlayWidgetRegistryTests
{
    [Fact]
    public void ExplicitListKeepsItsOrderAndFindsIdsIgnoringCase()
    {
        OverlayWidgetRegistry registry = TestOverlayWidgets.Registry("test-b", "test-a");

        Assert.Equal(["test-b", "test-a"], registry.Definitions.Select(static definition => definition.Id));
        Assert.Same(registry.Definitions[1], registry.Find("TEST-A"));
        Assert.Null(registry.Find("future-widget"));
        Assert.Empty(OverlayWidgetRegistry.Empty.Definitions);
    }

    [Fact]
    public void WidgetCanBeRegisteredOnlyOnce()
    {
        ArgumentException error = Assert.Throws<ArgumentException>(() => TestOverlayWidgets.Registry("test-a", "Test-A"));

        Assert.Contains("Test-A", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RegistryRejectsInvalidEntries()
    {
        Assert.Throws<ArgumentNullException>(() => new OverlayWidgetRegistry(null!));
        Assert.Throws<ArgumentException>(() => new OverlayWidgetRegistry([TestOverlayWidgets.Definition("test-a"), null!]));
        Assert.Throws<ArgumentException>(() => new OverlayWidgetRegistry([new InvalidDefinition()]));
    }

    [Fact]
    public void RegisteredWidgetsOfThisVersionHaveKnownIdsEachOnce()
    {
        OverlayWidgetRegistry registry = OverlayWidgets.CreateRegistry();

        // Every registered widget is one of OverlayWidgetIds (its placement is persisted), and only once.
        Assert.All(registry.Definitions, static definition => Assert.Contains(definition.Id, OverlayWidgetIds.Known));
        Assert.Equal(registry.Definitions.Count, registry.Definitions.Select(static definition => definition.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(registry.Definitions, static definition => Assert.Equal(OverlayWidgetNames.KeyFor(definition.Id), definition.NameKey));
    }

    [Fact]
    public void EveryWidgetOfThisVersionHasANameInEveryLanguage()
    {
        foreach (string id in OverlayWidgetIds.Known)
        {
            string key = Assert.IsType<string>(OverlayWidgetNames.KeyFor(id));
            foreach (string language in AppLanguages.Supported)
            {
                Assert.False(string.IsNullOrWhiteSpace(Strings.ResourceManager.GetString(key, CultureInfo.GetCultureInfo(language))), $"{key} ({language})");
            }
        }

        Assert.Null(OverlayWidgetNames.KeyFor("future-widget"));
        using (new Tray.UiCultureScope("cs"))
        {
            Assert.Equal("Vychladnutí", Strings.CameraOverlayWidgetCooldown);
        }
    }

    [Fact]
    public void CollisionPrioritiesFollowTheDesign()
    {
        // T-076 1.4 rule 3: Status > Times > Cool-down > Temperatures > Print name.
        int[] order = [OverlayWidgets.StatusPriority, OverlayWidgets.TimesPriority, OverlayWidgets.CooldownPriority, OverlayWidgets.TemperaturesPriority, OverlayWidgets.PrintNamePriority];

        Assert.Equal(order.OrderDescending(), order);
        Assert.Equal(order.Length, order.Distinct().Count());
    }

    private sealed class InvalidDefinition : IOverlayWidgetDefinition
    {
        public string Id => " invalid";

        public string NameKey => "x";

        public IReadOnlyList<OverlayWidgetSize> SupportedSizes => [OverlayWidgetSize.Medium];

        public int Priority => 0;

        public Avalonia.Controls.Templates.IDataTemplate CardTemplate => TestOverlayWidgets.Template;

        public double MinWidth(OverlayWidgetSize size) => 0;

        public OverlayWidgetViewModel Create(OverlayWidgetContext context) => new TestOverlayWidget();
    }
}
