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
            link.SetPath(shortcut.TargetPath);
            link.SetWorkingDirectory(Path.GetDirectoryName(shortcut.TargetPath) ?? string.Empty);
            IPropertyStore properties = (IPropertyStore)link;
            if (shortcut.AppUserModelId is not null)
            {
                SetString(properties, ShellLinkIds.AppUserModelId, shortcut.AppUserModelId);
            }

            if (shortcut.ToastActivatorClsid is { } clsid)
            {
                SetClsid(properties, ShellLinkIds.ToastActivatorClsid, clsid);
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
