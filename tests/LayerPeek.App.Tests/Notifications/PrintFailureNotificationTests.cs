using System.Globalization;
using System.Xml.Linq;

using Microsoft.Extensions.Logging.Abstractions;

using LayerPeek.App.Detection;
using LayerPeek.App.Localization;
using LayerPeek.App.Notifications;
using LayerPeek.App.Resources;
using LayerPeek.App.Tests.Tray;
using LayerPeek.Core.Detection;
using LayerPeek.Core.Settings;

namespace LayerPeek.App.Tests.Notifications;

/// <summary>
/// The notification of a suspected print failure (T-154, user decisions 2026-11-04): the same texts on both OS in every
/// language, the snapshot as an image file (Windows: the saved snapshot or a temporary copy, macOS: always a copy, the
/// folder keeps a few), a click opens the camera window and the button "False alarm" records the alarm. Only fakes: nothing
/// reaches Windows or macOS.
/// </summary>
public sealed class PrintFailureNotificationTests : IDisposable
{
    private const string AlarmId = "0123456789abcdef0123456789abcdef";

    private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 1, 2, 3, 0xFF, 0xD9];

    private readonly string _root = Path.Combine(Path.GetTempPath(), "LayerPeek.PrintFailureNotificationTests." + Guid.NewGuid().ToString("N"));

    public static TheoryData<string> Languages => [.. AppLanguages.Supported];

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    internal static PrintFailureAlarm Alarm(string id = AlarmId, string printerName = "Core One", string? fileName = "Benchy_0.4n_PLA.bgcode", string? snapshotPath = null, byte[]? jpeg = null) => new()
    {
        Id = id,
        PrinterId = "core",
        PrinterName = printerName,
        DetectorId = DetectorTexts.BedChange,
        Timestamp = DateTimeOffset.UnixEpoch,
        Score = 0.08f,
        Threshold = 0.04f,
        Sensitivity = 50,
        Context = new PrintFailureContext(7, 1.2, 12, TimeSpan.FromMinutes(5), DetectionRegion.CoreOneBed),
        FileName = fileName,
        SnapshotPath = snapshotPath,
        SnapshotJpeg = jpeg ?? Jpeg,
    };

    private AlarmNotificationImages Images() => new(Path.Combine(_root, "images"), NullLogger.Instance);

    // ---- Texts ----

    [Theory]
    [MemberData(nameof(Languages))]
    public void TheTextsAreTranslatedWithTheKindOfFailureAndTheFile(string language)
    {
        CultureInfo culture = CultureInfo.GetCultureInfo(language);

        NotificationLines lines = ToastContentBuilder.PrintFailureLines(new PrintFailureNotification(Alarm(), PlaySound: true), culture);

        Assert.Equal("Core One", lines.Title);
        Assert.Contains(Strings.ResourceManager.GetString(nameof(Strings.DetectorBedChange), culture)!, lines.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("{", lines.Body, StringComparison.Ordinal);
        Assert.Equal("Benchy_0.4n_PLA.bgcode", lines.Detail);
        Assert.False(string.IsNullOrWhiteSpace(PrintFailureNotificationText.FalseAlarmAction(culture)));
        if (language != "en")
        {
            Assert.NotEqual(ToastContentBuilder.PrintFailureLines(new PrintFailureNotification(Alarm(), true), CultureInfo.GetCultureInfo("en")).Body, lines.Body);
            Assert.NotEqual(PrintFailureNotificationText.FalseAlarmAction(CultureInfo.GetCultureInfo("en")), PrintFailureNotificationText.FalseAlarmAction(culture));
        }
    }

    [Fact]
    public void AnEmptyNameIsTheIdAndAnUnknownDetectorIsNamedByItsId()
    {
        NotificationLines lines = ToastContentBuilder.PrintFailureLines(
            new PrintFailureNotification(Alarm(printerName: " ", fileName: null) with { DetectorId = "future-kind" }, PlaySound: false),
            CultureInfo.GetCultureInfo("en"));

        Assert.Equal("core", lines.Title);
        Assert.Equal("Possible print failure – Detector future-kind", lines.Body);
        Assert.Null(lines.Detail);
    }

    // ---- Windows toast ----

    [Fact]
    public void TheToastOpensTheCameraHasTheImageAndTheFalseAlarmButton()
    {
        string image = Path.Combine(_root, "a b&c", "alarm.jpg");

        XDocument toast = XDocument.Parse(ToastContentBuilder.BuildPrintFailure(new PrintFailureNotification(Alarm(printerName: "Core\"/><action/>One"), PlaySound: true), CultureInfo.GetCultureInfo("cs-CZ"), image));

        Assert.True(ToastLaunchArgument.TryGetCameraPrinterId(toast.Root!.Attribute("launch")?.Value, out string? printerId));
        Assert.Equal("core", printerId);
        Assert.Equal("foreground", toast.Root.Attribute("activationType")?.Value);
        Assert.Equal(["Core\"/><action/>One", "Možná chyba tisku – Změna na podložce", "Benchy_0.4n_PLA.bgcode"], toast.Descendants("text").Select(static text => text.Value));
        Assert.Equal(new Uri(image).AbsoluteUri, toast.Descendants("image").Single().Attribute("src")?.Value);
        XElement action = toast.Descendants("action").Single();
        Assert.Equal("Falešný poplach", action.Attribute("content")?.Value);
        Assert.Equal(ToastLaunchArgument.ForFalseAlarm(AlarmId), action.Attribute("arguments")?.Value);
        Assert.Equal("foreground", action.Attribute("activationType")?.Value);
        // The sound of a printer error: the default sound.
        Assert.Empty(toast.Descendants("audio"));
    }

    [Fact]
    public void WithoutAnImageOrSoundOrAValidIdTheToastHasNeither()
    {
        XDocument toast = XDocument.Parse(ToastContentBuilder.BuildPrintFailure(new PrintFailureNotification(Alarm(id: "NOT valid"), PlaySound: false), CultureInfo.GetCultureInfo("en"), imagePath: null));

        Assert.Empty(toast.Descendants("image"));
        Assert.Empty(toast.Descendants("action"));
        Assert.Equal("true", toast.Descendants("audio").Single().Attribute("silent")?.Value);

        // A relative path is never turned into an image.
        XDocument relative = XDocument.Parse(ToastContentBuilder.BuildPrintFailure(new PrintFailureNotification(Alarm(), PlaySound: false), CultureInfo.GetCultureInfo("en"), "alarm.jpg"));
        Assert.Empty(relative.Descendants("image"));
    }

    [Fact]
    public async Task TheWindowsToastUsesTheSavedSnapshotOrATemporaryCopy()
    {
        string saved = Path.Combine(_root, "Recordings", "Core One", "2026-11-04_10-00-00_alarm-bed-change.jpg");
        Directory.CreateDirectory(Path.GetDirectoryName(saved)!);
        await File.WriteAllBytesAsync(saved, Jpeg, TestContext.Current.CancellationToken);
        FakeToastPlatform platform = new();
        AlarmNotificationImages images = Images();
        using ToastNotificationService service = new(platform, static _ => { }, static () => CultureInfo.GetCultureInfo("en-US"), new ListLogger<ToastNotificationService>()) { AlarmImages = images };

        service.ShowPrintFailure(new PrintFailureNotification(Alarm(snapshotPath: saved), PlaySound: true));
        service.ShowPrintFailure(new PrintFailureNotification(Alarm(id: "fedcba9876543210fedcba9876543210"), PlaySound: true));
        await Waiting.ForAsync(() => platform.Session.Shown.Count == 2);

        Assert.Equal([null, null], platform.Session.ShownTags);
        Assert.Equal(new Uri(saved).AbsoluteUri, XDocument.Parse(platform.Session.Shown[0]).Descendants("image").Single().Attribute("src")?.Value);
        // Saving was off: the JPEG in memory went into the folder of the notification images.
        string copy = Path.Combine(images.Folder, "alarm-fedcba9876543210fedcba9876543210.jpg");
        Assert.Equal(new Uri(copy).AbsoluteUri, XDocument.Parse(platform.Session.Shown[1]).Descendants("image").Single().Attribute("src")?.Value);
        Assert.Equal(Jpeg, await File.ReadAllBytesAsync(copy, TestContext.Current.CancellationToken));
        platform.Session.Release();
    }

    // ---- macOS ----

    [Fact]
    public async Task TheMacNotificationAttachesACopyAndHasTheButton()
    {
        string saved = Path.Combine(_root, "Recordings", "alarm.jpg");
        Directory.CreateDirectory(Path.GetDirectoryName(saved)!);
        await File.WriteAllBytesAsync(saved, Jpeg, TestContext.Current.CancellationToken);
        FakeUserNotificationCenter center = new();
        AlarmNotificationImages images = Images();
        using UserNotificationService service = new(center, static () => CultureInfo.GetCultureInfo("en-US"), new ListLogger<UserNotificationService>()) { AlarmImages = images };

        service.ShowPrintFailure(new PrintFailureNotification(Alarm(snapshotPath: saved), PlaySound: true));
        await Waiting.ForAsync(() => center.Posted.Count == 1);

        UserNotificationRequest request = center.Posted[0];
        Assert.Equal("Core One", request.Title);
        Assert.Equal("Possible print failure – Change on the bed\nBenchy_0.4n_PLA.bgcode", request.Body);
        Assert.True(request.PlaySound);
        Assert.True(ToastLaunchArgument.TryGetCameraPrinterId(request.LaunchArgument, out string? printerId));
        Assert.Equal("core", printerId);
        Assert.Equal(new UserNotificationAction("False alarm", ToastLaunchArgument.ForFalseAlarm(AlarmId)), request.Action);
        // Never the saved snapshot itself: macOS moves an attached file into its store.
        Assert.Equal(Path.Combine(images.Folder, "alarm-" + AlarmId + ".jpg"), request.AttachmentPath);
        Assert.True(File.Exists(saved));
    }

    [Fact]
    public async Task WithoutASnapshotTheMacNotificationHasNoAttachment()
    {
        FakeUserNotificationCenter center = new();
        using UserNotificationService service = new(center, static () => CultureInfo.GetCultureInfo("en-US"), new ListLogger<UserNotificationService>()) { AlarmImages = Images() };

        service.ShowPrintFailure(new PrintFailureNotification(Alarm(jpeg: []), PlaySound: false));
        await Waiting.ForAsync(() => center.Posted.Count == 1);

        Assert.Null(center.Posted[0].AttachmentPath);
        Assert.NotNull(center.Posted[0].Action);
    }

    // ---- Image files ----

    [Fact]
    public void TheFolderKeepsOnlyTheNewestImagesAndDropsLeftovers()
    {
        AlarmNotificationImages images = Images();
        Directory.CreateDirectory(images.Folder);
        string leftover = Path.Combine(images.Folder, "alarm-old.jpg.partial");
        File.WriteAllBytes(leftover, Jpeg);
        string foreign = Path.Combine(images.Folder, "notes.txt");
        File.WriteAllText(foreign, "kept");
        List<string> paths = [];
        for (int i = 0; i < AlarmNotificationImages.KeptFiles + 3; i++)
        {
            string path = images.Prepare(Alarm(id: $"alarm{i:D2}"), useSavedSnapshot: false)!;
            // Distinct write times, oldest first.
            File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(i - 100));
            paths.Add(path);
        }

        string[] left = [.. Directory.GetFiles(images.Folder, "alarm-*").Select(Path.GetFileName).Order(StringComparer.Ordinal)!];
        Assert.Equal([.. paths.Skip(3).Select(Path.GetFileName)], left);
        Assert.False(File.Exists(leftover));
        Assert.True(File.Exists(foreign));
    }

    [Fact]
    public void AMissingTooLargeOrRelativeSavedSnapshotFallsBackToTheCopy()
    {
        AlarmNotificationImages images = Images();

        Assert.Equal(Path.Combine(images.Folder, "alarm-" + AlarmId + ".jpg"), images.Prepare(Alarm(snapshotPath: Path.Combine(_root, "gone.jpg")), useSavedSnapshot: true));
        Assert.Equal(Path.Combine(images.Folder, "alarm-" + AlarmId + ".jpg"), images.Prepare(Alarm(snapshotPath: "relative.jpg"), useSavedSnapshot: true));
        Assert.Null(images.Prepare(Alarm(jpeg: new byte[AlarmNotificationImages.MaxImageBytes + 1]), useSavedSnapshot: false));
        // An id that is no file name (never from the service, which uses hex digits) gets no file.
        Assert.Null(images.Prepare(Alarm(id: "../x"), useSavedSnapshot: false));
    }

    [Fact]
    public void AFolderThatCannotBeWrittenGivesNoImage()
    {
        string file = Path.Combine(_root, "file");
        Directory.CreateDirectory(_root);
        File.WriteAllText(file, "not a folder");

        Assert.Null(new AlarmNotificationImages(Path.Combine(file, "images"), NullLogger.Instance).Prepare(Alarm(), useSavedSnapshot: false));
    }

    // ---- False alarm ----

    [Fact]
    public async Task FalseAlarmRecordsAKnownAlarmOnlyAndChecksTheId()
    {
        List<PrintFailureAlarm> recorded = [];
        List<string> asked = [];
        PrintFailureAlarm known = Alarm();
        FalseAlarmReporter reporter = new(
            id =>
            {
                asked.Add(id);
                return id == AlarmId ? known : null;
            },
            (alarm, _) =>
            {
                recorded.Add(alarm);
                return Task.FromResult<string?>("/data/false-alarms/x.json");
            },
            NullLogger<FalseAlarmReporter>.Instance);

        Assert.True(await reporter.ReportAsync(AlarmId, CancellationToken.None));
        Assert.False(await reporter.ReportAsync("00000000000000000000000000000000", CancellationToken.None));
        Assert.False(await reporter.ReportAsync("", CancellationToken.None));
        Assert.False(await reporter.ReportAsync(null!, CancellationToken.None));
        Assert.False(await reporter.ReportAsync("UPPER", CancellationToken.None));
        Assert.False(await reporter.ReportAsync(new string('a', ToastLaunchArgument.MaxAlarmIdLength + 1), CancellationToken.None));

        Assert.Equal([known], recorded);
        // Invalid ids never reach the service (TryGetAlarm throws for null).
        Assert.Equal([AlarmId, "00000000000000000000000000000000"], asked);
    }

    [Fact]
    public async Task ARecordThatCannotBeSavedOrAServiceThatIsGoneIsNoSuccess()
    {
        FalseAlarmReporter notSaved = new(static _ => Alarm(), static (_, _) => Task.FromResult<string?>(null), NullLogger<FalseAlarmReporter>.Instance);
        FalseAlarmReporter gone = new(static _ => throw new ObjectDisposedException("services"), static (_, _) => Task.FromResult<string?>("x"), NullLogger<FalseAlarmReporter>.Instance);

        Assert.False(await notSaved.ReportAsync(AlarmId, CancellationToken.None));
        Assert.False(await gone.ReportAsync(AlarmId, CancellationToken.None));
    }
}
