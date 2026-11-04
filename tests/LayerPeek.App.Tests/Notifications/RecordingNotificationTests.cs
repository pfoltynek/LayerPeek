using System.Globalization;
using System.Xml.Linq;

using Microsoft.Extensions.Logging.Abstractions;

using LayerPeek.App.Localization;
using LayerPeek.App.Notifications;
using LayerPeek.App.Resources;
using LayerPeek.App.Tests.Tray;
using LayerPeek.Core.Cameras;
using LayerPeek.Core.Cameras.Recording;
using LayerPeek.Core.Notifications;
using LayerPeek.Core.Printers;
using LayerPeek.Core.Settings;

namespace LayerPeek.App.Tests.Notifications;

/// <summary>
/// The notification of a failed camera recording (T-146; the only notified recording event, user decision 2026-11-03 on
/// T-145): <see cref="CameraRecordingService.Failed"/> becomes a notification with the printer name and a localized reason per
/// <see cref="CameraRecordingProblem"/> on both OS, following "Show notifications" and the sound setting; a click opens the
/// settings window. Only fakes: nothing reaches Windows or macOS.
/// </summary>
public sealed class RecordingNotificationTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "LayerPeek.RecordingNotificationTests." + Guid.NewGuid().ToString("N"));

    public static TheoryData<string> Languages => [.. AppLanguages.Supported];

    public static TheoryData<CameraRecordingProblem> Problems => [.. Enum.GetValues<CameraRecordingProblem>()];

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private static PrinterDefinition Printer(string name = "Core One") =>
        TestPrinters.Definition("core", name) with { Camera = new CameraSettings(enabled: true, new Uri("rtsp://192.168.0.227/live")) };

    private CameraRecordingService Service(FakePrinterMonitor monitor) => new(
        monitor,
        new UnavailableRecorder(),
        CameraRecordingStorage.Instance,
        TimeProvider.System,
        NullLogger<CameraRecordingService>.Instance,
        new CameraRecordingServiceOptions { DefaultRoot = _root });

    [Fact]
    public async Task AFailureOfTheServiceIsNotifiedWithTheNameTheLimitAndTheSound()
    {
        FakePrinterMonitor monitor = new();
        await using CameraRecordingService service = Service(monitor);
        RecordingSink notifications = new();
        using RecordingNotificationObserver observer = new(service, notifications, minFreeBytes: 5_000_000_000, NullLogger<RecordingNotificationObserver>.Instance);
        AppSettings settings = new()
        {
            Printers = [Printer()],
            Recording = new CameraRecordingSettings { SizeLimitGigabytes = 20 },
            Notifications = new NotificationSettings { Sound = NotificationSound.ImportantOnly },
        };
        observer.ApplySettings(settings);
        service.Configure(settings.Printers, settings.Recording);

        // This build cannot record (no muxer): the manual start fails with a reason.
        Assert.True(service.Start("core"));
        RecordingNotification notification = await notifications.Next.WaitAsync(TestTimeouts.Wait, TestContext.Current.CancellationToken);

        Assert.Equal(new RecordingNotification("core", "Core One", CameraRecordingProblem.Unavailable, 20, 5, PlaySound: true), notification);
    }

    [Fact]
    public void NotificationsTurnedOffNotifyNothingAndAFailingServiceIsLogged()
    {
        FakePrinterMonitor monitor = new();
        using CameraRecordingService service = Service(monitor);
        RecordingSink notifications = new() { Throw = true };
        ListLogger<RecordingNotificationObserver> logger = new();
        using RecordingNotificationObserver observer = new(service, notifications, 5_000_000_000, logger);
        CameraRecordingError error = new("core", CameraRecordingProblem.DiskFull, "The disk is full.", DateTimeOffset.UnixEpoch, CameraRecordingMode.Automatic);

        observer.ApplySettings(new AppSettings { Printers = [Printer()], Notifications = new NotificationSettings { Enabled = false } });
        observer.OnFailed(service, error);
        Assert.Equal(0, notifications.Count);

        // On, but the notification service throws: logged, never into the recording service.
        observer.ApplySettings(new AppSettings { Printers = [Printer()], Notifications = new NotificationSettings { Sound = NotificationSound.Off } });
        observer.OnFailed(service, error);
        Assert.Equal(1, notifications.Count);
        Assert.Contains(logger.Entries, static entry => entry.Level == Microsoft.Extensions.Logging.LogLevel.Error && entry.Exception is InvalidOperationException);
    }

    [Fact]
    public void AnUnknownPrinterIsNamedByItsIdAndDisposeUnsubscribes()
    {
        FakePrinterMonitor monitor = new();
        using CameraRecordingService service = Service(monitor);
        RecordingSink notifications = new();
        RecordingNotificationObserver observer = new(service, notifications, 4_500_000_000, NullLogger<RecordingNotificationObserver>.Instance);

        observer.OnFailed(service, new CameraRecordingError("gone", CameraRecordingProblem.DiskSpaceLow, "x", DateTimeOffset.UnixEpoch, CameraRecordingMode.Manual));
        observer.Dispose();
        observer.OnFailed(service, new CameraRecordingError("gone", CameraRecordingProblem.DiskSpaceLow, "x", DateTimeOffset.UnixEpoch, CameraRecordingMode.Manual));

        RecordingNotification notification = Assert.Single(notifications.All);
        Assert.Equal(("gone", "gone"), (notification.PrinterId, notification.PrinterName));
        // Rounded up: the hint never promises less space than is needed.
        Assert.Equal(5, notification.MinFreeGigabytes);
    }

    [Theory]
    [MemberData(nameof(Languages))]
    public void EveryProblemHasItsOwnTranslatedReason(string language)
    {
        CultureInfo culture = CultureInfo.GetCultureInfo(language);
        HashSet<string> reasons = [];
        foreach (CameraRecordingProblem problem in Enum.GetValues<CameraRecordingProblem>())
        {
            NotificationLines lines = RecordingNotificationText.Lines(new RecordingNotification("core", "Core One", problem, 37, 5, PlaySound: false), culture);
            Assert.Equal("Core One", lines.Title);
            Assert.Equal(Strings.ResourceManager.GetString(nameof(Strings.NotificationRecordingFailed), culture), lines.Body);
            Assert.False(string.IsNullOrWhiteSpace(lines.Detail));
            Assert.DoesNotContain("{", lines.Detail, StringComparison.Ordinal);
            Assert.True(reasons.Add(lines.Detail!), $"{language}: {problem} has the reason of another problem.");
            if (problem == CameraRecordingProblem.SizeLimitReached)
            {
                Assert.Contains("37", lines.Detail, StringComparison.Ordinal);
            }

            if (problem == CameraRecordingProblem.DiskSpaceLow)
            {
                Assert.Contains("5", lines.Detail, StringComparison.Ordinal);
            }
        }

        if (language != "en")
        {
            // Translated, not the English fallback.
            Assert.NotEqual(
                RecordingNotificationText.Reason(new RecordingNotification("c", "c", CameraRecordingProblem.DiskFull, 1, 5, false), CultureInfo.GetCultureInfo("en")),
                RecordingNotificationText.Reason(new RecordingNotification("c", "c", CameraRecordingProblem.DiskFull, 1, 5, false), culture));
        }
    }

    [Fact]
    public void TheToastOpensTheSettingsAndANameCannotAddMarkup()
    {
        RecordingNotification notification = new("core", "Core\"/><action/>One", CameraRecordingProblem.FolderUnavailable, 50, 5, PlaySound: false);

        XDocument toast = XDocument.Parse(ToastContentBuilder.BuildRecordingFailed(notification, CultureInfo.GetCultureInfo("cs-CZ")));

        Assert.Equal(ToastLaunchArgument.RecordingSettings, toast.Root!.Attribute("launch")?.Value);
        Assert.Equal("foreground", toast.Root.Attribute("activationType")?.Value);
        Assert.Empty(toast.Descendants("action"));
        Assert.Equal(
            ["Core\"/><action/>One", "Nahrávání kamery selhalo", "Do složky s nahrávkami nelze zapisovat. Zkontrolujte disk nebo v nastavení vyberte jinou složku."],
            toast.Descendants("text").Select(static text => text.Value));
        Assert.Equal("true", toast.Descendants("audio").Single().Attribute("silent")?.Value);
    }

    [Fact]
    public async Task TheWindowsToastIsShownWithoutATag()
    {
        FakeToastPlatform platform = new();
        using ToastNotificationService service = new(platform, static _ => { }, static () => CultureInfo.GetCultureInfo("en-US"), new ListLogger<ToastNotificationService>());

        service.ShowRecordingFailed(new RecordingNotification("core", "Core One", CameraRecordingProblem.DiskFull, 50, 5, PlaySound: true));
        await Waiting.ForAsync(() => platform.Session.Shown.Count == 1);

        Assert.Equal([null], platform.Session.ShownTags);
        Assert.Contains("The drive is full.", platform.Session.Shown[0], StringComparison.Ordinal);
        platform.Session.Release();
    }

    [Fact]
    public async Task TheMacNotificationOpensTheSettings()
    {
        FakeUserNotificationCenter center = new();
        using UserNotificationService service = new(center, static () => CultureInfo.GetCultureInfo("en-US"), new ListLogger<UserNotificationService>());

        service.ShowRecordingFailed(new RecordingNotification("core", "", CameraRecordingProblem.TooSlow, 50, 5, PlaySound: false));
        await Waiting.ForAsync(() => center.Posted.Count == 1);

        UserNotificationRequest request = center.Posted[0];
        // An empty name: the id is the title.
        Assert.Equal("core", request.Title);
        Assert.Equal("Camera recording failed\nWriting to the drive could not keep up with the camera.", request.Body);
        Assert.False(request.PlaySound);
        Assert.Equal(ToastLaunchArgument.RecordingSettings, request.LaunchArgument);
    }

    /// <summary>A recorder of a build without the MP4 muxer (T-144).</summary>
    private sealed class UnavailableRecorder : ICameraRecorder
    {
        public CameraRecorderAvailability Availability { get; } = CameraRecorderAvailability.Unavailable("No MP4 muxer in this build.");

        public ICameraRecording Start(string printerId, string filePath, Action<ICameraRecording>? changed = null) =>
            throw new InvalidOperationException("Never started without the muxer.");
    }

    private sealed class RecordingSink : INotificationService
    {
        private readonly Lock _gate = new();
        private readonly List<RecordingNotification> _all = [];
        private readonly TaskCompletionSource<RecordingNotification> _next = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool Throw { get; init; }

        public Task<RecordingNotification> Next => _next.Task;

        public int Count
        {
            get
            {
                lock (_gate)
                {
                    return _all.Count;
                }
            }
        }

        public IReadOnlyList<RecordingNotification> All
        {
            get
            {
                lock (_gate)
                {
                    return [.. _all];
                }
            }
        }

        public void Show(PrintNotification notification)
        {
        }

        public void ShowRecordingFailed(RecordingNotification notification)
        {
            lock (_gate)
            {
                _all.Add(notification);
            }

            _next.TrySetResult(notification);
            if (Throw)
            {
                throw new InvalidOperationException("The notification service failed.");
            }
        }
    }
}
