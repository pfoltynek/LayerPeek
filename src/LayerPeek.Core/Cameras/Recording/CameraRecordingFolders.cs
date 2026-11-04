using System.Buffers;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

using LayerPeek.Core.Printers;

namespace LayerPeek.Core.Cameras.Recording;

/// <summary>
/// Where recordings go and how they are named (T-145): <c>&lt;root&gt;/&lt;printer folder&gt;/&lt;yyyy-MM-dd_HH-mm-ss&gt;[_&lt;print title&gt;].mp4</c>.
/// The root is the user's folder or <see cref="DefaultRoot"/>; every printer folder carries <see cref="MarkerFileName"/>,
/// and only files of marked folders that follow <see cref="IsRecordingFileName"/> or <see cref="IsSnapshotFileName"/> are
/// ever deleted by the rotation. Alarm snapshots of the print failure detection (T-153) are JPEG files next to the
/// recordings, named and rotated like them (<see cref="SnapshotFileName"/>).
/// Names use only characters that are valid on Windows and macOS, sort by time and never collide (the recorder adds
/// <c>-2</c>, <c>-3</c> … for a taken name and after a stream restart, T-144).
/// </summary>
public static class CameraRecordingFolders
{
    /// <summary>Extension of every recording (fragmented MP4, T-144).</summary>
    public const string Extension = ".mp4";

    /// <summary>Extension of the alarm snapshots (T-153).</summary>
    public const string SnapshotExtension = ".jpg";

    /// <summary>
    /// Marks a printer folder as LayerPeek's (a small text file that explains itself): the rotation deletes nothing in a
    /// folder without it, so a custom root that also holds other videos never loses them.
    /// </summary>
    public const string MarkerFileName = ".layerpeek-recordings";

    /// <summary>Name of the folder in the system's video folder (and of the root inside the test data folder).</summary>
    public const string RootFolderName = "LayerPeek";

    /// <summary>Longest printer folder name (without the uniqueness suffix).</summary>
    internal const int MaxFolderNameLength = 60;

    /// <summary>Longest print title in a file name.</summary>
    internal const int MaxTitleLength = 60;

    /// <summary>Length of <c>yyyy-MM-dd_HH-mm-ss</c>.</summary>
    internal const int TimestampLength = 19;

    private const string TimestampFormat = "yyyy-MM-dd_HH-mm-ss";

    /// <summary>Folder used for a printer whose name has no usable character.</summary>
    private const string FallbackFolderName = "Printer";

    /// <summary>Characters invalid in Windows file names (a superset of macOS' <c>/</c> and <c>:</c>).</summary>
    private static readonly SearchValues<char> InvalidNameChars = SearchValues.Create("<>:\"/\\|?*");

    /// <summary>
    /// The default root (user decision 2026-11-03): Windows <c>%USERPROFILE%\Videos\LayerPeek</c> (the Videos known
    /// folder, also when it was moved), macOS <c>~/Movies/LayerPeek</c>; elsewhere the video folder of the user or
    /// <c>~/Videos</c>.
    /// </summary>
    public static string DefaultRoot()
    {
        string videos = Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);
        if (string.IsNullOrEmpty(videos))
        {
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            videos = Path.Combine(home, OperatingSystem.IsMacOS() ? "Movies" : "Videos");
        }

        return Path.Combine(videos, RootFolderName);
    }

    /// <summary>
    /// The folder name of every printer (by id, case-insensitive): its name made safe for file systems (invalid and
    /// control characters become <c>_</c>, white space collapses, no trailing dots or spaces, no reserved Windows device
    /// name, at most <see cref="MaxFolderNameLength"/> characters). Printers whose names give the same folder
    /// (case-insensitive) all get a short suffix derived from their id, so that two printers never share a folder.
    /// </summary>
    public static IReadOnlyDictionary<string, string> PrinterFolderNames(IReadOnlyList<PrinterDefinition> printers)
    {
        ArgumentNullException.ThrowIfNull(printers);
        Dictionary<string, string> names = new(printers.Count, StringComparer.OrdinalIgnoreCase);
        Dictionary<string, int> uses = new(printers.Count, StringComparer.OrdinalIgnoreCase);
        foreach (PrinterDefinition printer in printers)
        {
            string name = SafeName(printer.Name, MaxFolderNameLength, FallbackFolderName);
            if (names.TryAdd(printer.Id, name))
            {
                uses[name] = uses.GetValueOrDefault(name) + 1;
            }
        }

        foreach (string id in names.Keys.ToArray())
        {
            string name = names[id];
            if (uses[name] > 1)
            {
                names[id] = $"{name} ({IdSuffix(id)})";
            }
        }

        return names;
    }

    /// <summary>
    /// The file name of a recording that starts at <paramref name="start"/> (local time of the user):
    /// <c>2026-11-03_14-05-09_Benchy.mp4</c>, with the readable title of the print file (T-078) when there is one,
    /// otherwise <c>2026-11-03_14-05-09.mp4</c>.
    /// </summary>
    public static string FileName(DateTimeOffset start, string? printFileName)
    {
        string timestamp = start.ToString(TimestampFormat, CultureInfo.InvariantCulture);
        string title = SafeName(PrintFileNameInfo.Parse(printFileName).Title, MaxTitleLength, fallback: "");
        return title.Length == 0 ? timestamp + Extension : $"{timestamp}_{title}{Extension}";
    }

    /// <summary>
    /// The file name of the snapshot of a print failure alarm (T-153) taken at <paramref name="time"/> (local time of the
    /// user): <c>2026-11-04_14-05-09_alarm-bed-change.jpg</c>. <paramref name="detectorId"/> is a valid detector id
    /// (lower-case letters, digits, dashes), so the name is valid everywhere.
    /// </summary>
    public static string SnapshotFileName(DateTimeOffset time, string detectorId)
    {
        ArgumentException.ThrowIfNullOrEmpty(detectorId);
        return $"{time.ToString(TimestampFormat, CultureInfo.InvariantCulture)}_alarm-{SafeName(detectorId, MaxTitleLength, "detector")}{SnapshotExtension}";
    }

    /// <summary>
    /// Whether <paramref name="fileName"/> follows the pattern of <see cref="FileName"/> (also with the recorder's
    /// <c>-2</c>, <c>-3</c> … suffix): a valid <c>yyyy-MM-dd_HH-mm-ss</c> timestamp at the start, then nothing, <c>_</c> or
    /// <c>-</c> and anything, and <see cref="Extension"/> (any case).
    /// </summary>
    public static bool IsRecordingFileName(string? fileName) => IsTimestampedFileName(fileName, Extension);

    /// <summary>Whether <paramref name="fileName"/> is an alarm snapshot: the pattern of <see cref="IsRecordingFileName"/> with <see cref="SnapshotExtension"/>.</summary>
    public static bool IsSnapshotFileName(string? fileName) => IsTimestampedFileName(fileName, SnapshotExtension);

    private static bool IsTimestampedFileName(string? fileName, string extension)
    {
        if (fileName is null || fileName.Length < TimestampLength + extension.Length
            || !fileName.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        ReadOnlySpan<char> name = fileName.AsSpan(0, fileName.Length - extension.Length);
        if (!DateTime.TryParseExact(name[..TimestampLength], TimestampFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
        {
            return false;
        }

        return name.Length == TimestampLength || name[TimestampLength] is '_' or '-';
    }

    /// <summary>
    /// <paramref name="text"/> as a file or folder name valid on Windows and macOS (see <see cref="PrinterFolderNames"/>),
    /// or <paramref name="fallback"/> when nothing usable is left.
    /// </summary>
    internal static string SafeName(string? text, int maxLength, string fallback)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return fallback;
        }

        StringBuilder builder = new(Math.Min(text.Length, maxLength));
        bool space = false;
        foreach (char c in text)
        {
            if (char.IsWhiteSpace(c))
            {
                space = builder.Length > 0;
                continue;
            }

            if (space)
            {
                builder.Append(' ');
                space = false;
            }

            builder.Append(char.IsControl(c) || InvalidNameChars.Contains(c) ? '_' : c);
            if (builder.Length >= maxLength)
            {
                break;
            }
        }

        // No surrogate half at the cut, no trailing dots or spaces (Windows drops them), no hidden "." name.
        if (builder.Length > 0 && char.IsHighSurrogate(builder[^1]))
        {
            builder.Length--;
        }

        string name = builder.ToString().TrimEnd('.', ' ').TrimStart('.');
        if (name.Length == 0)
        {
            return fallback;
        }

        return IsReservedDeviceName(name) ? "_" + name : name;
    }

    /// <summary>CON, PRN, AUX, NUL, COM0–9, LPT0–9 (also with an extension): Windows cannot create such files.</summary>
    private static bool IsReservedDeviceName(string name)
    {
        int dot = name.IndexOf('.', StringComparison.Ordinal);
        ReadOnlySpan<char> stem = (dot < 0 ? name : name[..dot]).AsSpan().TrimEnd(' ');
        if (stem.Length == 3)
        {
            return stem.Equals("CON", StringComparison.OrdinalIgnoreCase) || stem.Equals("PRN", StringComparison.OrdinalIgnoreCase)
                || stem.Equals("AUX", StringComparison.OrdinalIgnoreCase) || stem.Equals("NUL", StringComparison.OrdinalIgnoreCase);
        }

        return stem.Length == 4 && char.IsAsciiDigit(stem[3])
            && (stem[..3].Equals("COM", StringComparison.OrdinalIgnoreCase) || stem[..3].Equals("LPT", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Six hex digits of the SHA-256 of the id (lower case): stable across runs and machines.</summary>
    private static string IdSuffix(string printerId)
    {
        Span<byte> hash = stackalloc byte[SHA256.HashSizeInBytes];
        SHA256.HashData(Encoding.UTF8.GetBytes(printerId.ToUpperInvariant()), hash);
        return Convert.ToHexStringLower(hash[..3]);
    }
}
