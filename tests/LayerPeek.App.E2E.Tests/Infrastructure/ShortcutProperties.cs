using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace LayerPeek.App.E2E.Tests.Infrastructure;

/// <summary>What Windows reads from a <c>.lnk</c>: its target, AppUserModelID and toast activator (T-017).</summary>
/// <param name="ToastActivatorClsid">The CLSID as Windows formats it (<c>{…}</c>), or <see langword="null"/>.</param>
internal sealed record ShortcutInfo(string? TargetPath, string? AppUserModelId, string? ToastActivatorClsid);

/// <summary>
/// Reads a shortcut through the shell (<c>Shell.Application</c>, late bound like <see cref="ShellStart.CreateShortcut"/>;
/// the tests run on the JIT): the target of the link and the properties <c>System.AppUserModel.ID</c> and
/// <c>System.AppUserModel.ToastActivatorCLSID</c> of its property store.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class ShortcutProperties
{
    public static ShortcutInfo Read(string path)
    {
        Type shellType = Type.GetTypeFromProgID("Shell.Application") ?? throw new InvalidOperationException("Shell.Application is not available.");
        object shell = Activator.CreateInstance(shellType)!;
        try
        {
            dynamic folder = ((dynamic)shell).NameSpace(Path.GetDirectoryName(path)!) ?? throw new InvalidOperationException($"The folder of '{path}' cannot be opened.");
            dynamic item = folder.ParseName(Path.GetFileName(path)) ?? throw new InvalidOperationException($"'{path}' does not exist.");
            string? target = (string?)item.GetLink?.Path;
            object? aumid = item.ExtendedProperty("System.AppUserModel.ID");
            object? clsid = item.ExtendedProperty("System.AppUserModel.ToastActivatorCLSID");
            return new ShortcutInfo(target, aumid?.ToString(), clsid?.ToString());
        }
        finally
        {
            Marshal.FinalReleaseComObject(shell);
        }
    }
}
