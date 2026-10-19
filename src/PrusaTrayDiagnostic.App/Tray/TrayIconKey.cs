namespace PrusaTrayDiagnostic.App.Tray;

/// <summary>
/// Identifies one rendered tray icon. Progress is quantized to <see cref="ProgressStep"/>, so the number of
/// distinct icons is small and icons can be cached; polls that do not change the key do not touch the icon.
/// </summary>
internal readonly record struct TrayIconKey
{
    /// <summary>Granularity of the progress shown in the icon, in percent (one step is about 2 px of the three layers at 16 px).</summary>
    public const int ProgressStep = 5;

    private TrayIconKey(TrayStatus status, int progress)
    {
        Status = status;
        Progress = progress;
    }

    public TrayStatus Status { get; }

    /// <summary>Progress rounded down to <see cref="ProgressStep"/> (0–100); always 0 unless <see cref="HasProgress"/>.</summary>
    public int Progress { get; }

    /// <param name="progressPercent">Job progress; ignored unless the status shows progress (printing, paused).</param>
    public static TrayIconKey For(TrayStatus status, double? progressPercent) =>
        new(status, ShowsProgress(status) ? Quantize(progressPercent) : 0);

    /// <summary>The icon shows progress in its layers (printing or paused).</summary>
    public bool HasProgress => ShowsProgress(Status);

    private static bool ShowsProgress(TrayStatus status) => status is TrayStatus.Printing or TrayStatus.Paused;

    private static int Quantize(double? progressPercent)
    {
        if (progressPercent is not { } value || double.IsNaN(value))
        {
            return 0;
        }

        double clamped = Math.Clamp(value, 0, 100);
        return (int)(clamped / ProgressStep) * ProgressStep;
    }
}
