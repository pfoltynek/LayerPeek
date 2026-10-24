using System.Globalization;
using LayerPeek.Core.Printers;

namespace LayerPeek.Core.Tests.Printers;

public class PrintFileNameInfoTests
{
    /// <summary>
    /// Names produced by the PrusaSlicer profile templates
    /// (<c>{input_filename_base}_{nozzle}n_{layer_height}mm_{printing_filament_types}_{printer_model}_{print_time}.gcode</c>
    /// and its variants): file name, title, materials, layer height, nozzle, estimate ("d.hh:mm:ss" or null), extension.
    /// </summary>
    public static TheoryData<string, string, string[], double?, double?, string?, string> SlicerNames => new()
    {
        // Core One, the example from the task.
        { "foldable_book_holder_v1_0.4n_0.2mm_PLA_COREONE_2h51m.bgcode", "Foldable book holder v1", ["PLA"], 0.2, 0.4, "02:51:00", ".bgcode" },
        // MK4S, plain G-code, dashes in the base name.
        { "shape-box_0.4n_0.15mm_PETG_MK4S_45m.gcode", "Shape box", ["PETG"], 0.15, 0.4, "00:45:00", ".gcode" },
        // MINI with input shaper, base name with capitals stays as it is.
        { "3DBenchy_0.4n_0.2mm_PLA_MINIIS_1h6m.bgcode", "3DBenchy", ["PLA"], 0.2, 0.4, "01:06:00", ".bgcode" },
        // XL multi-material, days in the estimate.
        { "xl_tool_test_0.4n_0.2mm_PLA,PETG,PVA_XL5IS_1d2h3m.bgcode", "Xl tool test", ["PLA", "PETG", "PVA"], 0.2, 0.4, "1.02:03:00", ".bgcode" },
        // Diacritics; the printer model contains a dot.
        { "držák_na_kolo_0.6n_0.3mm_PETG_MK3.9S_3h0m.gcode", "Držák na kolo", ["PETG"], 0.3, 0.6, "03:00:00", ".gcode" },
        // User's own capitalised name.
        { "MyPart_Final_0.4n_0.2mm_ASA_COREONEL_42m.bgcode", "MyPart Final", ["ASA"], 0.2, 0.4, "00:42:00", ".bgcode" },
        // Core One INDX: the printer model contains an underscore; materials separated by a semicolon.
        { "tool_holder_0.4n_0.2mm_PLA;PETG_COREONE_INDX4T_5h12m.bgcode", "Tool holder", ["PLA", "PETG"], 0.2, 0.4, "05:12:00", ".bgcode" },
        // Hard-coded INDX model in the profile template.
        { "clip_0.25n_0.12mm_PLA_COREONEINDX_2h0m.bgcode", "Clip", ["PLA"], 0.12, 0.25, "02:00:00", ".bgcode" },
        // HT hotend profiles write the nozzle as "HT0.6" without the "n" suffix.
        { "gear_HT0.6_0.25mm_PC_COREONE_12h0m.bgcode", "Gear", ["PC"], 0.25, 0.6, "12:00:00", ".bgcode" },
        // Profiles without the nozzle segment (XL+, older templates).
        { "vase_0.2mm_PLA_XL+_1h30m.bgcode", "Vase", ["PLA"], 0.2, null, "01:30:00", ".bgcode" },
        // Decimal comma.
        { "vase_0,4n_0,2mm_PLA_MK4S_30m.gcode", "Vase", ["PLA"], 0.2, 0.4, "00:30:00", ".gcode" },
        // Seconds only (short prints) and minutes with seconds.
        { "tiny_0.4n_0.1mm_PLA_MK4S_42s.bgcode", "Tiny", ["PLA"], 0.1, 0.4, "00:00:42", ".bgcode" },
        { "tiny_0.4n_0.1mm_PLA_MK4S_42m42s.bgcode", "Tiny", ["PLA"], 0.1, 0.4, "00:42:42", ".bgcode" },
        // Extension in capitals.
        { "PART_0.4n_0.2mm_PETG_MK4_1h0m.BGCODE", "PART", ["PETG"], 0.2, 0.4, "01:00:00", ".bgcode" },
        // Path prefixes (PrusaLink "path", Windows style).
        { "/usb/projects/foldable_book_holder_v1_0.4n_0.2mm_PLA_COREONE_2h51m.bgcode", "Foldable book holder v1", ["PLA"], 0.2, 0.4, "02:51:00", ".bgcode" },
        { @"C:\prints\foldable_book_holder_v1_0.4n_0.2mm_PLA_COREONE_2h51m.bgcode", "Foldable book holder v1", ["PLA"], 0.2, 0.4, "02:51:00", ".bgcode" },
        // Only part of the template: no printer model and no time.
        { "part_0.4n_0.2mm_PLA.bgcode", "Part", ["PLA"], 0.2, 0.4, null, ".bgcode" },
        // No time, printer model present.
        { "part_0.4n_0.2mm_PLA_MK4S.bgcode", "Part", ["PLA"], 0.2, 0.4, null, ".bgcode" },
        // No printer model, time present.
        { "part_0.4n_0.2mm_PLA_2h51m.gcode", "Part", ["PLA"], 0.2, 0.4, "02:51:00", ".gcode" },
        // Custom filament types with digits and symbols; 8.3 extension of .gcode.
        { "bracket_0.4n_0.2mm_PA11,PLA-CF,PETG+_MK4S_2h51m.gco", "Bracket", ["PA11", "PLA-CF", "PETG+"], 0.2, 0.4, "02:51:00", ".gco" },
        // Emoji at the start stays; no capital letter is added in front of it.
        { "🚀_rocket_0.4n_0.2mm_PLA_MK4S_1h0m.bgcode", "🚀 rocket", ["PLA"], 0.2, 0.4, "01:00:00", ".bgcode" },
        // Template-like text inside the user's base name stays in the title.
        { "shelf_100mm_v2_0.4n_0.2mm_PLA_MK4S_1h0m.bgcode", "Shelf 100mm v2", ["PLA"], 0.2, 0.4, "01:00:00", ".bgcode" },
    };

    [Theory]
    [MemberData(nameof(SlicerNames))]
    public void SlicerTemplateIsParsed(
        string fileName,
        string title,
        string[] materials,
        double? layerHeightMm,
        double? nozzleDiameterMm,
        string? estimate,
        string extension)
    {
        PrintFileNameInfo info = PrintFileNameInfo.Parse(fileName);

        Assert.Equal(title, info.Title);
        Assert.Equal(materials, info.Materials);
        Assert.Equal(layerHeightMm, info.LayerHeightMm);
        Assert.Equal(nozzleDiameterMm, info.NozzleDiameterMm);
        Assert.Equal(estimate is null ? null : TimeSpan.Parse(estimate, CultureInfo.InvariantCulture), info.SlicerEstimate);
        Assert.Equal(extension, info.Extension);
        Assert.True(info.HasChips);
    }

    /// <summary>Names that do not follow the template: file name, title, extension.</summary>
    public static TheoryData<string?, string, string> UnknownNames => new()
    {
        { null, "", "" },
        { "", "", "" },
        { "   ", "", "" },
        { ".bgcode", "", ".bgcode" },
        { "/usb/", "", "" },
        { "benchy.gcode", "Benchy", ".gcode" },
        { "my_cool_part_v2.bgcode", "My cool part v2", ".bgcode" },
        { "MyPart Final.bgcode", "MyPart Final", ".bgcode" },
        // 8.3 short names keep their shape.
        { "BENCHY~1.BGC", "BENCHY~1", ".bgc" },
        { "FOLDAB~1.G", "FOLDAB~1", ".g" },
        // A time alone is not the template; it stays in the title.
        { "benchy_2h51m.gcode", "Benchy 2h51m", ".gcode" },
        // Lower-case "material" is not a filament type.
        { "part_0.2mm_test.gcode", "Part 0.2mm test", ".gcode" },
        // The template without a base name is not split.
        { "0.4n_0.2mm_PLA_MK4S_1h0m.bgcode", "0.4n 0.2mm PLA MK4S 1h0m", ".bgcode" },
        // Zero or malformed layer height.
        { "x_0.4n_0mm_PLA_MK4S_1h0m.bgcode", "x 0.4n 0mm PLA MK4S 1h0m", ".bgcode" },
        { "x_0.4n_1.2.3mm_PLA_MK4S_1h0m.bgcode", "x 0.4n 1.2.3mm PLA MK4S 1h0m", ".bgcode" },
        { "x_0.4n_99mm_PLA_MK4S_1h0m.bgcode", "x 0.4n 99mm PLA MK4S 1h0m", ".bgcode" },
        // Empty material list items.
        { "x_0.4n_0.2mm_PLA,,PETG_MK4S_1h0m.bgcode", "x 0.4n 0.2mm PLA,,PETG MK4S 1h0m", ".bgcode" },
        // Unknown extension is kept in the title.
        { "photo.jpg", "Photo.jpg", "" },
        // No extension at all; the dots belong to the segments.
        { "part_0.4n_0.2mm", "Part 0.4n 0.2mm", "" },
        // Separators only.
        { "__--__.gcode", "", ".gcode" },
        { "__weird--name__.gcode", "Weird name", ".gcode" },
        // Whitespace and control characters collapse to single spaces.
        { "  a\tb\u0000c  .gcode", "A b c", ".gcode" },
        // Lower-case diacritics get sentence case; capitals with diacritics stay.
        { "čtverec.gcode", "Čtverec", ".gcode" },
        { "ÉLAN.gcode", "ÉLAN", ".gcode" },
        // Lone surrogate does not break anything.
        { "\uD800abc.gcode", "\uD800abc", ".gcode" },
    };

    [Theory]
    [MemberData(nameof(UnknownNames))]
    public void UnknownShapeKeepsReadableTitleWithoutChips(string? fileName, string title, string extension)
    {
        PrintFileNameInfo info = PrintFileNameInfo.Parse(fileName);

        Assert.Equal(title, info.Title);
        Assert.Equal(extension, info.Extension);
        Assert.Empty(info.Materials);
        Assert.Null(info.LayerHeightMm);
        Assert.Null(info.NozzleDiameterMm);
        Assert.Null(info.SlicerEstimate);
        Assert.False(info.HasChips);
    }

    [Theory]
    [InlineData("foldable_book_holder_v1_0.4n_0.2mm_PLA_COREONE_2h51m.bgcode")]
    [InlineData("tool_holder_0.4n_0.2mm_PLA;PETG_COREONE_INDX4T_5h12m.bgcode")]
    [InlineData("part_0.4n_0.2mm_PLA_MK3.9SMMU3_1h0m.bgcode")]
    [InlineData("vase_0.2mm_PLA_XL+_1h30m.bgcode")]
    public void PrinterModelIsLeftOutOfTitleAndChips(string fileName)
    {
        PrintFileNameInfo info = PrintFileNameInfo.Parse(fileName);

        Assert.DoesNotContain("COREONE", info.Title, StringComparison.Ordinal);
        Assert.DoesNotContain("INDX", info.Title, StringComparison.Ordinal);
        Assert.DoesNotContain("MK3", info.Title, StringComparison.Ordinal);
        Assert.DoesNotContain("XL", info.Title, StringComparison.Ordinal);
        Assert.All(info.Materials, m => Assert.DoesNotMatch("COREONE|INDX|MK3|XL", m));
    }

    [Theory]
    [InlineData("tr-TR")]
    [InlineData("cs-CZ")]
    [InlineData("de-DE")]
    public void ParsingIsCultureInvariant(string culture)
    {
        CultureInfo original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);

            PrintFileNameInfo info = PrintFileNameInfo.Parse("insert_0.4n_0.2mm_PLA_MK4S_1h0m.gcode");

            Assert.Equal("Insert", info.Title);
            Assert.Equal(0.2, info.LayerHeightMm);
            Assert.Equal(0.4, info.NozzleDiameterMm);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    public void VeryLongNamesAreParsed()
    {
        string longBase = string.Concat(Enumerable.Repeat("very_long_name_", 2_000));

        PrintFileNameInfo info = PrintFileNameInfo.Parse(longBase + "end_0.4n_0.2mm_PLA_MK4S_1h0m.bgcode");

        Assert.StartsWith("Very long name very long name", info.Title, StringComparison.Ordinal);
        Assert.EndsWith("very long name end", info.Title, StringComparison.Ordinal);
        Assert.Equal(["PLA"], info.Materials);
        Assert.Equal(0.2, info.LayerHeightMm);
    }

    [Fact]
    public void VeryLongNameWithoutTemplateIsKept()
    {
        string name = new('a', 100_000);

        PrintFileNameInfo info = PrintFileNameInfo.Parse(name + ".gcode");

        Assert.Equal(100_000, info.Title.Length);
        Assert.Equal('A', info.Title[0]);
        Assert.False(info.HasChips);
    }

    [Fact]
    public void HugeTimeValuesAreNotTreatedAsEstimate()
    {
        PrintFileNameInfo info = PrintFileNameInfo.Parse("x_0.4n_0.2mm_PLA_MK4S_99999999999d.bgcode");

        Assert.Null(info.SlicerEstimate);
        Assert.Contains("99999999999d", info.Title, StringComparison.Ordinal);
    }

    [Fact]
    public void ParseNeverThrowsOnRandomInput()
    {
        const string alphabet = "_-.,;~/\\ 0123456789nmhdsHTPLAMK4SXCOREONEINDXabcéČ🚀\0\t\uD800";
        var random = new Random(78);
        for (int i = 0; i < 5_000; i++)
        {
            int length = random.Next(0, 60);
            char[] chars = new char[length];
            for (int c = 0; c < length; c++)
            {
                chars[c] = alphabet[random.Next(alphabet.Length)];
            }

            string input = new string(chars) + (random.Next(3) == 0 ? ".bgcode" : "");

            PrintFileNameInfo info = PrintFileNameInfo.Parse(input);

            Assert.NotNull(info.Title);
            Assert.NotNull(info.Extension);
            Assert.Equal(info.Title.Trim(), info.Title);
        }
    }

    [Fact]
    public void EqualNamesGiveEqualResults()
    {
        const string name = "tool_holder_0.4n_0.2mm_PLA;PETG_COREONE_INDX4T_5h12m.bgcode";

        PrintFileNameInfo first = PrintFileNameInfo.Parse(name);
        PrintFileNameInfo other = PrintFileNameInfo.Parse("benchy.gcode");
        PrintFileNameInfo second = PrintFileNameInfo.Parse(new string(name.AsSpan()));

        Assert.Equal(first, second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
        Assert.NotEqual(first, other);
        Assert.True(first == second);
        Assert.True(first != other);
    }

    [Fact]
    public void RepeatedNameReusesLastResult()
    {
        const string name = "foldable_book_holder_v1_0.4n_0.2mm_PLA_COREONE_2h51m.bgcode";

        PrintFileNameInfo first = PrintFileNameInfo.Parse(name);
        PrintFileNameInfo second = PrintFileNameInfo.Parse(new string(name.AsSpan()));

        Assert.Same(first, second);
    }

    [Fact]
    public void SameChipsWithDifferentMaterialSeparatorAreEqual()
    {
        PrintFileNameInfo comma = PrintFileNameInfo.Parse("x_0.4n_0.2mm_PLA,PETG_MK4S_1h0m.bgcode");
        PrintFileNameInfo semicolon = PrintFileNameInfo.Parse("x_0.4n_0.2mm_PLA;PETG_MK4S_1h0m.bgcode");

        Assert.Equal(comma, semicolon);
    }

    [Fact]
    public void EmptyHasNoTitleAndNoChips()
    {
        Assert.Equal("", PrintFileNameInfo.Empty.Title);
        Assert.Equal("", PrintFileNameInfo.Empty.Extension);
        Assert.False(PrintFileNameInfo.Empty.HasChips);
        Assert.Equal(PrintFileNameInfo.Empty, PrintFileNameInfo.Parse(null));
    }
}
