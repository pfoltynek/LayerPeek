using Microsoft.Extensions.Logging;

using LayerPeek.App.SingleInstance;
using LayerPeek.App.Tests.Tray;

namespace LayerPeek.App.Tests.SingleInstance;

/// <summary>
/// T-099: while the services run before the UI (macOS waiting for a display), a click on a notification opens the printer's
/// web like a launch click; a later start is ignored; only the test mode may quit through a command.
/// </summary>
public sealed class WaitingInstanceCommandsTests
{
    private readonly List<string> _opened = [];
    private readonly ListLogger<WaitingInstanceCommands> _logger = new();
    private int _quits;

    [Fact]
    public void AClickOnANotificationOpensThePrinter()
    {
        Assert.True(InstanceCommand.TryForToast("printer=xl", out InstanceCommand toast));

        Create(testCommands: false).Handle(toast);

        Assert.Equal(["printer=xl"], _opened);
        Assert.Contains(_logger.Entries, static e => e.Message.Contains("opening the printer's web", StringComparison.Ordinal));
    }

    [Fact]
    public void ALaterStartIsIgnoredWhileThereIsNothingToShow()
    {
        Create(testCommands: true).Handle(InstanceCommand.Activate);

        Assert.Empty(_opened);
        Assert.Equal(0, _quits);
        Assert.Contains(_logger.Entries, static e => e.Level == LogLevel.Information && e.Message.Contains("Activate", StringComparison.Ordinal));
    }

#if DEBUG
    [Fact]
    public void OnlyTheTestModeQuitsThroughACommand()
    {
        InstanceCommand quit = new(InstanceCommandKind.Quit);

        Create(testCommands: false).Handle(quit);
        Assert.Equal(0, _quits);

        Create(testCommands: true).Handle(quit);
        Assert.Equal(1, _quits);
    }
#endif

    private WaitingInstanceCommands Create(bool testCommands) => new(
        argument =>
        {
            _opened.Add(argument);
            return Task.CompletedTask;
        },
        () => _quits++,
        testCommands,
        _logger);
}
