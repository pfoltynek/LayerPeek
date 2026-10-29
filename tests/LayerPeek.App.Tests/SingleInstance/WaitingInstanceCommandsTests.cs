using Microsoft.Extensions.Logging;

using LayerPeek.App.SingleInstance;
using LayerPeek.App.Tests.Tray;

namespace LayerPeek.App.Tests.SingleInstance;

/// <summary>
/// T-099: while the services run before the UI (macOS waiting for a display), a click on a notification opens the printer's
/// web like a launch click; a later start is kept for the UI; only the test mode may quit through a command, the UI once the wait is over.
/// </summary>
public sealed class WaitingInstanceCommandsTests
{
    private readonly List<string> _opened = [];
    private readonly ListLogger<WaitingInstanceCommands> _logger = new();
    private int _quitAttempts;
#if DEBUG
    private bool _waitOver;
#endif

    [Fact]
    public void AClickOnANotificationOpensThePrinter()
    {
        Assert.True(InstanceCommand.TryForToast("printer=xl", out InstanceCommand toast));

        Assert.True(Create(testCommands: false).Handle(toast));

        Assert.Equal(["printer=xl"], _opened);
        Assert.Contains(_logger.Entries, static e => e.Message.Contains("opening the printer's web", StringComparison.Ordinal));
    }

    /// <summary>User decision 2026-10-29: a later start is kept for the UI (it opens what it always opens, after the wait).</summary>
    [Fact]
    public void ALaterStartIsKeptForTheUi()
    {
        Assert.False(Create(testCommands: true).Handle(InstanceCommand.Activate));

        Assert.Empty(_opened);
        Assert.Equal(0, _quitAttempts);
        Assert.Contains(_logger.Entries, static e => e.Level == LogLevel.Information && e.Message.Contains("Activate waits for the menu bar item", StringComparison.Ordinal));
    }

#if DEBUG
    [Fact]
    public void OnlyTheTestModeQuitsThroughACommandAndAfterTheWaitTheUiDoes()
    {
        InstanceCommand quit = new(InstanceCommandKind.Quit);

        Assert.False(Create(testCommands: false).Handle(quit));
        Assert.Equal(0, _quitAttempts);

        Assert.True(Create(testCommands: true).Handle(quit));
        Assert.Equal(1, _quitAttempts);

        // The wait is over (review finding 2): not dropped, kept for the UI.
        _waitOver = true;
        Assert.False(Create(testCommands: true).Handle(quit));
        Assert.Equal(2, _quitAttempts);
    }
#endif

    private WaitingInstanceCommands Create(bool testCommands) => new(
        argument =>
        {
            _opened.Add(argument);
            return Task.CompletedTask;
        },
        () =>
        {
            _quitAttempts++;
#if DEBUG
            return !_waitOver;
#else
            return true;
#endif
        },
        testCommands,
        _logger);
}
