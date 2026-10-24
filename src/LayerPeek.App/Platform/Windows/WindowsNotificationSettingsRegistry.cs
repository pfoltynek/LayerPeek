using System.Runtime.Versioning;

using Microsoft.Win32;

namespace LayerPeek.App.Platform.Windows;

/// <summary>Windows' per-app notification settings (on/off, banners, sound), removed by the uninstall hook (T-017).</summary>
internal interface IAppNotificationSettings
{
    /// <summary>Deletes the settings Windows keeps for <paramref name="appUserModelId"/>.</summary>
    /// <returns><see langword="false"/> when there were none.</returns>
    /// <exception cref="ArgumentException"><paramref name="appUserModelId"/> is not a single app id.</exception>
    bool Delete(string appUserModelId);
}

/// <summary>
/// <c>HKCU\Software\Microsoft\Windows\CurrentVersion\Notifications\Settings\&lt;AppUserModelID&gt;</c>, which Windows creates
/// after the first toast of an app (what the Settings app writes; not a documented API).
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class WindowsNotificationSettingsRegistry : IAppNotificationSettings
{
    public const string DefaultSubKey = @"Software\Microsoft\Windows\CurrentVersion\Notifications\Settings";

    private readonly string _subKey;

    /// <param name="subKey">Relative to HKCU; tests use a key of their own.</param>
    public WindowsNotificationSettingsRegistry(string subKey = DefaultSubKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subKey);
        _subKey = subKey;
    }

    public bool Delete(string appUserModelId)
    {
        // An empty id or a path would delete the settings of every app, or another key.
        ArgumentException.ThrowIfNullOrWhiteSpace(appUserModelId);
        if (appUserModelId.Contains('\\', StringComparison.Ordinal) || appUserModelId.Contains('/', StringComparison.Ordinal) || appUserModelId.Trim() != appUserModelId)
        {
            throw new ArgumentException("Expected a single AppUserModelID.", nameof(appUserModelId));
        }

        string key = _subKey + @"\" + appUserModelId;
        using (RegistryKey? existing = Registry.CurrentUser.OpenSubKey(key))
        {
            if (existing is null)
            {
                return false;
            }
        }

        Registry.CurrentUser.DeleteSubKeyTree(key, throwOnMissingSubKey: false);
        return true;
    }
}
