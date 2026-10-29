using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace LayerPeek.App.Platform.MacOS;

/// <summary>
/// The app's real Login Item on macOS (T-019): <c>SMAppService.mainAppService</c> of ServiceManagement (macOS 13+), the
/// registration System Settings shows under General &gt; Login Items as "Open at Login". It registers the running bundle
/// itself, no LaunchAgent plist and no helper app; nothing is written into <c>~/Library/LaunchAgents</c>.
/// </summary>
/// <remarks>
/// Only for the app running from its <c>LayerPeek.app</c> in production (<c>AppServices.CreateAutostart</c>); the test mode
/// uses <see cref="StandInLoginItem"/> and tests never create this class. Objective-C through <see cref="ObjCRuntime"/>
/// (Native AOT safe, no new package). Each call runs in its own autorelease pool and may block on XPC: thread pool only.
/// </remarks>
[SupportedOSPlatform("macos")]
internal sealed unsafe class SMAppServiceLoginItem : ILoginItemService
{
    private const string ServiceManagement = "/System/Library/Frameworks/ServiceManagement.framework/ServiceManagement";

    /// <summary>Loads ServiceManagement once (kept loaded for the life of the process).</summary>
    private static readonly Lazy<bool> Loaded = new(static () => NativeLibrary.TryLoad(ServiceManagement, out _));

    public LoginItemStatus Status() => ObjCRuntime.InAutoreleasePool(() =>
    {
        long value = ObjCRuntime.Send(MainAppService(), ObjCRuntime.Selector("status"));
        return value is >= (long)LoginItemStatus.NotRegistered and <= (long)LoginItemStatus.NotFound ? (LoginItemStatus)(int)value : LoginItemStatus.NotFound;
    });

    public void Register() => Change("registerAndReturnError:", "register");

    public void Unregister() => Change("unregisterAndReturnError:", "unregister");

    /// <summary><c>[SMAppService openSystemSettingsLoginItems]</c> (macOS 13+): System Settings &gt; General &gt; Login Items.</summary>
    public void OpenSystemSettings() => ObjCRuntime.InAutoreleasePool(() =>
    {
        ObjCRuntime.SendVoid(ServiceClass(), ObjCRuntime.Selector("openSystemSettingsLoginItems"));
        return true;
    });

    private static void Change(string selector, string what) => ObjCRuntime.InAutoreleasePool(() =>
    {
        nint error = 0;
        if (!ObjCRuntime.SendBool(MainAppService(), ObjCRuntime.Selector(selector), &error))
        {
            (long code, string? description) = ObjCRuntime.Describe(error);
            throw new LoginItemException($"SMAppService could not {what} the Login Item (code {code}): {description ?? "no description"}.") { Code = code };
        }

        return true;
    });

    /// <summary><c>[SMAppService mainAppService]</c> (autoreleased: call inside the pool of the caller).</summary>
    /// <exception cref="LoginItemException">ServiceManagement or <c>SMAppService</c> is not available.</exception>
    private static nint MainAppService()
    {
        nint service = ObjCRuntime.Send(ServiceClass(), ObjCRuntime.Selector("mainAppService"));
        return service != 0 ? service : throw new LoginItemException("SMAppService is not available on this macOS.");
    }

    /// <summary>The class <c>SMAppService</c>.</summary>
    /// <exception cref="LoginItemException">ServiceManagement or <c>SMAppService</c> is not available.</exception>
    private static nint ServiceClass()
    {
        nint type = Loaded.Value ? ObjCRuntime.Class("SMAppService") : 0;
        return type != 0 ? type : throw new LoginItemException("SMAppService is not available on this macOS.");
    }
}
