using Avalonia.Controls;
using Avalonia.Threading;

using Microsoft.Extensions.Time.Testing;

using LayerPeek.App.Diagnostics.Chart;
using LayerPeek.App.UiTests.Infrastructure;
using LayerPeek.Core.History;
using LayerPeek.Core.Printers;

using static LayerPeek.App.Tests.Tray.TestPrinters;

namespace LayerPeek.App.UiTests.Diagnostics;

/// <summary>
/// <see cref="TemperatureChart"/> listens to <see cref="TelemetryHistory.Changed"/> only while it is attached and shown
/// with all its ancestors (T-084): the watched ancestors follow a move to another parent (no stale handlers), a hidden
/// ancestor that is shown again resubscribes and redraws with what was recorded meanwhile.
/// </summary>
public sealed class TemperatureChartVisibilityUiTests(HeadlessSession session)
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(20);

    private static TelemetryHistory History()
    {
        TelemetryHistory history = new();
        history.Configure([Definition()], Interval);
        history.Record(Sample(Now));
        return history;
    }

    private static PrinterSnapshot Sample(DateTimeOffset time) => new()
    {
        PrinterId = "mk4",
        State = PrinterState.Printing,
        Timestamp = time,
        Telemetry = new Telemetry { Nozzle = new Temperature(215, 215), Bed = new Temperature(60, 60) },
    };

    private static TemperatureChart Chart(TelemetryHistory history, FakeTimeProvider time) => new()
    {
        IsCompact = true,
        Width = 200,
        Height = 60,
        History = history,
        PrinterId = "mk4",
        Clock = time,
    };

    [Fact]
    public Task MovedChartFollowsOnlyItsNewAncestors() =>
        session.RunAsync(scope =>
        {
            FakeTimeProvider time = new(Now);
            TelemetryHistory history = History();
            TemperatureChart chart = Chart(history, time);
            Border first = new() { Child = chart };
            Border second = new();
            Window window = scope.Track(new Window { Width = 400, Height = 300, Content = new StackPanel { Children = { first, second } } });
            window.Show();
            window.Settle();
            Assert.True(chart.IsSubscribedToHistory);

            first.Child = null;
            second.Child = chart;
            window.Settle();
            Assert.True(chart.IsSubscribedToHistory);

            // The former parent no longer matters ...
            first.IsVisible = false;
            Assert.True(chart.IsSubscribedToHistory);
            first.IsVisible = true;

            // ... the new one does.
            second.IsVisible = false;
            Assert.False(chart.IsSubscribedToHistory);
            second.IsVisible = true;
            Assert.True(chart.IsSubscribedToHistory);

            // Detached: no ancestor brings the subscription back.
            second.Child = null;
            Assert.False(chart.IsSubscribedToHistory);
            second.IsVisible = false;
            second.IsVisible = true;
            first.IsVisible = false;
            first.IsVisible = true;
            Assert.False(chart.IsSubscribedToHistory);
            return Task.CompletedTask;
        });

    [Fact]
    public Task ChartShownAgainRedrawsWithWhatWasRecordedMeanwhile() =>
        session.RunAsync(scope =>
        {
            FakeTimeProvider time = new(Now);
            TelemetryHistory history = History();
            TemperatureChart chart = Chart(history, time);
            Border inner = new() { Child = chart };
            Border outer = new() { Child = new Decorator { Child = inner } };
            Window window = scope.Track(new Window { Width = 400, Height = 300, Content = outer });
            window.Show();
            window.Settle();
            int rebuilds = chart.SeriesRebuilds;

            // A hidden grand-ancestor: no listening, nothing redrawn for new samples.
            outer.IsVisible = false;
            Assert.False(chart.IsSubscribedToHistory);
            time.Advance(Interval);
            history.Record(Sample(time.GetUtcNow()));
            Dispatcher.UIThread.RunJobs();
            window.Settle();
            Assert.Equal(rebuilds, chart.SeriesRebuilds);

            // Shown again: listening, and the chart catches up with the sample recorded while hidden.
            outer.IsVisible = true;
            Assert.True(chart.IsSubscribedToHistory);
            window.Settle();
            Assert.Equal(history.GetVersion("mk4"), chart.Data.Version);
            Assert.Equal(time.GetUtcNow(), chart.Data.To);
            Assert.Equal(2, chart.Data.SampleCount);
            return Task.CompletedTask;
        });

    [Fact]
    public Task HiddenWindowStopsListeningAndAMinimizedOneKeepsListening() =>
        session.RunAsync(scope =>
        {
            FakeTimeProvider time = new(Now);
            TemperatureChart chart = Chart(History(), time);
            Window window = scope.Track(new Window { Width = 400, Height = 300, Content = chart });
            window.Show();
            window.Settle();
            Assert.True(chart.IsSubscribedToHistory);

            window.Hide();
            Assert.False(chart.IsSubscribedToHistory);

            window.Show();
            Assert.True(chart.IsSubscribedToHistory);

            // A minimized window counts as shown (documented behaviour, as before in the diagnostics window).
            window.WindowState = WindowState.Minimized;
            window.Settle();
            Assert.True(chart.IsSubscribedToHistory);
            return Task.CompletedTask;
        });
}
