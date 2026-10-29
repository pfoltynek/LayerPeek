using System.Runtime.InteropServices;
using System.Runtime.Versioning;

using Microsoft.Extensions.Logging;

using LayerPeek.App.Notifications;

namespace LayerPeek.App.Platform.MacOS;

/// <summary>
/// <c>UNUserNotificationCenter</c> of the User Notifications framework (T-020), through the Objective-C runtime
/// (<see cref="ObjCRuntime"/>, <see cref="ObjCBlocks"/>; Native AOT safe, no new package, no reflection).
/// <para>
/// Opened only for the expected bundle identifier (<see cref="TryOpen"/>): the production app in its <c>LayerPeek.app</c>,
/// in the Debug test mode with notifications only the test bundle <c>…LayerPeek.E2E</c>. A process without a bundle
/// identifier (a development build, the test host) never calls <c>currentNotificationCenter</c>, which would raise an
/// Objective-C exception there.
/// </para>
/// <para>
/// Clicks: one delegate object of a class registered at run time (<see cref="DelegateClassName"/>) reports the launch
/// argument of a clicked notification (<see cref="InstallClickHandler"/>) and lets notifications show as banners while the
/// app is active. The delegate is set before AppKit finishes launching, so a click that started the app reaches it too.
/// </para>
/// <para>
/// The completion handlers are global blocks with their state in static fields: there is one notification center (and one
/// delegate) per process. Each kind of question takes turns (<see cref="NativeReply{T}"/>).
/// </para>
/// </summary>
[SupportedOSPlatform("macos")]
internal sealed partial class MacUserNotificationCenter : IUserNotificationCenter
{
    /// <summary>The key of the launch argument in the <c>userInfo</c> of a notification.</summary>
    public const string LaunchArgumentKey = "layerpeek.launch";

    /// <summary>The class of the delegate object, registered once per process.</summary>
    public const string DelegateClassName = "LayerPeekUserNotificationCenterDelegate";

    /// <summary>The action of a click on the notification itself (<c>UNNotificationDefaultActionIdentifier</c>).</summary>
    private const string DefaultActionIdentifier = "com.apple.UNNotificationDefaultActionIdentifier";

    private const string UserNotificationsPath = "/System/Library/Frameworks/UserNotifications.framework/UserNotifications";
    private const string FoundationPath = "/System/Library/Frameworks/Foundation.framework/Foundation";

    /// <summary><c>UNAuthorizationOptionSound | UNAuthorizationOptionAlert</c>.</summary>
    private const nint AlertAndSound = 2 | 4;

    /// <summary>
    /// <c>UNNotificationPresentationOptionSound | List | Banner</c>: while the app is active (an open settings window) the
    /// notification still shows as a banner; the sound plays only when the notification has one.
    /// </summary>
    private const nuint PresentWhileActive = 2 | 8 | 16;

    private static readonly TimeSpan ReplyTimeout = TimeSpan.FromSeconds(10);

    /// <summary>The user may take a while to answer the first question of the OS.</summary>
    private static readonly TimeSpan AuthorizationTimeout = TimeSpan.FromMinutes(10);

    private static readonly NativeReply<(bool Granted, long ErrorCode)> AuthorizationReply = new();
    private static readonly NativeReply<long> SettingsReply = new();
    private static readonly NativeReply<long> DeliveredReply = new();

    private static readonly Lazy<Blocks> CompletionBlocks = new(static () => new Blocks());

    private static MacUserNotificationCenter? s_shared;
    private static Action<string?>? s_clicked;
    private static ILogger? s_logger;
    private static nint s_delegate;

    private readonly nint _center;
    private readonly ILogger _logger;

    private MacUserNotificationCenter(nint center, string bundleIdentifier, ILogger logger)
    {
        _center = center;
        BundleIdentifier = bundleIdentifier;
        _logger = logger;
    }

    /// <summary>
    /// The center <c>Program.Main</c> opened for this run (<see cref="Publish"/>), or <see langword="null"/>: no
    /// notifications in this process (no bundle, another bundle, the test mode without notifications, the test host).
    /// </summary>
    public static MacUserNotificationCenter? Shared => Volatile.Read(ref s_shared);

    public string BundleIdentifier { get; }

    /// <summary>
    /// The notification center when the process runs as the bundle <paramref name="expectedBundleIdentifier"/>; otherwise
    /// <see langword="null"/> with the reason. Asks nothing and shows nothing.
    /// </summary>
    /// <param name="expectedBundleIdentifier">
    /// The only identity this run may use (<c>AppEnvironment.NotificationBundleIdentifier</c>); <see langword="null"/>: none.
    /// </param>
    /// <param name="mainBundleIdentifier">The identifier of the process (<see cref="MacMainBundle.Identifier"/>).</param>
    public static MacUserNotificationCenter? TryOpen(string? expectedBundleIdentifier, Func<string?> mainBundleIdentifier, ILogger logger, out string? reason)
    {
        ArgumentNullException.ThrowIfNull(mainBundleIdentifier);
        ArgumentNullException.ThrowIfNull(logger);
        if (expectedBundleIdentifier is null)
        {
            reason = "this run shows no notifications";
            return null;
        }

        string? actual = mainBundleIdentifier();
        if (actual is null)
        {
            // dotnet run, the test host: currentNotificationCenter would throw an Objective-C exception.
            reason = "the app does not run from an app bundle";
            return null;
        }

        if (!string.Equals(actual, expectedBundleIdentifier, StringComparison.Ordinal))
        {
            reason = $"the app bundle is '{actual}', not '{expectedBundleIdentifier}'";
            return null;
        }

        if (!NativeLibrary.TryLoad(FoundationPath, out _) || !NativeLibrary.TryLoad(UserNotificationsPath, out _))
        {
            reason = "the User Notifications framework cannot be loaded";
            return null;
        }

        nint center = ObjCRuntime.InAutoreleasePool(static () =>
        {
            nint type = ObjCRuntime.Class("UNUserNotificationCenter");
            // A singleton of the framework: never released.
            return type == 0 ? 0 : ObjCRuntime.Send(type, ObjCRuntime.Selector("currentNotificationCenter"));
        });
        if (center == 0)
        {
            reason = "UNUserNotificationCenter is not available";
            return null;
        }

        reason = null;
        return new MacUserNotificationCenter(center, actual, logger);
    }

    /// <summary>Makes this the center of the process for <see cref="Shared"/> (once; later calls are ignored).</summary>
    public void Publish()
    {
        Interlocked.CompareExchange(ref s_logger, _logger, null);
        Interlocked.CompareExchange(ref s_shared, this, null);
    }

    /// <summary>
    /// Sets the delegate that reports clicks to <paramref name="clicked"/> (the launch argument, <see langword="null"/>
    /// when the notification has none; any thread). Call once, on the main thread, before AppKit finishes launching.
    /// </summary>
    /// <exception cref="InvalidOperationException">The runtime refused the delegate class.</exception>
    public unsafe void InstallClickHandler(Action<string?> clicked)
    {
        ArgumentNullException.ThrowIfNull(clicked);
        Volatile.Write(ref s_clicked, clicked);
        Interlocked.CompareExchange(ref s_logger, _logger, null);
        if (Volatile.Read(ref s_delegate) != 0)
        {
            return;
        }

        ObjCRuntime.InAutoreleasePool(() =>
        {
            nint type = ObjCRuntime.DefineClass(
                DelegateClassName,
                "UNUserNotificationCenterDelegate",
                [
                    ("userNotificationCenter:didReceiveNotificationResponse:withCompletionHandler:", (nint)(delegate* unmanaged<nint, nint, nint, nint, nint, void>)&DidReceiveResponse, "v@:@@@?"),
                    ("userNotificationCenter:willPresentNotification:withCompletionHandler:", (nint)(delegate* unmanaged<nint, nint, nint, nint, nint, void>)&WillPresent, "v@:@@@?"),
                ]);
            // The center holds its delegate weakly: this object is kept for the life of the process.
            nint instance = ObjCRuntime.Send(ObjCRuntime.Send(type, ObjCRuntime.Selector("alloc")), ObjCRuntime.Selector("init"));
            if (instance == 0)
            {
                throw new InvalidOperationException("The notification delegate could not be created.");
            }

            ObjCRuntime.Send(_center, ObjCRuntime.Selector("setDelegate:"), instance);
            Volatile.Write(ref s_delegate, instance);
            return true;
        });
    }

    public async Task<bool> RequestAuthorizationAsync(CancellationToken cancellationToken)
    {
        nint block = CompletionBlocks.Value.Authorization;
        (bool granted, long errorCode) = await AuthorizationReply.AskAsync(
            () => ObjCRuntime.InAutoreleasePool(() => ObjCRuntime.Send(_center, ObjCRuntime.Selector("requestAuthorizationWithOptions:completionHandler:"), AlertAndSound, block)),
            AuthorizationTimeout,
            cancellationToken).ConfigureAwait(false);
        if (errorCode != 0)
        {
            LogAuthorizationError(_logger, errorCode);
        }

        return granted;
    }

    public async Task<NotificationPermission> GetPermissionAsync(CancellationToken cancellationToken)
    {
        nint block = CompletionBlocks.Value.Settings;
        long status = await SettingsReply.AskAsync(
            () => ObjCRuntime.InAutoreleasePool(() => ObjCRuntime.Send(_center, ObjCRuntime.Selector("getNotificationSettingsWithCompletionHandler:"), block)),
            ReplyTimeout,
            cancellationToken).ConfigureAwait(false);
        return PermissionOf(status);
    }

    /// <summary>
    /// <c>UNAuthorizationStatus</c>: 0 not determined, 1 denied, 2 authorized, 3 provisional, 4 ephemeral; -1 (no settings)
    /// and anything new are unknown.
    /// </summary>
    internal static NotificationPermission PermissionOf(long status) => status switch
    {
        0 => NotificationPermission.NotDetermined,
        1 => NotificationPermission.Denied,
        2 or 3 or 4 => NotificationPermission.Allowed,
        _ => NotificationPermission.Unknown,
    };

    public void Post(UserNotificationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        nint block = CompletionBlocks.Value.Posted;
        ObjCRuntime.InAutoreleasePool(() =>
        {
            nint content = ObjCRuntime.Send(ObjCRuntime.Send(RequiredClass("UNMutableNotificationContent"), ObjCRuntime.Selector("alloc")), ObjCRuntime.Selector("init"));
            if (content == 0)
            {
                throw new InvalidOperationException("UNMutableNotificationContent could not be created.");
            }

            try
            {
                ObjCRuntime.Send(content, ObjCRuntime.Selector("setTitle:"), ObjCRuntime.NSString(request.Title));
                ObjCRuntime.Send(content, ObjCRuntime.Selector("setBody:"), ObjCRuntime.NSString(request.Body));
                if (request.PlaySound)
                {
                    ObjCRuntime.Send(content, ObjCRuntime.Selector("setSound:"), ObjCRuntime.Send(RequiredClass("UNNotificationSound"), ObjCRuntime.Selector("defaultSound")));
                }

                if (request.LaunchArgument is { } argument)
                {
                    nint userInfo = ObjCRuntime.Send(
                        RequiredClass("NSDictionary"),
                        ObjCRuntime.Selector("dictionaryWithObject:forKey:"),
                        ObjCRuntime.NSString(argument),
                        ObjCRuntime.NSString(LaunchArgumentKey));
                    ObjCRuntime.Send(content, ObjCRuntime.Selector("setUserInfo:"), userInfo);
                }

                // No trigger: delivered right away.
                nint notification = ObjCRuntime.Send(
                    RequiredClass("UNNotificationRequest"),
                    ObjCRuntime.Selector("requestWithIdentifier:content:trigger:"),
                    ObjCRuntime.NSString(request.Identifier),
                    content,
                    0);
                if (notification == 0)
                {
                    throw new InvalidOperationException("UNNotificationRequest could not be created.");
                }

                ObjCRuntime.Send(_center, ObjCRuntime.Selector("addNotificationRequest:withCompletionHandler:"), notification, block);
            }
            finally
            {
                // The request keeps its own copy of the content.
                ObjCRuntime.Release(content);
            }

            return true;
        });
    }

    public void RemoveAll() => ObjCRuntime.InAutoreleasePool(() =>
    {
        ObjCRuntime.SendVoid(_center, ObjCRuntime.Selector("removeAllPendingNotificationRequests"));
        ObjCRuntime.SendVoid(_center, ObjCRuntime.Selector("removeAllDeliveredNotifications"));
        return true;
    });

    public async Task<int> CountDeliveredAsync(CancellationToken cancellationToken)
    {
        nint block = CompletionBlocks.Value.Delivered;
        long count = await DeliveredReply.AskAsync(
            () => ObjCRuntime.InAutoreleasePool(() => ObjCRuntime.Send(_center, ObjCRuntime.Selector("getDeliveredNotificationsWithCompletionHandler:"), block)),
            ReplyTimeout,
            cancellationToken).ConfigureAwait(false);
        return count < 0 ? throw new InvalidOperationException("The notification center did not list its notifications.") : (int)Math.Min(count, int.MaxValue);
    }

    private static nint RequiredClass(string name)
    {
        nint type = ObjCRuntime.Class(name);
        return type != 0 ? type : throw new InvalidOperationException($"The class {name} is not available.");
    }

    // ---- Callbacks of the OS (any thread). An exception escaping them would end the process: everything is caught. ----

    /// <summary><c>void (^)(BOOL granted, NSError *error)</c> of <c>requestAuthorizationWithOptions:completionHandler:</c>.</summary>
    [UnmanagedCallersOnly]
    private static void OnAuthorization(nint block, byte granted, nint error)
    {
        try
        {
            long code = error == 0 ? 0 : ObjCRuntime.InAutoreleasePool(() => ObjCRuntime.Describe(error).Code);
            AuthorizationReply.Complete((granted != 0, error == 0 ? 0 : code == 0 ? -1 : code));
        }
        catch (Exception)
        {
            AuthorizationReply.Complete((granted != 0, -1));
        }
    }

    /// <summary><c>void (^)(UNNotificationSettings *settings)</c> of <c>getNotificationSettingsWithCompletionHandler:</c>.</summary>
    [UnmanagedCallersOnly]
    private static void OnSettings(nint block, nint settings)
    {
        try
        {
            SettingsReply.Complete(settings == 0 ? -1 : ObjCRuntime.Send(settings, ObjCRuntime.Selector("authorizationStatus")));
        }
        catch (Exception)
        {
            SettingsReply.Complete(-1);
        }
    }

    /// <summary><c>void (^)(NSArray&lt;UNNotification *&gt; *notifications)</c> of <c>getDeliveredNotificationsWithCompletionHandler:</c>.</summary>
    [UnmanagedCallersOnly]
    private static void OnDelivered(nint block, nint notifications)
    {
        try
        {
            DeliveredReply.Complete(notifications == 0 ? -1 : ObjCRuntime.Send(notifications, ObjCRuntime.Selector("count")));
        }
        catch (Exception)
        {
            DeliveredReply.Complete(-1);
        }
    }

    /// <summary><c>void (^)(NSError *error)</c> of <c>addNotificationRequest:withCompletionHandler:</c>.</summary>
    [UnmanagedCallersOnly]
    private static void OnPosted(nint block, nint error)
    {
        if (error == 0 || Volatile.Read(ref s_logger) is not { } logger)
        {
            return;
        }

        try
        {
            (long code, string? description) = ObjCRuntime.InAutoreleasePool(() => ObjCRuntime.Describe(error));
            LogPostFailed(logger, code, description ?? "no description");
        }
        catch (Exception)
        {
            // The logger is gone (quitting); the failure is simply not recorded.
        }
    }

    /// <summary>
    /// <c>userNotificationCenter:didReceiveNotificationResponse:withCompletionHandler:</c>: a click on a notification. The
    /// completion handler is always called.
    /// </summary>
    [UnmanagedCallersOnly]
    private static void DidReceiveResponse(nint self, nint selector, nint center, nint response, nint completion)
    {
        try
        {
            (bool isClick, string? argument) = ObjCRuntime.InAutoreleasePool(() => ReadResponse(response));
            if (isClick)
            {
                Volatile.Read(ref s_clicked)?.Invoke(argument);
            }
        }
        catch (Exception ex)
        {
            try
            {
                if (Volatile.Read(ref s_logger) is { } logger)
                {
                    LogClickFailed(logger, ex);
                }
            }
            catch (Exception)
            {
                // The logger is gone (quitting); the click is simply lost.
            }
        }
        finally
        {
            ObjCBlocks.Invoke(completion);
        }
    }

    /// <summary>
    /// <c>userNotificationCenter:willPresentNotification:withCompletionHandler:</c>: a notification arrives while the app is
    /// active; it is shown like when the app is in the background.
    /// </summary>
    [UnmanagedCallersOnly]
    private static void WillPresent(nint self, nint selector, nint center, nint notification, nint completion) =>
        ObjCBlocks.Invoke(completion, PresentWhileActive);

    /// <summary>Whether the response is a click on the notification, and the launch argument it carries.</summary>
    private static (bool IsClick, string? Argument) ReadResponse(nint response)
    {
        if (response == 0)
        {
            return (false, null);
        }

        string? action = ObjCRuntime.ToText(ObjCRuntime.Send(response, ObjCRuntime.Selector("actionIdentifier")));
        if (!string.Equals(action, DefaultActionIdentifier, StringComparison.Ordinal))
        {
            // Dismissed (only reported for categories that ask for it) or another action: nothing to open.
            return (false, null);
        }

        nint notification = ObjCRuntime.Send(response, ObjCRuntime.Selector("notification"));
        nint request = notification == 0 ? 0 : ObjCRuntime.Send(notification, ObjCRuntime.Selector("request"));
        nint content = request == 0 ? 0 : ObjCRuntime.Send(request, ObjCRuntime.Selector("content"));
        nint userInfo = content == 0 ? 0 : ObjCRuntime.Send(content, ObjCRuntime.Selector("userInfo"));
        nint value = userInfo == 0 ? 0 : ObjCRuntime.Send(userInfo, ObjCRuntime.Selector("objectForKey:"), ObjCRuntime.NSString(LaunchArgumentKey));
        return (true, ObjCRuntime.IsKindOf(value, "NSString") ? ObjCRuntime.ToText(value) : null);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "macOS reported an error ({Code}) while asking for permission to show notifications.")]
    private static partial void LogAuthorizationError(ILogger logger, long code);

    [LoggerMessage(Level = LogLevel.Error, Message = "macOS did not show a notification (code {Code}): {Description}")]
    private static partial void LogPostFailed(ILogger logger, long code, string description);

    [LoggerMessage(Level = LogLevel.Error, Message = "Reading a click on a notification failed.")]
    private static partial void LogClickFailed(ILogger logger, Exception exception);

    /// <summary>The completion handlers, created once per process (never freed).</summary>
    private sealed unsafe class Blocks
    {
        public nint Authorization { get; } = ObjCBlocks.Global((nint)(delegate* unmanaged<nint, byte, nint, void>)&OnAuthorization);

        public nint Settings { get; } = ObjCBlocks.Global((nint)(delegate* unmanaged<nint, nint, void>)&OnSettings);

        public nint Delivered { get; } = ObjCBlocks.Global((nint)(delegate* unmanaged<nint, nint, void>)&OnDelivered);

        public nint Posted { get; } = ObjCBlocks.Global((nint)(delegate* unmanaged<nint, nint, void>)&OnPosted);
    }
}
