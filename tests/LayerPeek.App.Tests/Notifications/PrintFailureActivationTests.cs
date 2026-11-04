using Microsoft.Extensions.Time.Testing;

using LayerPeek.App.Detection;
using LayerPeek.App.Notifications;
using LayerPeek.App.Tests.Tray;
using LayerPeek.App.Tests.Tray.Menu;
using LayerPeek.App.Tray;
using LayerPeek.Core.Printers;
using LayerPeek.Core.Settings;

using static LayerPeek.App.Tests.Tray.TestPrinters;

namespace LayerPeek.App.Tests.Notifications;

/// <summary>
/// The arguments of the print failure notification (T-154) are untrusted input of the OS: only the exact forms the app
/// creates are accepted. A click opens the camera window of a configured printer (held until the UI runs), the button
/// "False alarm" goes to the reporter; nothing opens the web.
/// </summary>
public sealed class PrintFailureActivationTests : IDisposable
{
    private const string AlarmId = "0123456789abcdef0123456789abcdef";

    private readonly FakeUrlLauncher _launcher = new();
    private readonly FakeWebGateway _gateway = FakeWebGateway.WithoutPassword();
    private readonly ListLogger<ToastActivationRouter> _logger = new();
    private readonly FakeFalseAlarms _falseAlarms = new();
    private readonly FakeTimeProvider _time = new();
    private readonly ToastActivationRouter _router;

    public PrintFailureActivationTests()
    {
        _router = new ToastActivationRouter(TestWebOpener.Create(_launcher, _gateway), _logger, _time, _falseAlarms);
    }

    public void Dispose() => _router.Dispose();

    // ---- Arguments ----

    [Theory]
    [InlineData("core")]
    [InlineData("Core One / workshop")]
    [InlineData("ü&<>\"'")]
    public void TheCameraArgumentRoundTripsAndIsWellFormed(string printerId)
    {
        Assert.True(ToastLaunchArgument.TryForCamera(printerId, out string? argument));

        Assert.True(ToastLaunchArgument.IsWellFormed(argument));
        Assert.True(ToastLaunchArgument.TryGetCameraPrinterId(argument, out string? decoded));
        Assert.Equal(printerId, decoded);
        // A camera argument is not a printer argument (that would open the web) and vice versa.
        Assert.False(ToastLaunchArgument.TryGetPrinterId(argument, out _));
        Assert.False(ToastLaunchArgument.TryGetCameraPrinterId(ToastLaunchArgument.ForPrinter(printerId), out _));
    }

    [Fact]
    public void TheFalseAlarmArgumentRoundTripsAndIsWellFormed()
    {
        string argument = ToastLaunchArgument.ForFalseAlarm(AlarmId);

        Assert.Equal("false-alarm=" + AlarmId, argument);
        Assert.True(ToastLaunchArgument.IsWellFormed(argument));
        Assert.True(ToastLaunchArgument.TryGetFalseAlarmId(argument, out string? id));
        Assert.Equal(AlarmId, id);
        Assert.Throws<ArgumentException>(() => ToastLaunchArgument.ForFalseAlarm("UPPER"));
        Assert.Throws<ArgumentException>(() => ToastLaunchArgument.ForFalseAlarm(""));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("false-alarm=")]
    [InlineData("false-alarm= 0123")]
    [InlineData("false-alarm=0123%20")]
    [InlineData("false-alarm=ABCDEF")]
    [InlineData("false-alarm=../../etc")]
    [InlineData("False-alarm=0123")]
    [InlineData(" false-alarm=0123")]
    [InlineData("false-alarm=0123\n")]
    [InlineData("camera=")]
    [InlineData("camera=%20")]
    [InlineData("camera=a b")]
    [InlineData("camera=<a>")]
    [InlineData("Camera=core")]
    [InlineData("camera=core&false-alarm=0123")]
    public void MalformedArgumentsAreRejected(string? argument)
    {
        Assert.False(ToastLaunchArgument.IsWellFormed(argument));
        Assert.False(ToastLaunchArgument.TryGetFalseAlarmId(argument, out _) && ToastLaunchArgument.IsWellFormed(argument));
    }

    [Fact]
    public void TooLongArgumentsAreRejected()
    {
        Assert.False(ToastLaunchArgument.TryGetFalseAlarmId("false-alarm=" + new string('a', ToastLaunchArgument.MaxAlarmIdLength + 1), out _));
        Assert.True(ToastLaunchArgument.TryGetFalseAlarmId("false-alarm=" + new string('a', ToastLaunchArgument.MaxAlarmIdLength), out _));
        Assert.False(ToastLaunchArgument.TryForCamera(new string('x', ToastLaunchArgument.MaxLength), out _));
        Assert.False(ToastLaunchArgument.IsWellFormed("camera=" + new string('x', ToastLaunchArgument.MaxLength)));
        Assert.False(ToastLaunchArgument.TryGetCameraPrinterId("camera=" + new string('x', ToastLaunchArgument.MaxLength), out _));
    }

    // ---- Camera window ----

    [Fact]
    public async Task AClickOpensTheCameraWindowOfAConfiguredPrinterNotItsWeb()
    {
        List<string> opened = [];
        _router.AttachCameraOpener(opened.Add);
        Apply(Definition(id: "core", name: "Core One"));

        Assert.True(await _router.ActivateAsync(Camera("CORE")));
        Assert.False(await _router.ActivateAsync(Camera("gone")));

        // The configured id, matched ignoring case.
        Assert.Equal(["core"], opened);
        Assert.Empty(_launcher.Opened);
        Assert.Empty(_gateway.Opened);
    }

    [Fact]
    public async Task AClickBeforeTheUiRunsIsHeldUntilTheOpenerIsAttachedOnce()
    {
        Apply(Definition(id: "core"), Definition(id: "xl", name: "XL"));
        Assert.True(await _router.ActivateAsync(Camera("core")));
        Assert.True(await _router.ActivateAsync(Camera("xl")));
        List<string> opened = [];

        _router.AttachCameraOpener(opened.Add);

        // The last click.
        Assert.Equal(["xl"], opened);
        Assert.Throws<InvalidOperationException>(() => _router.AttachCameraOpener(static _ => { }));
    }

    [Fact]
    public async Task TheSameClickReportedTwiceOpensOnceAndAFailingOpenerIsLogged()
    {
        int opened = 0;
        _router.AttachCameraOpener(_ =>
        {
            opened++;
            throw new InvalidOperationException("no window");
        });
        Apply(Definition(id: "core"));

        Assert.True(await _router.ActivateAsync(Camera("core")));
        Assert.False(await _router.ActivateAsync(Camera("core")));
        _time.Advance(ToastActivationRouter.DuplicateWindow);
        Assert.True(await _router.ActivateAsync(Camera("core")));

        Assert.Equal(2, opened);
        Assert.Contains(_logger.Entries, static entry => entry.Exception is InvalidOperationException);
    }

    [Fact]
    public async Task NothingOpensOnceTheAppQuits()
    {
        Apply(Definition(id: "core"));
        Assert.True(await _router.ActivateAsync(Camera("core")));
        _router.Dispose();
        List<string> opened = [];

        _router.AttachCameraOpener(opened.Add);

        Assert.Empty(opened);
        Assert.False(await _router.ActivateAsync(ToastLaunchArgument.ForFalseAlarm(AlarmId)));
        Assert.Empty(_falseAlarms.Reported);
    }

    // ---- False alarm ----

    [Fact]
    public async Task TheButtonGoesToTheReporterOnceAndOpensNothing()
    {
        List<string> opened = [];
        _router.AttachCameraOpener(opened.Add);

        Assert.True(await _router.ActivateAsync(ToastLaunchArgument.ForFalseAlarm(AlarmId)));
        // Windows may report one click twice.
        Assert.False(await _router.ActivateAsync(ToastLaunchArgument.ForFalseAlarm(AlarmId)));

        Assert.Equal([AlarmId], _falseAlarms.Reported);
        Assert.Empty(opened);
        Assert.Empty(_launcher.Opened);
    }

    [Fact]
    public async Task AFailingReporterIsLoggedAndWithoutOneTheButtonDoesNothing()
    {
        _falseAlarms.Throw = true;
        Assert.False(await _router.ActivateAsync(ToastLaunchArgument.ForFalseAlarm(AlarmId)));
        Assert.Contains(_logger.Entries, static entry => entry.Exception is InvalidOperationException);

        using ToastActivationRouter without = new(TestWebOpener.Create(_launcher, _gateway), _logger, _time);
        Assert.False(await without.ActivateAsync(ToastLaunchArgument.ForFalseAlarm(AlarmId)));
    }

    private static string Camera(string printerId) =>
        ToastLaunchArgument.TryForCamera(printerId, out string? argument) ? argument : throw new InvalidOperationException();

    private void Apply(params PrinterDefinition[] printers) =>
        ((IMonitoringObserver)_router).ApplySettings(new AppSettings { Printers = printers });

    private sealed class FakeFalseAlarms : IFalseAlarmReporter
    {
        private readonly Lock _gate = new();
        private readonly List<string> _reported = [];

        public bool Throw { get; set; }

        public IReadOnlyList<string> Reported
        {
            get
            {
                lock (_gate)
                {
                    return [.. _reported];
                }
            }
        }

        public Task<bool> ReportAsync(string alarmId, CancellationToken cancellationToken)
        {
            if (Throw)
            {
                throw new InvalidOperationException("broken");
            }

            lock (_gate)
            {
                _reported.Add(alarmId);
            }

            return Task.FromResult(true);
        }
    }
}
