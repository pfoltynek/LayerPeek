using LayerPeek.App.Localization;
using LayerPeek.App.Platform;
using LayerPeek.App.Tray.Menu;

namespace LayerPeek.App.Settings;

/// <summary>
/// T-182: the "More information" link next to the warning about an unencrypted address outside the local network (printer
/// address and camera address). It opens the chapter about securing the access to PrusaLink (T-183) in the manual of the
/// language of the texts (<see cref="UserManual.SecurityChapterUriFor"/>); hidden while that chapter has no address or
/// nothing can open it.
/// </summary>
public sealed partial class SettingsViewModel
{
    /// <summary>Opens the security chapter in the browser; <see langword="null"/>: no link (tests, designer).</summary>
    internal IUrlLauncher? UrlLauncher { get; init; }

    /// <summary>The security chapter in the current language; default: <see cref="UserManual.SecurityChapterUriFor"/>.</summary>
    internal Func<Uri?> SecurityHelpUri { get; init; } = static () => UserManual.SecurityChapterUriFor(UiLanguage.CurrentTextCulture());

    /// <summary>The link can be shown: there is an address to open and something to open it with.</summary>
    internal bool HasSecurityHelp => UrlLauncher is not null && SecurityHelpUri() is not null;

    /// <summary>Opens the chapter in the current language (looked up again: the language may have changed).</summary>
    internal void OpenSecurityHelp()
    {
        if (UrlLauncher is { } launcher && !IsDisposed && SecurityHelpUri() is { } uri)
        {
            _ = launcher.OpenAsync(uri);
        }
    }
}
