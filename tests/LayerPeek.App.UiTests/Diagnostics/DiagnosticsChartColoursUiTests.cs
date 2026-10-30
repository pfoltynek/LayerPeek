using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;

using LayerPeek.App.Diagnostics.Chart;
using LayerPeek.App.UiTests.Infrastructure;

namespace LayerPeek.App.UiTests.Diagnostics;

/// <summary>
/// The colours of the nozzle and the bed in the diagnostics chart (T-084, design T-076 1.3): shared resources with the
/// camera overlay; the dark theme has exactly the overlay colours, the light theme darker shades of the same hue for
/// contrast on white. The layout of the window does not change (<see cref="DiagnosticsWindowLayoutTests"/>).
/// </summary>
public sealed class DiagnosticsChartColoursUiTests(HeadlessSession session)
{
    [Theory]
    [InlineData("Light", "#D9480F", "#1864AB")]
    [InlineData("Dark", "#FF8A3D", "#6CB4FF")]
    public Task ChartAndLegendUseTheSharedColoursOfTheTheme(string theme, string nozzle, string bed) =>
        session.RunAsync(scope =>
        {
            DiagnosticsWindowHarness ui = DiagnosticsWindowLayoutTests.OpenPrinting(scope);
            ui.Window.RequestedThemeVariant = theme == "Dark" ? ThemeVariant.Dark : ThemeVariant.Light;
            ui.Window.Settle();
            TemperatureChart chart = ui.Find<TemperatureChart>("DiagnosticsChart");

            Assert.Equal(Color.Parse(nozzle), ColorOf(chart.NozzleBrush));
            Assert.Equal(Color.Parse(bed), ColorOf(chart.BedBrush));
            Assert.False(chart.IsCompact);
            Assert.True(chart.HasAxes);
            // The legend draws the same colours.
            Rectangle[] legend = [.. ui.Window.Find<Grid>("DiagnosticsChartHeader").GetVisualDescendants().OfType<Rectangle>()];
            Assert.Equal([Color.Parse(nozzle), Color.Parse(bed)], legend.Select(static swatch => ColorOf(swatch.Fill)));
            // The lines of the chart: actual 1.75, targets dashed 1 without extra transparency (unchanged look).
            Assert.Equal(Color.Parse(nozzle), ColorOf(chart.SeriesPenOf(TemperatureSeries.NozzleActual)!.Brush));
            Assert.Equal(1.75, chart.SeriesPenOf(TemperatureSeries.NozzleActual)!.Thickness);
            Assert.Equal(1, ((ISolidColorBrush)chart.SeriesPenOf(TemperatureSeries.BedTarget)!.Brush!).Opacity);
            return Task.CompletedTask;
        });

    [Fact]
    public Task ThemeSwitchRecolorsTheOpenChart() =>
        session.RunAsync(scope =>
        {
            DiagnosticsWindowHarness ui = DiagnosticsWindowLayoutTests.OpenPrinting(scope);
            ui.Window.RequestedThemeVariant = ThemeVariant.Light;
            ui.Window.Settle();
            TemperatureChart chart = ui.Find<TemperatureChart>("DiagnosticsChart");
            Assert.Equal(Color.Parse("#D9480F"), ColorOf(chart.SeriesPenOf(TemperatureSeries.NozzleActual)!.Brush));

            ui.Window.RequestedThemeVariant = ThemeVariant.Dark;
            ui.Window.Settle();

            Assert.Equal(Color.Parse("#FF8A3D"), ColorOf(chart.SeriesPenOf(TemperatureSeries.NozzleActual)!.Brush));
            Assert.Equal(Color.Parse("#6CB4FF"), ColorOf(chart.SeriesPenOf(TemperatureSeries.BedActual)!.Brush));
            return Task.CompletedTask;
        });

    [Fact]
    public Task FullChartKeepsOneAxisFromZeroForAllSeries() =>
        session.RunAsync(scope =>
        {
            // T-106 gives only the compact chart its bands: the diagnostics chart keeps 0 °C at the bottom of the plot and
            // the same axis for the nozzle, the bed and their targets.
            DiagnosticsWindowHarness ui = DiagnosticsWindowLayoutTests.OpenPrinting(scope);
            TemperatureChart chart = ui.Find<TemperatureChart>("DiagnosticsChart");
            ValueBand axis = chart.BandOf(TemperatureSeries.NozzleActual)!.Value;

            Assert.Equal(0, axis.Low);
            Assert.True(axis.High >= ChartScale.MinimumTop);
            Assert.True(axis.Height > 150, $"The axis is {axis.Height:0.#} px high.");
            foreach (TemperatureSeries series in Enum.GetValues<TemperatureSeries>())
            {
                Assert.Equal(axis, chart.BandOf(series));
                Assert.Equal(chart.ClipOf(TemperatureSeries.NozzleActual), chart.ClipOf(series));
            }

            Assert.Equal(axis.Bottom, chart.ClipOf(TemperatureSeries.BedActual).Bottom, 6);
            Assert.Equal(axis.Top, chart.ClipOf(TemperatureSeries.BedActual).Top, 6);
            return Task.CompletedTask;
        });

    private static Color ColorOf(IBrush? brush) => Assert.IsType<ISolidColorBrush>(brush, exactMatch: false).Color;
}
