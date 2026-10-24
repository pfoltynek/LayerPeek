using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace LayerPeek.App.Platform.Windows;

// Shell link COM interfaces (Windows SDK 10.0.26100 ShObjIdl_core.h, ObjIdl.h, propsys.h). Method order must match.

/// <summary>IShellLinkW.</summary>
[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
[Guid("000214F9-0000-0000-C000-000000000046")]
internal unsafe partial interface IShellLinkW
{
    void GetPath(char* file, int maxPath, nint findData, uint flags);

    void GetIDList(out nint idList);

    void SetIDList(nint idList);

    void GetDescription(char* name, int maxName);

    void SetDescription(string name);

    void GetWorkingDirectory(char* directory, int maxPath);

    void SetWorkingDirectory(string directory);

    void GetArguments(char* arguments, int maxPath);

    void SetArguments(string arguments);

    void GetHotkey(out ushort hotkey);

    void SetHotkey(ushort hotkey);

    void GetShowCmd(out int showCommand);

    void SetShowCmd(int showCommand);

    void GetIconLocation(char* iconPath, int maxIconPath, out int iconIndex);

    void SetIconLocation(string iconPath, int iconIndex);

    void SetRelativePath(string relativePath, uint reserved);

    void Resolve(nint window, uint flags);

    void SetPath(string file);
}

/// <summary>IPersistFile (including the IPersist slot).</summary>
[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
[Guid("0000010B-0000-0000-C000-000000000046")]
internal partial interface IPersistFile
{
    void GetClassID(out Guid classId);

    [PreserveSig]
    int IsDirty();

    void Load(string fileName, uint mode);

    void Save(string? fileName, [MarshalAs(UnmanagedType.Bool)] bool remember);

    void SaveCompleted(string fileName);

    void GetCurFile(out nint fileName);
}

/// <summary>IPropertyStore.</summary>
[GeneratedComInterface]
[Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99")]
internal partial interface IPropertyStore
{
    void GetCount(out uint count);

    void GetAt(uint index, out PropertyKey key);

    void GetValue(in PropertyKey key, out PropVariant value);

    void SetValue(in PropertyKey key, in PropVariant value);

    void Commit();
}

internal static class ShellLinkIds
{
    public static readonly Guid ShellLinkClass = new("00021401-0000-0000-C000-000000000046");
    public static readonly Guid ShellLinkW = new("000214F9-0000-0000-C000-000000000046");

    /// <summary>PKEY_AppUserModel_ID (propkey.h): System.AppUserModel.ID.</summary>
    public static readonly PropertyKey AppUserModelId = new(new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"), 5);

    /// <summary>PKEY_AppUserModel_ToastActivatorCLSID (propkey.h): System.AppUserModel.ToastActivatorCLSID, a VT_CLSID (T-068).</summary>
    public static readonly PropertyKey ToastActivatorClsid = new(new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"), 26);

    /// <summary>STGM_READ.</summary>
    public const uint StorageModeRead = 0;

    /// <summary>STGM_READWRITE: an existing shortcut is changed in place (T-017).</summary>
    public const uint StorageModeReadWrite = 2;
}
