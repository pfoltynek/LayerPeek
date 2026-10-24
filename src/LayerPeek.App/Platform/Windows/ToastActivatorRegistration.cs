using System.Runtime.Versioning;

using Microsoft.Extensions.Logging;
using Microsoft.Win32;

namespace LayerPeek.App.Platform.Windows;

/// <summary>The COM toast activator of this run: its CLSID and the command line Windows starts the app with (T-068).</summary>
/// <param name="CommandLine">The <c>LocalServer32</c> value (<see cref="Notifications.ToastActivatorCommandLine.For"/>).</param>
internal sealed record ToastActivatorSettings(Guid Clsid, string CommandLine);

/// <summary>Per-user registrations of local COM servers (<c>HKCU\Software\Classes\CLSID</c>).</summary>
internal interface IComServerRegistry
{
    /// <returns>The <c>LocalServer32</c> command line of <paramref name="clsid"/>; <see langword="null"/> when it has none.</returns>
    /// <exception cref="Exception">The registry cannot be read.</exception>
    string? ReadLocalServer(Guid clsid);

    /// <summary>Creates or replaces the registration of <paramref name="clsid"/> with <paramref name="commandLine"/>.</summary>
    void WriteLocalServer(Guid clsid, string commandLine);

    /// <summary>Removes the whole registration of <paramref name="clsid"/>.</summary>
    /// <returns><see langword="false"/> when there was none.</returns>
    bool Delete(Guid clsid);
}

/// <summary>
/// Keeps the per-user registration of the COM toast activator (T-068), through which Windows reports clicks on toasts in
/// the notification center and starts the app for them when it does not run. Like the Start menu shortcut: read first,
/// written only when it differs (first start, the exe moved, another build ran last), so a normal start costs one
/// registry read. No admin rights needed.
/// </summary>
internal sealed partial class ToastActivatorRegistration
{
    private readonly IComServerRegistry _registry;
    private readonly ILogger<ToastActivatorRegistration> _logger;

    public ToastActivatorRegistration(IComServerRegistry registry, ILogger<ToastActivatorRegistration> logger)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(logger);
        _registry = registry;
        _logger = logger;
    }

    /// <summary>Makes the <c>LocalServer32</c> of <paramref name="clsid"/> be <paramref name="commandLine"/>.</summary>
    /// <exception cref="Exception">Writing the registration failed.</exception>
    public ShortcutAction Ensure(Guid clsid, string commandLine)
    {
        if (clsid == Guid.Empty)
        {
            throw new ArgumentException("The CLSID must not be empty.", nameof(clsid));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(commandLine);
        ShortcutAction action;
        try
        {
            string? registered = _registry.ReadLocalServer(clsid);
            action = registered is null ? ShortcutAction.Create
                : string.Equals(registered, commandLine, StringComparison.Ordinal) ? ShortcutAction.UpToDate
                : ShortcutAction.Update;
        }
        catch (Exception ex)
        {
            LogUnreadable(ex);
            action = ShortcutAction.Update;
        }

        if (action != ShortcutAction.UpToDate)
        {
            _registry.WriteLocalServer(clsid, commandLine);
            LogWritten(action);
        }

        return action;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "The registration of the toast activator could not be read; it will be rewritten.")]
    private partial void LogUnreadable(Exception exception);

    [LoggerMessage(Level = LogLevel.Information, Message = "Toast activator for clicks in the notification center: {Action}.")]
    private partial void LogWritten(ShortcutAction action);
}

/// <summary>
/// <see cref="IComServerRegistry"/> in the registry of the current user: <c>HKCU\Software\Classes\CLSID\{clsid}</c> with the
/// app name as its default value and the <c>LocalServer32</c> subkey holding the command line.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class WindowsComServerRegistry : IComServerRegistry
{
    public const string DefaultSubKey = @"Software\Classes\CLSID";

    /// <summary>The default value of the CLSID key: what tools like regedit show for it.</summary>
    private const string DisplayName = "LayerPeek";

    private const string LocalServerKey = "LocalServer32";

    private readonly string _subKey;

    /// <param name="subKey">Relative to HKCU; tests use a key of their own.</param>
    public WindowsComServerRegistry(string subKey = DefaultSubKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subKey);
        _subKey = subKey;
    }

    public string? ReadLocalServer(Guid clsid)
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(KeyOf(clsid) + @"\" + LocalServerKey);
        return key?.GetValue(null, null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string;
    }

    public void WriteLocalServer(Guid clsid, string commandLine)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(commandLine);
        using RegistryKey key = Registry.CurrentUser.CreateSubKey(KeyOf(clsid), writable: true);
        key.SetValue(null, DisplayName, RegistryValueKind.String);
        using RegistryKey server = key.CreateSubKey(LocalServerKey, writable: true);
        server.SetValue(null, commandLine, RegistryValueKind.String);
    }

    public bool Delete(Guid clsid)
    {
        string key = KeyOf(clsid);
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

    private string KeyOf(Guid clsid) => _subKey + @"\" + clsid.ToString("B").ToUpperInvariant();
}
