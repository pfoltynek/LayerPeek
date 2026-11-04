using System.Globalization;
using System.Xml.Linq;

using Microsoft.Extensions.Logging.Abstractions;

using LayerPeek.App.Localization;
using LayerPeek.App.Notifications;
using LayerPeek.App.Resources;
using LayerPeek.App.Tests.Tray;
using LayerPeek.Core.Cameras;
using LayerPeek.Core.Cameras.Timelapse;
using LayerPeek.Core.Notifications;
using LayerPeek.Core.Settings;

namespace LayerPeek.App.Tests.Notifications;

/// <summary>
/// T-165: the end of a JPEG export of a timelapse becomes a short notification (done with the frames, or failed with a
/// localized reason; a cancel is not notified), with the printer name as the title, following "Show notifications" and the
/// sound setting (information). Only fakes: nothing reaches Windows or macOS.
/// </summary>
public sealed class TimelapseExportNotificationTests
{
    public static TheoryData<string> Languages => [.. AppLanguages.Supported];

    private static CameraTimelapseJpegExporter Exporter() =>
        new(new NoFeeds(), new NoEncoders(), NullLogger<CameraTimelapseJpegExporter>.Instance);

    private static CameraTimelapseExportResult Result(CameraTimelapseExportProblem problem, string? printerId = "core", int frames = 120) =>
        new(printerId, "/rec/Core One/2026-11-04_10-00-00_timelapse.mp4", problem == CameraTimelapseExportProblem.None ? "/rec/Core One/2026-11-04_10-00-00_timelapse_jpg" : null, frames, problem);

    [Fact]
    public void ADoneAndAFailedExportAreNotifiedWithThePrinterNameButACancelIsNot()
    {
        using CameraTimelapseJpegExporter exporter = Exporter();
        ExportSink notifications = new();
        using TimelapseExportNotificationObserver observer = new(exporter, notifications, NullLogger<TimelapseExportNotificationObserver>.Instance);
        observer.ApplySettings(new AppSettings
        {
            Printers = [TestPrinters.Definition("core", "Core One")],
            Notifications = new NotificationSettings { Sound = NotificationSound.ImportantOnly },
        });

        observer.OnCompleted(exporter, Result(CameraTimelapseExportProblem.None));
        observer.OnCompleted(exporter, Result(CameraTimelapseExportProblem.Unreadable, frames: 0));
        observer.OnCompleted(exporter, Result(CameraTimelapseExportProblem.Cancelled));
        observer.OnCompleted(exporter, Result(CameraTimelapseExportProblem.NotATimelapse, printerId: "gone", frames: 0));

        Assert.Equal(
            [
                new TimelapseExportNotification("core", "Core One", 120, CameraTimelapseExportProblem.None, PlaySound: false),
                new TimelapseExportNotification("core", "Core One", 0, CameraTimelapseExportProblem.Unreadable, PlaySound: false),
                new TimelapseExportNotification("gone", null, 0, CameraTimelapseExportProblem.NotATimelapse, PlaySound: false),
            ],
            notifications.All);
    }

    [Fact]
    public void ItSoundsWithAllNotificationsAndNothingIsShownWhenTheyAreOff()
    {
        using CameraTimelapseJpegExporter exporter = Exporter();
        ExportSink notifications = new() { Throw = true };
        using TimelapseExportNotificationObserver observer = new(exporter, notifications, NullLogger<TimelapseExportNotificationObserver>.Instance);
        observer.ApplySettings(new AppSettings { Notifications = new NotificationSettings { Sound = NotificationSound.All } });

        // A failing notification service never reaches the exporter.
        observer.OnCompleted(exporter, Result(CameraTimelapseExportProblem.None, printerId: null));
        Assert.True(Assert.Single(notifications.All).PlaySound);

        observer.UpdateSettings(new AppSettings { Notifications = new NotificationSettings { Enabled = false } });
        observer.OnCompleted(exporter, Result(CameraTimelapseExportProblem.Failed));
        Assert.Single(notifications.All);

        observer.Dispose();
        observer.OnCompleted(exporter, Result(CameraTimelapseExportProblem.None));
        Assert.Single(notifications.All);
    }

    [Theory]
    [MemberData(nameof(Languages))]
    public void EveryEndHasItsOwnTranslatedText(string language)
    {
        CultureInfo culture = CultureInfo.GetCultureInfo(language);
        CultureInfo english = CultureInfo.GetCultureInfo("en");
        NotificationLines done = TimelapseExportNotificationText.Lines(new TimelapseExportNotification("core", "Core One", 1234, CameraTimelapseExportProblem.None, false), culture);
        Assert.Equal("Core One", done.Title);
        Assert.Equal(Strings.ResourceManager.GetString(nameof(Strings.NotificationTimelapseExportDone), culture), done.Body);
        Assert.Contains(1234.ToString(culture), done.Detail, StringComparison.Ordinal);

        HashSet<string> reasons = [];
        foreach (CameraTimelapseExportProblem problem in Enum.GetValues<CameraTimelapseExportProblem>().Where(static p => p is not CameraTimelapseExportProblem.None and not CameraTimelapseExportProblem.Cancelled))
        {
            NotificationLines failed = TimelapseExportNotificationText.Lines(new TimelapseExportNotification(null, null, 0, problem, false), culture);
            Assert.Equal("LayerPeek", failed.Title);
            Assert.Equal(Strings.ResourceManager.GetString(nameof(Strings.NotificationTimelapseExportFailed), culture), failed.Body);
            Assert.False(string.IsNullOrWhiteSpace(failed.Detail));
            Assert.DoesNotContain("{", failed.Detail, StringComparison.Ordinal);
            Assert.True(reasons.Add(failed.Detail!), $"{language}: {problem} has the reason of another problem.");
            if (language != "en")
            {
                Assert.NotEqual(TimelapseExportNotificationText.Reason(problem, english), failed.Detail);
            }
        }

        if (language != "en")
        {
            Assert.NotEqual(TimelapseExportNotificationText.Lines(new TimelapseExportNotification(null, null, 1, CameraTimelapseExportProblem.None, false), english).Body, done.Body);
        }
    }

    [Fact]
    public void TheToastHasNoLaunchArgumentAndANameCannotAddMarkup()
    {
        TimelapseExportNotification notification = new("core", "Core\"/><action/>One", 42, CameraTimelapseExportProblem.None, PlaySound: false);

        XDocument toast = XDocument.Parse(ToastContentBuilder.BuildTimelapseExport(notification, CultureInfo.GetCultureInfo("cs-CZ")));

        Assert.Null(toast.Root!.Attribute("launch"));
        Assert.Equal("foreground", toast.Root.Attribute("activationType")?.Value);
        Assert.Empty(toast.Descendants("action"));
        Assert.Equal(["Core\"/><action/>One", "Timelapse exportován do JPEG", "Snímků: 42, ve složce vedle timelapse."], toast.Descendants("text").Select(static text => text.Value));
        Assert.Equal("true", toast.Descendants("audio").Single().Attribute("silent")?.Value);
    }

    [Fact]
    public async Task TheWindowsToastIsShownWithoutATag()
    {
        FakeToastPlatform platform = new();
        using ToastNotificationService service = new(platform, static _ => { }, static () => CultureInfo.GetCultureInfo("en-US"), new ListLogger<ToastNotificationService>());

        service.ShowTimelapseExportFinished(new TimelapseExportNotification("core", "Core One", 0, CameraTimelapseExportProblem.DiskFull, PlaySound: true));
        await Waiting.ForAsync(() => platform.Session.Shown.Count == 1);

        Assert.Equal([null], platform.Session.ShownTags);
        Assert.Contains("The drive is full.", platform.Session.Shown[0], StringComparison.Ordinal);
        platform.Session.Release();
    }

    [Fact]
    public async Task TheMacNotificationHasTheTextsAndNoLaunchArgument()
    {
        FakeUserNotificationCenter center = new();
        using UserNotificationService service = new(center, static () => CultureInfo.GetCultureInfo("en-US"), new ListLogger<UserNotificationService>());

        service.ShowTimelapseExportFinished(new TimelapseExportNotification("core", "", 7, CameraTimelapseExportProblem.None, PlaySound: false));
        await Waiting.ForAsync(() => center.Posted.Count == 1);

        UserNotificationRequest request = center.Posted[0];
        Assert.Equal("core", request.Title);
        Assert.Equal("Timelapse exported to JPEG\n7 frames in a folder next to the timelapse.", request.Body);
        Assert.False(request.PlaySound);
        Assert.Null(request.LaunchArgument);
    }

    private sealed class ExportSink : INotificationService
    {
        private readonly List<TimelapseExportNotification> _all = [];

        public bool Throw { get; init; }

        public IReadOnlyList<TimelapseExportNotification> All
        {
            get
            {
                lock (_all)
                {
                    return [.. _all];
                }
            }
        }

        public void Show(PrintNotification notification)
        {
        }

        public void ShowTimelapseExportFinished(TimelapseExportNotification notification)
        {
            lock (_all)
            {
                _all.Add(notification);
            }

            if (Throw)
            {
                throw new InvalidOperationException("The notification service failed.");
            }
        }
    }

    private sealed class NoFeeds : ICameraFeedFactory
    {
        public ICameraFeed Create(Uri streamUri) => throw new CameraFeedException("No decoder.");
    }

    private sealed class NoEncoders : ICameraJpegSequenceEncoderFactory
    {
        public ICameraJpegSequenceEncoder Create(int quality) => throw new InvalidOperationException("No encoder.");
    }
}
