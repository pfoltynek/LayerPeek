using System.Globalization;
using System.Xml.Linq;

using LayerPeek.App.Notifications;
using LayerPeek.App.Tests.Tray;
using LayerPeek.Core.Cooldown;
using LayerPeek.Core.Events;
using LayerPeek.Core.Notifications;

using static LayerPeek.App.Tests.Tray.TestPrinters;

namespace LayerPeek.App.Tests.Notifications;

/// <summary>
/// The "printer cooled down" toast (T-080): printer name, how the print ended and that it cooled down, then "safe to turn
/// off" with the nozzle and bed temperatures and their thresholds (T-109; the nozzle alone for a printer that reports no
/// bed) instead of the file name. Click and sound as for every toast.
/// </summary>
public sealed class ToastContentBuilderCooledDownTests
{
    [Theory]
    [InlineData("en-US", CooldownPrintOutcome.Finished, "Print finished, printer cooled down", "Safe to turn off. Nozzle 47 °C, threshold 50 °C")]
    [InlineData("en-US", CooldownPrintOutcome.Stopped, "Print stopped, printer cooled down", "Safe to turn off. Nozzle 47 °C, threshold 50 °C")]
    [InlineData("cs-CZ", CooldownPrintOutcome.Finished, "Tisk dokončen, tiskárna vychladla", "Můžete tiskárnu vypnout. Tryska 47 °C, práh 50 °C")]
    [InlineData("cs-CZ", CooldownPrintOutcome.Stopped, "Tisk zastaven, tiskárna vychladla", "Můžete tiskárnu vypnout. Tryska 47 °C, práh 50 °C")]
    [InlineData("fr-FR", CooldownPrintOutcome.Finished, "Impression terminée, imprimante refroidie", "Vous pouvez l'éteindre. Buse 47 °C, seuil 50 °C")]
    [InlineData("fr-FR", CooldownPrintOutcome.Stopped, "Impression arrêtée, imprimante refroidie", "Vous pouvez l'éteindre. Buse 47 °C, seuil 50 °C")]
    public void ToastNamesThePrinterTheOutcomeAndTheTemperatures(string cultureName, CooldownPrintOutcome outcome, string body, string detail)
    {
        using CultureScope culture = new(cultureName);

        string[] texts = Texts(Build(CooledDown(outcome, nozzle: 46.7, threshold: 50)));

        Assert.Equal(["MK4", body, detail], texts);
    }

    [Theory]
    [InlineData("en-US", "Safe to turn off. Nozzle 37 °C (threshold 40 °C), bed 39 °C (threshold 45 °C)")]
    [InlineData("cs-CZ", "Můžete tiskárnu vypnout. Tryska 37 °C (práh 40 °C), podložka 39 °C (práh 45 °C)")]
    [InlineData("sk-SK", "Môžete tlačiareň vypnúť. Tryska 37 °C (prah 40 °C), podložka 39 °C (prah 45 °C)")]
    [InlineData("de-DE", "Sie können ihn jetzt ausschalten. Düse 37 °C (Schwelle 40 °C), Heizbett 39 °C (Schwelle 45 °C)")]
    [InlineData("es-ES", "Ya puede apagarla. Boquilla 37 °C (umbral 40 °C), base 39 °C (umbral 45 °C)")]
    [InlineData("fr-FR", "Vous pouvez l'éteindre. Buse 37 °C (seuil 40 °C), plateau 39 °C (seuil 45 °C)")]
    [InlineData("it-IT", "Puoi spegnerla. Ugello 37 °C (soglia 40 °C), piano 39 °C (soglia 45 °C)")]
    [InlineData("pl-PL", "Można ją wyłączyć. Dysza 37 °C (próg 40 °C), stół 39 °C (próg 45 °C)")]
    public void ToastNamesBothTemperaturesAndThresholdsWithABed(string cultureName, string detail)
    {
        using CultureScope culture = new(cultureName);

        string[] texts = Texts(Build(CooledDown(CooldownPrintOutcome.Finished, nozzle: 36.6, threshold: 40, bed: 38.5, bedThreshold: 45)));

        Assert.Equal(detail, texts[2]);
    }

    [Fact]
    public void TextsFollowTheGivenLanguageNotTheCurrentOne()
    {
        using CultureScope culture = new("en-US");

        string[] texts = Texts(XElement.Parse(ToastContentBuilder.Build(CooledDown(CooldownPrintOutcome.Stopped, 40, 45), CultureInfo.GetCultureInfo("cs"))));

        Assert.Equal("Tisk zastaven, tiskárna vychladla", texts[1]);
        Assert.Equal("Můžete tiskárnu vypnout. Tryska 40 °C, práh 45 °C", texts[2]);
    }

    [Theory]
    [InlineData(49.4, "49")]
    [InlineData(49.6, "50")]
    [InlineData(30, "30")]
    public void NozzleIsShownInWholeDegrees(double nozzle, string shown)
    {
        using CultureScope culture = new("en-US");

        string[] texts = Texts(Build(CooledDown(CooldownPrintOutcome.Finished, nozzle, 50)));

        Assert.Equal($"Safe to turn off. Nozzle {shown} °C, threshold 50 °C", texts[2]);
    }

    [Fact]
    public void FileNameIsNotShown()
    {
        using CultureScope culture = new("en-US");

        string[] texts = Texts(Build(CooledDown(CooldownPrintOutcome.Finished, 47, 50) with { FileName = "benchy.bgcode" }));

        Assert.Equal(3, texts.Length);
        Assert.DoesNotContain("benchy.bgcode", texts);
    }

    [Fact]
    public void WithoutCooldownDetailsTheToastStillSaysItCooledDown()
    {
        using CultureScope culture = new("en-US");

        string[] texts = Texts(Build(ToastContentBuilderTests.Notification(PrintEventKind.CooledDown)));

        Assert.Equal(["MK4", "Printer cooled down"], texts);
        Assert.Equal("Printer cooled down", ToastContentBuilder.EventText(PrintEventKind.CooledDown, null, CultureInfo.GetCultureInfo("en")));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SoundAndClickWorkAsForOtherToasts(bool playSound)
    {
        XElement toast = Build(CooledDown(CooldownPrintOutcome.Finished, 47, 50) with { PlaySound = playSound });

        Assert.Equal(playSound, !toast.Elements("audio").Any());
        Assert.Equal("foreground", toast.Attribute("activationType")?.Value);
        Assert.True(ToastLaunchArgument.TryGetPrinterId(toast.Attribute("launch")?.Value, out string? printerId));
        Assert.Equal(Definition().Id, printerId);
    }

    /// <summary>Without <paramref name="bed"/> like a printer that reports no bed temperature.</summary>
    private static PrintNotification CooledDown(CooldownPrintOutcome outcome, double nozzle, int threshold, double? bed = null, int bedThreshold = 40) =>
        ToastContentBuilderTests.Notification(PrintEventKind.CooledDown) with
        {
            Cooldown = new CooldownEvent(Definition().Id, Now, "benchy.bgcode", outcome, nozzle, threshold, bed, bedThreshold),
        };

    private static XElement Build(PrintNotification notification) => XElement.Parse(ToastContentBuilder.Build(notification, CultureInfo.CurrentUICulture));

    private static string[] Texts(XElement toast) =>
        [.. toast.Elements("visual").Elements("binding").Elements("text").Select(e => e.Value)];
}
