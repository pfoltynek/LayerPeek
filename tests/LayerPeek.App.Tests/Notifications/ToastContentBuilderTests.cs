using System.Globalization;
using System.Xml.Linq;

using LayerPeek.App.Notifications;
using LayerPeek.App.Tests.Tray;
using LayerPeek.Core.Events;
using LayerPeek.Core.Notifications;
using LayerPeek.Core.Printers;

using static LayerPeek.App.Tests.Tray.TestPrinters;

namespace LayerPeek.App.Tests.Notifications;

public sealed class ToastContentBuilderTests
{
    [Theory]
    [InlineData(PrintEventKind.PrintStarted, "Print started")]
    [InlineData(PrintEventKind.PrintFinished, "Print finished")]
    [InlineData(PrintEventKind.PrintPaused, "Print paused")]
    [InlineData(PrintEventKind.PrintResumed, "Print resumed")]
    [InlineData(PrintEventKind.PrintStopped, "Print stopped")]
    [InlineData(PrintEventKind.Error, "The printer reports an error")]
    [InlineData(PrintEventKind.AttentionRequired, "The printer needs your attention")]
    [InlineData(PrintEventKind.CredentialsRejected, "The printer rejected the credentials")]
    [InlineData(PrintEventKind.PrinterOffline, "The printer is offline")]
    [InlineData(PrintEventKind.PrinterOnline, "The printer is back online")]
    [InlineData((PrintEventKind)999, "The printer status changed")]
    public void EnglishTitleIsPrinterNameAndBodyDescribesEvent(PrintEventKind kind, string expected)
    {
        using CultureScope culture = new("en-US");

        string[] texts = Texts(Build(Notification(kind)));

        Assert.Equal("MK4", texts[0]);
        Assert.Equal(expected, texts[1]);
    }

    [Theory]
    [InlineData(PrintEventKind.PrintStarted, "Tisk zahájen")]
    [InlineData(PrintEventKind.PrintFinished, "Tisk dokončen")]
    [InlineData(PrintEventKind.PrintPaused, "Tisk pozastaven")]
    [InlineData(PrintEventKind.PrintResumed, "Tisk pokračuje")]
    [InlineData(PrintEventKind.PrintStopped, "Tisk zastaven")]
    [InlineData(PrintEventKind.Error, "Tiskárna hlásí chybu")]
    [InlineData(PrintEventKind.AttentionRequired, "Tiskárna vyžaduje pozornost")]
    [InlineData(PrintEventKind.CredentialsRejected, "Tiskárna odmítla přihlašovací údaje")]
    [InlineData(PrintEventKind.PrinterOffline, "Tiskárna je offline")]
    [InlineData(PrintEventKind.PrinterOnline, "Tiskárna je opět online")]
    [InlineData((PrintEventKind)999, "Stav tiskárny se změnil")]
    public void CzechBodyDescribesEvent(PrintEventKind kind, string expected)
    {
        using CultureScope culture = new("cs-CZ");

        string[] texts = Texts(Build(Notification(kind)));

        Assert.Equal("MK4", texts[0]);
        Assert.Equal(expected, texts[1]);
    }

    [Theory]
    [InlineData("en-US", "10 min remaining")]
    [InlineData("cs-CZ", "Zbývá 10 min")]
    public void RemainingTimeShowsFormattedDuration(string cultureName, string expected)
    {
        using CultureScope culture = new(cultureName);

        string[] texts = Texts(Build(Notification(PrintEventKind.RemainingTime) with { Remaining = TimeSpan.FromMinutes(9.5) }));

        Assert.Equal(expected, texts[1]);
    }

    [Fact]
    public void RemainingTimeWithoutValueStillHasText()
    {
        using CultureScope culture = new("en-US");

        string[] texts = Texts(Build(Notification(PrintEventKind.RemainingTime) with { Remaining = null }));

        Assert.Equal("The print is about to finish", texts[1]);
    }

    [Fact]
    public void FileNameIsThirdLine()
    {
        string[] texts = Texts(Build(Notification(PrintEventKind.PrintFinished) with { FileName = "benchy.bgcode" }));

        Assert.Equal(3, texts.Length);
        Assert.Equal("benchy.bgcode", texts[2]);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void MissingFileNameAddsNoLine(string? fileName)
    {
        string[] texts = Texts(Build(Notification(PrintEventKind.PrinterOffline) with { FileName = fileName }));

        Assert.Equal(2, texts.Length);
    }

    [Fact]
    public void SilentToastWhenSoundIsOff()
    {
        XElement toast = Build(Notification(PrintEventKind.PrintFinished) with { PlaySound = false });

        XElement audio = Assert.Single(toast.Elements("audio"));
        Assert.Equal("true", audio.Attribute("silent")?.Value);
    }

    [Fact]
    public void DefaultSoundWhenSoundIsOn()
    {
        XElement toast = Build(Notification(PrintEventKind.Error) with { PlaySound = true });

        Assert.Empty(toast.Elements("audio"));
    }

    [Fact]
    public void LaunchArgumentIdentifiesPrinter()
    {
        PrinterDefinition printer = Definition(id: "garage & <\"lab\">");

        XElement toast = Build(Notification(PrintEventKind.PrintFinished) with { Printer = printer });

        Assert.Equal("toast", toast.Name.LocalName);
        Assert.Equal("foreground", toast.Attribute("activationType")?.Value);
        Assert.True(ToastLaunchArgument.TryGetPrinterId(toast.Attribute("launch")?.Value, out string? printerId));
        Assert.Equal(printer.Id, printerId);
    }

    [Fact]
    public void TooLongPrinterIdLeavesTheToastWithoutLaunchArgument()
    {
        PrinterDefinition printer = Definition(id: new string('x', ToastLaunchArgument.MaxLength * 4));

        XElement toast = Build(Notification(PrintEventKind.PrintFinished) with { Printer = printer });

        // Still shown (the payload stays small), only a click has nothing to open.
        Assert.Null(toast.Attribute("launch"));
        Assert.Equal("MK4", Texts(toast)[0]);
    }

    [Fact]
    public void UsesGenericTemplate()
    {
        XElement binding = Assert.Single(Build(Notification(PrintEventKind.PrintFinished)).Elements("visual").Elements("binding"));

        Assert.Equal("ToastGeneric", binding.Attribute("template")?.Value);
    }

    [Fact]
    public void PrinterAndFileNamesCannotInjectMarkup()
    {
        const string Name = "</text></binding></visual><actions><action content=\"x\" arguments=\"file:///c:/\"/></actions><visual><binding template=\"ToastGeneric\"><text>";
        const string FileName = "a&b <c> \"d\" 'e' &amp; ]]> <!-- x -->";
        PrintNotification notification = Notification(PrintEventKind.PrintFinished) with
        {
            Printer = Definition(name: Name),
            FileName = FileName,
        };

        string xml = ToastContentBuilder.Build(notification, CultureInfo.CurrentUICulture);
        XElement toast = XElement.Parse(xml);

        // Exactly the elements the builder writes, nothing smuggled in.
        Assert.Equal(["toast", "visual", "binding", "text", "text", "text", "audio"], toast.DescendantsAndSelf().Select(e => e.Name.LocalName));
        string[] texts = Texts(toast);
        Assert.StartsWith("</text></binding>", texts[0], StringComparison.Ordinal);
        Assert.Equal(FileName, texts[2]);
    }

    [Fact]
    public void CharactersInvalidInXmlAreRemoved()
    {
        PrintNotification notification = Notification(PrintEventKind.PrintFinished) with
        {
            Printer = Definition(name: "MK\u00014\uFFFE"),
            FileName = "bad\uD800surrogate 🐎\u0008.bgcode",
        };

        string[] texts = Texts(XElement.Parse(ToastContentBuilder.Build(notification, CultureInfo.CurrentUICulture)));

        Assert.Equal("MK4", texts[0]);
        Assert.Equal("badsurrogate 🐎.bgcode", texts[2]);
    }

    [Fact]
    public void TextMadeOnlyOfInvalidCharactersFallsBackToPrinterId()
    {
        PrintNotification notification = Notification(PrintEventKind.PrintFinished) with
        {
            Printer = Definition(id: "mk4", name: "\u0001\u0002"),
        };

        Assert.Equal("mk4", Texts(Build(notification))[0]);
    }

    [Fact]
    public void LongTextsAreShortened()
    {
        PrintNotification notification = Notification(PrintEventKind.PrintFinished) with
        {
            Printer = Definition(name: new string('n', 1000)),
            FileName = new string('f', 1000) + ".bgcode",
        };

        string[] texts = Texts(Build(notification));

        Assert.Equal(ToastContentBuilder.MaxTextLength, texts[0].Length);
        Assert.EndsWith("…", texts[0], StringComparison.Ordinal);
        Assert.Equal(ToastContentBuilder.MaxTextLength, texts[2].Length);
    }

    [Fact]
    public void NullArgumentsAreRejected()
    {
        Assert.Throws<ArgumentNullException>(() => ToastContentBuilder.Build(null!, CultureInfo.InvariantCulture));
        Assert.Throws<ArgumentNullException>(() => ToastContentBuilder.Build(Notification(PrintEventKind.PrintFinished), null!));
    }

    internal static PrintNotification Notification(PrintEventKind kind) => new()
    {
        Printer = Definition(),
        Kind = kind,
        Timestamp = Now,
    };

    private static XElement Build(PrintNotification notification) => XElement.Parse(ToastContentBuilder.Build(notification, CultureInfo.CurrentUICulture));

    private static string[] Texts(XElement toast) =>
        [.. toast.Elements("visual").Elements("binding").Elements("text").Select(e => e.Value)];
}
