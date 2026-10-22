using System.Text;

using PrusaTrayDiagnostic.App.Logging;
using PrusaTrayDiagnostic.App.Support;

namespace PrusaTrayDiagnostic.App.Tests.Support;

/// <summary>The last log lines for the clipboard (T-049) and the 64 KB limit of the whole text.</summary>
public sealed class LogTailTests : IDisposable
{
    private readonly string _folder = TestDirectories.NewDirectoryPath();

    public LogTailTests() => Directory.CreateDirectory(_folder);

    public void Dispose() => TestDirectories.Delete(_folder);

    [Fact]
    public void LastTwoHundredLinesOfTheCurrentFile()
    {
        WriteLines(0, Enumerable.Range(1, 250).Select(static i => $"line {i}"));

        IReadOnlyList<string> lines = LogTail.Read(_folder);

        Assert.Equal(Enumerable.Range(51, 200).Select(static i => $"line {i}"), lines);
    }

    [Fact]
    public void ShortCurrentFileAfterARotationIsCompletedFromThePreviousOne()
    {
        WriteLines(1, Enumerable.Range(1, 300).Select(static i => $"old {i}"));
        WriteLines(0, Enumerable.Range(1, 50).Select(static i => $"new {i}"));

        IReadOnlyList<string> lines = LogTail.Read(_folder);

        Assert.Equal(
            [.. Enumerable.Range(151, 150).Select(static i => $"old {i}"), .. Enumerable.Range(1, 50).Select(static i => $"new {i}")],
            lines);
    }

    [Fact]
    public void LongLinesReadOnlyTheEndOfALargeFileAndNoOlderFile()
    {
        WriteLines(1, ["older file"]);
        // 100 lines of 1 kB: only the last 64 kB are read, the first (cut) line of that range is left out.
        WriteLines(0, Enumerable.Range(1, 100).Select(static i => $"{i:000} " + new string('x', 1019)));

        IReadOnlyList<string> lines = LogTail.Read(_folder);

        Assert.InRange(lines.Count, 60, 64);
        Assert.StartsWith("100 ", lines[^1], StringComparison.Ordinal);
        Assert.All(lines, static line => Assert.Equal(1023, line.Length));
        Assert.DoesNotContain("older file", lines);
    }

    [Fact]
    public void FileThatARotationIsMovingIsNotSkipped()
    {
        // The current file was just renamed and the new one not created yet.
        WriteLines(1, ["old"]);
        File.WriteAllText(Path.Combine(_folder, LogFile.RotatingFileName), "moving 1\nmoving 2\n");

        Assert.Equal(["old", "moving 1", "moving 2"], LogTail.Read(_folder));
    }

    [Fact]
    public void ReadRangeThatStartsRightAfterALineBreakKeepsItsFirstLine()
    {
        // 64 lines of exactly 1 kB (line break included): the read range starts exactly with line 2.
        WriteLines(0, Enumerable.Range(1, 65).Select(static i => $"{i:000} " + new string('z', 1019)));

        IReadOnlyList<string> lines = LogTail.Read(_folder);

        Assert.Equal(64, lines.Count);
        Assert.StartsWith("002 ", lines[0], StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void CurrentFileUpToTheReadLimitIsReadWholeAndCompletedFromTheOlderOne(int extraBytes)
    {
        WriteLines(1, ["older"]);
        // 64 lines of 1 kB = exactly the read limit (plus one more byte, which still reads the whole file).
        string content = string.Concat(Enumerable.Range(1, 64).Select(static i => $"{i:000} " + new string('w', 1019) + "\n"));
        File.WriteAllText(LogFile.PathOf(_folder, 0), new string('v', extraBytes) + content);

        IReadOnlyList<string> lines = LogTail.Read(_folder);

        Assert.Equal("older", lines[0]);
        Assert.Equal(65, lines.Count);
        Assert.StartsWith(new string('v', extraBytes) + "001 ", lines[1], StringComparison.Ordinal);
    }

    [Fact]
    public void CurrentRotatingAndOlderFileComeInOrder()
    {
        WriteLines(1, ["oldest"]);
        File.WriteAllText(Path.Combine(_folder, LogFile.RotatingFileName), "moving\n");
        WriteLines(0, ["newest"]);

        Assert.Equal(["oldest", "moving", "newest"], LogTail.Read(_folder));
    }

    [Fact]
    public void LastRecordWithoutLineEndIsKept()
    {
        // A write cut off half-way; the next write of the app ends the line first.
        File.WriteAllText(LogFile.PathOf(_folder, 0), "whole\ncut off");

        Assert.Equal(["whole", "cut off"], LogTail.Read(_folder));
    }

    [Fact]
    public void FileOpenByTheWriterIsRead()
    {
        // The app's writer keeps the file shared for reading, renaming and deleting.
        using FileStream writer = new(LogFile.PathOf(_folder, 0), FileMode.Create, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
        writer.Write("first\r\nsecond\n"u8);
        writer.Flush();

        Assert.Equal(["first", "second"], LogTail.Read(_folder));
    }

    [Fact]
    public void NoLogGivesNoLines()
    {
        Assert.Empty(LogTail.Read(null));
        Assert.Empty(LogTail.Read(_folder));
        Assert.Empty(LogTail.Read(Path.Combine(_folder, "missing")));
        File.WriteAllText(LogFile.PathOf(_folder, 0), string.Empty);
        Assert.Empty(LogTail.Read(_folder));
    }

    [Fact]
    public void TextKeepsTheInfoAndTheNewestLinesWithinTheLimit()
    {
        string info = "Prusa Tray Diagnostic 1.2.3\n";
        string[] lines = [.. Enumerable.Range(1, 200).Select(static i => $"{i:000} " + new string('y', 496))];

        string text = SupportText.Compose(info, lines);

        Assert.True(Encoding.UTF8.GetByteCount(text) <= SupportText.DefaultMaxBytes, $"{Encoding.UTF8.GetByteCount(text)} bytes");
        Assert.StartsWith(info, text, StringComparison.Ordinal);
        Assert.EndsWith("200 " + new string('y', 496) + "\n", text, StringComparison.Ordinal);
        Assert.DoesNotContain("001 ", text, StringComparison.Ordinal);
        Assert.Contains("older left out to stay within 64 KB):\n", text, StringComparison.Ordinal);
    }

    [Fact]
    public void TextWithFewLinesHasThemAll()
    {
        Assert.Equal("info\n\nLast log lines (2):\na\nb\n", SupportText.Compose("info\n", ["a", "b"]));
        Assert.Equal("info\n\nLast log lines: none\n", SupportText.Compose("info\n", []));
    }

    private void WriteLines(int index, IEnumerable<string> lines) =>
        File.WriteAllText(LogFile.PathOf(_folder, index), string.Concat(lines.Select(static line => line + "\n")));
}
