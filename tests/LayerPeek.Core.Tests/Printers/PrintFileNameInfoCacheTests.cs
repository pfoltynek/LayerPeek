using LayerPeek.Core.Printers;

namespace LayerPeek.Core.Tests.Printers;

/// <summary>
/// The tests that observe the static one-entry cache of <see cref="PrintFileNameInfo.Parse"/>: any other test parsing
/// another name in between would replace the cached entry, so they run alone, after all parallel tests of the assembly
/// (T-085: more and more callers parse names, e.g. the print name widget).
/// </summary>
[CollectionDefinition(nameof(PrintFileNameCache), DisableParallelization = true)]
public sealed class PrintFileNameCache;

[Collection(nameof(PrintFileNameCache))]
public sealed class PrintFileNameInfoCacheTests
{
    private const string Name = "foldable_book_holder_v1_0.4n_0.2mm_PLA_COREONE_2h51m.bgcode";

    [Fact]
    public void RepeatedNameReusesLastResult()
    {
        PrintFileNameInfo first = PrintFileNameInfo.Parse(Name);
        PrintFileNameInfo second = PrintFileNameInfo.Parse(new string(Name.AsSpan()));

        Assert.Same(first, second);
    }

    [Fact]
    public void AnotherNameReplacesTheCachedResult()
    {
        PrintFileNameInfo first = PrintFileNameInfo.Parse(Name);
        PrintFileNameInfo other = PrintFileNameInfo.Parse("benchy.gcode");
        PrintFileNameInfo again = PrintFileNameInfo.Parse(Name);

        Assert.Equal("Benchy", other.Title);
        Assert.NotSame(first, again);
        Assert.Equal(first, again);
    }
}
