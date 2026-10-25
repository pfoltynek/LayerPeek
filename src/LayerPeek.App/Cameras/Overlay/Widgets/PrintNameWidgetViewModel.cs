using System.ComponentModel;
using System.Globalization;

using LayerPeek.App.Resources;
using LayerPeek.App.Tray;
using LayerPeek.Core.Printers;

namespace LayerPeek.App.Cameras.Overlay.Widgets;

/// <summary>
/// What the Print name widget shows (T-085): the file name of the job and its parsed form. The parsed form is the same
/// instance for the same name (the widget parses a name once), so two polls of one job compare equal without comparing
/// the chips. <c>default</c> = nothing to show.
/// </summary>
public readonly record struct PrintNameContent(string? FileName, PrintFileNameInfo? Info)
{
    public bool IsEmpty => FileName is null;

    public bool Equals(PrintNameContent other) =>
        string.Equals(FileName, other.FileName, StringComparison.Ordinal) && ReferenceEquals(Info, other.Info);

    public override int GetHashCode() => FileName is null ? 0 : StringComparer.Ordinal.GetHashCode(FileName);
}

/// <summary>
/// The Print name widget of the camera overlay (T-085, design T-076 2.4): the readable title of the print file (its key
/// value, emphasized in the accent colour) and chips with the material, the layer height and the nozzle from the slicer
/// template (T-078); in L also the slicer's print time estimate. The G-code preview next to it comes with T-087.
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

    // Monitor thread, under the overlay's lock: the last parsed name.
    private string? _parsedFileName;
    private PrintFileNameInfo? _parsedInfo;

    private string _title = string.Empty;
    private string _materialChip = string.Empty;
    private string _layerHeightChip = string.Empty;
    private string _nozzleChip = string.Empty;
    private string _estimate = string.Empty;
    private bool _hasMaterial;
    private bool _hasLayerHeight;
    private bool _hasNozzle;
    private bool _hasEstimate;

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

        return new PrintNameContent(_parsedFileName, _parsedInfo);
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
    }

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.PropertyName == nameof(RenderedSize))
        {
            OnPartsChanged();
        }
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

    /// <summary>"0.2 mm" / "0,2 mm".</summary>
    public static string Millimetres(double millimetres) => Number(millimetres) + " mm";

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
