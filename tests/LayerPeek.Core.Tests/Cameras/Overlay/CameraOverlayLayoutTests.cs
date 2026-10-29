using LayerPeek.Core.Cameras;
using LayerPeek.Core.Cameras.Overlay;
using LayerPeek.Core.Cooldown;
using LayerPeek.Core.Printers;

namespace LayerPeek.Core.Tests.Cameras.Overlay;

/// <summary>The widget layout of the camera overlay (T-077): default per T-076 3.3, validation, equality and pure operations.</summary>
public class CameraOverlayLayoutTests
{
    private const string Future = "future-widget";

    private static OverlayWidgetPlacement P(string id, OverlayAnchor anchor, OverlayWidgetSize size = OverlayWidgetSize.Medium, bool emphasized = false, bool visible = true) =>
        new(id, anchor, size, emphasized, visible);

    // ---- Default (T-076 3.3) ----

    [Fact]
    public void DefaultFollowsTheApprovedDesign()
    {
        Assert.Equal(
            [
                P(OverlayWidgetIds.Status, OverlayAnchor.TopLeft, emphasized: true),
                P(OverlayWidgetIds.PrintName, OverlayAnchor.TopCenter),
                P(OverlayWidgetIds.Times, OverlayAnchor.BottomLeft),
                P(OverlayWidgetIds.Cooldown, OverlayAnchor.BottomLeft),
                P(OverlayWidgetIds.Temperatures, OverlayAnchor.BottomRight),
            ],
            CameraOverlayLayout.Default.Widgets);
    }

    [Fact]
    public void MainAnchorIsTheAnchorOfTheTimesWidget()
    {
        Assert.Equal(OverlayAnchor.BottomLeft, CameraOverlayLayout.MainAnchor);
        Assert.Equal(CameraOverlayLayout.MainAnchor, CameraOverlayLayout.Default.Find(OverlayWidgetIds.Times)!.Anchor);
    }

    [Fact]
    public void DefaultContainsEveryKnownWidgetOnce()
    {
        Assert.Equal(OverlayWidgetIds.Known.Order(StringComparer.Ordinal), CameraOverlayLayout.Default.Widgets.Select(static w => w.WidgetId).Order(StringComparer.Ordinal));
        Assert.Equal(["status", "times", "temperatures", "print-name", "cooldown"], OverlayWidgetIds.Known);
    }

    [Fact]
    public void DefaultPlacementIsTheEntryOfTheDefaultLayout()
    {
        Assert.Equal(P(OverlayWidgetIds.Temperatures, OverlayAnchor.BottomRight), CameraOverlayLayout.DefaultPlacement(OverlayWidgetIds.Temperatures));
        Assert.Null(CameraOverlayLayout.DefaultPlacement(Future));
    }

    // ---- Placement validation ----

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData(" status")]
    [InlineData("status ")]
    [InlineData("a\u0001b")]
    public void InvalidWidgetIdIsRejected(string id) =>
        Assert.Throws<ArgumentException>(() => P(id, OverlayAnchor.TopLeft));

    [Fact]
    public void TooLongWidgetIdIsRejected()
    {
        Assert.Throws<ArgumentException>(() => P(new string('a', OverlayWidgetIds.MaxLength + 1), OverlayAnchor.TopLeft));
        Assert.Equal(OverlayWidgetIds.MaxLength, P(new string('a', OverlayWidgetIds.MaxLength), OverlayAnchor.TopLeft).WidgetId.Length);
    }

    [Fact]
    public void NullWidgetIdIsRejected() =>
        Assert.Throws<ArgumentNullException>(() => P(null!, OverlayAnchor.TopLeft));

    [Fact]
    public void UnknownEnumValuesAreRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => P(OverlayWidgetIds.Status, (OverlayAnchor)6));
        Assert.Throws<ArgumentOutOfRangeException>(() => P(OverlayWidgetIds.Status, OverlayAnchor.TopLeft, (OverlayWidgetSize)3));
        OverlayWidgetPlacement placement = P(OverlayWidgetIds.Status, OverlayAnchor.TopLeft);
        Assert.Throws<ArgumentOutOfRangeException>(() => placement with { Anchor = (OverlayAnchor)(-1) });
        Assert.Throws<ArgumentOutOfRangeException>(() => placement with { Size = (OverlayWidgetSize)(-1) });
    }

    [Fact]
    public void UnknownWidgetIdIsAValidPlacement()
    {
        OverlayWidgetPlacement placement = P(Future, OverlayAnchor.TopRight, OverlayWidgetSize.Large);

        Assert.Equal(Future, placement.WidgetId);
        Assert.False(OverlayWidgetIds.IsKnown(Future));
    }

    // ---- Layout validation ----

    [Fact]
    public void DuplicateWidgetIdIsRejectedIgnoringCase()
    {
        Assert.Throws<ArgumentException>(() => new CameraOverlayLayout([P("status", OverlayAnchor.TopLeft), P("STATUS", OverlayAnchor.BottomLeft)]));
    }

    [Fact]
    public void NullEntryIsRejected()
    {
        Assert.Throws<ArgumentException>(() => new CameraOverlayLayout([P("status", OverlayAnchor.TopLeft), null!]));
        Assert.Throws<ArgumentNullException>(() => new CameraOverlayLayout(null!));
    }

    [Fact]
    public void TooManyWidgetsAreRejected()
    {
        OverlayWidgetPlacement[] many = [.. Enumerable.Range(0, CameraOverlayLayout.MaxWidgets + 1).Select(static i => P($"w{i}", OverlayAnchor.TopLeft))];

        Assert.Throws<ArgumentException>(() => new CameraOverlayLayout(many));
        Assert.Equal(CameraOverlayLayout.MaxWidgets, new CameraOverlayLayout(many[..^1]).Widgets.Count);
    }

    [Fact]
    public void EmptyLayoutIsAllowed() => Assert.Empty(new CameraOverlayLayout([]).Widgets);

    [Fact]
    public void WidgetsAreGroupedByAnchorKeepingTheOrderWithinEachAnchor()
    {
        CameraOverlayLayout layout = new(
        [
            P("times", OverlayAnchor.BottomLeft),
            P("status", OverlayAnchor.TopLeft),
            P("cooldown", OverlayAnchor.BottomLeft),
            P(Future, OverlayAnchor.TopLeft),
        ]);

        Assert.Equal(["status", Future, "times", "cooldown"], layout.Widgets.Select(static w => w.WidgetId));
    }

    [Fact]
    public void ConstructorCopiesTheInput()
    {
        List<OverlayWidgetPlacement> input = [P("status", OverlayAnchor.TopLeft)];
        CameraOverlayLayout layout = new(input);

        input.Add(P("times", OverlayAnchor.TopLeft));

        Assert.Single(layout.Widgets);
        Assert.False(layout.Widgets is IList<OverlayWidgetPlacement> { IsReadOnly: false });
    }

    // ---- Equality ----

    [Fact]
    public void EqualityComparesTheWidgetsElementByElement()
    {
        CameraOverlayLayout copy = new(CameraOverlayLayout.Default.Widgets.Select(static w => w with { }));

        Assert.Equal(CameraOverlayLayout.Default, copy);
        Assert.True(CameraOverlayLayout.Default == copy);
        Assert.Equal(CameraOverlayLayout.Default.GetHashCode(), copy.GetHashCode());
        Assert.NotEqual(CameraOverlayLayout.Default, copy.WithSize("status", OverlayWidgetSize.Large));
        Assert.NotEqual(CameraOverlayLayout.Default, copy.MoveTo("cooldown", OverlayAnchor.BottomLeft, 0));
        Assert.False(CameraOverlayLayout.Default.Equals(null));
    }

    [Fact]
    public void EqualityDoesNotDependOnTheInputOrderAcrossAnchors()
    {
        CameraOverlayLayout a = new([P("status", OverlayAnchor.TopLeft), P("times", OverlayAnchor.BottomLeft)]);
        CameraOverlayLayout b = new([P("times", OverlayAnchor.BottomLeft), P("status", OverlayAnchor.TopLeft)]);

        Assert.Equal(a, b);
    }

    [Fact]
    public void ToStringListsTheWidgets() =>
        Assert.Contains("print-name", CameraOverlayLayout.Default.ToString(), StringComparison.Ordinal);

    // ---- Queries ----

    [Fact]
    public void InAnchorListsTheWidgetsFromTheWindowEdgeInward()
    {
        Assert.Equal(["times", "cooldown"], CameraOverlayLayout.Default.InAnchor(OverlayAnchor.BottomLeft).Select(static w => w.WidgetId));
        Assert.Empty(CameraOverlayLayout.Default.InAnchor(OverlayAnchor.TopRight));
        Assert.Throws<ArgumentOutOfRangeException>(() => CameraOverlayLayout.Default.InAnchor((OverlayAnchor)9));
    }

    [Fact]
    public void FindIgnoresCase()
    {
        Assert.Equal("print-name", CameraOverlayLayout.Default.Find("PRINT-NAME")!.WidgetId);
        Assert.Null(CameraOverlayLayout.Default.Find(Future));
        Assert.True(CameraOverlayLayout.Default.Contains("Times"));
    }

    // ---- MoveTo ----

    [Fact]
    public void MoveToAnotherAnchorAtThePosition()
    {
        CameraOverlayLayout moved = CameraOverlayLayout.Default.MoveTo("temperatures", OverlayAnchor.BottomLeft, 1);

        Assert.Equal(["times", "temperatures", "cooldown"], moved.InAnchor(OverlayAnchor.BottomLeft).Select(static w => w.WidgetId));
        Assert.Empty(moved.InAnchor(OverlayAnchor.BottomRight));
        Assert.Equal(OverlayAnchor.BottomLeft, moved.Find("temperatures")!.Anchor);
    }

    [Theory]
    [InlineData(0, new[] { "temperatures", "times", "cooldown" })]
    [InlineData(1, new[] { "times", "temperatures", "cooldown" })]
    [InlineData(2, new[] { "times", "cooldown", "temperatures" })]
    public void MoveToEveryPositionOfAnAnchor(int position, string[] expected)
    {
        CameraOverlayLayout moved = CameraOverlayLayout.Default.MoveTo("temperatures", OverlayAnchor.BottomLeft, position);

        Assert.Equal(expected, moved.InAnchor(OverlayAnchor.BottomLeft).Select(static w => w.WidgetId));
    }

    [Fact]
    public void MoveWithinTheSameAnchorReorders()
    {
        CameraOverlayLayout moved = CameraOverlayLayout.Default.MoveTo("times", OverlayAnchor.BottomLeft, 1);

        Assert.Equal(["cooldown", "times"], moved.InAnchor(OverlayAnchor.BottomLeft).Select(static w => w.WidgetId));
    }

    [Fact]
    public void MoveIntoAnEmptyAnchor()
    {
        CameraOverlayLayout moved = CameraOverlayLayout.Default.MoveTo("status", OverlayAnchor.TopRight, 0);

        Assert.Equal(["status"], moved.InAnchor(OverlayAnchor.TopRight).Select(static w => w.WidgetId));
        Assert.Empty(moved.InAnchor(OverlayAnchor.TopLeft));
    }

    [Fact]
    public void MoveKeepsSizeFlagsAndOtherWidgets()
    {
        CameraOverlayLayout moved = CameraOverlayLayout.Default.MoveTo("status", OverlayAnchor.BottomCenter, 0);

        Assert.Equal(P("status", OverlayAnchor.BottomCenter, emphasized: true), moved.Find("status"));
        Assert.Equal(CameraOverlayLayout.Default.Widgets.Where(static w => w.WidgetId != "status"), moved.Widgets.Where(static w => w.WidgetId != "status"));
    }

    [Fact]
    public void MoveToTheCurrentPlaceReturnsTheSameInstance()
    {
        Assert.Same(CameraOverlayLayout.Default, CameraOverlayLayout.Default.MoveTo("cooldown", OverlayAnchor.BottomLeft, 1));
        Assert.Same(CameraOverlayLayout.Default, CameraOverlayLayout.Default.MoveTo("Status", OverlayAnchor.TopLeft, 0));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(3)]
    public void MoveToAPositionOutsideTheAnchorIsRejected(int position) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => CameraOverlayLayout.Default.MoveTo("temperatures", OverlayAnchor.BottomLeft, position));

    [Fact]
    public void MoveWithinTheSameAnchorRejectsThePositionAfterItself() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => CameraOverlayLayout.Default.MoveTo("times", OverlayAnchor.BottomLeft, 2));

    [Fact]
    public void OperationsOnAMissingWidgetAreRejected()
    {
        Assert.Throws<ArgumentException>(() => CameraOverlayLayout.Default.MoveTo(Future, OverlayAnchor.TopLeft, 0));
        Assert.Throws<ArgumentException>(() => CameraOverlayLayout.Default.WithSize(Future, OverlayWidgetSize.Small));
        Assert.Throws<ArgumentException>(() => CameraOverlayLayout.Default.WithEmphasized(Future, true));
        Assert.Throws<ArgumentException>(() => CameraOverlayLayout.Default.WithVisible(Future, false));
        Assert.Throws<ArgumentOutOfRangeException>(() => CameraOverlayLayout.Default.MoveTo("status", (OverlayAnchor)7, 0));
    }

    // ---- Size, emphasis, visibility ----

    [Fact]
    public void WithSizeChangesOnlyThatWidget()
    {
        CameraOverlayLayout changed = CameraOverlayLayout.Default.WithSize("times", OverlayWidgetSize.Large);

        Assert.Equal(OverlayWidgetSize.Large, changed.Find("times")!.Size);
        Assert.Equal(CameraOverlayLayout.Default.Widgets.Where(static w => w.WidgetId != "times"), changed.Widgets.Where(static w => w.WidgetId != "times"));
        Assert.Equal(CameraOverlayLayout.Default.Widgets.Select(static w => w.WidgetId), changed.Widgets.Select(static w => w.WidgetId));
        Assert.Same(CameraOverlayLayout.Default, CameraOverlayLayout.Default.WithSize("times", OverlayWidgetSize.Medium));
        Assert.Throws<ArgumentOutOfRangeException>(() => CameraOverlayLayout.Default.WithSize("times", (OverlayWidgetSize)5));
    }

    [Fact]
    public void WithEmphasizedChangesOnlyThatWidget()
    {
        CameraOverlayLayout changed = CameraOverlayLayout.Default.WithEmphasized("status", false).WithEmphasized("temperatures", true);

        Assert.False(changed.Find("status")!.Emphasized);
        Assert.True(changed.Find("temperatures")!.Emphasized);
        Assert.Same(CameraOverlayLayout.Default, CameraOverlayLayout.Default.WithEmphasized("status", true));
    }

    [Fact]
    public void WithVisibleChangesOnlyThatWidget()
    {
        CameraOverlayLayout changed = CameraOverlayLayout.Default.WithVisible("cooldown", false);

        Assert.False(changed.Find("cooldown")!.Visible);
        Assert.All(changed.Widgets.Where(static w => w.WidgetId != "cooldown"), static w => Assert.True(w.Visible));
        Assert.Same(changed, changed.WithVisible("cooldown", false));
        Assert.Equal(CameraOverlayLayout.Default, changed.WithVisible("COOLDOWN", true));
    }

    // ---- Reset ----

    [Fact]
    public void ResetGivesTheDefault()
    {
        CameraOverlayLayout changed = CameraOverlayLayout.Default
            .MoveTo("status", OverlayAnchor.BottomRight, 0).WithSize("times", OverlayWidgetSize.Small).WithVisible("cooldown", false);

        Assert.Same(CameraOverlayLayout.Default, changed.ResetToDefault());
    }

    [Fact]
    public void ResetKeepsWidgetsUnknownToThisVersion()
    {
        OverlayWidgetPlacement future = P(Future, OverlayAnchor.TopRight, OverlayWidgetSize.Large, emphasized: true, visible: false);
        CameraOverlayLayout changed = new([.. CameraOverlayLayout.Default.WithSize("times", OverlayWidgetSize.Small).Widgets, future]);

        CameraOverlayLayout reset = changed.ResetToDefault();

        Assert.Equal(new CameraOverlayLayout([.. CameraOverlayLayout.Default.Widgets, future]), reset);
    }

    // ---- Mirroring and the old corner ----

    [Fact]
    public void MirroredMovesEveryWidgetAndKeepsOrderAndSizes()
    {
        CameraOverlayLayout layout = CameraOverlayLayout.Default.WithSize("cooldown", OverlayWidgetSize.Large);

        CameraOverlayLayout mirrored = layout.Mirrored(horizontally: true, vertically: true);

        Assert.Equal(["times", "cooldown"], mirrored.InAnchor(OverlayAnchor.TopRight).Select(static w => w.WidgetId));
        Assert.Equal(OverlayWidgetSize.Large, mirrored.Find("cooldown")!.Size);
        Assert.Equal(layout, mirrored.Mirrored(horizontally: true, vertically: true));
        Assert.Same(layout, layout.Mirrored(horizontally: false, vertically: false));
    }

    /// <summary>The mirroring table of T-076 3.3 (anchors of status, print name, times + cool-down, temperatures).</summary>
    [Theory]
    [InlineData(OverlayAnchor.BottomLeft, OverlayAnchor.TopLeft, OverlayAnchor.TopCenter, OverlayAnchor.BottomLeft, OverlayAnchor.BottomRight)]
    [InlineData(OverlayAnchor.BottomRight, OverlayAnchor.TopRight, OverlayAnchor.TopCenter, OverlayAnchor.BottomRight, OverlayAnchor.BottomLeft)]
    [InlineData(OverlayAnchor.TopLeft, OverlayAnchor.BottomLeft, OverlayAnchor.BottomCenter, OverlayAnchor.TopLeft, OverlayAnchor.TopRight)]
    [InlineData(OverlayAnchor.TopRight, OverlayAnchor.BottomRight, OverlayAnchor.BottomCenter, OverlayAnchor.TopRight, OverlayAnchor.TopLeft)]
    public void FromLegacyCornerFollowsTheDesignTable(OverlayAnchor corner, OverlayAnchor status, OverlayAnchor printName, OverlayAnchor timesAndCooldown, OverlayAnchor temperatures)
    {
        CameraOverlayLayout layout = CameraOverlayLayout.FromLegacyCorner(corner);

        Assert.Equal(
            [
                P("status", status, emphasized: true),
                P("print-name", printName),
                P("times", timesAndCooldown),
                P("cooldown", timesAndCooldown),
                P("temperatures", temperatures),
            ],
            layout.Widgets.OrderBy(static w => Array.IndexOf(["status", "print-name", "times", "cooldown", "temperatures"], w.WidgetId)));
        Assert.Equal(["times", "cooldown"], layout.InAnchor(timesAndCooldown).Select(static w => w.WidgetId));
        Assert.Equal(corner, layout.Find("times")!.Anchor);
    }

    [Fact]
    public void FromLegacyDefaultCornerIsExactlyTheDefault() =>
        Assert.Same(CameraOverlayLayout.Default, CameraOverlayLayout.FromLegacyCorner(OverlayAnchor.BottomLeft));

    [Theory]
    [InlineData(OverlayAnchor.TopCenter)]
    [InlineData(OverlayAnchor.BottomCenter)]
    [InlineData((OverlayAnchor)6)]
    public void FromLegacyCornerRejectsCentersAndUnknownValues(OverlayAnchor corner) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => CameraOverlayLayout.FromLegacyCorner(corner));

    [Fact]
    public void FromLegacyCornerIsDeterministic() =>
        Assert.Equal(CameraOverlayLayout.FromLegacyCorner(OverlayAnchor.TopRight), CameraOverlayLayout.FromLegacyCorner(OverlayAnchor.TopRight));

    // ---- Part of the camera settings ----

    [Fact]
    public void CameraSettingsHaveTheDefaultLayout()
    {
        Assert.Same(CameraOverlayLayout.Default, CameraSettings.Disabled.Overlay);
        Assert.Same(CameraOverlayLayout.Default, new CameraSettings(enabled: true, new Uri("rtsp://192.168.0.227/live")).Overlay);
        Assert.Same(CameraOverlayLayout.Default, CameraSettings.FromInvalidStreamUrl("http://a/live").Overlay);
    }

    [Fact]
    public void CameraSettingsEqualityIncludesTheLayout()
    {
        CameraSettings camera = new(enabled: true, new Uri("rtsp://192.168.0.227/live"));
        CameraSettings changed = camera with { Overlay = CameraOverlayLayout.Default.WithVisible("times", false) };
        CameraSettings same = camera with { Overlay = CameraOverlayLayout.Default.WithVisible("times", false) };

        Assert.NotEqual(camera, changed);
        Assert.Equal(changed, same);
        Assert.Equal(changed.GetHashCode(), same.GetHashCode());
        Assert.Throws<ArgumentNullException>(() => camera with { Overlay = null! });
        Assert.Contains("print-name", changed.ToString(), StringComparison.Ordinal);
    }

    // ---- Use for all printers ----

    [Fact]
    public void ApplyToAllCopiesTheLayoutToEveryPrinterAlsoWithTheCameraOff()
    {
        CameraOverlayLayout layout = CameraOverlayLayout.Default.WithVisible("cooldown", false);
        PrinterDefinition on = Printer("a") with { Camera = new CameraSettings(enabled: true, new Uri("rtsp://10.0.0.2/live")) { Window = CameraWindowDefaults.Default with { TopmostNormal = true } } };
        PrinterDefinition off = Printer("b") with { CooldownThresholds = new(45, 55) };
        PrinterDefinition already = Printer("c") with { Camera = CameraSettings.Disabled with { Overlay = layout } };

        IReadOnlyList<PrinterDefinition> result = CameraOverlayLayout.ApplyToAll([on, off, already], layout);

        Assert.Equal(on with { Camera = on.Camera with { Overlay = layout } }, result[0]);
        Assert.Equal(off with { Camera = CameraSettings.Disabled with { Overlay = layout } }, result[1]);
        Assert.Same(already, result[2]);
        Assert.True(result[0].Camera.Window.TopmostNormal);
        Assert.Equal(new CooldownThresholds(45, 55), result[1].CooldownThresholds);
    }

    [Fact]
    public void ApplyToAllReturnsTheSameListWhenNothingChanges()
    {
        PrinterDefinition[] printers = [Printer("a"), Printer("b")];

        Assert.Same(printers, CameraOverlayLayout.ApplyToAll(printers, CameraOverlayLayout.Default));
        Assert.Throws<ArgumentException>(() => CameraOverlayLayout.ApplyToAll([Printer("a"), null!], CameraOverlayLayout.Default));
        Assert.Throws<ArgumentNullException>(() => CameraOverlayLayout.ApplyToAll(printers, null!));
    }

    private static PrinterDefinition Printer(string id) =>
        new() { Id = id, Name = id.ToUpperInvariant(), ConnectionType = ConnectionType.PrusaLink, Address = $"10.0.0.{id[0] - 'a' + 10}" };
}
