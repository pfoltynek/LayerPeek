using System.ComponentModel;
using System.Globalization;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

using LayerPeek.App.Cameras.Overlay;
using LayerPeek.App.Cameras.Overlay.Widgets;
using LayerPeek.App.Composition;
using LayerPeek.App.Resources;
using LayerPeek.App.Tests.Tray;
using LayerPeek.Core.Cameras;
using LayerPeek.Core.Cameras.Overlay;
using LayerPeek.Core.Printers;
using LayerPeek.Core.Settings;

using static LayerPeek.App.Tests.Tray.TestPrinters;

namespace LayerPeek.App.Tests.Cameras.Overlay.Widgets;

/// <summary>
/// The Print name widget (T-085, design T-076 2.4) driven by the real overlay: the readable title and the chips of the
/// slicer template (T-078 parser) during a job and after a finished or stopped print until the next one, chips formatted
/// in the region, words in the app language, the name parsed only when it changes and texts formatted only when the
/// shown name changes. The per-snapshot path without allocations: <see cref="PrintNameWidgetAllocationTests"/>.
/// </summary>
public sealed class PrintNameWidgetTests : IDisposable
{
    internal const string Foldable = "foldable_book_holder_v1_0.4n_0.2mm_PLA_COREONE_2h51m.bgcode";

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

    /// <summary>A snapshot of <paramref name="state"/> with a job of <paramref name="fileName"/>.</summary>
    internal static PrinterSnapshot Job(
        string? fileName = Foldable,
        PrinterState state = PrinterState.Printing,
        double progress = 42.7,
        long id = 1) => new()
        {
            PrinterId = "mk4",
            State = state,
            Timestamp = Now,
            Job = new PrintJob
            {
                Id = id,
                FileName = fileName,
                ProgressPercent = progress,
                Remaining = TimeSpan.FromMinutes(65),
                Elapsed = TimeSpan.FromMinutes(130),
            },
        };

    private CameraOverlayViewModel Create(OverlayWidgetSize size = OverlayWidgetSize.Medium, bool emphasized = true)
    {
        CameraOverlayViewModel overlay = new(
            OverlayWidgets.CreateRegistry(),
            new OverlayWidgetContext("mk4", _time),
            _dispatcher,
            NullLogger.Instance,
            Definition(),
            new CameraOverlayLayout([new OverlayWidgetPlacement(OverlayWidgetIds.PrintName, OverlayAnchor.TopCenter, size, emphasized)]),
            CameraOverlayOpacity.DefaultPercent,
            CameraWindowMode.Normal,
            snapshot: null);
        _created.Add(overlay);
        return overlay;
    }

    private static PrintNameWidgetViewModel Widget(CameraOverlayViewModel overlay) => (PrintNameWidgetViewModel)Assert.Single(overlay.Widgets);

    private PrintNameWidgetViewModel Show(CameraOverlayViewModel overlay, PrinterSnapshot snapshot)
    {
        overlay.OnSnapshot(snapshot);
        _dispatcher.RunAll();
        return Widget(overlay);
    }

    private PrintNameWidgetViewModel Show(PrinterSnapshot snapshot, OverlayWidgetSize size = OverlayWidgetSize.Medium)
    {
        PrintNameWidgetViewModel widget = Show(Create(size), snapshot);
        widget.RenderedSize = size;
        return widget;
    }

    // ---- Registration ----

    [Fact]
    public void PrintNameWidgetIsRegisteredWithItsNamePriorityAndSizes()
    {
        IOverlayWidgetDefinition definition = Assert.IsType<IOverlayWidgetDefinition>(OverlayWidgets.CreateRegistry().Find(OverlayWidgetIds.PrintName), exactMatch: false);

        Assert.Equal(OverlayWidgetNames.PrintName, definition.NameKey);
        Assert.Equal(OverlayWidgets.PrintNamePriority, definition.Priority);
        Assert.Equal([OverlayWidgetSize.Small, OverlayWidgetSize.Medium, OverlayWidgetSize.Large], definition.SupportedSizes);
        // T-076 2.4: S max 280 (no minimum), M min 240, L min 300.
        Assert.Equal(0, definition.MinWidth(OverlayWidgetSize.Small));
        Assert.Equal(240, definition.MinWidth(OverlayWidgetSize.Medium));
        Assert.Equal(300, definition.MinWidth(OverlayWidgetSize.Large));
        Assert.Same(PrintNameWidgetView.CardTemplate, definition.CardTemplate);
        using OverlayWidgetViewModel widget = definition.Create(new OverlayWidgetContext("mk4", _time));
        Assert.IsType<PrintNameWidgetViewModel>(widget);
    }

    // ---- What is shown and when ----

    [Fact]
    public void RunningJobShowsTheTitleAndTheChips()
    {
        PrintNameWidgetViewModel widget = Show(Job());

        Assert.True(widget.IsShown);
        Assert.Equal("Foldable book holder v1", widget.Title);
        Assert.Equal("PLA", widget.MaterialChip);
        Assert.Equal("0.2 mm", widget.LayerHeightChip);
        Assert.Equal("nozzle 0.4", widget.NozzleChip);
        Assert.Equal("slicer estimate 2 h 51 min", widget.Estimate);
        Assert.True(widget.HasMaterial);
        Assert.True(widget.HasLayerHeight);
        Assert.True(widget.HasNozzle);
        Assert.True(widget.HasChips);
        Assert.True(widget.HasEstimate);
        Assert.Equal("Foldable book holder v1, PLA, layer height 0.2 mm, nozzle 0.4 mm, slicer estimate 2 h 51 min", widget.AccessibleName);
    }

    [Theory]
    [InlineData(PrinterState.Printing)]
    [InlineData(PrinterState.Paused)]
    [InlineData(PrinterState.Attention)]
    [InlineData(PrinterState.Finished)]
    [InlineData(PrinterState.Stopped)]
    public void NameIsShownDuringAJobAndAfterItFinishedOrStopped(PrinterState state)
    {
        PrintNameWidgetViewModel widget = Show(Job(state: state));

        Assert.True(widget.IsShown);
        Assert.Equal("Foldable book holder v1", widget.Title);
    }

    [Theory]
    [InlineData(PrinterState.Idle)]
    [InlineData(PrinterState.Busy)]
    [InlineData(PrinterState.Error)]
    [InlineData(PrinterState.Offline)]
    [InlineData(PrinterState.Unauthorized)]
    [InlineData(PrinterState.Unknown)]
    public void CardHidesWithoutAJobOrWhileThePrinterIsUnreachable(PrinterState state)
    {
        CameraOverlayViewModel overlay = Create();
        Assert.True(Show(overlay, Job()).IsShown);

        // Even when the snapshot still carries a job: outside a job, a finished or a stopped print nothing is shown.
        PrintNameWidgetViewModel widget = Show(overlay, Job(state: state));

        Assert.False(widget.HasContent);
        Assert.False(widget.IsShown);
    }

    [Fact]
    public void CardIsHiddenBeforeTheFirstPollWithoutAJobAndWithoutAFileName()
    {
        CameraOverlayViewModel overlay = Create();
        Assert.False(Widget(overlay).IsShown);

        Assert.False(Show(overlay, Job() with { Job = null }).IsShown);
        Assert.False(Show(overlay, Job(fileName: null)).IsShown);
        Assert.False(Show(overlay, Job(fileName: "   ")).IsShown);
    }

    [Fact]
    public void NameStaysAfterTheJobFinishesUntilTheNextPrint()
    {
        CameraOverlayViewModel overlay = Create();
        Show(overlay, Job());

        PrintNameWidgetViewModel widget = Show(overlay, Job(state: PrinterState.Finished, progress: 100));
        Assert.True(widget.IsShown);
        Assert.Equal("Foldable book holder v1", widget.Title);

        widget = Show(overlay, Job(fileName: "shape-box_0.4n_0.15mm_PETG_MK4S_45m.gcode", id: 2));
        Assert.True(widget.IsShown);
        Assert.Equal("Shape box", widget.Title);
        Assert.Equal("PETG", widget.MaterialChip);
        Assert.Equal("0.15 mm", widget.LayerHeightChip);
    }

    [Fact]
    public void NameStaysAfterTheJobIsStoppedUntilTheNextPrint()
    {
        CameraOverlayViewModel overlay = Create();
        Show(overlay, Job());

        Assert.True(Show(overlay, Job(state: PrinterState.Stopped)).IsShown);

        PrintNameWidgetViewModel widget = Show(overlay, Job(fileName: "benchy.gcode", id: 2));
        Assert.Equal("Benchy", widget.Title);
        Assert.False(widget.HasChips);
    }

    [Fact]
    public void ReprintOfTheSameFileKeepsTheShownTexts()
    {
        CameraOverlayViewModel overlay = Create();
        PrintNameWidgetViewModel widget = Show(overlay, Job(state: PrinterState.Finished));
        List<string?> changed = [];
        widget.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        // A new job (T-087: its G-code preview is asked for again), but the same name: nothing parsed, no text changes.
        Show(overlay, Job(id: 2));

        Assert.Equal(1, widget.ParseCount);
        Assert.Empty(changed);
        Assert.True(widget.IsShown);
    }

    // ---- Parser results ----

    [Fact]
    public void UnknownShapeShowsTheTitleWithoutChips()
    {
        PrintNameWidgetViewModel widget = Show(Job(fileName: "my_print-final.gcode"));

        Assert.True(widget.IsShown);
        Assert.Equal("My print final", widget.Title);
        Assert.False(widget.HasChips);
        Assert.False(widget.HasMaterial);
        Assert.False(widget.HasLayerHeight);
        Assert.False(widget.HasNozzle);
        Assert.False(widget.HasEstimate);
        Assert.False(widget.ShowsChips);
        Assert.Equal(string.Empty, widget.MaterialChip);
        Assert.Equal("My print final", widget.AccessibleName);
    }

    [Fact]
    public void ShortDosNameIsShownAsItIs()
    {
        // 8.3 names (USB stick listings) have no template.
        PrintNameWidgetViewModel widget = Show(Job(fileName: "BENCHY~1.BGC"));

        Assert.Equal("BENCHY~1", widget.Title);
        Assert.False(widget.HasChips);
    }

    [Theory]
    [InlineData(".bgcode", ".bgcode")]
    [InlineData("/usb/___.gcode", "___.gcode")]
    [InlineData("C:\\prints\\-.g", "-.g")]
    public void NameWithoutATitleShowsTheFileName(string fileName, string shown)
    {
        // The parser leaves no title (only an extension or separators): the file name as the printer lists it.
        PrintNameWidgetViewModel widget = Show(Job(fileName: fileName));

        Assert.True(widget.IsShown);
        Assert.Equal(shown, widget.Title);
        Assert.Equal(shown, widget.AccessibleName);
    }

    [Fact]
    public void CopySuffixIsLeftOut()
    {
        PrintNameWidgetViewModel widget = Show(Job(fileName: "foldable_book_holder_v1_0.4n_0.2mm_PLA_COREONE_2h51m (1).bgcode"));

        Assert.Equal("Foldable book holder v1", widget.Title);
        Assert.True(widget.HasChips);
    }

    [Fact]
    public void TemplateWithoutNozzleOrEstimateShowsTheKnownChipsOnly()
    {
        PrintNameWidgetViewModel widget = Show(Job(fileName: "vase_0.2mm_PLA_XL+.bgcode"), OverlayWidgetSize.Large);

        Assert.True(widget.HasMaterial);
        Assert.True(widget.HasLayerHeight);
        Assert.False(widget.HasNozzle);
        Assert.False(widget.HasEstimate);
        Assert.False(widget.ShowsEstimate);
        Assert.Equal("Vase, PLA, layer height 0.2 mm", widget.AccessibleName);
    }

    [Fact]
    public void SeveralMaterialsShareOneChip()
    {
        PrintNameWidgetViewModel widget = Show(Job(fileName: "xl_tool_test_0.4n_0.2mm_PLA,PETG,PVA_XL5IS_1d2h3m.bgcode"));

        Assert.Equal("PLA, PETG, PVA", widget.MaterialChip);
        Assert.Equal("slicer estimate " + OverlayFormat.Duration((26 * 60) + 3), widget.Estimate);
    }

    [Fact]
    public void LongMaterialListIsShortenedInTheChipOnly()
    {
        // T-076 2.4: at most 16 characters + "…"; the screen reader gets all materials.
        PrintNameWidgetViewModel widget = Show(Job(fileName: "x_0.4n_0.2mm_PLA,PETG,PVA,ASA,TPU_XL5IS_1h0m.bgcode"));

        Assert.Equal("PLA, PETG, PVA…", widget.MaterialChip);
        Assert.StartsWith("X, PLA, PETG, PVA, ASA, TPU, layer height", widget.AccessibleName, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("PLA", "PLA")]
    [InlineData("PLA, PETG, PVA, A", "PLA, PETG, PVA…")]
    [InlineData("PLA-CF, PETG-CF, ASA", "PLA-CF, PETG-CF…")]
    [InlineData("ABCDEFGHIJKLMNOP", "ABCDEFGHIJKLMNOP")]
    [InlineData("ABCDEFGHIJKLMNOPQ", "ABCDEFGHIJKLMNOP…")]
    public void MaterialChipKeepsAtMostSixteenCharacters(string materials, string chip) =>
        // T-076 2.4: "max 16 znaků + …"; a cut never ends with the separator.
        Assert.Equal(chip, PrintNameFormat.MaterialChip(materials.Split(", ")));

    [Fact]
    public void ShortSlicerEstimateIsRoundedUpToAMinute()
    {
        PrintNameWidgetViewModel widget = Show(Job(fileName: "tiny_0.4n_0.1mm_PLA_MK4S_42s.bgcode"));

        Assert.Equal("slicer estimate 1 min", widget.Estimate);
        Assert.Equal("0.1 mm", widget.LayerHeightChip);
    }

    [Fact]
    public void LongTitleIsKeptWholeForTheViewAndTheScreenReader()
    {
        // The view shortens the title by its width (in the middle); the view model keeps it whole.
        string title = string.Concat(Enumerable.Repeat("very_long_name_", 20)) + "v2";
        PrintNameWidgetViewModel widget = Show(Job(fileName: title + "_0.4n_0.2mm_PLA_MK4S_1h0m.bgcode"));

        Assert.Equal(title.Replace('_', ' ').TrimEnd(), widget.Title.ToLowerInvariant());
        Assert.StartsWith(widget.Title + ", PLA", widget.AccessibleName, StringComparison.Ordinal);
    }

    // ---- Chips by region and language ----

    [Theory]
    [InlineData("en-US", "0.2 mm", "nozzle 0.4", "slicer estimate 2 h 51 min", "Foldable book holder v1, PLA, layer height 0.2 mm, nozzle 0.4 mm, slicer estimate 2 h 51 min")]
    [InlineData("cs-CZ", "0,2 mm", "tryska 0,4", "odhad sliceru 2 h 51 min", "Foldable book holder v1, PLA, výška vrstvy 0,2 mm, tryska 0,4 mm, odhad sliceru 2 h 51 min")]
    [InlineData("de-DE", "0,2 mm", "Düse 0,4", "Slicer-Schätzung 2 h 51 min", "Foldable book holder v1, PLA, Schichthöhe 0,2 mm, Düse 0,4 mm, Slicer-Schätzung 2 h 51 min")]
    public void ChipsFollowTheRegionAndTheLanguage(string culture, string layer, string nozzle, string estimate, string accessibleName)
    {
        using CultureScope scope = new(culture);

        PrintNameWidgetViewModel widget = Show(Job());

        Assert.Equal("PLA", widget.MaterialChip);
        Assert.Equal(layer, widget.LayerHeightChip);
        Assert.Equal(nozzle, widget.NozzleChip);
        Assert.Equal(estimate, widget.Estimate);
        Assert.Equal(accessibleName, widget.AccessibleName);
    }

    [Theory]
    [InlineData("en-US", 0.15, "0.15 mm")]
    [InlineData("cs-CZ", 0.15, "0,15 mm")]
    [InlineData("cs-CZ", 0.075, "0,075 mm")]
    [InlineData("de-DE", 1.0, "1 mm")]
    [InlineData("en-US", 0.25, "0.25 mm")]
    public void MillimetresHaveNoTrailingZeros(string culture, double millimetres, string text)
    {
        using CultureScope scope = new(culture);

        Assert.Equal(text, PrintNameFormat.Millimetres(millimetres));
    }

    [Fact]
    public void LanguageSwitchFormatsTheShownNameAgainWithoutAPoll()
    {
        CameraOverlayViewModel overlay = Create();
        PrintNameWidgetViewModel widget = Show(overlay, Job());
        int posts = _dispatcher.PostCount;

        using (new CultureScope("cs-CZ"))
        {
            overlay.RefreshTexts();

            Assert.Equal("tryska 0,4", widget.NozzleChip);
            Assert.Equal("0,2 mm", widget.LayerHeightChip);
            Assert.Equal("odhad sliceru 2 h 51 min", widget.Estimate);
            Assert.Equal("Foldable book holder v1, PLA, výška vrstvy 0,2 mm, tryska 0,4 mm, odhad sliceru 2 h 51 min", widget.AccessibleName);
        }

        Assert.Equal(posts, _dispatcher.PostCount);
        Assert.Equal(1, widget.ParseCount);
    }

    [Fact]
    public void EveryLanguageHasTheTextsOfTheWidget()
    {
        // T-076 10: the chip at most 14 characters and the estimate at most 32 with their values.
        (string Key, int MaxLength, string Value)[] texts =
        [
            (nameof(Strings.CameraOverlayNozzleChip), 14, "0,4"),
            (nameof(Strings.CameraOverlaySlicerEstimate), 32, "2 h 51 min"),
            (nameof(Strings.CameraOverlayLayerHeightName), int.MaxValue, "0,2"),
            (nameof(Strings.CameraOverlayNozzleDiameterName), int.MaxValue, "0,4"),
        ];
        foreach (string language in AppLanguages.Supported)
        {
            foreach ((string key, int maxLength, string value) in texts)
            {
                string? text = Strings.ResourceManager.GetString(key, CultureInfo.GetCultureInfo(language));
                Assert.False(string.IsNullOrWhiteSpace(text), $"{language}: {key}");
                string formatted = string.Format(CultureInfo.InvariantCulture, text, value);
                Assert.Contains(value, formatted, StringComparison.Ordinal);
                Assert.True(formatted.Length <= maxLength, $"{language}: {formatted}");
            }
        }
    }

    // ---- Cheap comparison ----

    [Fact]
    public void NameIsParsedOnlyWhenTheFileNameChanges()
    {
        CameraOverlayViewModel overlay = Create();
        PrintNameWidgetViewModel widget = Show(overlay, Job());
        Assert.Equal(1, widget.ParseCount);

        // New string instances of the same name, other progress and other states of the same job.
        overlay.OnSnapshot(Job(fileName: new string(Foldable.AsSpan()), progress: 50));
        overlay.OnSnapshot(Job(fileName: new string(Foldable.AsSpan()), state: PrinterState.Paused));
        overlay.OnSnapshot(Job(fileName: new string(Foldable.AsSpan()), state: PrinterState.Finished));
        _dispatcher.RunAll();
        Assert.Equal(1, widget.ParseCount);

        Show(overlay, Job(fileName: "benchy.gcode", id: 2));
        Assert.Equal(2, widget.ParseCount);

        // No job: nothing to parse; the name of the last one is not parsed again when it comes back.
        Show(overlay, Job(state: PrinterState.Idle) with { Job = null });
        Show(overlay, Job(fileName: "benchy.gcode", id: 2));
        Assert.Equal(2, widget.ParseCount);
    }

    [Fact]
    public void PollWithTheSameNamePostsAndFormatsNothing()
    {
        CameraOverlayViewModel overlay = Create();
        PrintNameWidgetViewModel widget = Show(overlay, Job());
        int posts = _dispatcher.PostCount;
        int changes = 0;
        widget.PropertyChanged += (_, _) => changes++;

        overlay.OnSnapshot(Job(fileName: new string(Foldable.AsSpan()), progress: 43.1));
        overlay.OnSnapshot(Job(state: PrinterState.Finished, progress: 100));

        Assert.Equal(posts, _dispatcher.PostCount);
        Assert.Equal(0, _dispatcher.Pending);
        Assert.Equal(0, changes);
    }

    [Fact]
    public void ContentIsTheFileNameAndItsParsedName()
    {
        PrintNameContent content = new(Foldable, PrintFileNameInfo.Parse(Foldable));

        Assert.Equal(content, new PrintNameContent(new string(Foldable.AsSpan()), content.Info));
        Assert.NotEqual(content, new PrintNameContent("benchy.gcode", content.Info));
        Assert.NotEqual(content, default);
        Assert.True(default(PrintNameContent).IsEmpty);
        Assert.False(content.IsEmpty);
        Assert.Equal(content.GetHashCode(), new PrintNameContent(new string(Foldable.AsSpan()), content.Info).GetHashCode());
    }

    [Fact]
    public void FileNameShownFollowsTheJobRules()
    {
        Assert.Equal(Foldable, PrintNameWidgetViewModel.JobFileName(Job()));
        Assert.Equal(Foldable, PrintNameWidgetViewModel.JobFileName(Job(state: PrinterState.Stopped)));
        Assert.Null(PrintNameWidgetViewModel.JobFileName(Job(state: PrinterState.Idle)));
        Assert.Null(PrintNameWidgetViewModel.JobFileName(Job(state: PrinterState.Offline)));
        Assert.Null(PrintNameWidgetViewModel.JobFileName(Job(fileName: null)));
        Assert.Null(PrintNameWidgetViewModel.JobFileName(null));
    }

    // ---- Sizes and emphasis ----

    [Fact]
    public void SmallCardShowsTheIconAndTheTitleOnly()
    {
        PrintNameWidgetViewModel widget = Show(Job(), OverlayWidgetSize.Small);

        Assert.True(widget.IsShown);
        Assert.True(widget.ShowsIcon);
        Assert.False(widget.ShowsChips);
        Assert.False(widget.ShowsEstimate);
        // The screen reader still gets every value.
        Assert.Equal("Foldable book holder v1, PLA, layer height 0.2 mm, nozzle 0.4 mm, slicer estimate 2 h 51 min", widget.AccessibleName);
    }

    [Fact]
    public void MediumCardShowsTheChipsWithoutTheEstimate()
    {
        PrintNameWidgetViewModel widget = Show(Job(), OverlayWidgetSize.Medium);

        Assert.False(widget.ShowsIcon);
        Assert.True(widget.ShowsChips);
        Assert.False(widget.ShowsEstimate);
    }

    [Fact]
    public void LargeCardShowsTheChipsAndTheEstimate()
    {
        PrintNameWidgetViewModel widget = Show(Job(), OverlayWidgetSize.Large);

        Assert.False(widget.ShowsIcon);
        Assert.True(widget.ShowsChips);
        Assert.True(widget.ShowsEstimate);
    }

    [Fact]
    public void RenderedSizeChangeUpdatesThePartsWithoutFormatting()
    {
        PrintNameWidgetViewModel widget = Show(Job(), OverlayWidgetSize.Medium);
        List<string?> changed = [];
        PropertyChangedEventHandler handler = (_, e) => changed.Add(e.PropertyName);
        widget.PropertyChanged += handler;

        widget.RenderedSize = OverlayWidgetSize.Large;
        Assert.True(widget.ShowsEstimate);
        widget.RenderedSize = OverlayWidgetSize.Small;
        widget.PropertyChanged -= handler;

        Assert.Contains(nameof(PrintNameWidgetViewModel.ShowsIcon), changed);
        Assert.Contains(nameof(PrintNameWidgetViewModel.ShowsChips), changed);
        Assert.Contains(nameof(PrintNameWidgetViewModel.ShowsEstimate), changed);
        Assert.False(widget.ShowsChips);
        Assert.DoesNotContain(nameof(PrintNameWidgetViewModel.Title), changed);
        Assert.DoesNotContain(nameof(OverlayWidgetViewModel.AccessibleName), changed);
        Assert.True(widget.IsShown);
    }

    [Fact]
    public void EmphasisFollowsTheLayout()
    {
        CameraOverlayViewModel overlay = Create(emphasized: true);
        PrintNameWidgetViewModel widget = Show(overlay, Job());
        Assert.True(widget.IsEmphasized);

        overlay.ApplySettings(
            Definition(),
            new CameraOverlayLayout([new OverlayWidgetPlacement(OverlayWidgetIds.PrintName, OverlayAnchor.TopCenter, OverlayWidgetSize.Medium, emphasized: false)]),
            CameraOverlayOpacity.DefaultPercent);

        Assert.Same(widget, Widget(overlay));
        Assert.False(widget.IsEmphasized);
    }
}
