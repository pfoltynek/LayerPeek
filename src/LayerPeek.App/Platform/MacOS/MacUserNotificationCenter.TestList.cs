#if DEBUG
using System.Runtime.InteropServices;

namespace LayerPeek.App.Platform.MacOS;

/// <summary>
/// Debug builds only (T-212): reads what the notification center holds for <see cref="TestNotificationList"/>, the delivered
/// notifications (<c>getDeliveredNotificationsWithCompletionHandler:</c>) and the registered categories
/// (<c>getNotificationCategoriesWithCompletionHandler:</c>). Reads only: nothing is changed, asked or shown. The objects of
/// the OS are valid only inside its completion handler, so everything is copied into managed records there.
/// </summary>
internal sealed partial class MacUserNotificationCenter
{
    private static readonly NativeReply<IReadOnlyList<DeliveredNotificationInfo>?> ListReply = new();
    private static readonly NativeReply<IReadOnlyList<NotificationCategoryInfo>?> CategoriesReply = new();

    private static readonly Lazy<ListBlocks> TestListBlocks = new(static () => new ListBlocks());

    /// <summary>The notifications of the app the notification center shows, in its order.</summary>
    /// <exception cref="Exception">The OS did not answer.</exception>
    public async Task<IReadOnlyList<DeliveredNotificationInfo>> ListDeliveredAsync(CancellationToken cancellationToken)
    {
        nint block = TestListBlocks.Value.Delivered;
        IReadOnlyList<DeliveredNotificationInfo>? list = await ListReply.AskAsync(
            () => ObjCRuntime.InAutoreleasePool(() => ObjCRuntime.Send(_center, ObjCRuntime.Selector("getDeliveredNotificationsWithCompletionHandler:"), block)),
            ReplyTimeout,
            cancellationToken).ConfigureAwait(false);
        return list ?? throw new InvalidOperationException("The notification center did not list its notifications.");
    }

    /// <summary>The categories the app registered (with their buttons), by identifier.</summary>
    /// <exception cref="Exception">The OS did not answer.</exception>
    public async Task<IReadOnlyList<NotificationCategoryInfo>> ListCategoriesAsync(CancellationToken cancellationToken)
    {
        nint block = TestListBlocks.Value.Categories;
        IReadOnlyList<NotificationCategoryInfo>? list = await CategoriesReply.AskAsync(
            () => ObjCRuntime.InAutoreleasePool(() => ObjCRuntime.Send(_center, ObjCRuntime.Selector("getNotificationCategoriesWithCompletionHandler:"), block)),
            ReplyTimeout,
            cancellationToken).ConfigureAwait(false);
        return list ?? throw new InvalidOperationException("The notification center did not list the categories.");
    }

    /// <summary><c>void (^)(NSArray&lt;UNNotification *&gt; *notifications)</c>: copied; <see langword="null"/> on any failure.</summary>
    [UnmanagedCallersOnly]
    private static void OnListed(nint block, nint notifications)
    {
        IReadOnlyList<DeliveredNotificationInfo>? list;
        try
        {
            list = notifications == 0 ? null : ObjCRuntime.InAutoreleasePool(() => Items(notifications, ReadNotification));
        }
        catch (Exception)
        {
            list = null;
        }

        ListReply.Complete(list);
    }

    /// <summary><c>void (^)(NSSet&lt;UNNotificationCategory *&gt; *categories)</c>: copied; <see langword="null"/> on any failure.</summary>
    [UnmanagedCallersOnly]
    private static void OnCategoriesListed(nint block, nint categories)
    {
        IReadOnlyList<NotificationCategoryInfo>? list;
        try
        {
            list = categories == 0 ? null : ObjCRuntime.InAutoreleasePool(() =>
                Items(ObjCRuntime.Send(categories, ObjCRuntime.Selector("allObjects")), ReadCategory)
                    .OrderBy(static category => category.Identifier, StringComparer.Ordinal)
                    .ToArray());
        }
        catch (Exception)
        {
            list = null;
        }

        CategoriesReply.Complete(list);
    }

    private static DeliveredNotificationInfo ReadNotification(nint notification)
    {
        nint request = ObjCRuntime.Send(notification, ObjCRuntime.Selector("request"));
        nint content = request == 0 ? 0 : ObjCRuntime.Send(request, ObjCRuntime.Selector("content"));
        nint date = ObjCRuntime.Send(notification, ObjCRuntime.Selector("date"));
        nint userInfo = content == 0 ? 0 : ObjCRuntime.Send(content, ObjCRuntime.Selector("userInfo"));
        nint attachments = content == 0 ? 0 : ObjCRuntime.Send(content, ObjCRuntime.Selector("attachments"));
        return new DeliveredNotificationInfo(
            Identifier: Text(request, "identifier") ?? string.Empty,
            Title: Text(content, "title"),
            Subtitle: Text(content, "subtitle"),
            Body: Text(content, "body"),
            CategoryIdentifier: Text(content, "categoryIdentifier"),
            ThreadIdentifier: Text(content, "threadIdentifier"),
            HasSound: content != 0 && ObjCRuntime.Send(content, ObjCRuntime.Selector("sound")) != 0,
            Attachments: attachments == 0 ? [] : Items(attachments, static attachment => new NotificationAttachmentInfo(Text(attachment, "identifier"), Text(attachment, "type"))),
            LaunchArgument: UserInfoText(userInfo, LaunchArgumentKey),
            ActionArgument: UserInfoText(userInfo, ActionArgumentKey),
            DeliveredAt: date == 0 ? null : DateTimeOffset.UnixEpoch.AddSeconds(ObjCRuntime.SendCGFloat(date, ObjCRuntime.Selector("timeIntervalSince1970"))));
    }

    private static NotificationCategoryInfo ReadCategory(nint category)
    {
        nint actions = ObjCRuntime.Send(category, ObjCRuntime.Selector("actions"));
        return new NotificationCategoryInfo(
            Text(category, "identifier") ?? string.Empty,
            actions == 0 ? [] : Items(actions, static action => new NotificationActionInfo(
                Text(action, "identifier") ?? string.Empty,
                Text(action, "title"),
                (long)ObjCRuntime.Send(action, ObjCRuntime.Selector("options")))));
    }

    /// <summary>Each object of an <c>NSArray</c>, read by <paramref name="read"/>.</summary>
    private static T[] Items<T>(nint array, Func<nint, T> read)
    {
        long count = ObjCRuntime.Send(array, ObjCRuntime.Selector("count"));
        T[] items = new T[count];
        for (long i = 0; i < count; i++)
        {
            items[i] = read(ObjCRuntime.Send(array, ObjCRuntime.Selector("objectAtIndex:"), (nint)i));
        }

        return items;
    }

    private static string? Text(nint receiver, string selector)
    {
        nint value = receiver == 0 ? 0 : ObjCRuntime.Send(receiver, ObjCRuntime.Selector(selector));
        return ObjCRuntime.IsKindOf(value, "NSString") ? ObjCRuntime.ToText(value) : null;
    }

    private static string? UserInfoText(nint userInfo, string key)
    {
        nint value = userInfo == 0 ? 0 : ObjCRuntime.Send(userInfo, ObjCRuntime.Selector("objectForKey:"), ObjCRuntime.NSString(key));
        return ObjCRuntime.IsKindOf(value, "NSString") ? ObjCRuntime.ToText(value) : null;
    }

    /// <summary>The completion handlers of the list, created once per process (never freed).</summary>
    private sealed unsafe class ListBlocks
    {
        public nint Delivered { get; } = ObjCBlocks.Global((nint)(delegate* unmanaged<nint, nint, void>)&OnListed);

        public nint Categories { get; } = ObjCBlocks.Global((nint)(delegate* unmanaged<nint, nint, void>)&OnCategoriesListed);
    }
}
#endif
