using Microsoft.Extensions.Logging;

using LayerPeek.App.Notifications;
using LayerPeek.App.Tests.Tray;

namespace LayerPeek.App.Tests.Notifications;

/// <summary>
/// Clicks Windows reports through the COM toast activator (T-068): only those of the app's own id, kept until the app
/// knows what to do with them (a start by Windows waits for its click, then forwards it or handles it itself).
/// </summary>
public sealed class ToastActivationInboxTests
{
    private const string AppId = "LayerPeek";

    private readonly ListLogger<ToastActivationInbox> _logger = new();
    private readonly ToastActivationInbox _inbox;

    public ToastActivationInboxTests() => _inbox = new ToastActivationInbox(AppId, _logger);

    [Fact]
    public void ClicksBeforeTheAppIsReadyAreDeliveredOnAttachInOrder()
    {
        _inbox.Receive(AppId, "printer=a");
        _inbox.Receive(AppId, "printer=b");
        List<string> delivered = [];

        _inbox.Attach(delivered.Add);
        _inbox.Receive(AppId, "printer=c");

        Assert.Equal(["printer=a", "printer=b", "printer=c"], delivered);
    }

    [Fact]
    public async Task TheFirstClickIsAwaitedAndStaysForTheApp()
    {
        Task<string?> first = Task.Run(() => _inbox.WaitForFirst(TimeSpan.FromSeconds(10)), TestContext.Current.CancellationToken);
        await Task.Delay(50, TestContext.Current.CancellationToken);
        _inbox.Receive(AppId, "printer=a");

        Assert.Equal("printer=a", await first);
        List<string> delivered = [];
        _inbox.Attach(delivered.Add);
        Assert.Equal(["printer=a"], delivered);
    }

    [Fact]
    public void WaitingEndsWithoutAClick()
    {
        Assert.Null(_inbox.WaitForFirst(TimeSpan.FromMilliseconds(50)));
    }

    [Theory]
    [InlineData("LayerPeek.E2E")]
    [InlineData("SomeOtherApp")]
    [InlineData("")]
    [InlineData(null)]
    public void ClicksOfAnotherAppAreIgnored(string? appUserModelId)
    {
        _inbox.Receive(appUserModelId, "printer=a");

        Assert.Null(_inbox.WaitForFirst(TimeSpan.Zero));
        Assert.Contains(_logger.Entries, static entry => entry.Level == LogLevel.Warning);
    }

    [Fact]
    public void TheAppIdIsComparedIgnoringCase()
    {
        _inbox.Receive("layerpeek", "printer=a");

        Assert.Equal("printer=a", _inbox.WaitForFirst(TimeSpan.Zero));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void ClicksWithoutAnArgumentAreIgnored(string? arguments)
    {
        // A click on the app name in the header, or a toast without a printer: nothing to open.
        _inbox.Receive(AppId, arguments);

        Assert.Null(_inbox.WaitForFirst(TimeSpan.Zero));
    }

    [Theory]
    [InlineData("https://evil.example/")]
    [InlineData("printer=")]
    [InlineData("printer=a b")]
    [InlineData("printer=a&url=https://evil.example/")]
    [InlineData("PRINTER=a")]
    public void ClicksWithAForeignArgumentAreNeitherKeptNorAwaited(string arguments)
    {
        // Anything but the launch argument of the app's own toasts: never kept, so it can neither take the place of the
        // click a start by Windows waits for nor fill the bounded queue.
        _inbox.Receive(AppId, arguments);
        Assert.Null(_inbox.WaitForFirst(TimeSpan.Zero));

        _inbox.Receive(AppId, "printer=a");
        Assert.Equal("printer=a", _inbox.WaitForFirst(TimeSpan.Zero));
        List<string> delivered = [];
        _inbox.Attach(delivered.Add);
        _inbox.Receive(AppId, arguments);
        Assert.Equal(["printer=a"], delivered);
        Assert.Contains(_logger.Entries, static entry => entry.Level == LogLevel.Warning);
    }

    [Fact]
    public void AnOverlongArgumentIsNotKept()
    {
        _inbox.Receive(AppId, "printer=" + new string('a', ToastLaunchArgument.MaxLength));

        Assert.Null(_inbox.WaitForFirst(TimeSpan.Zero));
    }

    [Fact]
    public void AFloodOfClicksBeforeTheAppIsReadyIsBounded()
    {
        for (int i = 0; i < 100; i++)
        {
            _inbox.Receive(AppId, $"printer=p{i}");
        }

        List<string> delivered = [];
        _inbox.Attach(delivered.Add);

        Assert.Equal(ToastActivationInbox.MaxPending, delivered.Count);
        Assert.Equal("printer=p0", delivered[0]);
    }

    [Fact]
    public void AFailingTargetNeverReachesWindows()
    {
        _inbox.Attach(static _ => throw new InvalidOperationException("boom"));

        _inbox.Receive(AppId, "printer=a");

        Assert.Contains(_logger.Entries, static entry => entry.Exception is InvalidOperationException);
    }

    [Fact]
    public void AttachingTwiceIsRefused()
    {
        _inbox.Attach(static _ => { });

        Assert.Throws<InvalidOperationException>(() => _inbox.Attach(static _ => { }));
    }

    [Fact]
    public void ArgumentsAreChecked()
    {
        Assert.Throws<ArgumentException>(() => new ToastActivationInbox(" ", _logger));
        Assert.Throws<ArgumentNullException>(() => new ToastActivationInbox(AppId, null!));
        Assert.Throws<ArgumentNullException>(() => _inbox.Attach(null!));
    }
}
