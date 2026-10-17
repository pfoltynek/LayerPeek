namespace PrusaTrayDiagnostic.Core.History;

public sealed record TelemetryHistoryOptions
{
    public static readonly TimeSpan MinRetention = TimeSpan.FromSeconds(1);

    /// <summary>Upper bound of the retention; keeps the worst-case memory per printer at ~4 MB (see <see cref="TelemetryHistory"/>).</summary>
    public static readonly TimeSpan MaxRetention = TimeSpan.FromDays(1);

    public static TelemetryHistoryOptions Default { get; } = new();

    /// <summary>How long samples are kept per printer (1 s – 1 day, default 1 day).</summary>
    public TimeSpan Retention
    {
        get;
        init
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, MinRetention);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(value, MaxRetention);
            field = value;
        }
    } = MaxRetention;
}
