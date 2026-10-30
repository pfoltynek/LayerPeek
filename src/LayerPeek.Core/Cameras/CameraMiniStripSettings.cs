namespace LayerPeek.Core.Cameras;

/// <summary>
/// What the info strip below the image of the mini preview shows (T-104 design 1, T-110), part of
/// <see cref="CameraSettings"/>: seven independent items, all on by default (= the strip of T-103). The state dot or icon
/// and the state name are always shown, whatever is switched off. Persisted as <c>printers[*].camera.miniStrip</c> with only
/// the items that are off; kept while the camera is disabled and not forgotten with the remembered camera window state
/// (it is a setting, the expanded rows in <c>camera-windows.json</c> are remembered state).
/// </summary>
public sealed record CameraMiniStripSettings
{
    /// <summary>Everything on: the strip as it was before it could be configured (T-103).</summary>
    public static CameraMiniStripSettings Default { get; } = new();

    /// <summary>The 3 DIP bar on the top edge (job progress, cool-down progress); off: always the faint track.</summary>
    public bool ProgressBar { get; init; } = true;

    /// <summary>The job percentage in the summary row ("19 %").</summary>
    public bool Percent { get; init; } = true;

    /// <summary>The remaining time in the summary row ("2 h 35 min").</summary>
    public bool Remaining { get; init; } = true;

    /// <summary>The finish time after the flag ("⚑ 19:50").</summary>
    public bool Finish { get; init; } = true;

    /// <summary>The cool-down after the print (snowflake / power icon and its texts); off: the state name, as without cool-down tracking.</summary>
    public bool Cooldown { get; init; } = true;

    /// <summary>The print name row and its toggle button; off: the toggle disappears and the row is never shown.</summary>
    public bool NameRow { get; init; } = true;

    /// <summary>The temperature chart row and its toggle button; off: the toggle disappears and the row is never shown.</summary>
    public bool ChartRow { get; init; } = true;
}
