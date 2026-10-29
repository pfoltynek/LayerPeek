namespace LayerPeek.App.Tests.Cameras.Mini;

/// <summary>The system clock in UTC: shown finish times do not depend on the time zone of the machine (T-103).</summary>
internal sealed class UtcSystemClock : TimeProvider
{
    public static readonly UtcSystemClock Instance = new();

    public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
}
