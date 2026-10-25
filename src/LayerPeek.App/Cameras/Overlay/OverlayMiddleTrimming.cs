using Avalonia.Media;
using Avalonia.Media.TextFormatting;

namespace LayerPeek.App.Cameras.Overlay;

/// <summary>
/// Shortens a line that does not fit <b>in the middle</b> (T-076 1.4, the T-036 rule "start + … + end" applied to the
/// width): the first <see cref="PrefixLength"/> characters, "…" and as much of the end as fits. Names keep both their start
/// and their end ("Foldable book h…holder v2"), where versions and variants usually are. A prefix wider than the line is cut
/// with the "…" at the end.
/// </summary>
public sealed class OverlayMiddleTrimming : TextTrimming
{
    /// <summary>Characters kept at the start: about half of the narrowest title line (M, 212 DIP at 14 semi-bold).</summary>
    public const int DefaultPrefixLength = 14;

    private const string Ellipsis = "…";

    public OverlayMiddleTrimming(int prefixLength = DefaultPrefixLength)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(prefixLength);
        PrefixLength = prefixLength;
    }

    /// <summary>The shared instance for XAML (<c>{x:Static ov:OverlayMiddleTrimming.Middle}</c>).</summary>
    public static OverlayMiddleTrimming Middle { get; } = new();

    public int PrefixLength { get; }

    public override TextCollapsingProperties CreateCollapsingProperties(TextCollapsingCreateInfo createInfo) =>
        new TextLeadingPrefixCharacterEllipsis(Ellipsis, PrefixLength, createInfo.Width, createInfo.TextRunProperties, createInfo.FlowDirection);

    public override string ToString() => nameof(OverlayMiddleTrimming);
}
