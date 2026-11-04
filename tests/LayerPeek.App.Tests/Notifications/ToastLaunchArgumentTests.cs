using LayerPeek.App.Notifications;
using LayerPeek.App.Settings;

namespace LayerPeek.App.Tests.Notifications;

public sealed class ToastLaunchArgumentTests
{
    [Theory]
    [InlineData("mk4")]
    [InlineData("MK4 in the garage")]
    [InlineData("a&b<c>\"'=;%20/?#")]
    [InlineData("příliš žluťoučký 🐎")]
    public void PrinterIdRoundTrips(string printerId)
    {
        string argument = ToastLaunchArgument.ForPrinter(printerId);

        Assert.True(ToastLaunchArgument.TryGetPrinterId(argument, out string? parsed));
        Assert.Equal(printerId, parsed);
    }

    [Fact]
    public void ArgumentContainsNoCharactersThatNeedXmlEscaping()
    {
        string argument = ToastLaunchArgument.ForPrinter("a&b<c>\"'");

        Assert.DoesNotContain('&', argument);
        Assert.DoesNotContain('<', argument);
        Assert.DoesNotContain('"', argument);
        Assert.DoesNotContain('\'', argument);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("garbage")]
    [InlineData("printer=")]
    [InlineData("printer=%20%20")]
    [InlineData("job=mk4")]
    [InlineData("PRINTER=mk4")]
    public void MalformedArgumentIsRejected(string? argument)
    {
        Assert.False(ToastLaunchArgument.TryGetPrinterId(argument, out string? printerId));
        Assert.Null(printerId);
    }

    [Fact]
    public void LongestEncodableIdStillFits()
    {
        string id = new('a', ToastLaunchArgument.MaxLength - "printer=".Length);

        Assert.True(ToastLaunchArgument.TryForPrinter(id, out string? argument));
        Assert.Equal(ToastLaunchArgument.MaxLength, argument.Length);
    }

    [Theory]
    [InlineData(600, 'a')]
    // Short as text, but every 'ř' takes six characters when percent-encoded.
    [InlineData(100, 'ř')]
    public void TooLongIdCannotBeEncoded(int length, char character)
    {
        string id = new(character, length);

        Assert.False(ToastLaunchArgument.TryForPrinter(id, out string? argument));
        Assert.Null(argument);
        Assert.Throws<ArgumentException>(() => ToastLaunchArgument.ForPrinter(id));
    }

    [Fact]
    public void EmptyPrinterIdIsRejected()
    {
        Assert.Throws<ArgumentException>(() => ToastLaunchArgument.ForPrinter(" "));
        Assert.Throws<ArgumentNullException>(() => ToastLaunchArgument.ForPrinter(null!));
    }

    /// <summary>T-101: the test notification carries exactly this argument; it names no printer.</summary>
    [Fact]
    public void TheArgumentOfTheTestNotificationIsWellFormedButNoPrinter()
    {
        Assert.Equal("settings=general", ToastLaunchArgument.Settings);
        Assert.True(ToastLaunchArgument.IsSettings(ToastLaunchArgument.Settings));
        Assert.True(ToastLaunchArgument.IsWellFormed(ToastLaunchArgument.Settings));
        Assert.False(ToastLaunchArgument.TryGetPrinterId(ToastLaunchArgument.Settings, out _));
        Assert.True(ToastLaunchArgument.TryGetSettingsPage(ToastLaunchArgument.Settings, out SettingsPage page));
        Assert.Equal(SettingsPage.General, page);
    }

    /// <summary>T-146 (user decision 2026-11-04): a failed recording opens the settings on the Recording tab; no printer either.</summary>
    [Fact]
    public void TheArgumentOfTheRecordingNotificationIsWellFormedButNoPrinter()
    {
        Assert.Equal("settings=recording", ToastLaunchArgument.RecordingSettings);
        Assert.True(ToastLaunchArgument.IsSettings(ToastLaunchArgument.RecordingSettings));
        Assert.True(ToastLaunchArgument.IsWellFormed(ToastLaunchArgument.RecordingSettings));
        Assert.False(ToastLaunchArgument.TryGetPrinterId(ToastLaunchArgument.RecordingSettings, out _));
        Assert.True(ToastLaunchArgument.TryGetSettingsPage(ToastLaunchArgument.RecordingSettings, out SettingsPage page));
        Assert.Equal(SettingsPage.Recording, page);
        Assert.False(ToastLaunchArgument.TryGetSettingsPage(ToastLaunchArgument.Update, out _));
        Assert.False(ToastLaunchArgument.TryGetSettingsPage(ToastLaunchArgument.ForPrinter("mk4"), out _));
    }

    /// <summary>T-101: only the exact text opens the settings; anything near it is refused like any foreign argument (T-068).</summary>
    [Theory]
    [InlineData("settings=General")]
    [InlineData("SETTINGS=general")]
    [InlineData("settings=general ")]
    [InlineData(" settings=general")]
    [InlineData("settings=")]
    [InlineData("settings")]
    [InlineData("settings=printers")]
    [InlineData("settings=general&printer=mk4")]
    [InlineData("settings=general\n")]
    [InlineData("settings=Recording")]
    [InlineData("settings=recording ")]
    public void NothingButTheExactArgumentOfTheTestNotificationIsAccepted(string argument)
    {
        Assert.False(ToastLaunchArgument.IsSettings(argument));
        Assert.False(ToastLaunchArgument.IsWellFormed(argument));
    }

    [Fact]
    public void NoPrinterIdProducesTheArgumentOfTheTestNotification()
    {
        Assert.False(ToastLaunchArgument.IsSettings(ToastLaunchArgument.ForPrinter("settings=general")));
        Assert.False(ToastLaunchArgument.IsSettings(ToastLaunchArgument.ForPrinter("general")));
    }
}
