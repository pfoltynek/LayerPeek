#if DEBUG
using System.Runtime.Versioning;
using System.Text.Json;
using System.Text.Json.Serialization;

using LayerPeek.App.Composition;

namespace LayerPeek.App.Platform.MacOS;

/// <summary>One delivered notification of the test bundle as the notification center keeps it (T-212).</summary>
/// <param name="Identifier">The identifier of its request (a new request with the same one replaces it).</param>
/// <param name="HasSound">Whether it was posted with a sound.</param>
/// <param name="LaunchArgument">What a click hands to the app (<c>userInfo</c>), or <see langword="null"/>.</param>
/// <param name="ActionArgument">What its button hands to the app (<c>userInfo</c>), or <see langword="null"/>.</param>
/// <param name="DeliveredAt">When it was delivered (UTC), or <see langword="null"/>.</param>
internal sealed record DeliveredNotificationInfo(
    string Identifier,
    string? Title,
    string? Subtitle,
    string? Body,
    string? CategoryIdentifier,
    string? ThreadIdentifier,
    bool HasSound,
    IReadOnlyList<NotificationAttachmentInfo> Attachments,
    string? LaunchArgument,
    string? ActionArgument,
    DateTimeOffset? DeliveredAt);

/// <summary>An attachment of a delivered notification: its identifier and its type (a uniform type identifier, e.g. <c>public.jpeg</c>).</summary>
internal sealed record NotificationAttachmentInfo(string? Identifier, string? Type);

/// <summary>A registered category of the test bundle and its buttons (T-212).</summary>
internal sealed record NotificationCategoryInfo(string Identifier, IReadOnlyList<NotificationActionInfo> Actions);

/// <summary>A button of a category: identifier, label and <c>UNNotificationActionOptions</c> (4: brings the app to the front).</summary>
internal sealed record NotificationActionInfo(string Identifier, string? Title, long Options);

/// <summary>What <see cref="AppEnvironment.NotificationListArgument"/> prints.</summary>
internal sealed record NotificationListOutput(
    string BundleIdentifier,
    IReadOnlyList<DeliveredNotificationInfo> Notifications,
    IReadOnlyList<NotificationCategoryInfo> Categories);

/// <summary>
/// Debug builds only (T-212): the command line mode <see cref="AppEnvironment.NotificationListArgument"/> of the test bundle,
/// which prints the delivered notifications and the registered categories of <see cref="AppEnvironment.TestBundleIdentifier"/>
/// as JSON for the content checks of <c>tools/macos-verify.ps1</c>. Reads only; refused in any other bundle (like
/// <see cref="TestNotificationCleanup.StatusFromCommandLine"/>).
/// </summary>
internal static class TestNotificationList
{
    /// <summary>The JSON of <paramref name="list"/> (camelCase, indented, every property also when <see langword="null"/>).</summary>
    public static string Format(NotificationListOutput list)
    {
        ArgumentNullException.ThrowIfNull(list);
        return JsonSerializer.Serialize(list, TestNotificationListJsonContext.Default.NotificationListOutput);
    }

    /// <summary>
    /// Writes the JSON of what <paramref name="read"/> returns to <paramref name="output"/>; a failure of
    /// <paramref name="read"/> writes nothing there, only the reason to <paramref name="error"/>.
    /// </summary>
    /// <returns>0, or <see cref="TestNotificationCleanup.FailedExitCode"/>.</returns>
    public static int Write(Func<NotificationListOutput> read, TextWriter output, TextWriter error)
    {
        ArgumentNullException.ThrowIfNull(read);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);
        string json;
        try
        {
            json = Format(read());
        }
        catch (Exception ex)
        {
            error.WriteLine($"Reading the notifications failed: {ex.GetType().Name} {ex.Message}");
            error.Flush();
            return TestNotificationCleanup.FailedExitCode;
        }

        output.WriteLine(json);
        output.Flush();
        return 0;
    }

    /// <summary>The command line mode on macOS: only as the executable of the test bundle.</summary>
    [SupportedOSPlatform("macos")]
    public static int ListFromCommandLine(TextWriter output, TextWriter error)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);
        if (TestNotificationCleanup.OpenTestCenter(out string? reason) is not { } center)
        {
            error.WriteLine($"The notifications are listed only in the test bundle: {reason}.");
            error.Flush();
            return TestNotificationCleanup.FailedExitCode;
        }

        // Program.Main before anything else: no synchronization context, so waiting here cannot deadlock.
        return Write(
            () => new NotificationListOutput(
                center.BundleIdentifier,
                center.ListDeliveredAsync(CancellationToken.None).GetAwaiter().GetResult(),
                center.ListCategoriesAsync(CancellationToken.None).GetAwaiter().GetResult()),
            output,
            error);
    }
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.Never)]
[JsonSerializable(typeof(NotificationListOutput))]
internal sealed partial class TestNotificationListJsonContext : JsonSerializerContext;
#endif
