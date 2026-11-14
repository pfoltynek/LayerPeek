using Avalonia.Media;
using Avalonia.Media.Fonts;

namespace LayerPeek.App.UiTests.Infrastructure;

/// <summary>
/// The macOS system font San Francisco of the Mac the tests run on (T-214). Read from <see cref="FilePath"/> at test
/// time only: Apple's licence allows neither committing nor shipping it, so it never leaves the machine (no snapshot is
/// ever rendered with it, see <see cref="SnapshotAssert"/>).
/// </summary>
internal static class SystemFont
{
    /// <summary>The variable San Francisco font of macOS (upright; the UI uses no italic).</summary>
    public const string FilePath = "/System/Library/Fonts/SFNS.ttf";

    /// <summary>
    /// Why the system font tests cannot run here, or <see langword="null"/> when they can: on Windows, Linux and a macOS
    /// without the file every test is skipped with this reason instead of failing.
    /// </summary>
    public static string? MissingReason { get; } =
        !OperatingSystem.IsMacOS() ? "The San Francisco layout tests run on macOS only (the font is part of macOS and is never committed)."
        : !File.Exists(FilePath) ? $"The San Francisco layout tests need {FilePath}, which this macOS does not have."
        : null;
}

/// <summary>Font collection with the single file <see cref="SystemFont.FilePath"/>, loaded when the app starts.</summary>
internal sealed class SystemFontCollection : FontCollectionBase
{
    public const string FamilyName = "San Francisco";

    public static readonly Uri CollectionKey = new("fonts:SystemFont");

    public override Uri Key => CollectionKey;

    public void Load()
    {
        using FileStream stream = File.OpenRead(SystemFont.FilePath);
        // The file names its family in 30 languages; the tests use one fixed name instead of whichever Avalonia picks.
        if (!TryAddGlyphTypeface(stream, out GlyphTypeface? typeface)
            || !TryAddGlyphTypeface(FamilyName, new FontCollectionKey(typeface.Style, typeface.Weight, typeface.Stretch), typeface))
        {
            throw new InvalidOperationException($"{SystemFont.FilePath} could not be loaded as a font.");
        }
    }
}
