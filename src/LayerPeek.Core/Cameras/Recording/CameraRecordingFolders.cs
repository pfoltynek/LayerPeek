using System.Buffers;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

using LayerPeek.Core.Printers;

namespace LayerPeek.Core.Cameras.Recording;

/// <summary>
/// Where recordings go and how they are named (T-145): <c>&lt;root&gt;/&lt;printer folder&gt;/&lt;yyyy-MM-dd_HH-mm-ss&gt;[_&lt;print title&gt;].mp4</c>;
/// dashcam saves (T-148) and timelapses (T-160, <see cref="TimelapseFileName"/>) add a marker before the extension.
/// The root is the user's folder or <see cref="DefaultRoot"/>; every printer folder carries <see cref="MarkerFileName"/>,
/// and only files of marked folders that follow <see cref="IsRecordingFileName"/> or <see cref="IsSnapshotFileName"/> are
/// ever deleted by the rotation. Alarm snapshots of the print failure detection (T-153) are JPEG files next to the
/// recordings, named and rotated like them (<see cref="SnapshotFileName"/>). A JPEG export of a timelapse (T-165) is a
/// folder next to it (<see cref="JpegExportFolderName"/>) with numbered frames (<see cref="JpegExportFrameFileName"/>); the
/// rotation counts and deletes it as a whole.
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

    /// <summary>What ends the name of a timelapse before the extension (T-160).</summary>
    private const string TimelapseMarker = "_timelapse";

    /// <summary>What ends the name of the JPEG export folder of a timelapse (T-165), before a <c>-2</c>, <c>-3</c> … suffix.</summary>
    private const string JpegExportMarker = "_jpg";

    /// <summary>The name of a JPEG export folder while it is written: a dot before it, this after it (T-165).</summary>
    private const string UnfinishedSuffix = ".partial";

    /// <summary>Fewest digits of the number of an exported frame (<c>0001.jpg</c>).</summary>
    private const int MinFrameDigits = 4;

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
    /// The file name of a dashcam save (T-148) of an event at <paramref name="eventTime"/> (local time of the user): like
    /// <see cref="FileName"/> with <c>_dashcam-&lt;trigger&gt;</c> before the extension, e.g.
    /// <c>2026-11-04_14-05-09_Benchy_dashcam-error.mp4</c>. It follows <see cref="IsRecordingFileName"/>, so the rotation
    /// keeps it within the same limit as the recordings.
    /// </summary>
    public static string DashcamFileName(DateTimeOffset eventTime, string? printFileName, CameraDashcamTrigger trigger)
    {
        string name = FileName(eventTime, printFileName);
        string marker = trigger switch
        {
            CameraDashcamTrigger.Error => "error",
            CameraDashcamTrigger.Attention => "attention",
            CameraDashcamTrigger.Stopped => "stopped",
            CameraDashcamTrigger.FailureDetected => "failure",
            _ => "manual",
        };
        return string.Concat(name.AsSpan(0, name.Length - Extension.Length), "_dashcam-", marker, Extension);
    }

    /// <summary>
    /// The file name of a timelapse (T-160, user decision 2026-11-04) that starts at <paramref name="start"/> (local time of
    /// the user): like <see cref="FileName"/> with <see cref="TimelapseMarker"/> before the extension, e.g.
    /// <c>2026-11-04_14-05-09_Benchy_timelapse.mp4</c>. It follows <see cref="IsRecordingFileName"/>, so the rotation keeps it
    /// within the same limit as the recordings; <see cref="IsTimelapseFileName"/> tells it apart.
    /// </summary>
    public static string TimelapseFileName(DateTimeOffset start, string? printFileName)
    {
        string name = FileName(start, printFileName);
        return string.Concat(name.AsSpan(0, name.Length - Extension.Length), TimelapseMarker, Extension);
    }

    /// <summary>
    /// Whether <paramref name="fileName"/> is a timelapse (<see cref="TimelapseFileName"/>): a recording file name whose
    /// name ends with <see cref="TimelapseMarker"/>, also with the recorder's <c>-2</c>, <c>-3</c> … suffix after it (any
    /// case). A recording of a print titled "timelapse" without a time of its own cannot be told apart; such a file is
    /// listed as a timelapse.
    /// </summary>
    public static bool IsTimelapseFileName(string? fileName)
    {
        if (!IsRecordingFileName(fileName))
        {
            return false;
        }

        ReadOnlySpan<char> name = fileName.AsSpan(TimestampLength, fileName!.Length - TimestampLength - Extension.Length);
        int dash = name.LastIndexOf('-');
        if (dash >= 0 && dash < name.Length - 1 && !name[(dash + 1)..].ContainsAnyExceptInRange('0', '9'))
        {
            name = name[..dash];
        }

        return name.EndsWith(TimelapseMarker, StringComparison.OrdinalIgnoreCase);
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
    /// The folder of the JPEG export (T-165, user decision 2026-11-04) of the timelapse <paramref name="timelapseFileName"/>,
    /// next to it: its name without the extension and with <see cref="JpegExportMarker"/>, e.g.
    /// <c>2026-11-04_14-05-09_Benchy_timelapse_jpg</c>. It follows <see cref="IsJpegExportFolderName"/>, so the rotation keeps
    /// it within the same limit as the recordings.
    /// </summary>
    public static string JpegExportFolderName(string timelapseFileName)
    {
        ArgumentException.ThrowIfNullOrEmpty(timelapseFileName);
        return string.Concat(Path.GetFileNameWithoutExtension(timelapseFileName.AsSpan()), JpegExportMarker);
    }

    /// <summary>
    /// Whether <paramref name="folderName"/> is a JPEG export folder (<see cref="JpegExportFolderName"/>): a valid
    /// <c>yyyy-MM-dd_HH-mm-ss</c> timestamp at the start, then nothing, <c>_</c> or <c>-</c> and anything, ending with
    /// <see cref="JpegExportMarker"/>, also with a <c>-2</c>, <c>-3</c> … suffix after it (any case).
    /// </summary>
    public static bool IsJpegExportFolderName(string? folderName) => folderName is not null && IsJpegExportFolderName(folderName.AsSpan());

    /// <summary>
    /// The name an export folder has while it is written (T-165): <c>.&lt;name&gt;.partial</c>, hidden on macOS; it gets its
    /// final name only when every frame is written, so a cancelled or failed export never leaves a half-written folder.
    /// </summary>
    public static string UnfinishedJpegExportFolderName(string folderName)
    {
        ArgumentException.ThrowIfNullOrEmpty(folderName);
        return string.Concat(".", folderName, UnfinishedSuffix);
    }

    /// <summary>Whether <paramref name="folderName"/> is an export folder that is still written, or left by a crash (<see cref="UnfinishedJpegExportFolderName"/>).</summary>
    public static bool IsUnfinishedJpegExportFolderName(string? folderName) =>
        folderName is not null && folderName.Length > 1 + UnfinishedSuffix.Length && folderName[0] == '.'
        && folderName.EndsWith(UnfinishedSuffix, StringComparison.OrdinalIgnoreCase)
        && IsJpegExportFolderName(folderName.AsSpan(1, folderName.Length - 1 - UnfinishedSuffix.Length));

    /// <summary>
    /// The file name of frame <paramref name="number"/> (from 1) of a JPEG export of <paramref name="frames"/> frames:
    /// <c>0001.jpg</c>, with at least four digits and as many as the last frame needs, so that the names sort in order.
    /// </summary>
    public static string JpegExportFrameFileName(int number, int frames)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(number);
        int digits = Math.Max(MinFrameDigits, CountDigits(Math.Max(frames, number)));
        return string.Create(digits + SnapshotExtension.Length, (number, digits), static (span, state) =>
        {
            int value = state.number;
            for (int i = state.digits - 1; i >= 0; i--)
            {
                span[i] = (char)('0' + (value % 10));
                value /= 10;
            }

            SnapshotExtension.AsSpan().CopyTo(span[state.digits..]);
        });
    }

    /// <summary>Whether <paramref name="fileName"/> is a frame of a JPEG export (<see cref="JpegExportFrameFileName"/>): digits only, then <c>.jpg</c> (any case).</summary>
    public static bool IsJpegExportFrameFileName(string? fileName) =>
        fileName is not null && fileName.Length > SnapshotExtension.Length
        && fileName.EndsWith(SnapshotExtension, StringComparison.OrdinalIgnoreCase)
        && !fileName.AsSpan(0, fileName.Length - SnapshotExtension.Length).ContainsAnyExceptInRange('0', '9');

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

        return StartsWithTimestamp(fileName.AsSpan(0, fileName.Length - extension.Length));
    }

    /// <summary>A valid timestamp, then the end, <c>_</c> or <c>-</c>.</summary>
    private static bool StartsWithTimestamp(ReadOnlySpan<char> name)
    {
        if (name.Length < TimestampLength
            || !DateTime.TryParseExact(name[..TimestampLength], TimestampFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
        {
            return false;
        }

        return name.Length == TimestampLength || name[TimestampLength] is '_' or '-';
    }

    private static bool IsJpegExportFolderName(ReadOnlySpan<char> name)
    {
        // Without the -2, -3 … suffix of a second export of the same timelapse.
        int dash = name.LastIndexOf('-');
        if (dash >= 0 && dash < name.Length - 1 && !name[(dash + 1)..].ContainsAnyExceptInRange('0', '9')
            && name[..dash].EndsWith(JpegExportMarker, StringComparison.OrdinalIgnoreCase))
        {
            name = name[..dash];
        }

        return name.EndsWith(JpegExportMarker, StringComparison.OrdinalIgnoreCase) && StartsWithTimestamp(name[..^JpegExportMarker.Length]);
    }

    private static int CountDigits(int value)
    {
        int digits = 1;
        while (value >= 10)
        {
            value /= 10;
            digits++;
        }

        return digits;
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
