using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace PrusaTrayDiagnostic.App.Platform.Windows;

// WinRT ABI of Windows.UI.Notifications and Windows.Data.Xml.Dom (Windows SDK 10.0.26100 headers
// windows.ui.notifications.h / windows.data.xml.dom.h). Only a vtable prefix is declared where later methods are not
// used; method order must match the headers. HSTRINGs and returned objects are nint so that their lifetime is explicit.

/// <summary>IInspectable: base of every WinRT interface. Its methods only reserve the vtable slots.</summary>
[GeneratedComInterface]
[Guid("AF86E2E0-B12D-4C6A-9C5A-D7AA65101E90")]
internal partial interface IInspectable
{
    void GetIids(out uint iidCount, out nint iids);

    void GetRuntimeClassName(out nint className);

    void GetTrustLevel(out int trustLevel);
}

/// <summary>IActivationFactory (activation.h): creates instances of a runtime class with a default constructor.</summary>
[GeneratedComInterface]
[Guid("00000035-0000-0000-C000-000000000046")]
internal partial interface IActivationFactory : IInspectable
{
    void ActivateInstance(out nint instance);
}

/// <summary>Windows.UI.Notifications.IToastNotificationManagerStatics.</summary>
[GeneratedComInterface]
[Guid("50AC103F-D235-4598-BBEF-98FE4D1A3AD4")]
internal partial interface IToastNotificationManagerStatics : IInspectable
{
    void CreateToastNotifier(out nint result);

    void CreateToastNotifierWithId(nint applicationId, out nint result);
}

/// <summary>Windows.UI.Notifications.IToastNotificationManagerStatics2 (the same activation factory).</summary>
[GeneratedComInterface]
[Guid("7AB93C52-0E48-4750-BA9D-1A4113981847")]
internal partial interface IToastNotificationManagerStatics2 : IInspectable
{
    void GetHistory(out nint value);
}

/// <summary>Windows.UI.Notifications.IToastNotificationHistory: the toasts of an app in the notification center.</summary>
[GeneratedComInterface]
[Guid("5CADDC63-01D3-4C97-986F-0533483FEE14")]
internal partial interface IToastNotificationHistory : IInspectable
{
    void RemoveGroup(nint group);

    void RemoveGroupWithId(nint group, nint applicationId);

    void RemoveGroupedTagWithId(nint tag, nint group, nint applicationId);

    void RemoveGroupedTag(nint tag, nint group);

    void Remove(nint tag);

    void Clear();

    void ClearWithId(nint applicationId);
}

/// <summary>Windows.UI.Notifications.IToastNotifier.</summary>
[GeneratedComInterface]
[Guid("75927B93-03F3-41EC-91D3-6E5BAC1B38E7")]
internal partial interface IToastNotifier : IInspectable
{
    void Show(IToastNotification notification);

    void Hide(IToastNotification notification);

    /// <summary>Whether Windows shows the toasts of this notifier's app (<see cref="ToastNotifierSetting"/>).</summary>
    void GetSetting(out int value);
}

/// <summary>Windows.UI.Notifications.IToastNotificationFactory (activation factory of ToastNotification).</summary>
[GeneratedComInterface]
[Guid("04124B20-82C6-4229-B109-FD9ED4662B53")]
internal partial interface IToastNotificationFactory : IInspectable
{
    void CreateToastNotification(IXmlDocument content, out nint value);
}

/// <summary>Windows.UI.Notifications.IToastNotification. Event tokens are EventRegistrationToken (one Int64).</summary>
[GeneratedComInterface]
[Guid("997E2675-059E-4E60-8B06-1760917C8B80")]
internal partial interface IToastNotification : IInspectable
{
    void GetContent(out nint value);

    void PutExpirationTime(nint value);

    void GetExpirationTime(out nint value);

    void AddDismissed(nint handler, out long token);

    void RemoveDismissed(long token);

    void AddActivated(nint handler, out long token);

    void RemoveActivated(long token);

    void AddFailed(nint handler, out long token);

    void RemoveFailed(long token);
}

/// <summary>Windows.UI.Notifications.IToastActivatedEventArgs.</summary>
[GeneratedComInterface]
[Guid("E3BF92F3-C197-436F-8265-0625824F8DAC")]
internal partial interface IToastActivatedEventArgs : IInspectable
{
    void GetArguments(out nint value);
}

/// <summary>Windows.UI.Notifications.IToastFailedEventArgs.</summary>
[GeneratedComInterface]
[Guid("35176862-CFD4-44F8-AD64-F500FD896C3B")]
internal partial interface IToastFailedEventArgs : IInspectable
{
    /// <summary>The HRESULT why Windows did not show the toast.</summary>
    void GetErrorCode(out int value);
}

/// <summary>Windows.Data.Xml.Dom.IXmlDocument; only passed on, no methods are called.</summary>
[GeneratedComInterface]
[Guid("F7F3A506-1E87-42D6-BCFB-B8C809FA5494")]
internal partial interface IXmlDocument : IInspectable
{
}

/// <summary>Windows.Data.Xml.Dom.IXmlDocumentIO.</summary>
[GeneratedComInterface]
[Guid("6CD0E74E-EE65-4489-9EBF-CA43E87BA637")]
internal partial interface IXmlDocumentIO : IInspectable
{
    void LoadXml(nint xml);
}

/// <summary>
/// TypedEventHandler&lt;ToastNotification, Object&gt; (delegate interface, IUnknown based); the IID is the
/// parameterized-interface IID from windows.ui.notifications.h.
/// </summary>
[GeneratedComInterface]
[Guid("AB54DE2D-97D9-5528-B6AD-105AFE156530")]
internal partial interface IToastActivatedHandler
{
    void Invoke(nint sender, nint args);
}

/// <summary>
/// TypedEventHandler&lt;ToastNotification, ToastFailedEventArgs&gt; (delegate interface, IUnknown based); the IID is the
/// parameterized-interface IID from windows.ui.notifications.h.
/// </summary>
[GeneratedComInterface]
[Guid("95E3E803-C969-5E3A-9753-EA2AD22A9A33")]
internal partial interface IToastFailedHandler
{
    void Invoke(nint sender, nint args);
}

/// <summary>Windows.UI.Notifications.NotificationSetting: whether Windows shows the toasts of an app.</summary>
internal enum ToastNotifierSetting
{
    Enabled = 0,
    DisabledForApplication = 1,
    DisabledForUser = 2,
    DisabledByGroupPolicy = 3,
    DisabledByManifest = 4,
}

internal static class WinRtClassNames
{
    public const string ToastNotificationManager = "Windows.UI.Notifications.ToastNotificationManager";
    public const string ToastNotification = "Windows.UI.Notifications.ToastNotification";
    public const string XmlDocument = "Windows.Data.Xml.Dom.XmlDocument";
}

/// <summary>
/// Interface ids as values: <c>typeof(T).GUID</c> would read <see cref="GuidAttribute"/> through reflection, which
/// Native AOT does not guarantee. Must equal the <see cref="GuidAttribute"/> of the interfaces above.
/// </summary>
internal static class WinRtInterfaceIds
{
    public static readonly Guid ActivationFactory = new("00000035-0000-0000-C000-000000000046");
    public static readonly Guid ToastNotificationManagerStatics = new("50AC103F-D235-4598-BBEF-98FE4D1A3AD4");
    public static readonly Guid ToastNotificationManagerStatics2 = new("7AB93C52-0E48-4750-BA9D-1A4113981847");
    public static readonly Guid ToastNotificationFactory = new("04124B20-82C6-4229-B109-FD9ED4662B53");
    public static readonly Guid ToastActivatedHandler = new("AB54DE2D-97D9-5528-B6AD-105AFE156530");
    public static readonly Guid ToastFailedHandler = new("95E3E803-C969-5E3A-9753-EA2AD22A9A33");
}
