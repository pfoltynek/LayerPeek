namespace LayerPeek.Core.Previews;

/// <summary>Settings of <see cref="PrintPreviewCache"/>.</summary>
public sealed record PrintPreviewCacheOptions
{
    public static PrintPreviewCacheOptions Default { get; } = new();

    /// <summary>
    /// How long a transient failure (<see cref="PrintPreviewFetchStatus.Failed"/>) is remembered before the next request
    /// may try again, so that an offline printer is not asked on every poll. Positive.
    /// </summary>
    public TimeSpan RetryDelay
    {
        get;
        init
        {
            ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(value, TimeSpan.Zero);
            field = value;
        }
    } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// How many printers keep a preview at most; beyond that the least recently requested one is dropped. Each printer
    /// holds one job with at most one preview per <see cref="PrintPreviewSize"/>. At least 1.
    /// </summary>
    public int MaxPrinters
    {
        get;
        init
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, 1);
            field = value;
        }
    } = 32;
}
