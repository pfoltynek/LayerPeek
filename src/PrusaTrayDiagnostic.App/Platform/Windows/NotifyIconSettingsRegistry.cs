using System.Runtime.InteropServices;
using System.Runtime.Versioning;

using Microsoft.Win32;

namespace PrusaTrayDiagnostic.App.Platform.Windows;

/// <summary>
/// Explorer's notification area settings in <c>HKCU\Control Panel\NotifyIconSettings</c> (undocumented, Windows 11 22H2+):
/// one subkey per icon with <c>ExecutablePath</c> and, once decided, <c>IsPromoted</c> (DWORD, 1 = on the taskbar).
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class NotifyIconSettingsRegistry : INotifyIconSettings
{
    public const string DefaultSubKey = @"Control Panel\NotifyIconSettings";

    private const string ExecutablePathValue = "ExecutablePath";
    private const string IsPromotedValue = "IsPromoted";

    private readonly string _subKey;

    /// <param name="subKey">Relative to HKCU; tests use a key of their own.</param>
    public NotifyIconSettingsRegistry(string subKey = DefaultSubKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subKey);
        _subKey = subKey;
    }

    public IReadOnlyList<NotifyIconEntry> ReadEntries()
    {
        using RegistryKey? parent = Registry.CurrentUser.OpenSubKey(_subKey);
        if (parent is null)
        {
            return [];
        }

        // The same few folders repeat in almost every entry.
        Dictionary<Guid, string?> folders = [];
        List<NotifyIconEntry> entries = [];
        foreach (string name in parent.GetSubKeyNames())
        {
            using RegistryKey? key = parent.OpenSubKey(name);
            if (key is null)
            {
                continue;
            }

            string? path = key.GetValue(ExecutablePathValue) as string;
            if (path is not null)
            {
                path = ExpandKnownFolder(path, id => folders.TryGetValue(id, out string? folder) ? folder : folders[id] = KnownFolderNative.TryGetPath(id));
            }

            entries.Add(new NotifyIconEntry(name, path, key.GetValue(IsPromotedValue) is not null));
        }

        return entries;
    }

    public void Promote(string keyName)
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(EntryPath(keyName), writable: true);
        key?.SetValue(IsPromotedValue, 1, RegistryValueKind.DWord);
    }

    public void ClearPromotion(string keyName)
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(EntryPath(keyName), writable: true);
        key?.DeleteValue(IsPromotedValue, throwOnMissingValue: false);
    }

    /// <summary>
    /// Explorer stores paths in known folders as <c>{FOLDERID}\rest</c> (e.g. Program Files); resolves such a prefix with
    /// <paramref name="resolve"/>. Anything else, or an unknown folder, is returned unchanged.
    /// </summary>
    internal static string ExpandKnownFolder(string path, Func<Guid, string?> resolve)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(resolve);
        const int GuidLength = 38; // "{xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx}"
        if (path.Length <= GuidLength + 1
            || path[0] != '{'
            || path[GuidLength] != '\\'
            || !Guid.TryParseExact(path.AsSpan(0, GuidLength), "B", out Guid folderId)
            || resolve(folderId) is not { Length: > 0 } folder)
        {
            return path;
        }

        return Path.Join(folder, path.AsSpan(GuidLength + 1));
    }

    private string EntryPath(string keyName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(keyName);
        if (keyName.Contains('\\', StringComparison.Ordinal) || keyName.Contains('/', StringComparison.Ordinal))
        {
            throw new ArgumentException("A key name must not contain path separators.", nameof(keyName));
        }

        return _subKey + @"\" + keyName;
    }
}

/// <summary><c>SHGetKnownFolderPath</c>. Source-generated marshalling only (Native AOT).</summary>
[SupportedOSPlatform("windows")]
internal static partial class KnownFolderNative
{
    /// <returns>The folder, or <see langword="null"/> when this system does not have it.</returns>
    public static string? TryGetPath(Guid folderId)
    {
        int result = SHGetKnownFolderPath(folderId, 0, 0, out nint buffer);
        try
        {
            return result == ComNative.SOk ? Marshal.PtrToStringUni(buffer) : null;
        }
        finally
        {
            // Allocated even on failure (documented); freeing null is a no-op.
            Marshal.FreeCoTaskMem(buffer);
        }
    }

    [LibraryImport("shell32.dll")]
    private static partial int SHGetKnownFolderPath(in Guid folderId, uint flags, nint token, out nint path);
}
