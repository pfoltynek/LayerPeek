using System.ComponentModel;
using System.Globalization;

using Avalonia.Media;

using LayerPeek.App.Previews;
using LayerPeek.App.Resources;
using LayerPeek.App.Tray;
using LayerPeek.Core.Cameras;
using LayerPeek.Core.Previews;
using LayerPeek.Core.Printers;

namespace LayerPeek.App.Cameras.Overlay.Widgets;

/// <summary>
/// What the Print name widget shows (T-085): the file name of the job and its parsed form, and what its G-code thumbnail
/// (T-087) is asked for with: the job, the printer and whether cards are drawn (not in the mini preview). The parsed
/// form is the same instance for the same name (the widget parses a name once), and the job is compared by its identity
/// (<see cref="PrintJob.Id"/> + <see cref="PrintJob.PreviewReference"/>), so two polls of one job compare equal without
/// allocating. <c>default</c> = nothing to show.
/// </summary>
public readonly record struct PrintNameContent(
    string? FileName,
    PrintFileNameInfo? Info,
    PrintJob? Job = null,
    PrinterDefinition? Printer = null,
    bool CardsVisible = false)
{
    public bool IsEmpty => FileName is null;

    public bool Equals(PrintNameContent other) =>
        string.Equals(FileName, other.FileName, StringComparison.Ordinal)
        && ReferenceEquals(Info, other.Info)
        && IsSameJob(Job, other.Job)
        && Equals(Printer, other.Printer)
        && CardsVisible == other.CardsVisible;

    public override int GetHashCode() => FileName is null ? 0 : StringComparer.Ordinal.GetHashCode(FileName);

    /// <summary>The same job for its preview: the same id and preview reference (each poll brings a new instance).</summary>
    internal static bool IsSameJob(PrintJob? job, PrintJob? other) =>
        ReferenceEquals(job, other)
        || (job is not null && other is not null && job.Id == other.Id && Equals(job.PreviewReference, other.PreviewReference));
}

/// <summary>
/// The Print name widget of the camera overlay (T-085, design T-076 2.4): the readable title of the print file (its key
/// value, emphasized in the accent colour) and chips with the material, the layer height and the nozzle from the slicer
/// template (T-078); in L also the slicer's print time estimate. In M and L the G-code thumbnail (T-087) is drawn left of
/// the texts (40 / 72 DIP); without one the card is the same, without an empty frame.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>Shown during a job and after a finished or stopped print, until the next print (user decision 2026-10-24); the
/// PrusaLink client keeps the last job in FINISHED / STOPPED. Hidden without a known file name, outside a job (ready,
/// busy, error) and while the printer is unreachable.</item>
/// <item>S: cube icon + title. M: title + chips. L: title (2 lines) + chips + estimate.</item>
/// <item>A name without the slicer template shows its title without chips; a name without any title (only an extension)
/// shows the file name itself.</item>
/// <item>The name is parsed on the monitor thread only when the file name changes; the texts are formatted on the UI thread
/// only when the shown name changes or the language switches.</item>
/// <item>Thumbnail (UI thread, <see cref="PrintThumbnails"/>): asked for only while the card is shown in M or L in the
/// normal window or on full screen, and again only when the job, the printer, the size or the monitor scaling changes,
/// never per poll. It stays after the print (the job stays) until the next job, which drops it at once and asks for its
/// own. Another size or scaling of the same job keeps the shown thumbnail until the new one is decoded. Released when
/// the card hides, goes to S or the window closes.</item>
/// </list>
/// The same view model also feeds the name row of the mini preview (<see cref="PrintNameCompactView"/>, T-090).
/// </remarks>
public sealed class PrintNameWidgetViewModel : OverlayWidgetViewModel<PrintNameContent>
{
    /// <summary>Narrowest card per size (T-076 2.4: M min 240, L min 300; S has no minimum).</summary>
    public static readonly OverlayWidgetMinWidths MinWidths = new(0, 240, 300);

    private static readonly PropertyChangedEventArgs ShowsIconChanged = new(nameof(ShowsIcon));
    private static readonly PropertyChangedEventArgs ShowsChipsChanged = new(nameof(ShowsChips));
    private static readonly PropertyChangedEventArgs ShowsEstimateChanged = new(nameof(ShowsEstimate));
    private static readonly PropertyChangedEventArgs HasChipsChanged = new(nameof(HasChips));
    private static readonly PropertyChangedEventArgs ShowsThumbnailChanged = new(nameof(ShowsThumbnail));

    /// <summary>The thumbnail of the M card, DIP (T-076 2.4).</summary>
    public const double MediumThumbnailSize = 40;

    /// <summary>The thumbnail of the L card, DIP (T-076 2.4).</summary>
    public const double LargeThumbnailSize = 72;

    /// <summary>Above any real monitor (a 4 × scaled 72 DIP thumbnail is 288 px, still below the 380 px preview).</summary>
    private const double MaxRenderScaling = 4;

    private readonly PrintThumbnails? _thumbnails;

    // Monitor thread, under the overlay's lock: the last parsed name.
    private string? _parsedFileName;
    private PrintFileNameInfo? _parsedInfo;

    // UI thread: the thumbnail asked for (and its request), and the one shown (with the request that keeps it alive).
    private double _renderScaling = 1;
    private ThumbnailKey? _requestedKey;
    private IDisposable? _pendingRequest;
    private ThumbnailKey? _shownKey;
    private IDisposable? _shownRequest;
    private IImage? _thumbnail;
    private string _thumbnailName = string.Empty;
    private bool _disposed;

    private string _title = string.Empty;
    private string _materialChip = string.Empty;
    private string _layerHeightChip = string.Empty;
    private string _nozzleChip = string.Empty;
    private string _estimate = string.Empty;
    private bool _hasMaterial;
    private bool _hasLayerHeight;
    private bool _hasNozzle;
    private bool _hasEstimate;

    /// <param name="context">The window's services; its <see cref="OverlayWidgetContext.Thumbnails"/> draw the G-code thumbnail (none without).</param>
    public PrintNameWidgetViewModel(OverlayWidgetContext? context = null)
    {
        _thumbnails = context?.Thumbnails;
    }

    /// <summary>The decoded G-code thumbnail of the job (T-087); <see langword="null"/> while there is none.</summary>
    public IImage? Thumbnail
    {
        get => _thumbnail;
        private set
        {
            if (SetProperty(ref _thumbnail, value))
            {
                OnPropertyChanged(ShowsThumbnailChanged);
            }
        }
    }

    /// <summary>The thumbnail column is drawn (M / L with a decoded thumbnail); otherwise the card is the one of T-085.</summary>
    public bool ShowsThumbnail => _thumbnail is not null;

    /// <summary>"Print preview": the screen reader name of the thumbnail.</summary>
    public string ThumbnailName
    {
        get => _thumbnailName;
        private set => SetProperty(ref _thumbnailName, value);
    }

    /// <summary>
    /// The render scaling of the monitor the card is on (set by the view): the thumbnail is decoded at its size × this,
    /// so that it is sharp at 125–200 % without decoding the whole preview. UI thread.
    /// </summary>
    internal double RenderScaling
    {
        get => _renderScaling;
        set
        {
            double scaling = double.IsFinite(value) && value > 0 ? Math.Min(value, MaxRenderScaling) : 1;
            if (scaling != _renderScaling)
            {
                _renderScaling = scaling;
                UpdateThumbnail();
            }
        }
    }

    /// <summary>The thumbnail store of the window (tests: the composition root hands it over).</summary>
    internal PrintThumbnails? Thumbnails => _thumbnails;

    /// <summary>The readable name of the print ("Foldable book holder v1"), whole; the view shortens it by its width.</summary>
    public string Title
    {
        get => _title;
        private set => SetProperty(ref _title, value);
    }

    /// <summary>"PLA" or "PLA, PETG" (at most 16 characters + "…").</summary>
    public string MaterialChip
    {
        get => _materialChip;
        private set => SetProperty(ref _materialChip, value);
    }

    /// <summary>"0.2 mm" (decimal separator of the region).</summary>
    public string LayerHeightChip
    {
        get => _layerHeightChip;
        private set => SetProperty(ref _layerHeightChip, value);
    }

    /// <summary>"nozzle 0.4".</summary>
    public string NozzleChip
    {
        get => _nozzleChip;
        private set => SetProperty(ref _nozzleChip, value);
    }

    /// <summary>"slicer estimate 2 h 51 min" (L only).</summary>
    public string Estimate
    {
        get => _estimate;
        private set => SetProperty(ref _estimate, value);
    }

    public bool HasMaterial
    {
        get => _hasMaterial;
        private set => SetProperty(ref _hasMaterial, value);
    }

    public bool HasLayerHeight
    {
        get => _hasLayerHeight;
        private set
        {
            if (SetProperty(ref _hasLayerHeight, value))
            {
                OnPartsChanged();
            }
        }
    }

    public bool HasNozzle
    {
        get => _hasNozzle;
        private set => SetProperty(ref _hasNozzle, value);
    }

    public bool HasEstimate
    {
        get => _hasEstimate;
        private set
        {
            if (SetProperty(ref _hasEstimate, value))
            {
                OnPartsChanged();
            }
        }
    }

    /// <summary>The name followed the slicer template: the chips are known (the layer height always is then).</summary>
    public bool HasChips => _hasLayerHeight;

    /// <summary>The cube icon before the title (S only).</summary>
    public bool ShowsIcon => IsSmall;

    /// <summary>The chip row (M / L, when the name followed the template).</summary>
    public bool ShowsChips => !IsSmall && _hasLayerHeight;

    /// <summary>The slicer estimate row (L only, when the name has one).</summary>
    public bool ShowsEstimate => IsLarge && _hasEstimate;

    /// <summary>How many times a file name was parsed (tests: only when it changes).</summary>
    internal int ParseCount { get; private set; }

    /// <summary>
    /// The file name whose print the widget shows: the job's, while the printer is reachable and prints, is paused, waits
    /// for the user, or finished or stopped that print; <see langword="null"/> otherwise (also before the first poll).
    /// </summary>
    public static string? JobFileName(PrinterSnapshot? snapshot) =>
        snapshot is { Job.FileName: { } fileName } && snapshot.State.IsReachable() && ShowsJobIn(snapshot.State)
            ? fileName
            : null;

    protected override PrintNameContent ComputeContent(in OverlayWidgetInput input)
    {
        if (JobFileName(input.Snapshot) is not { } fileName)
        {
            return default;
        }

        if (!string.Equals(fileName, _parsedFileName, StringComparison.Ordinal))
        {
            _parsedInfo = PrintFileNameInfo.Parse(fileName);
            _parsedFileName = fileName;
            ParseCount++;
        }

        // The job of this (the latest) snapshot: the preview cache never goes back to an older job (T-086).
        return new PrintNameContent(_parsedFileName, _parsedInfo, input.Snapshot!.Job, input.Printer, input.Mode != CameraWindowMode.Mini);
    }

    protected override bool HasData(in PrintNameContent content) => !content.IsEmpty;

    protected override void Format(in PrintNameContent content)
    {
        PrintFileNameInfo info = content.Info ?? PrintFileNameInfo.Empty;
        string title = PrintNameFormat.Title(content.FileName!, info);
        Title = title;
        MaterialChip = PrintNameFormat.MaterialChip(info.Materials);
        LayerHeightChip = info.LayerHeightMm is { } layer ? PrintNameFormat.Millimetres(layer) : string.Empty;
        NozzleChip = info.NozzleDiameterMm is { } nozzle ? PrintNameFormat.NozzleChip(nozzle) : string.Empty;
        Estimate = info.SlicerEstimate is { } estimate ? PrintNameFormat.Estimate(estimate) : string.Empty;
        HasMaterial = info.Materials.Count > 0;
        HasNozzle = info.NozzleDiameterMm is not null;
        HasLayerHeight = info.LayerHeightMm is not null;
        HasEstimate = info.SlicerEstimate is not null;
        AccessibleName = PrintNameFormat.AccessibleText(title, info);
        ThumbnailName = Strings.CameraOverlayThumbnailName;
        UpdateThumbnail();
    }

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        switch (e.PropertyName)
        {
            case nameof(RenderedSize):
                OnPartsChanged();
                UpdateThumbnail();
                break;
            case nameof(HasContent):
            case nameof(IsFaulted):
                UpdateThumbnail();
                break;
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_disposed)
        {
            _disposed = true;
            _pendingRequest?.Dispose();
            _pendingRequest = null;
            _requestedKey = null;
            ClearShownThumbnail();
        }

        base.Dispose(disposing);
    }

    /// <summary>
    /// UI thread: asks for the thumbnail the card needs now, when that differs from the one asked for last (a new job,
    /// another printer definition, size or scaling); none while the card is hidden, small or in the mini preview.
    /// </summary>
    private void UpdateThumbnail()
    {
        if (_disposed)
        {
            return;
        }

        ThumbnailKey? wanted = WantedThumbnail(out PrintJob? job);
        if (wanted == _requestedKey)
        {
            return;
        }

        _pendingRequest?.Dispose();
        _pendingRequest = null;
        _requestedKey = wanted;
        if (_shownKey is { } shown && (wanted is not { } next || !next.IsSameJob(shown)))
        {
            // Another job (or none): its thumbnail must not stay while the new one loads.
            ClearShownThumbnail();
        }

        if (wanted is not { } key || job is null || wanted == _shownKey)
        {
            return;
        }

        // The update is always posted (never inline), so the request is assigned before it can run.
        IDisposable? request = null;
        request = _thumbnails!.Request(key.Printer, job, key.MaxSide, image => OnThumbnail(request!, key, image));
        _pendingRequest = request;
    }

    /// <summary>The thumbnail the card needs now; <see langword="null"/> for none.</summary>
    private ThumbnailKey? WantedThumbnail(out PrintJob? job)
    {
        job = null;
        PrintNameContent content = ShownContent;
        if (_thumbnails is null
            || !HasContent
            || IsFaulted
            || IsSmall
            || !content.CardsVisible
            || content.Printer is not { } printer
            || content.Job is not { PreviewReference: { } reference } shownJob)
        {
            return null;
        }

        job = shownJob;
        double side = IsLarge ? LargeThumbnailSize : MediumThumbnailSize;
        // Physical pixels; the small tolerance keeps e.g. 40 × 1.25 = 50 from becoming 51 through rounding errors.
        int maxSide = Math.Max(1, (int)Math.Ceiling((side * _renderScaling) - 0.001));
        return new ThumbnailKey(printer, shownJob.Id, reference, maxSide);
    }

    /// <summary>UI thread: a request delivered its thumbnail, or none (yet).</summary>
    private void OnThumbnail(IDisposable request, ThumbnailKey key, IImage? image)
    {
        if (!ReferenceEquals(request, _pendingRequest))
        {
            return;
        }

        if (image is null)
        {
            // No preview (yet): the request keeps asking; an older size of the job must not stand in for it.
            ClearShownThumbnail();
            return;
        }

        IDisposable? previous = _shownRequest;
        _pendingRequest = null;
        _shownRequest = request;
        _shownKey = key;
        Thumbnail = image;
        // Only now: the previous image may still have been drawn until the new one replaced it.
        previous?.Dispose();
    }

    private void ClearShownThumbnail()
    {
        Thumbnail = null;
        _shownRequest?.Dispose();
        _shownRequest = null;
        _shownKey = null;
    }

    private static bool ShowsJobIn(PrinterState state) =>
        state.HasActiveJob() || state is PrinterState.Attention or PrinterState.Finished or PrinterState.Stopped;

    private void OnPartsChanged()
    {
        OnPropertyChanged(ShowsIconChanged);
        OnPropertyChanged(ShowsChipsChanged);
        OnPropertyChanged(ShowsEstimateChanged);
        OnPropertyChanged(HasChipsChanged);
    }

    /// <summary>One thumbnail: the job (by identity) on the printer, decoded with its longer side at most <see cref="MaxSide"/> px.</summary>
    private readonly record struct ThumbnailKey(PrinterDefinition Printer, long? JobId, PrintPreviewReference Reference, int MaxSide)
    {
        public bool IsSameJob(ThumbnailKey other) =>
            string.Equals(Printer.Id, other.Printer.Id, StringComparison.OrdinalIgnoreCase) && JobId == other.JobId && Reference.Equals(other.Reference);
    }
}

/// <summary>
/// Texts of the Print name widget (T-085, design T-076 2.4): numbers in the region (<c>CurrentCulture</c>, "0,2 mm"),
/// words in the app language. UI thread, only when the shown name changed or the language switched.
/// </summary>
public static class PrintNameFormat
{
    /// <summary>Most characters of the material chip before "…" (T-076 2.4).</summary>
    public const int MaxMaterialChipLength = 16;

    private const string Ellipsis = "…";

    private const string MaterialSeparator = ", ";

    /// <summary>
    /// The title of <paramref name="info"/>; when the name has none (only an extension or separators), the file name
    /// without its folder, as the printer lists it.
    /// </summary>
    public static string Title(string fileName, PrintFileNameInfo info)
    {
        ArgumentNullException.ThrowIfNull(fileName);
        ArgumentNullException.ThrowIfNull(info);
        if (info.Title.Length > 0)
        {
            return info.Title;
        }

        ReadOnlySpan<char> name = fileName.AsSpan().Trim();
        return name[(name.LastIndexOfAny('/', '\\') + 1)..].ToString();
    }

    /// <summary>"PLA, PETG"; longer than <see cref="MaxMaterialChipLength"/> characters: cut, without a trailing separator, + "…".</summary>
    public static string MaterialChip(IReadOnlyList<string> materials)
    {
        ArgumentNullException.ThrowIfNull(materials);
        string all = string.Join(MaterialSeparator, materials);
        if (all.Length <= MaxMaterialChipLength)
        {
            return all;
        }

        int cut = MaxMaterialChipLength;
        // Never split a surrogate pair.
        if (char.IsHighSurrogate(all[cut - 1]))
        {
            cut--;
        }

        return string.Concat(all.AsSpan(0, cut).TrimEnd(", ;"), Ellipsis);
    }

    /// <summary>"0.2" / "0,2": without trailing zeros, at most three decimals.</summary>
    public static string Number(double millimetres) => millimetres.ToString("0.###", CultureInfo.CurrentCulture);

    /// <summary>"0.2 mm" / "0,2 mm", joined by a non-breaking space (U+00A0) so the chip never wraps between them.</summary>
    public static string Millimetres(double millimetres) => Number(millimetres) + " mm";

    /// <summary>"nozzle 0.4" / "tryska 0,4".</summary>
    public static string NozzleChip(double millimetres) => Format(Strings.CameraOverlayNozzleChip, Number(millimetres));

    /// <summary>"slicer estimate 2 h 51 min" (rounded up to the minute, like the other times of the overlay).</summary>
    public static string Estimate(TimeSpan estimate) =>
        Format(Strings.CameraOverlaySlicerEstimate, OverlayFormat.Duration(ToolTipContent.CeilingMinutes(estimate)));

    /// <summary>
    /// "Foldable book holder v1, PLA, layer height 0.2 mm, nozzle 0.4 mm, slicer estimate 2 h 51 min": everything the card
    /// knows, in words (the chips have no labels on screen); all materials, also those the chip leaves out.
    /// </summary>
    public static string AccessibleText(string title, PrintFileNameInfo info)
    {
        ArgumentNullException.ThrowIfNull(title);
        ArgumentNullException.ThrowIfNull(info);
        List<string> parts = [title];
        if (info.Materials.Count > 0)
        {
            parts.Add(string.Join(MaterialSeparator, info.Materials));
        }

        if (info.LayerHeightMm is { } layer)
        {
            parts.Add(Format(Strings.CameraOverlayLayerHeightName, Number(layer)));
        }

        if (info.NozzleDiameterMm is { } nozzle)
        {
            parts.Add(Format(Strings.CameraOverlayNozzleDiameterName, Number(nozzle)));
        }

        if (info.SlicerEstimate is { } estimate)
        {
            parts.Add(Estimate(estimate));
        }

        return string.Join(", ", parts);
    }

    private static string Format(string format, object? arg0) => string.Format(CultureInfo.CurrentCulture, format, arg0);
}
