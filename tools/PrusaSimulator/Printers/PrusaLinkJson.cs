using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace LayerPeek.Simulator.Printers;

/// <summary>
/// Response bodies in the shape and number format of Buddy firmware 6.x (field order and decimals as in the recorded
/// fixtures, tests/LayerPeek.PrusaLink.Tests/Fixtures/firmware): progress with two decimals, temperatures with
/// one, fans, flow and speed as integers. Written by hand so that the format is exactly the firmware's.
/// </summary>
public static class PrusaLinkJson
{
    public static string Version(SimulatedModel model, string hostname) =>
        Invariant($"{{\"api\":\"2.0.0\",\"server\":\"2.1.2\",\"nozzle_diameter\":0.40,\"text\":\"PrusaLink\",\"hostname\":{Text(hostname)},\"firmware\":{Text(SimulatedModel.Firmware)},\"printer\":{Text(model.Code)},\"capabilities\":{{\"upload-by-put\":true}}}}");

    public static string Info(string hostname, string serial) =>
        Invariant($"{{\"nozzle_diameter\":0.40,\"mmu\":false,\"serial\":{Text(serial)},\"hostname\":{Text(hostname)},\"min_extrusion_temp\":170}}");

    public static string Status(SimulatedStatus status)
    {
        StringBuilder json = new(512);
        json.Append('{');
        if (status.Job is { } job)
        {
            json.Append(Invariant($"\"job\":{{\"id\":{job.Id},\"progress\":{job.Progress:0.00},"));
            if (job.TimeRemaining is { } remaining)
            {
                json.Append(Invariant($"\"time_remaining\":{remaining},"));
            }

            json.Append(Invariant($"\"time_printing\":{job.TimePrinting}}},"));
        }

        string xy = status.AxisXY is { } p ? Invariant($"\"axis_x\":{p.X:0.0},\"axis_y\":{p.Y:0.0},") : "";
        json.Append("\"storage\":{\"path\":\"/usb/\",\"name\":\"usb\",\"read_only\":false},");
        json.Append(Invariant(
            $"\"printer\":{{\"state\":{Text(status.State)},\"temp_bed\":{status.TempBed:0.0},\"target_bed\":{status.TargetBed:0.0},\"temp_nozzle\":{status.TempNozzle:0.0},\"target_nozzle\":{status.TargetNozzle:0.0},\"axis_z\":{status.AxisZ:0.0#},{xy}\"flow\":{status.Flow},\"speed\":{status.Speed},\"fan_hotend\":{status.FanHotend},\"fan_print\":{status.FanPrint}}}"));
        json.Append('}');
        return json.ToString();
    }

    /// <summary><c>/api/v1/job</c> of a running job (the firmware answers 204 without one).</summary>
    public static string Job(SimulatedJob job, string state)
    {
        SimulatedFile file = job.File;
        string remaining = job.TimeRemaining is { } r ? Invariant($"\"time_remaining\":{r},") : "";
        return Invariant(
            $"{{\"id\":{job.Id},\"state\":{Text(state)},\"progress\":{job.Progress:0.00},{remaining}\"time_printing\":{job.TimePrinting},\"file\":{{\"refs\":{{\"icon\":{Text("/thumb/s/usb/" + file.Name)},\"thumbnail\":{Text("/thumb/l/usb/" + file.Name)},\"download\":{Text("/usb/" + file.Name)}}},\"name\":{Text(file.Name)},\"display_name\":{Text(file.DisplayName)},\"path\":\"/usb\",\"size\":{file.Size},\"m_timestamp\":1760500000}}}}");
    }

    /// <summary>Error body of the firmware, e.g. <c>{"title": "401: Unauthorized","message":""}</c>.</summary>
    public static string Error(string title) => Invariant($"{{\"title\": {Text(title)},\"message\":\"\"}}");

    private static string Invariant(FormattableString value) => value.ToString(CultureInfo.InvariantCulture);

    private static string Text(string value) => "\"" + JsonEncodedText.Encode(value, JavaScriptEncoder.UnsafeRelaxedJsonEscaping).Value + "\"";
}
