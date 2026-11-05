using System.Diagnostics;
using System.Globalization;

namespace LayerPeek.Detection.Tests;

/// <summary>
/// The local recordings of real prints in <c>~/LayerPeek-samples</c> (or <c>LAYERPEEK_SAMPLES</c>), never in the repository
/// (T-155, T-167): frames decoded with the FFmpeg command line tool (no decoder as a test dependency, T-153 question 7).
/// Tests that use them are skipped without the data or without FFmpeg.
/// </summary>
internal static class LocalSamples
{
    public static string Folder =>
        Environment.GetEnvironmentVariable("LAYERPEEK_SAMPLES") is { Length: > 0 } custom
            ? custom
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "LayerPeek-samples");

    public static DateTime TakenAt(string path) =>
        DateTime.ParseExact(Path.GetFileNameWithoutExtension(path)["frame-".Length..], "yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);

    /// <summary>The FFmpeg command line tool (as for the fixtures, Fixtures/coreone/README.md); skips the test without it.</summary>
    public static string FFmpeg()
    {
        string name = OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg";
        IEnumerable<string> folders = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Concat(["/opt/homebrew/bin", "/usr/local/bin"]);
        string? found = folders.Select(folder => Path.Combine(folder, name)).FirstOrDefault(File.Exists);
        Assert.SkipWhen(found is null, "The FFmpeg command line tool is not installed (needed to decode the local samples).");
        return found!;
    }

    /// <summary>A list file for FFmpeg's concat demuxer (in the temporary folder, deleted with the test run's leftovers).</summary>
    public static string ConcatList(IEnumerable<string> paths)
    {
        string list = Path.Combine(Path.GetTempPath(), $"layerpeek-series-{Guid.NewGuid():N}.txt");
        File.WriteAllLines(list, paths.Select(static p => $"file '{p.Replace("'", @"'\''", StringComparison.Ordinal)}'"));
        return list;
    }

    /// <summary>Decodes into raw 8-bit gray 320×180 frames like the fixtures (area average, Fixtures/coreone/README.md).</summary>
    public static byte[] Decode(string ffmpeg, string input, params string[] inputOptions)
    {
        bool isList = input.EndsWith(".txt", StringComparison.Ordinal);
        string[] filter = inputOptions.Length > 1 && inputOptions[0] == "-vf" ? ["-vf", inputOptions[1] + ",scale=320:180:flags=area"] : ["-vf", "scale=320:180:flags=area"];
        string[] before = isList ? inputOptions : [];
        ProcessStartInfo start = new(ffmpeg) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (string argument in (string[])["-v", "error", .. before, "-i", input, .. filter, "-pix_fmt", "gray", "-f", "rawvideo", "pipe:1"])
        {
            start.ArgumentList.Add(argument);
        }

        try
        {
            using Process process = Process.Start(start)!;
            Task<string> errors = process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
            using MemoryStream output = new();
            process.StandardOutput.BaseStream.CopyTo(output);
            process.WaitForExit();
            Assert.True(process.ExitCode == 0, $"FFmpeg failed: {errors.Result}");
            return output.ToArray();
        }
        finally
        {
            if (isList)
            {
                File.Delete(input);
            }
        }
    }

    /// <summary>
    /// Writes a measurement to the test output and, with <c>LAYERPEEK_DETECTION_REPORT=&lt;path&gt;</c>, appends it to that file
    /// (the output of a passing test is not shown by the runner; the task notes quote these lines).
    /// </summary>
    public static void Report(string line)
    {
        TestContext.Current.TestOutputHelper?.WriteLine(line);
        if (Environment.GetEnvironmentVariable("LAYERPEEK_DETECTION_REPORT") is { Length: > 0 } file)
        {
            lock (ReportLock)
            {
                File.AppendAllLines(file, [line]);
            }
        }
    }

    private static readonly Lock ReportLock = new();
}
