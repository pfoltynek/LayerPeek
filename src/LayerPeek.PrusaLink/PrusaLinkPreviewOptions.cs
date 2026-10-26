namespace LayerPeek.PrusaLink;

/// <summary>Limits of <see cref="PrusaLinkPreviewFetcher"/>.</summary>
public sealed record PrusaLinkPreviewOptions
{
    public static PrusaLinkPreviewOptions Default { get; } = new();

    /// <summary>
    /// Largest accepted PNG; a longer answer is abandoned while streaming, so memory stays bounded whatever answers.
    /// Previews of stock Prusa profiles have tens of kB. At least 1 kB.
    /// </summary>
    public int MaxBytes
    {
        get;
        init
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, 1024);
            field = value;
        }
    } = 512 * 1024;

    /// <summary>Time for the whole download (login round trip, headers and body). Positive.</summary>
    public TimeSpan Timeout
    {
        get;
        init
        {
            ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(value, TimeSpan.Zero);
            field = value;
        }
    } = TimeSpan.FromSeconds(10);
}
