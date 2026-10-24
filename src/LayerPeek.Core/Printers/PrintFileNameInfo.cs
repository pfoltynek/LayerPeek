using System.Buffers;
using System.Globalization;

namespace LayerPeek.Core.Printers;

/// <summary>
/// A print file name split into a readable title and the values PrusaSlicer writes into it with its default
/// <c>output_filename_format</c> of the Prusa profiles,
/// <c>{input_filename_base}_{nozzle}n_{layer_height}mm_{printing_filament_types}_{printer_model}_{print_time}.gcode</c>
/// (https://github.com/prusa3d/PrusaSlicer-settings-prusa-fff, <c>PrusaResearch/*.ini</c>).
/// Values are culture-invariant numbers; formatting them for display is up to the UI.
/// </summary>
public sealed record PrintFileNameInfo
{
    private const double MaxMillimetres = 10;
    private const int MaxNumberLength = 16;
    private const int MaxTimeDigits = 5;
    private const int MaxModelSegments = 2;
    private const int StackTitleLength = 256;

    // Template tail from the right: time, printer model (up to two segments), materials, layer height, nozzle.
    private const int MaxTailSegments = 1 + MaxModelSegments + 3;

    private static readonly string[] Extensions = [".bgcode", ".gcode", ".bgc", ".gco", ".g"];

    private static Cached? s_last;

    private PrintFileNameInfo(string title, string extension)
    {
        Title = title;
        Extension = extension;
    }

    /// <summary>No file name: empty title, no extension and no chips.</summary>
    public static PrintFileNameInfo Empty { get; } = new("", "");

    /// <summary>
    /// Readable name: the base name with <c>_</c> and <c>-</c> turned into spaces, or the whole name without the
    /// extension when it does not follow the template. A title without capital letters gets its first letter capitalised;
    /// one that already contains a capital letter is kept as it is. Empty when the name has nothing but an extension.
    /// </summary>
    public string Title { get; }

    /// <summary>Filament types in slicer order (e.g. <c>PLA</c>, <c>PETG</c>); empty when unknown.</summary>
    public IReadOnlyList<string> Materials { get; private init; } = [];

    public double? LayerHeightMm { get; private init; }

    public double? NozzleDiameterMm { get; private init; }

    /// <summary>Print time estimated by the slicer when the file was exported.</summary>
    public TimeSpan? SlicerEstimate { get; private init; }

    /// <summary>
    /// Recognised print file extension in lower case with the dot (<c>.bgcode</c>, <c>.gcode</c>, <c>.bgc</c>, <c>.gco</c>,
    /// <c>.g</c>); empty when the name has none of them (an unknown extension stays in <see cref="Title"/>).
    /// </summary>
    public string Extension { get; }

    /// <summary>The name followed the slicer template, so the chips (materials, layer height…) are known.</summary>
    public bool HasChips => LayerHeightMm is not null;

    /// <summary>
    /// Splits <paramref name="fileName"/> (optionally with a <c>/</c> or <c>\</c> path) into title and chips. Never throws;
    /// a name that does not follow the template gives a readable title without chips. Repeated calls with the same name
    /// return the cached result of the last call.
    /// </summary>
    public static PrintFileNameInfo Parse(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return Empty;
        }

        if (s_last is { } last && string.Equals(last.FileName, fileName, StringComparison.Ordinal))
        {
            return last.Info;
        }

        PrintFileNameInfo info = ParseCore(fileName);
        s_last = new Cached(fileName, info);
        return info;
    }

    public bool Equals(PrintFileNameInfo? other) =>
        other is not null
        && Title == other.Title
        && Extension == other.Extension
        && LayerHeightMm == other.LayerHeightMm
        && NozzleDiameterMm == other.NozzleDiameterMm
        && SlicerEstimate == other.SlicerEstimate
        && Materials.SequenceEqual(other.Materials, StringComparer.Ordinal);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Title, StringComparer.Ordinal);
        hash.Add(Extension, StringComparer.Ordinal);
        hash.Add(LayerHeightMm);
        hash.Add(NozzleDiameterMm);
        hash.Add(SlicerEstimate);
        foreach (string material in Materials)
        {
            hash.Add(material, StringComparer.Ordinal);
        }

        return hash.ToHashCode();
    }

    private static PrintFileNameInfo ParseCore(string fileName)
    {
        ReadOnlySpan<char> name = fileName.AsSpan().Trim();
        name = name[(name.LastIndexOfAny('/', '\\') + 1)..];
        string extension = SplitExtension(ref name);

        if (TryParseTemplate(name, extension) is { } parsed)
        {
            return parsed;
        }

        return new PrintFileNameInfo(MakeTitle(name), extension);
    }

    private static string SplitExtension(ref ReadOnlySpan<char> name)
    {
        int dot = name.LastIndexOf('.');
        if (dot < 0)
        {
            return "";
        }

        ReadOnlySpan<char> candidate = name[dot..];
        foreach (string extension in Extensions)
        {
            if (candidate.Equals(extension, StringComparison.OrdinalIgnoreCase))
            {
                name = name[..dot];
                return extension;
            }
        }

        return "";
    }

    private static PrintFileNameInfo? TryParseTemplate(ReadOnlySpan<char> stem, string extension)
    {
        // Segment i counts from the right (0 = last); starts[i] is its first index, the segment ends before starts[i - 1] - 1.
        Span<int> starts = stackalloc int[MaxTailSegments + 1];
        int count = 0;
        int end = stem.Length;
        while (count < starts.Length)
        {
            int underscore = stem[..end].LastIndexOf('_');
            starts[count++] = underscore + 1;
            if (underscore < 0)
            {
                break;
            }

            end = underscore;
        }

        int next = 0;
        TimeSpan? estimate = null;
        if (count > 0 && TryParseTime(Segment(stem, starts, 0), out TimeSpan time))
        {
            estimate = time;
            next = 1;
        }

        // Most profiles have a one-segment printer model, Core One INDX has two (COREONE_INDX4T), custom ones may have none.
        ReadOnlySpan<int> modelSegmentCounts = [1, 2, 0];
        foreach (int modelSegments in modelSegmentCounts)
        {
            int material = next + modelSegments;
            int layer = material + 1;
            if (layer >= count
                || !AreModelSegments(stem, starts, next, material)
                || !IsMaterialList(Segment(stem, starts, material))
                || !TryParseMillimetres(Segment(stem, starts, layer), "", "mm", out double layerHeight))
            {
                continue;
            }

            double? nozzle = null;
            int baseEnd = starts[layer] - 1;
            if (layer + 1 < count && TryParseNozzle(Segment(stem, starts, layer + 1), out double nozzleDiameter))
            {
                nozzle = nozzleDiameter;
                baseEnd = starts[layer + 1] - 1;
            }

            string title = baseEnd > 0 ? MakeTitle(stem[..baseEnd]) : "";
            if (title.Length == 0)
            {
                return null;
            }

            return new PrintFileNameInfo(title, extension)
            {
                Materials = SplitMaterials(Segment(stem, starts, material)),
                LayerHeightMm = layerHeight,
                NozzleDiameterMm = nozzle,
                SlicerEstimate = estimate,
            };
        }

        return null;
    }

    private static ReadOnlySpan<char> Segment(ReadOnlySpan<char> stem, ReadOnlySpan<int> starts, int index)
    {
        int end = index == 0 ? stem.Length : starts[index - 1] - 1;
        return stem[starts[index]..end];
    }

    private static bool AreModelSegments(ReadOnlySpan<char> stem, ReadOnlySpan<int> starts, int from, int to)
    {
        for (int i = from; i < to; i++)
        {
            if (!IsModelSegment(Segment(stem, starts, i)))
            {
                return false;
            }
        }

        return true;
    }

    // Printer models are upper-case codes such as COREONE, MK3.9S, XL+ or INDX4T.
    private static bool IsModelSegment(ReadOnlySpan<char> segment)
    {
        if (segment.IsEmpty || !char.IsAsciiLetterUpper(segment[0]))
        {
            return false;
        }

        foreach (char c in segment)
        {
            if (!char.IsAsciiLetterUpper(c) && !char.IsAsciiDigit(c) && c != '.' && c != '+')
            {
                return false;
            }
        }

        return true;
    }

    // Filament types are upper-case (PLA, PETG, PA11, PLA-CF…), joined by ',' (or ';' in custom templates).
    private static bool IsMaterialList(ReadOnlySpan<char> segment)
    {
        if (segment.IsEmpty)
        {
            return false;
        }

        foreach (Range range in segment.SplitAny(',', ';'))
        {
            if (!IsMaterial(segment[range]))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsMaterial(ReadOnlySpan<char> material)
    {
        bool hasLetter = false;
        foreach (char c in material)
        {
            if (char.IsAsciiLetterUpper(c))
            {
                hasLetter = true;
            }
            else if (!char.IsAsciiDigit(c) && c != '+' && c != '-' && c != '.')
            {
                return false;
            }
        }

        return hasLetter;
    }

    private static string[] SplitMaterials(ReadOnlySpan<char> segment)
    {
        int count = segment.Count(',') + segment.Count(';') + 1;
        string[] materials = new string[count];
        int i = 0;
        foreach (Range range in segment.SplitAny(',', ';'))
        {
            materials[i++] = segment[range].ToString();
        }

        return materials;
    }

    // "0.4n" (nozzle_diameter template) or "HT0.6" (HT hotend profiles).
    private static bool TryParseNozzle(ReadOnlySpan<char> segment, out double diameter) =>
        TryParseMillimetres(segment, "", "n", out diameter) || TryParseMillimetres(segment, "HT", "", out diameter);

    private static bool TryParseMillimetres(ReadOnlySpan<char> segment, string prefix, string suffix, out double value)
    {
        value = 0;
        if (!segment.StartsWith(prefix, StringComparison.Ordinal) || !segment.EndsWith(suffix, StringComparison.Ordinal))
        {
            return false;
        }

        ReadOnlySpan<char> number = segment[prefix.Length..^suffix.Length];
        if (number.IsEmpty || number.Length > MaxNumberLength || !char.IsAsciiDigit(number[0]))
        {
            return false;
        }

        Span<char> buffer = stackalloc char[MaxNumberLength];
        bool separator = false;
        for (int i = 0; i < number.Length; i++)
        {
            char c = number[i];
            if (c is '.' or ',')
            {
                if (separator)
                {
                    return false;
                }

                separator = true;
                c = '.';
            }
            else if (!char.IsAsciiDigit(c))
            {
                return false;
            }

            buffer[i] = c;
        }

        return double.TryParse(buffer[..number.Length], NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out value)
            && value > 0
            && value <= MaxMillimetres;
    }

    // PrusaSlicer short_time(): "1d2h3m", "2h51m", "45m" or "42s"; parts in d-h-m-s order, each at most once.
    private static bool TryParseTime(ReadOnlySpan<char> segment, out TimeSpan time)
    {
        time = default;
        ReadOnlySpan<char> units = "dhms";
        ReadOnlySpan<long> unitSeconds = [86_400, 3_600, 60, 1];
        long seconds = 0;
        int nextUnit = 0;
        int position = 0;
        while (position < segment.Length)
        {
            int digits = 0;
            long value = 0;
            while (position < segment.Length && char.IsAsciiDigit(segment[position]))
            {
                if (++digits > MaxTimeDigits)
                {
                    return false;
                }

                value = (value * 10) + (segment[position++] - '0');
            }

            if (digits == 0 || position == segment.Length)
            {
                return false;
            }

            int unit = units[nextUnit..].IndexOf(segment[position++]);
            if (unit < 0)
            {
                return false;
            }

            nextUnit += unit + 1;
            seconds += value * unitSeconds[nextUnit - 1];
        }

        if (position == 0)
        {
            return false;
        }

        time = TimeSpan.FromSeconds(seconds);
        return true;
    }

    private static string MakeTitle(ReadOnlySpan<char> source)
    {
        char[]? rented = null;
        Span<char> buffer = source.Length <= StackTitleLength
            ? stackalloc char[StackTitleLength]
            : (rented = ArrayPool<char>.Shared.Rent(source.Length));
        try
        {
            int length = 0;
            bool pendingSpace = false;
            bool hasUpper = false;
            foreach (char c in source)
            {
                if (c is '_' or '-' || char.IsWhiteSpace(c) || char.IsControl(c))
                {
                    pendingSpace = length > 0;
                    continue;
                }

                if (pendingSpace)
                {
                    buffer[length++] = ' ';
                    pendingSpace = false;
                }

                hasUpper |= char.IsUpper(c);
                buffer[length++] = c;
            }

            if (length == 0)
            {
                return "";
            }

            if (!hasUpper && char.IsLower(buffer[0]))
            {
                buffer[0] = char.ToUpperInvariant(buffer[0]);
            }

            return new string(buffer[..length]);
        }
        finally
        {
            if (rented is not null)
            {
                ArrayPool<char>.Shared.Return(rented);
            }
        }
    }

    private sealed record Cached(string FileName, PrintFileNameInfo Info);
}
