namespace LayerPeek.Core.Previews;

/// <summary>
/// Which G-code preview (thumbnail embedded by the slicer) to fetch. The pixel size depends on the file, not on the
/// app; for PrusaLink see docs/api/prusalink.md, section "Náhled G-code".
/// </summary>
public enum PrintPreviewSize
{
    /// <summary>The icon: on Buddy firmware exactly 16 × 16 px PNG (stock Prusa profiles embed it only as QOI, then there is none).</summary>
    Small = 0,

    /// <summary>The thumbnail: on Buddy firmware the first PNG of at least 17 × 17 px in the file (stock Prusa profiles: 380 × 285).</summary>
    Large = 1,
}
