using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace LayerPeek.App.Platform.Windows;

/// <summary>
/// <c>.lnk</c> files through the shell's ShellLink object (IShellLinkW, IPersistFile, IPropertyStore). Usable from
/// any thread: every call joins the COM apartment for its duration.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed unsafe class WindowsShortcutStore : IShortcutStore
{
    /// <summary>Longest target path read back; the shell link format itself stores MAX_PATH paths.</summary>
    private const int MaxPath = 1024;

    public AppShortcut? Read(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!File.Exists(path))
        {
            return null;
        }

        using ComApartment apartment = ComApartment.EnterMultithreaded();
        IShellLinkW link = CreateShellLink();
        try
        {
            ((IPersistFile)link).Load(path, ShellLinkIds.StorageModeRead);

            char* buffer = stackalloc char[MaxPath];
            buffer[0] = '\0';
            link.GetPath(buffer, MaxPath, 0, 0);
            string target = new(buffer);

            IPropertyStore properties = (IPropertyStore)link;
            return new AppShortcut(target, ReadAppUserModelId(properties), ReadToastActivator(properties));
        }
        finally
        {
            ComObjects.Release(link);
        }
    }

    public void Write(string path, AppShortcut shortcut)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(shortcut);
        ArgumentException.ThrowIfNullOrWhiteSpace(shortcut.TargetPath, nameof(shortcut));

        using ComApartment apartment = ComApartment.EnterMultithreaded();
        IShellLinkW link = CreateShellLink();
        try
        {
            // An existing shortcut (e.g. the one Velopack created, T-017) is changed in place: its description, icon and
            // other properties stay; the target and working folder change only when the target differs.
            bool loaded = false;
            if (File.Exists(path))
            {
                try
                {
                    ((IPersistFile)link).Load(path, ShellLinkIds.StorageModeReadWrite);
                    loaded = true;
                }
                catch (Exception ex) when (ex is COMException or IOException or UnauthorizedAccessException)
                {
                    // Not a readable shortcut: written anew below. The new object first, so that a failure to create it leaves
                    // only the old one to the finally block (never released twice).
                    IShellLinkW fresh = CreateShellLink();
                    ComObjects.Release(link);
                    link = fresh;
                }
            }

            if (!loaded || !SamePath(TargetOf(link), shortcut.TargetPath))
            {
                link.SetPath(shortcut.TargetPath);
                link.SetWorkingDirectory(Path.GetDirectoryName(shortcut.TargetPath) ?? string.Empty);
            }

            IPropertyStore properties = (IPropertyStore)link;
            // A value left out is removed (an existing shortcut may carry one).
            if (shortcut.AppUserModelId is not null)
            {
                SetString(properties, ShellLinkIds.AppUserModelId, shortcut.AppUserModelId);
            }
            else if (loaded)
            {
                Clear(properties, ShellLinkIds.AppUserModelId);
            }

            if (shortcut.ToastActivatorClsid is { } clsid)
            {
                SetClsid(properties, ShellLinkIds.ToastActivatorClsid, clsid);
            }
            else if (loaded)
            {
                Clear(properties, ShellLinkIds.ToastActivatorClsid);
            }

            properties.Commit();

            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            ((IPersistFile)link).Save(path, remember: true);
        }
        finally
        {
            ComObjects.Release(link);
        }
    }

    public bool Delete(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!File.Exists(path))
        {
            return false;
        }

        File.Delete(path);
        return true;
    }

    private static string TargetOf(IShellLinkW link)
    {
        char* buffer = stackalloc char[MaxPath];
        buffer[0] = '\0';
        link.GetPath(buffer, MaxPath, 0, 0);
        return new string(buffer);
    }

    private static bool SamePath(string a, string b)
    {
        try
        {
            return a.Length > 0 && string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    private static IShellLinkW CreateShellLink()
    {
        Marshal.ThrowExceptionForHR(ComNative.CoCreateInstance(
            ShellLinkIds.ShellLinkClass, 0, ComNative.ClsCtxInprocServer, ShellLinkIds.ShellLinkW, out nint link));
        return ComObjects.Wrap<IShellLinkW>(link);
    }

    private static string? ReadAppUserModelId(IPropertyStore store)
    {
        store.GetValue(ShellLinkIds.AppUserModelId, out PropVariant value);
        try
        {
            return value.VarType == PropVariant.VtLpwstr ? Marshal.PtrToStringUni(value.Value) : null;
        }
        finally
        {
            _ = ComNative.PropVariantClear(&value);
        }
    }

    /// <summary>System.AppUserModel.ToastActivatorCLSID (VT_CLSID), or <see langword="null"/>.</summary>
    private static Guid? ReadToastActivator(IPropertyStore store)
    {
        store.GetValue(ShellLinkIds.ToastActivatorClsid, out PropVariant value);
        try
        {
            return value.VarType == PropVariant.VtClsid && value.Value != 0 ? *(Guid*)value.Value : null;
        }
        finally
        {
            _ = ComNative.PropVariantClear(&value);
        }
    }

    private static void SetString(IPropertyStore store, in PropertyKey key, string text)
    {
        PropVariant value = new() { VarType = PropVariant.VtLpwstr, Value = Marshal.StringToCoTaskMemUni(text) };
        try
        {
            store.SetValue(key, value);
        }
        finally
        {
            Marshal.FreeCoTaskMem(value.Value);
        }
    }

    /// <summary>Removes a property (VT_EMPTY).</summary>
    private static void Clear(IPropertyStore store, in PropertyKey key)
    {
        PropVariant empty = default;
        store.SetValue(key, empty);
    }

    private static void SetClsid(IPropertyStore store, in PropertyKey key, Guid clsid)
    {
        PropVariant value = new() { VarType = PropVariant.VtClsid, Value = Marshal.AllocCoTaskMem(sizeof(Guid)) };
        try
        {
            *(Guid*)value.Value = clsid;
            store.SetValue(key, value);
        }
        finally
        {
            Marshal.FreeCoTaskMem(value.Value);
        }
    }
}
