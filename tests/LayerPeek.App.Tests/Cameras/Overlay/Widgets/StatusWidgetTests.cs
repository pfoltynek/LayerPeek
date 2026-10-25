using Avalonia.Media;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

using LayerPeek.App.Cameras.Overlay;
using LayerPeek.App.Cameras.Overlay.Widgets;
using LayerPeek.App.Composition;
using LayerPeek.App.Tests.Tray;
using LayerPeek.Core.Cameras;
using LayerPeek.Core.Cameras.Overlay;
using LayerPeek.Core.Printers;

using static LayerPeek.App.Tests.Tray.TestPrinters;

namespace LayerPeek.App.Tests.Cameras.Overlay.Widgets;

/// <summary>
/// The Status widget (T-082, design T-076 2.1): name, state pill, percent and bar in every printer state, the T-036 rules
/// for the name, the state and the progress, cultures, a language switch and the cheap comparison (the per-snapshot path
/// without allocations: <see cref="StatusWidgetAllocationTests"/>).
/// </summary>
public sealed class StatusWidgetTests : IDisposable
{
    private readonly ManualDispatcher _dispatcher = new();
    private readonly FakeTimeProvider _time = new(Now);
    private readonly CultureScope _culture = new("en-US");
    private readonly List<CameraOverlayViewModel> _created = [];

    public void Dispose()
    {
        foreach (CameraOverlayViewModel overlay in _created)
        {
            overlay.Dispose();
        }

        _culture.Dispose();
    }

    /// <summary>The registered Status widget, alone in a layout (as the app registers it).</summary>
    internal static OverlayWidgetRegistry Registry() =>
        new([Assert.IsType<IOverlayWidgetDefinition>(OverlayWidgets.CreateRegistry().Find(OverlayWidgetIds.Status), exactMatch: false)]);

    internal static CameraOverlayLayout Layout(OverlayWidgetSize size = OverlayWidgetSize.Medium, bool emphasized = true) =>
        new([new OverlayWidgetPlacement(OverlayWidgetIds.Status, OverlayAnchor.TopLeft, size, emphasized)]);

    private CameraOverlayViewModel Create(PrinterSnapshot? snapshot = null, string name = "MK4", CameraOverlayLayout? layout = null)
    {
        CameraOverlayViewModel overlay = new(
            Registry(),
            new OverlayWidgetContext("mk4", _time),
            _dispatcher,
            NullLogger.Instance,
            Definition(name: name),
            layout ?? Layout(),
            CameraOverlayOpacity.DefaultPercent,
            CameraWindowMode.Normal,
            snapshot);
        _created.Add(overlay);
        return overlay;
    }

    private static StatusWidgetViewModel Widget(CameraOverlayViewModel overlay) => Assert.IsType<StatusWidgetViewModel>(Assert.Single(overlay.Widgets));

    private StatusWidgetViewModel Show(PrinterSnapshot snapshot, string name = "MK4")
    {
        CameraOverlayViewModel overlay = Create(name: name);
        overlay.OnSnapshot(snapshot);
        _dispatcher.RunAll();
        return Widget(overlay);
    }

    private static PrinterSnapshot Printing(PrinterState state = PrinterState.Printing, double progress = 42.7) =>
        Snapshot(state, Job(progress, TimeSpan.FromMinutes(65))) with { Telemetry = new Telemetry { Nozzle = new Temperature(214.6, 215) } };

    // ---- Registration ----

    [Fact]
    public void StatusIsRegisteredWithItsNamePriorityAndSizes()
    {
        IOverlayWidgetDefinition definition = Registry().Definitions[0];

        Assert.Equal(OverlayWidgetIds.Status, definition.Id);
        Assert.Equal(OverlayWidgetNames.Status, definition.NameKey);
        Assert.Equal(OverlayWidgets.StatusPriority, definition.Priority);
        Assert.Equal([OverlayWidgetSize.Small, OverlayWidgetSize.Medium, OverlayWidgetSize.Large], definition.SupportedSizes);
        // T-076 2.1: S only has a maximum, M is at least 180, L at least 220 wide.
        Assert.Equal(0, definition.MinWidth(OverlayWidgetSize.Small));
        Assert.Equal(180, definition.MinWidth(OverlayWidgetSize.Medium));
        Assert.Equal(220, definition.MinWidth(OverlayWidgetSize.Large));
        Assert.Same(StatusWidgetTemplate.Card, definition.CardTemplate);
        Assert.True(definition.CardTemplate.Match(new StatusWidgetViewModel()));
    }

    // ---- States ----

    [Fact]
    public void BeforeTheFirstPollTheCardShowsTheNameAndConnecting()
    {
        StatusWidgetViewModel widget = Widget(Create(snapshot: null));

        Assert.True(widget.IsShown);
        Assert.Equal("MK4", widget.PrinterName);
        Assert.Equal("Connecting…", widget.StateText);
        Assert.False(widget.HasProgress);
        Assert.Same(OverlayStateBrushes.NoData, widget.DotBrush);
        Assert.Equal("MK4: Connecting…", widget.AccessibleName);
    }

    [Theory]
    [InlineData(PrinterState.Printing, "Printing", true)]
    [InlineData(PrinterState.Paused, "Paused", true)]
    [InlineData(PrinterState.Attention, "Needs attention", true)]
    [InlineData(PrinterState.Idle, "Ready", false)]
    [InlineData(PrinterState.Busy, "Busy", false)]
    [InlineData(PrinterState.Finished, "Finished", false)]
    [InlineData(PrinterState.Stopped, "Stopped", false)]
    [InlineData(PrinterState.Error, "Error", false)]
    [InlineData(PrinterState.Offline, "Offline", false)]
    [InlineData(PrinterState.Unauthorized, "Invalid credentials", false)]
    [InlineData(PrinterState.Unknown, "Unknown state", false)]
    public void ProgressOnlyDuringARunningPausedOrWaitingJob(PrinterState state, string stateText, bool hasProgress)
    {
        // Every snapshot carries a job: only the T-036 rule decides whether its progress is shown.
        StatusWidgetViewModel widget = Show(Printing(state));

        // The card never hides: offline, rejected credentials and errors are what it is there for.
        Assert.True(widget.IsShown);
        Assert.Equal("MK4", widget.PrinterName);
        Assert.Equal(stateText, widget.StateText);
        Assert.Equal(hasProgress, widget.HasProgress);
        Assert.Equal(hasProgress ? "42%" : string.Empty, widget.PercentText);
        Assert.Equal(hasProgress ? 42 : 0, widget.Progress);
        Assert.Equal(hasProgress ? $"MK4: {stateText}, 42%" : $"MK4: {stateText}", widget.AccessibleName);
    }

    [Theory]
    [InlineData(PrinterState.Printing, "#1E88E5")]
    [InlineData(PrinterState.Busy, "#1E88E5")]
    [InlineData(PrinterState.Paused, "#FDD835")]
    [InlineData(PrinterState.Finished, "#43A047")]
    [InlineData(PrinterState.Error, "#E53935")]
    [InlineData(PrinterState.Attention, "#FB8C00")]
    [InlineData(PrinterState.Unauthorized, "#FB8C00")]
    [InlineData(PrinterState.Idle, "#8A8D96")]
    [InlineData(PrinterState.Stopped, "#8A8D96")]
    [InlineData(PrinterState.Offline, "#9E9E9E")]
    [InlineData(PrinterState.Unknown, "#9E9E9E")]
    [InlineData(null, "#9E9E9E")]
    public void DotHasTheTrayColourOfTheState(PrinterState? state, string colour)
    {
        // T-076 1.3: the colours of the tray icon (ColorTrayIconPalette).
        Assert.Equal(Color.Parse(colour), OverlayStateBrushes.For(state).Color);

        if (state is { } known)
        {
            Assert.Equal(Color.Parse(colour), Assert.IsAssignableFrom<ISolidColorBrush>(Show(Printing(known)).DotBrush).Color);
        }
    }

    [Fact]
    public void ActiveStateWithoutAJobShowsNoProgress()
    {
        StatusWidgetViewModel widget = Show(Snapshot(PrinterState.Printing));

        Assert.Equal("Printing", widget.StateText);
        Assert.False(widget.HasProgress);
        Assert.Equal("MK4: Printing", widget.AccessibleName);
    }

    [Theory]
    [InlineData(0.0, 0)]
    [InlineData(42.99, 42)]
    [InlineData(99.9, 99)]
    [InlineData(100.0, 100)]
    public void PercentIsRoundedDownSoThatAnUnfinishedPrintNeverShows100(double progress, int shown)
    {
        StatusWidgetViewModel widget = Show(Printing(progress: progress));

        Assert.Equal($"{shown}%", widget.PercentText);
        Assert.Equal(shown, widget.Progress);
        Assert.True(widget.HasProgress);
    }

    [Fact]
    public void JobEndingHidesThePercentAndTheBar()
    {
        CameraOverlayViewModel overlay = Create();
        overlay.OnSnapshot(Printing());
        _dispatcher.RunAll();
        StatusWidgetViewModel widget = Widget(overlay);
        Assert.True(widget.HasProgress);

        overlay.OnSnapshot(Snapshot(PrinterState.Finished, Job(100)));
        _dispatcher.RunAll();

        Assert.False(widget.HasProgress);
        Assert.Equal("Finished", widget.StateText);
        Assert.Same(OverlayStateBrushes.Finished, widget.DotBrush);
        Assert.Equal("MK4: Finished", widget.AccessibleName);
    }

    // ---- Printer name ----

    [Fact]
    public void LongPrinterNameIsShortenedTo40Characters()
    {
        StatusWidgetViewModel widget = Show(Printing(), name: new string('N', 100));

        Assert.Equal(OverlayFormat.MaxNameLength, widget.PrinterName.Length);
        Assert.Equal(new string('N', OverlayFormat.NameHeadLength) + "…" + new string('N', OverlayFormat.MaxNameLength - OverlayFormat.NameHeadLength - 1), widget.PrinterName);
        Assert.StartsWith(widget.PrinterName + ": ", widget.AccessibleName, StringComparison.Ordinal);
    }

    [Fact]
    public void RenamedPrinterIsShownAfterSaving()
    {
        CameraOverlayViewModel overlay = Create();
        overlay.OnSnapshot(Printing());
        _dispatcher.RunAll();

        overlay.ApplySettings(Definition(name: "Workshop"), Layout(), CameraOverlayOpacity.DefaultPercent);

        Assert.Equal("Workshop", Widget(overlay).PrinterName);
        Assert.Equal("Workshop: Printing, 42%", Widget(overlay).AccessibleName);
    }

    [Fact]
    public void SameNameSavedAgainChangesNothing()
    {
        // Saving brings a new string instance with the same text: the shortened name is reused, nothing is formatted.
        string longName = new('N', 60);
        CameraOverlayViewModel overlay = Create(name: longName);
        overlay.OnSnapshot(Printing());
        _dispatcher.RunAll();
        StatusWidgetViewModel widget = Widget(overlay);
        string shown = widget.PrinterName;
        int changes = 0;
        widget.PropertyChanged += (_, _) => changes++;

        overlay.ApplySettings(Definition(name: new string('N', 60)), Layout(), CameraOverlayOpacity.DefaultPercent);

        Assert.Same(shown, widget.PrinterName);
        Assert.Equal(0, changes);
    }

    // ---- Emphasis and sizes ----

    [Theory]
    [InlineData(OverlayWidgetSize.Small, false)]
    [InlineData(OverlayWidgetSize.Medium, true)]
    [InlineData(OverlayWidgetSize.Large, true)]
    public void SizeAndEmphasisComeFromTheLayoutAndChangeNoContent(OverlayWidgetSize size, bool emphasized)
    {
        CameraOverlayViewModel overlay = Create(layout: Layout(size, emphasized));
        overlay.OnSnapshot(Printing());
        _dispatcher.RunAll();
        StatusWidgetViewModel widget = Widget(overlay);
        Assert.Equal(size, widget.Size);
        Assert.Equal(emphasized, widget.IsEmphasized);
        string percent = widget.PercentText;

        overlay.ApplySettings(Definition(), Layout(OverlayWidgetSize.Large, !emphasized), CameraOverlayOpacity.DefaultPercent);

        // The same instance changes in place; the texts are the same in every size.
        Assert.Same(widget, Widget(overlay));
        Assert.Equal(OverlayWidgetSize.Large, widget.Size);
        Assert.Equal(!emphasized, widget.IsEmphasized);
        Assert.Same(percent, widget.PercentText);
    }

    // ---- Cultures and language ----

    [Theory]
    [InlineData("cs-CZ", "Tiskne", "42 %", "Připojování…")]
    [InlineData("de-DE", "Druckt", "42 %", "Verbindung wird hergestellt…")]
    [InlineData("fr-FR", "Impression en cours", "42 %", "Connexion…")]
    public void TextsFollowTheLanguage(string culture, string printing, string percent, string connecting)
    {
        using CultureScope scope = new(culture);

        Assert.Equal(connecting, Widget(Create()).StateText);
        StatusWidgetViewModel widget = Show(Printing());
        Assert.Equal(printing, widget.StateText);
        Assert.Equal(percent, widget.PercentText);
        Assert.Equal($"MK4: {printing}, {percent}", widget.AccessibleName);
    }

    [Fact]
    public void LanguageSwitchFormatsTheShownContentAgain()
    {
        CameraOverlayViewModel overlay = Create();
        overlay.OnSnapshot(Printing(PrinterState.Paused));
        _dispatcher.RunAll();
        StatusWidgetViewModel widget = Widget(overlay);
        Assert.Equal("Paused", widget.StateText);

        using (new UiCultureScope("pl"))
        {
            overlay.RefreshTexts();
            Assert.Equal("Wstrzymano", widget.StateText);
            Assert.Equal("42%", widget.PercentText);
            Assert.Equal("MK4: Wstrzymano, 42%", widget.AccessibleName);
        }

        overlay.RefreshTexts();
        Assert.Equal("Paused", widget.StateText);
        // Nothing was posted for the switch: it formats on the UI thread right away.
        Assert.Equal(0, _dispatcher.Pending);
    }

    // ---- Cheap comparison ----

    [Fact]
    public void PollThatChangesNothingVisiblePostsNothingAndFormatsNothing()
    {
        CameraOverlayViewModel overlay = Create();
        overlay.OnSnapshot(Printing(progress: 42.1));
        _dispatcher.RunAll();
        StatusWidgetViewModel widget = Widget(overlay);
        int posts = _dispatcher.PostCount;
        int changes = 0;
        widget.PropertyChanged += (_, _) => changes++;

        // Below the displayed precision, values the widget does not show, a later time.
        overlay.OnSnapshot(Printing(progress: 42.9));
        overlay.OnSnapshot(Printing(progress: 42.5) with
        {
            Timestamp = Now.AddSeconds(5),
            Telemetry = new Telemetry { Nozzle = new Temperature(190, 215), Bed = new Temperature(55, 60) },
            Job = Printing().Job! with { Remaining = TimeSpan.FromMinutes(12), FileName = "other.bgcode" },
        });

        Assert.Equal(posts, _dispatcher.PostCount);
        Assert.Equal(0, changes);
    }

    [Fact]
    public void VisibleChangePostsOnceAndFormatsTheNewContent()
    {
        CameraOverlayViewModel overlay = Create();
        overlay.OnSnapshot(Printing(progress: 42.1));
        _dispatcher.RunAll();
        int posts = _dispatcher.PostCount;

        overlay.OnSnapshot(Printing(progress: 43.0));
        overlay.OnSnapshot(Printing(progress: 44.0));

        Assert.Equal(posts + 1, _dispatcher.PostCount);
        _dispatcher.RunAll();
        Assert.Equal("44%", Widget(overlay).PercentText);
    }
}
