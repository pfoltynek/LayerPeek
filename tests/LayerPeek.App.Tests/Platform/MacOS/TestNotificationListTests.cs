#if DEBUG
using System.Text.Json.Nodes;

using LayerPeek.App.Composition;
using LayerPeek.App.Platform.MacOS;

namespace LayerPeek.App.Tests.Platform.MacOS;

/// <summary>
/// The output of <see cref="AppEnvironment.NotificationListArgument"/> (T-212) without a notification center: the JSON that
/// <c>tools/macos-verify.ps1</c> reads, and the exit codes.
/// </summary>
public sealed class TestNotificationListTests
{
    private static readonly NotificationListOutput Sample = new(
        AppEnvironment.TestBundleIdentifier,
        [
            new DeliveredNotificationInfo(
                "0f3c",
                "MK4 – dílna",
                Subtitle: null,
                "Možná chyba tisku – Detector e2e-alarm\nbenchy.bgcode",
                "layerpeek.with-action",
                ThreadIdentifier: string.Empty,
                HasSound: true,
                [new NotificationAttachmentInfo("snapshot", "public.jpeg")],
                "camera=printer-1",
                "false-alarm=a1",
                new DateTimeOffset(2026, 11, 15, 10, 20, 30, TimeSpan.Zero)),
            new DeliveredNotificationInfo("layerpeek-test-notification", "Test notification", null, "Works.", null, null, false, [], "settings=general", null, null),
        ],
        [new NotificationCategoryInfo("layerpeek.with-action", [new NotificationActionInfo("layerpeek.action", "Falešný poplach", 0)])]);

    [Fact]
    public void TheArgumentIsTheDocumentedOne() =>
        Assert.Equal("--layerpeek-e2e-notification-list", AppEnvironment.NotificationListArgument);

    [Fact]
    public void EveryFieldIsWrittenInCamelCaseAndNullsStay()
    {
        JsonObject json = JsonNode.Parse(TestNotificationList.Format(Sample))!.AsObject();

        Assert.Equal(["bundleIdentifier", "notifications", "categories"], json.Select(static p => p.Key));
        Assert.Equal(AppEnvironment.TestBundleIdentifier, (string?)json["bundleIdentifier"]);
        JsonObject alarm = json["notifications"]![0]!.AsObject();
        Assert.Equal(
            ["identifier", "title", "subtitle", "body", "categoryIdentifier", "threadIdentifier", "hasSound", "attachments", "launchArgument", "actionArgument", "deliveredAt"],
            alarm.Select(static p => p.Key));
        Assert.Equal("MK4 – dílna", (string?)alarm["title"]);
        Assert.Equal("Možná chyba tisku – Detector e2e-alarm\nbenchy.bgcode", (string?)alarm["body"]);
        Assert.True(alarm.ContainsKey("subtitle"));
        Assert.Null(alarm["subtitle"]);
        Assert.Equal(string.Empty, (string?)alarm["threadIdentifier"]);
        Assert.Equal("layerpeek.with-action", (string?)alarm["categoryIdentifier"]);
        Assert.True((bool)alarm["hasSound"]!);
        Assert.Equal("snapshot", (string?)alarm["attachments"]![0]!["identifier"]);
        Assert.Equal("public.jpeg", (string?)alarm["attachments"]![0]!["type"]);
        Assert.Equal("camera=printer-1", (string?)alarm["launchArgument"]);
        Assert.Equal("false-alarm=a1", (string?)alarm["actionArgument"]);
        Assert.Equal(new DateTimeOffset(2026, 11, 15, 10, 20, 30, TimeSpan.Zero), DateTimeOffset.Parse((string)alarm["deliveredAt"]!, System.Globalization.CultureInfo.InvariantCulture));

        JsonObject test = json["notifications"]![1]!.AsObject();
        Assert.False((bool)test["hasSound"]!);
        Assert.Empty(test["attachments"]!.AsArray());
        Assert.Null(test["actionArgument"]);
        Assert.Null(test["deliveredAt"]);

        JsonObject category = json["categories"]![0]!.AsObject();
        Assert.Equal("layerpeek.with-action", (string?)category["identifier"]);
        JsonObject action = category["actions"]![0]!.AsObject();
        Assert.Equal(["identifier", "title", "options"], action.Select(static p => p.Key));
        Assert.Equal("layerpeek.action", (string?)action["identifier"]);
        Assert.Equal("Falešný poplach", (string?)action["title"]);
        Assert.Equal(0, (long)action["options"]!);
    }

    [Fact]
    public void AnEmptyCenterIsAnObjectWithEmptyLists()
    {
        JsonObject json = JsonNode.Parse(TestNotificationList.Format(new NotificationListOutput(AppEnvironment.TestBundleIdentifier, [], [])))!.AsObject();

        Assert.Empty(json["notifications"]!.AsArray());
        Assert.Empty(json["categories"]!.AsArray());
    }

    [Fact]
    public void WriteGivesTheJsonOnStdoutAndExitCodeZero()
    {
        using StringWriter output = new();
        using StringWriter error = new();

        Assert.Equal(0, TestNotificationList.Write(() => Sample, output, error));

        Assert.Equal(TestNotificationList.Format(Sample) + Environment.NewLine, output.ToString());
        Assert.Equal(string.Empty, error.ToString());
    }

    /// <summary>The test host is no app bundle: refused before the notification center is touched.</summary>
    [Fact]
    public void OutsideTheTestBundleTheListIsRefused()
    {
        if (!OperatingSystem.IsMacOS())
        {
            Assert.Skip("The notification center of macOS.");
            return;
        }

        using StringWriter output = new();
        using StringWriter error = new();

        int code = TestNotificationList.ListFromCommandLine(output, error);

        Assert.Equal(TestNotificationCleanup.FailedExitCode, code);
        Assert.Equal(string.Empty, output.ToString());
        Assert.Contains("only in the test bundle", error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void AFailedReadWritesOnlyTheReasonAndExitCodeOne()
    {
        using StringWriter output = new();
        using StringWriter error = new();

        int code = TestNotificationList.Write(static () => throw new TimeoutException("no reply"), output, error);

        Assert.Equal(TestNotificationCleanup.FailedExitCode, code);
        Assert.Equal(string.Empty, output.ToString());
        Assert.Contains("TimeoutException no reply", error.ToString(), StringComparison.Ordinal);
    }
}
#endif
