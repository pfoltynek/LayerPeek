namespace LayerPeek.Tests.Detection;

/// <summary>
/// The raw grayscale fixtures of tests/LayerPeek.Detection.Tests/Fixtures/coreone (see its README.md), copied into
/// <c>Fixtures/coreone</c> of every test project that links this file.
/// </summary>
internal static class CoreOneFixtures
{
    public static string Folder { get; } = Path.Combine(AppContext.BaseDirectory, "Fixtures", "coreone");

    /// <summary>Course of the print first, then the six consecutive key frames of the clip.</summary>
    public static IReadOnlyList<string> Names { get; } =
    [
        "series-20261103-181335",
        "series-20261103-183335",
        "series-20261103-191337",
        "series-20261103-200006",
        "clip-1905-key01",
        "clip-1905-key02",
        "clip-1905-key03",
        "clip-1905-key04",
        "clip-1905-key05",
        "clip-1905-key06",
    ];

    public static byte[] Load(string name) => File.ReadAllBytes(Path.Combine(Folder, name + ".gray"));
}
