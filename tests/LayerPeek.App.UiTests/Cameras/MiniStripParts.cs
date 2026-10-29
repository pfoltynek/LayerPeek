using Avalonia.Controls;
using Avalonia.VisualTree;

using LayerPeek.App.Cameras.Mini;
using LayerPeek.App.UiTests.Infrastructure;
using LayerPeek.App.Windows;

namespace LayerPeek.App.UiTests.Cameras;

/// <summary>The parts of the mini preview strip's summary row the tests look at (T-089, T-103).</summary>
internal static class MiniStripParts
{
    public static CameraMiniStrip Strip(CameraWindow window) => window.Find<CameraMiniStrip>("CameraMiniInfoStrip");

    /// <summary>The panel that draws the first variant of the summary row that fits whole (T-103).</summary>
    public static CameraMiniSummaryPanel SummaryPanel(CameraWindow window) =>
        Strip(window).GetVisualDescendants().OfType<CameraMiniSummaryPanel>().Single();

    /// <summary>The drawn variant of the summary row.</summary>
    public static Control ShownVariant(CameraWindow window) =>
        SummaryPanel(window).Shown ?? throw new InvalidOperationException("No variant of the summary row is drawn.");

    /// <summary>The summary text of the drawn variant.</summary>
    public static TextBlock SummaryText(CameraWindow window) => SummaryOf(ShownVariant(window));

    /// <summary>The finish time of the drawn variant; <see langword="null"/> when it has no flag group.</summary>
    public static TextBlock? FinishText(CameraWindow window) => FinishOf(ShownVariant(window));

    public static TextBlock SummaryOf(Control variant) =>
        variant.GetVisualDescendants().OfType<TextBlock>().Single(static text => text.Classes.Contains("miniSummary"));

    public static TextBlock? FinishOf(Control variant) =>
        variant.GetVisualDescendants().OfType<TextBlock>().SingleOrDefault(static text => text.Classes.Contains("miniFinishText") && text.IsEffectivelyVisible);

    /// <summary>The visible variants of the summary row, longest first.</summary>
    public static IReadOnlyList<Control> Variants(CameraWindow window) =>
        [.. SummaryPanel(window).Children.Where(static child => child.IsVisible)];

    /// <summary>A text of the summary row that is drawn (not one of the variants placed beyond the panel's clipped edge).</summary>
    public static bool IsDrawnInSummary(TextBlock text) =>
        text.FindAncestorOfType<CameraMiniSummaryPanel>() is not { } panel
        || (panel.Shown is { } shown && text.GetVisualAncestors().Contains(shown));
}
