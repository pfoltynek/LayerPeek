using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace LayerPeek.App.Platform.Windows;

/// <summary>
/// Taskbar properties of an auxiliary window (T-043): <c>System.AppUserModel.PreventPinning</c> removes the app entry and
/// "Pin to taskbar" from the window's jump list (only "Close window" stays), and <c>System.AppUserModel.ID</c> keeps the
/// window in the app's taskbar group. PreventPinning must be set before the id (ui-designer probe 2026-10-20, Windows 11
/// 26300). Both are removed again (VT_EMPTY) when the window closes, as the shell requires.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class TaskbarWindowProperties
{
    /// <summary>PKEY_AppUserModel_PreventPinning (propkey.h): System.AppUserModel.PreventPinning.</summary>
    public static readonly PropertyKey PreventPinning = new(new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"), 9);

    /// <summary>VT_BOOL.</summary>
    public const ushort VtBool = 11;

    /// <summary>VARIANT_TRUE (-1) in the 16 bits of a VARIANT_BOOL.</summary>
    public const ushort VariantTrue = 0xFFFF;

    /// <summary>Sets PreventPinning = true, then the AppUserModelID (the order matters).</summary>
    public static void Apply(IPropertyStore store, string appUserModelId)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentException.ThrowIfNullOrWhiteSpace(appUserModelId);
        PropVariant preventPinning = new() { VarType = VtBool, Value = VariantTrue };
        store.SetValue(in PreventPinning, in preventPinning);

        // The store copies the value; the text is ours to free.
        nint text = Marshal.StringToCoTaskMemUni(appUserModelId);
        try
        {
            PropVariant id = new() { VarType = PropVariant.VtLpwstr, Value = text };
            store.SetValue(in ShellLinkIds.AppUserModelId, in id);
        }
        finally
        {
            Marshal.FreeCoTaskMem(text);
        }
    }

    /// <summary>Removes both properties (VT_EMPTY), the id first.</summary>
    public static void Clear(IPropertyStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        PropVariant empty = default;
        store.SetValue(in ShellLinkIds.AppUserModelId, in empty);
        store.SetValue(in PreventPinning, in empty);
    }

    /// <summary>Applies the properties to the window <paramref name="hwnd"/>.</summary>
    /// <exception cref="COMException">The window has no property store (e.g. it was destroyed).</exception>
    public static void ApplyToWindow(nint hwnd, string appUserModelId) => WithStore(hwnd, store => Apply(store, appUserModelId));

    /// <summary>Removes the properties from the window <paramref name="hwnd"/>.</summary>
    /// <exception cref="COMException">The window has no property store (e.g. it was destroyed).</exception>
    public static void ClearFromWindow(nint hwnd) => WithStore(hwnd, Clear);

    /// <summary>Runs <paramref name="action"/> with the property store of <paramref name="hwnd"/> and releases it.</summary>
    public static void WithStore(nint hwnd, Action<IPropertyStore> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        Marshal.ThrowExceptionForHR(WindowShellNative.GetPropertyStoreForWindow(hwnd, out nint pointer));
        IPropertyStore store = ComObjects.Wrap<IPropertyStore>(pointer);
        try
        {
            action(store);
        }
        finally
        {
            ComObjects.Release(store);
        }
    }
}
