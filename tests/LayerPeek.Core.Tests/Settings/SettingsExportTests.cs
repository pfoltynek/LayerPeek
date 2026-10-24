using System.Text.Json;

using LayerPeek.Core.Cameras;
using LayerPeek.Core.Printers;
using LayerPeek.Core.Settings;

namespace LayerPeek.Core.Tests.Settings;

/// <summary>The settings of a diagnostic bundle (T-048): rebuilt from the settings, camera addresses without query.</summary>
public sealed class SettingsExportTests
{
    [Fact]
    public void CameraAddressLosesQueryAndFragmentWhilePrinterDataStays()
    {
        AppSettings settings = With(new CameraSettings(enabled: true, new Uri("rtsp://192.168.0.50:554/live?token=Tk9secret&x=1#frag")));

        string json = SettingsExport.ToSupportJson(settings);

        Assert.Contains("\"streamUrl\": \"rtsp://192.168.0.50:554/live\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("Tk9secret", json, StringComparison.Ordinal);
        Assert.DoesNotContain("frag", json, StringComparison.Ordinal);
        Assert.Contains("\"name\": \"Workshop MK4\"", json, StringComparison.Ordinal);
        Assert.Contains("\"address\": \"10.0.0.5\"", json, StringComparison.Ordinal);
        Assert.Contains("\"userName\": \"workshop\"", json, StringComparison.Ordinal);
        Assert.Contains("\"debugLogging\": true", json, StringComparison.Ordinal);
        // Valid JSON of the usual shape.
        using JsonDocument document = JsonDocument.Parse(json);
        Assert.Equal(2, document.RootElement.GetProperty("version").GetInt32());
    }

    [Fact]
    public void InvalidAddressKeptForCorrectionIsNotHandedOut()
    {
        CameraSettings camera = CameraSettings.FromInvalidStreamUrl("http://cam.local/live?token=Tk9secret");

        string json = SettingsExport.ToSupportJson(With(camera));

        Assert.DoesNotContain("Tk9secret", json, StringComparison.Ordinal);
        Assert.DoesNotContain("cam.local", json, StringComparison.Ordinal);
        Assert.Contains("(invalid address hidden: ", json, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("192.168.0.226", "192.168.0.226")]
    [InlineData("printer.local", "printer.local")]
    [InlineData("https://printer.local:8443/", "https://printer.local:8443/")]
    // Never stored by the settings editor, but a hand-edited file might hold it.
    [InlineData("http://admin:Pw7Secret@printer.local/", "http://***@printer.local/")]
    [InlineData("http://printer.local/?key=Ak3Secret", "http://printer.local/?***")]
    public void PrinterAddressStaysUnlessItCarriesASecret(string address, string expected) =>
        Assert.Equal(expected, SettingsExport.SupportAddress(address));

    [Fact]
    public void NoCameraAddressStaysEmpty() =>
        Assert.Null(SettingsExport.SupportStreamUrl(CameraSettings.Disabled));

    private static AppSettings With(CameraSettings camera) => new()
    {
        DebugLogging = true,
        Printers =
        [
            new PrinterDefinition
            {
                Id = "mk4",
                Name = "Workshop MK4",
                ConnectionType = ConnectionType.PrusaLink,
                Address = "10.0.0.5",
                UserName = "workshop",
                Camera = camera,
            },
        ],
    };
}
