using Microsoft.Extensions.Time.Testing;

using LayerPeek.App.Notifications;
using LayerPeek.App.Tests.Tray;
using LayerPeek.App.Tests.Tray.Menu;
using LayerPeek.App.Tray;
using LayerPeek.Core.Printers;
using LayerPeek.Core.Settings;

using static LayerPeek.App.Tests.Tray.TestPrinters;

namespace LayerPeek.App.Tests.Notifications;

/// <summary>
/// T-164 / T-165 (user decision 2026-11-04): a click on the notification about a finished JPEG export or timelapse from a
/// recording opens the printer's recordings folder, the tray menu's action. The argument carries only the printer id and is
/// untrusted input of the OS: only the exact form the app creates is accepted.
/// </summary>
public sealed class TimelapseNotificationActivationTests : IDisposable
{
    private readonly FakeUrlLauncher _launcher = new();
    private readonly FakeWebGateway _gateway = FakeWebGateway.WithoutPassword();
    private readonly ListLogger<ToastActivationRouter> _logger = new();
    private readonly FakeTimeProvider _time = new();
    private readonly List<string> _folders = [];
    private readonly ToastActivationRouter _router;
    private Exception? _folderFailure;

    public TimelapseNotificationActivationTests()
    {
        _router = new ToastActivationRouter(TestWebOpener.Create(_launcher, _gateway), _logger, _time, falseAlarms: null, OpenFolderAsync);
    }

    public void Dispose() => _router.Dispose();

    [Theory]
    [InlineData("core")]
    [InlineData("Core One / workshop")]
    [InlineData("ü&<>\"'")]
    public void TheArgumentRoundTripsAndIsNoOtherArgument(string printerId)
    {
        Assert.True(ToastLaunchArgument.TryForRecordingsFolder(printerId, out string? argument));

        Assert.StartsWith("recordings=", argument, StringComparison.Ordinal);
        Assert.True(ToastLaunchArgument.IsWellFormed(argument));
        Assert.True(ToastLaunchArgument.TryGetRecordingsFolderPrinterId(argument, out string? decoded));
        Assert.Equal(printerId, decoded);
        Assert.False(ToastLaunchArgument.TryGetPrinterId(argument, out _));
        Assert.False(ToastLaunchArgument.TryGetCameraPrinterId(argument, out _));
        Assert.True(ToastLaunchArgument.TryForCamera(printerId, out string? camera));
        Assert.False(ToastLaunchArgument.TryGetRecordingsFolderPrinterId(camera, out _));
        Assert.False(ToastLaunchArgument.TryGetRecordingsFolderPrinterId(ToastLaunchArgument.ForPrinter(printerId), out _));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("recordings=")]
    [InlineData("recordings=%20")]
    [InlineData("recordings=a b")]
    [InlineData("recordings=<a>")]
    [InlineData("recordings=/Users/x")]
    [InlineData("recordings=core\n")]
    [InlineData("recordings=core&x=1")]
    [InlineData("Recordings=core")]
    [InlineData(" recordings=core")]
    public void MalformedArgumentsAreRejected(string? argument)
    {
        Assert.False(ToastLaunchArgument.IsWellFormed(argument));
        Assert.False(ToastLaunchArgument.TryGetRecordingsFolderPrinterId(argument, out _));
    }

    [Fact]
    public void TooLongArgumentsAreRejected()
    {
        Assert.False(ToastLaunchArgument.TryForRecordingsFolder(new string('x', ToastLaunchArgument.MaxLength), out _));
        Assert.False(ToastLaunchArgument.IsWellFormed("recordings=" + new string('x', ToastLaunchArgument.MaxLength)));
    }

    [Fact]
    public async Task AClickOpensTheRecordingsFolderOfAConfiguredPrinterOnlyNeitherItsWebNorItsCamera()
    {
        List<string> cameras = [];
        _router.AttachCameraOpener(cameras.Add);
        Apply(Definition(id: "core", name: "Core One"));

        Assert.True(await _router.ActivateAsync(Folder("CORE")));
        Assert.False(await _router.ActivateAsync(Folder("gone")));
        Assert.False(await _router.ActivateAsync("recordings=a b"));

        // The configured id, matched ignoring case.
        Assert.Equal(["core"], _folders);
        Assert.Empty(cameras);
        Assert.Empty(_launcher.Opened);
        Assert.Empty(_gateway.Opened);
    }

    [Fact]
    public async Task TheSameClickReportedTwiceOpensOnceAndAFailingOpenerIsLogged()
    {
        Apply(Definition(id: "core"));

        Assert.True(await _router.ActivateAsync(Folder("core")));
        Assert.False(await _router.ActivateAsync(Folder("core")));
        _folderFailure = new InvalidOperationException("no folder");
        _time.Advance(ToastActivationRouter.DuplicateWindow);
        Assert.False(await _router.ActivateAsync(Folder("core")));

        Assert.Equal(["core", "core"], _folders);
        Assert.Contains(_logger.Entries, static entry => entry.Exception is InvalidOperationException);
    }

    [Fact]
    public async Task WithoutAnOpenerOrOnceTheAppQuitsNothingOpens()
    {
        using ToastActivationRouter without = new(TestWebOpener.Create(_launcher, _gateway), _logger, _time);
        ((IMonitoringObserver)without).ApplySettings(new AppSettings { Printers = [Definition(id: "core")] });
        Assert.False(await without.ActivateAsync(Folder("core")));

        Apply(Definition(id: "core"));
        _router.Dispose();
        Assert.False(await _router.ActivateAsync(Folder("core")));

        Assert.Empty(_folders);
        Assert.Empty(_launcher.Opened);
    }

    private Task<bool> OpenFolderAsync(string printerId)
    {
        _folders.Add(printerId);
        return _folderFailure is { } failure ? Task.FromException<bool>(failure) : Task.FromResult(true);
    }

    private static string Folder(string printerId) =>
        ToastLaunchArgument.TryForRecordingsFolder(printerId, out string? argument) ? argument : throw new InvalidOperationException();

    private void Apply(params PrinterDefinition[] printers) =>
        ((IMonitoringObserver)_router).ApplySettings(new AppSettings { Printers = printers });
}
