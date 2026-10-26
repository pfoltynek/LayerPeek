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
/// off" with the nozzle temperature and the threshold instead of the file name. Click and sound as for every toast.
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

    private static PrintNotification CooledDown(CooldownPrintOutcome outcome, double nozzle, int threshold) =>
        ToastContentBuilderTests.Notification(PrintEventKind.CooledDown) with
        {
            Cooldown = new CooldownEvent(Definition().Id, Now, "benchy.bgcode", outcome, nozzle, threshold),
        };

    private static XElement Build(PrintNotification notification) => XElement.Parse(ToastContentBuilder.Build(notification, CultureInfo.CurrentUICulture));

    private static string[] Texts(XElement toast) =>
        [.. toast.Elements("visual").Elements("binding").Elements("text").Select(e => e.Value)];
}
