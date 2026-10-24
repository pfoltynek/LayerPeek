using System.Globalization;
using System.IO.Compression;
using System.Text;

namespace LayerPeek.App.Support;

/// <summary>
/// The diagnostic bundle (T-048): a ZIP with every file of the log folder (<c>logs/</c>, unchanged), <c>info.txt</c>
/// (<see cref="SupportInfo"/>) and <c>settings.json</c> (rebuilt and redacted, never the raw file). Nothing else goes in:
/// no secrets, no <c>camera-windows.json</c>, no printer images.
/// </summary>
internal static class SupportBundle
{
    public const string InfoEntryName = "info.txt";

    public const string SettingsEntryName = "settings.json";

    public const string LogsFolderName = "logs";

    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>Always English, like its content: <c>LayerPeek-diagnostics-20261022-140312.zip</c> (decided 2026-10-22).</summary>
    public static string DefaultFileName(DateTimeOffset now) =>
        string.Create(CultureInfo.InvariantCulture, $"LayerPeek-diagnostics-{now:yyyyMMdd-HHmmss}.zip");

    /// <summary>
    /// Writes the bundle into <paramref name="destination"/> (left open). Log files are read with full sharing, because the
    /// app keeps writing them; a file that disappears meanwhile (rotated, deleted) is left out, one that cannot be read
    /// (or a folder that cannot be listed) is named at the end of <c>info.txt</c>. Blocking: call off the UI thread.
    /// </summary>
    /// <param name="logDirectory">The log folder, or <see langword="null"/> when the app has none.</param>
    /// <exception cref="IOException">The bundle cannot be written.</exception>
    public static void Write(Stream destination, string info, string settingsJson, string? logDirectory, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(info);
        ArgumentNullException.ThrowIfNull(settingsJson);
        using ZipArchive zip = new(destination, ZipArchiveMode.Create, leaveOpen: true, Utf8);
        List<string> unreadable = [];
        foreach (string file in LogFiles(logDirectory, unreadable))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!TryAddLog(zip, file))
            {
                unreadable.Add(Path.GetFileName(file));
            }
        }

        StringBuilder infoText = new(info);
        foreach (string name in unreadable)
        {
            infoText.Append("Log file not readable: ").Append(name).Append('\n');
        }

        AddText(zip, InfoEntryName, infoText.ToString());
        AddText(zip, SettingsEntryName, settingsJson);
    }

    /// <summary>
    /// The files of the log folder in name order; none when it does not exist. A folder that cannot be listed is noted in
    /// <paramref name="unreadable"/>.
    /// </summary>
    private static IEnumerable<string> LogFiles(string? logDirectory, List<string> unreadable)
    {
        if (logDirectory is null)
        {
            return [];
        }

        try
        {
            return Directory.GetFiles(logDirectory).Order(StringComparer.OrdinalIgnoreCase);
        }
        catch (DirectoryNotFoundException)
        {
            return [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            unreadable.Add(LogsFolderName + "/");
            return [];
        }
    }

    /// <summary>
    /// Reads the whole file first (a log is at most a little over 1 MB), so that a read error leaves no half entry in the
    /// ZIP; <see langword="false"/> when it cannot be read.
    /// </summary>
    private static bool TryAddLog(ZipArchive zip, string file)
    {
        byte[] content;
        try
        {
            using FileStream source = new(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using MemoryStream copy = new();
            source.CopyTo(copy);
            content = copy.ToArray();
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            // Rotated away or deleted since the folder was listed.
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }

        ZipArchiveEntry entry = zip.CreateEntry($"{LogsFolderName}/{Path.GetFileName(file)}", CompressionLevel.Optimal);
        using Stream target = entry.Open();
        target.Write(content);
        return true;
    }

    private static void AddText(ZipArchive zip, string name, string text)
    {
        ZipArchiveEntry entry = zip.CreateEntry(name, CompressionLevel.Optimal);
        using Stream target = entry.Open();
        target.Write(Utf8.GetBytes(text));
    }
}
