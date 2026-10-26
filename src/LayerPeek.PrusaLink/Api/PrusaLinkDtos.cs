using System.Text.Json.Serialization;

namespace LayerPeek.PrusaLink.Api;

// Shapes of the PrusaLink v1 API as sent by Buddy firmware 6.x (see docs/api/prusalink.md).
// Everything is optional: firmware versions differ and unknown fields are ignored.
// Integer-like values are parsed as double so that a firmware sending "100.0" cannot break the whole response.

internal sealed class StatusResponse
{
    public StatusJob? Job { get; set; }

    public StatusPrinter? Printer { get; set; }
}

internal sealed class StatusJob
{
    public long? Id { get; set; }

    /// <summary>Percent, 0–100.</summary>
    public double? Progress { get; set; }

    /// <summary>Seconds; omitted by the firmware while unknown.</summary>
    public double? TimeRemaining { get; set; }

    /// <summary>Seconds.</summary>
    public double? TimePrinting { get; set; }
}

internal sealed class StatusPrinter
{
    public string? State { get; set; }

    public double? TempNozzle { get; set; }

    public double? TargetNozzle { get; set; }

    public double? TempBed { get; set; }

    public double? TargetBed { get; set; }

    public double? AxisZ { get; set; }

    public double? Flow { get; set; }

    public double? Speed { get; set; }

    public double? FanHotend { get; set; }

    public double? FanPrint { get; set; }
}

internal sealed class JobResponse
{
    public long? Id { get; set; }

    public JobFile? File { get; set; }
}

internal sealed class JobFile
{
    /// <summary>Short (8.3) name.</summary>
    public string? Name { get; set; }

    /// <summary>Long file name as shown to the user.</summary>
    public string? DisplayName { get; set; }

    public JobFileRefs? Refs { get; set; }
}

/// <summary>Links of the job file; Buddy sends absolute paths (<c>/thumb/s/usb/BENCHY~1.BGC</c>), JSON-escaped but not URL-escaped.</summary>
internal sealed class JobFileRefs
{
    /// <summary><c>/thumb/s</c> + short (8.3) path: the 16 × 16 px PNG preview.</summary>
    public string? Icon { get; set; }

    /// <summary><c>/thumb/l</c> + short (8.3) path: the first PNG preview of at least 17 × 17 px.</summary>
    public string? Thumbnail { get; set; }

    public string? Download { get; set; }
}

internal sealed class VersionResponse
{
    public string? Api { get; set; }

    public string? Hostname { get; set; }

    /// <summary>Since firmware 6.5.</summary>
    public string? Firmware { get; set; }

    /// <summary>Printer model code "type.version.subversion"; since firmware 6.5.</summary>
    public string? Printer { get; set; }

    public double? NozzleDiameter { get; set; }
}

internal sealed class InfoResponse
{
    public bool? Mmu { get; set; }

    public string? Hostname { get; set; }

    public double? NozzleDiameter { get; set; }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]
[JsonSerializable(typeof(StatusResponse))]
[JsonSerializable(typeof(JobResponse))]
[JsonSerializable(typeof(VersionResponse))]
[JsonSerializable(typeof(InfoResponse))]
internal sealed partial class PrusaLinkJsonContext : JsonSerializerContext;
