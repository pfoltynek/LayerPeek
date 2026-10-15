using PrusaTrayDiagnostic.Core.Printers;
using PrusaTrayDiagnostic.Core.Settings;

namespace PrusaTrayDiagnostic.Core.Tests.Settings;

public class AppSettingsTests
{
    [Fact]
    public void DefaultHasFiveSecondPollingAndNoPrinters()
    {
        AppSettings settings = AppSettings.Default;

        Assert.Equal(TimeSpan.FromSeconds(5), settings.PollingInterval);
        Assert.Empty(settings.Printers);
        Assert.True(settings.Notifications.Enabled);
    }

    [Theory]
    [InlineData(0.5)]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(300.001)]
    public void PollingIntervalOutsideAllowedRangeIsRejected(double seconds)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new AppSettings { PollingInterval = TimeSpan.FromSeconds(seconds) });
    }

    [Theory]
    [InlineData(1)]
    [InlineData(30)]
    [InlineData(300)]
    public void PollingIntervalInsideAllowedRangeIsAccepted(double seconds)
    {
        AppSettings settings = new() { PollingInterval = TimeSpan.FromSeconds(seconds) };

        Assert.Equal(TimeSpan.FromSeconds(seconds), settings.PollingInterval);
    }

    [Fact]
    public void PrintersWithDuplicateIdsAreRejectedIgnoringCase()
    {
        Assert.Throws<ArgumentException>(() => new AppSettings { Printers = [Printer("mk4"), Printer("MK4")] });
    }

    [Fact]
    public void PrintersNullEntryIsRejected()
    {
        Assert.Throws<ArgumentException>(() => new AppSettings { Printers = [Printer("mk4"), null!] });
    }

    [Fact]
    public void PrintersAreDefensivelyCopied()
    {
        List<PrinterDefinition> source = [Printer("mk4")];
        AppSettings settings = new() { Printers = source };

        source.Add(Printer("xl"));

        Assert.Single(settings.Printers);
    }

    [Fact]
    public void PrintersCannotBeMutatedThroughCast()
    {
        AppSettings settings = new() { Printers = [Printer("mk4")] };

        IList<PrinterDefinition> list = Assert.IsAssignableFrom<IList<PrinterDefinition>>(settings.Printers);
        Assert.Throws<NotSupportedException>(() => list[0] = Printer("xl"));
        Assert.Throws<NotSupportedException>(() => list.Add(Printer("xl")));
    }

    [Fact]
    public void EqualityComparesPrintersByValue()
    {
        AppSettings first = new() { PollingInterval = TimeSpan.FromSeconds(7), Printers = [Printer("mk4"), Printer("xl")] };
        AppSettings second = new() { PollingInterval = TimeSpan.FromSeconds(7), Printers = [Printer("mk4"), Printer("xl")] };

        Assert.Equal(first, second);
        Assert.True(first == second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
        Assert.NotEqual(first, second with { Printers = [Printer("xl"), Printer("mk4")] });
        Assert.NotEqual(first, second with { Notifications = new NotificationSettings { Error = false } });
        Assert.NotEqual(first, second with { PollingInterval = TimeSpan.FromSeconds(8) });
        Assert.False(first.Equals(null));
    }

    private static PrinterDefinition Printer(string id) =>
        new() { Id = id, Name = id, ConnectionType = ConnectionType.PrusaLink, Address = "192.168.1.20" };
}
