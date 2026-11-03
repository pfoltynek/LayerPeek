using LayerPeek.Core.Cameras.Recording;
using LayerPeek.Core.Printers;

namespace LayerPeek.Core.Tests.Cameras.Recording;

/// <summary>T-145: folder and file names of the recordings are safe on Windows and macOS, readable, sortable and unique.</summary>
public sealed class CameraRecordingFoldersTests
{
    private static readonly DateTimeOffset Start = new(2026, 11, 3, 14, 5, 9, TimeSpan.FromHours(1));

    private static PrinterDefinition Printer(string id, string name) => new()
    {
        Id = id,
        Name = name,
        ConnectionType = ConnectionType.PrusaLink,
        Address = "192.168.0.10",
    };

    [Fact]
    public void TheDefaultRootIsLayerPeekInTheVideoFolder()
    {
        string root = CameraRecordingFolders.DefaultRoot();

        Assert.Equal("LayerPeek", Path.GetFileName(root));
        Assert.True(Path.IsPathFullyQualified(root));
        if (OperatingSystem.IsMacOS())
        {
            Assert.Equal(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Movies", "LayerPeek"), root);
        }
        else if (OperatingSystem.IsWindows())
        {
            Assert.Equal(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "LayerPeek"), root);
        }
    }

    [Theory]
    [InlineData("Workshop MK4", "Workshop MK4")]
    [InlineData("  Core   One\t(left) ", "Core One (left)")]
    [InlineData("MK4: 1/2 <a>|\"b\"?*\\", "MK4_ 1_2 _a___b____")]
    [InlineData("Dots...", "Dots")]
    [InlineData(".hidden", "hidden")]
    [InlineData("CON", "_CON")]
    [InlineData("lpt1.printer", "_lpt1.printer")]
    [InlineData("COM10", "COM10")]
    [InlineData("...", "Printer")]
    [InlineData("Tiskárna Průša", "Tiskárna Průša")]
    public void PrinterFolderNamesAreSafe(string name, string expected)
    {
        IReadOnlyDictionary<string, string> names = CameraRecordingFolders.PrinterFolderNames([Printer("p", name)]);

        Assert.Equal(expected, names["P"]);
    }

    [Fact]
    public void ALongNameIsShortened()
    {
        string name = CameraRecordingFolders.PrinterFolderNames([Printer("p", new string('x', 300))])["p"];

        Assert.Equal(CameraRecordingFolders.MaxFolderNameLength, name.Length);
    }

    [Fact]
    public void ACutNeverSplitsASurrogatePair()
    {
        string name = CameraRecordingFolders.SafeName(new string('x', 59) + "😀😀", 60, "Printer");

        Assert.Equal(new string('x', 59), name);
    }

    [Fact]
    public void PrintersWhoseNamesGiveTheSameFolderGetStableSuffixes()
    {
        IReadOnlyDictionary<string, string> names = CameraRecordingFolders.PrinterFolderNames(
            [Printer("a", "MK4"), Printer("b", "mk4"), Printer("c", "XL")]);
        IReadOnlyDictionary<string, string> reordered = CameraRecordingFolders.PrinterFolderNames(
            [Printer("c", "XL"), Printer("b", "mk4"), Printer("a", "MK4")]);

        Assert.Equal("XL", names["c"]);
        Assert.Matches("^MK4 \\([0-9a-f]{6}\\)$", names["a"]);
        Assert.Matches("^mk4 \\([0-9a-f]{6}\\)$", names["b"]);
        Assert.NotEqual(names["a"].ToUpperInvariant(), names["b"].ToUpperInvariant());
        Assert.Equal(names["a"], reordered["a"]);
        Assert.Equal(names["b"], reordered["b"]);
    }

    [Fact]
    public void FileNamesStartWithTheLocalStartTimeAndCarryThePrintTitle()
    {
        Assert.Equal("2026-11-03_14-05-09_Benchy.mp4", CameraRecordingFolders.FileName(Start, "benchy_0.4n_0.2mm_PLA_MK4S_1h2m.bgcode"));
        Assert.Equal("2026-11-03_14-05-09.mp4", CameraRecordingFolders.FileName(Start, null));
        Assert.Equal("2026-11-03_14-05-09.mp4", CameraRecordingFolders.FileName(Start, ".bgcode"));
        Assert.Equal("2026-11-03_14-05-09_A_b c.mp4", CameraRecordingFolders.FileName(Start, "a:b c.gcode"));
    }

    [Fact]
    public void ALongPrintTitleIsShortened()
    {
        string name = CameraRecordingFolders.FileName(Start, new string('y', 200) + ".gcode");

        Assert.Equal(19 + 1 + CameraRecordingFolders.MaxTitleLength + 4, name.Length);
        Assert.True(CameraRecordingFolders.IsRecordingFileName(name));
    }

    [Theory]
    [InlineData("2026-11-03_14-05-09.mp4", true)]
    [InlineData("2026-11-03_14-05-09_Benchy.mp4", true)]
    [InlineData("2026-11-03_14-05-09_Benchy-2.MP4", true)]
    [InlineData("2026-11-03_14-05-09-3.mp4", true)]
    [InlineData("2026-11-03_14-05-09x.mp4", false)]
    [InlineData("2026-02-30_14-05-09.mp4", false)]
    [InlineData("2026-11-03 14-05-09.mp4", false)]
    [InlineData("2026-11-03_14-05-09.mkv", false)]
    [InlineData("2026-11-03_14-05.mp4", false)]
    [InlineData("holiday.mp4", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void RecordingFileNamesAreRecognized(string? fileName, bool expected) =>
        Assert.Equal(expected, CameraRecordingFolders.IsRecordingFileName(fileName));

    [Fact]
    public void TheirOwnNamesAreRecognized()
    {
        Assert.True(CameraRecordingFolders.IsRecordingFileName(CameraRecordingFolders.FileName(Start, "x.gcode")));
        Assert.True(CameraRecordingFolders.IsRecordingFileName(CameraRecordingFolders.FileName(Start, null)));
    }
}
