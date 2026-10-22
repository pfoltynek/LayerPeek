using Microsoft.Extensions.Logging;

namespace PrusaTrayDiagnostic.App.Platform.Windows;

/// <summary>What a Start menu shortcut points to and which AppUserModelID it carries.</summary>
/// <param name="AppUserModelId"><see langword="null"/> when the shortcut has none.</param>
internal sealed record AppShortcut(string TargetPath, string? AppUserModelId);

/// <summary>Reads and writes <c>.lnk</c> files.</summary>
internal interface IShortcutStore
{
    /// <returns><see langword="null"/> when there is no file at <paramref name="path"/>.</returns>
    /// <exception cref="Exception">The file exists but cannot be read as a shortcut.</exception>
    AppShortcut? Read(string path);

    /// <summary>Creates or replaces the shortcut; the AppUserModelID is left out when it is <see langword="null"/>.</summary>
    void Write(string path, AppShortcut shortcut);

    /// <summary>Deletes the file at <paramref name="path"/>.</summary>
    /// <returns><see langword="false"/> when there was no file.</returns>
    /// <exception cref="Exception">The file exists but cannot be deleted.</exception>
    bool Delete(string path);
}

internal enum ShortcutAction
{
    /// <summary>The shortcut already points to this executable with this id; nothing was written.</summary>
    UpToDate,

    Create,

    /// <summary>Rewritten: the executable moved, the id differed or the file could not be read.</summary>
    Update,
}

/// <summary>
/// Keeps the per-user Start menu shortcut that gives an unpackaged app its AppUserModelID, which Windows requires
/// before it shows the app's toasts (and from which it takes the name and icon shown in them). Reads first and writes
/// only when something differs, so a normal start costs one small file read.
/// </summary>
internal sealed partial class AppShortcutRegistration
{
    /// <summary>File name in the Start menu; also the app name Windows shows in toasts and notification settings.</summary>
    public const string ShortcutFileName = "Prusa Tray Diagnostic.lnk";

    private readonly IShortcutStore _store;
    private readonly ILogger<AppShortcutRegistration> _logger;

    public AppShortcutRegistration(IShortcutStore store, ILogger<AppShortcutRegistration> logger)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(logger);
        _store = store;
        _logger = logger;
    }

    /// <summary>
    /// <c>%APPDATA%\Microsoft\Windows\Start Menu\Programs\Prusa Tray Diagnostic.lnk</c>: per user, no admin rights.
    /// </summary>
    public static string DefaultShortcutPath() => ShortcutPathFor(ShortcutFileName);

    /// <summary>A shortcut named <paramref name="fileName"/> in the per-user Start menu (the test app has its own, T-067).</summary>
    public static string ShortcutPathFor(string fileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), fileName);
    }

    public static ShortcutAction Decide(AppShortcut? existing, AppShortcut desired)
    {
        ArgumentNullException.ThrowIfNull(desired);
        if (existing is null)
        {
            return ShortcutAction.Create;
        }

        return SamePath(existing.TargetPath, desired.TargetPath)
            && string.Equals(existing.AppUserModelId, desired.AppUserModelId, StringComparison.Ordinal)
            ? ShortcutAction.UpToDate
            : ShortcutAction.Update;
    }

    /// <summary>Makes the shortcut at <paramref name="shortcutPath"/> match <paramref name="desired"/>.</summary>
    /// <exception cref="Exception">Writing the shortcut failed.</exception>
    public ShortcutAction Ensure(string shortcutPath, AppShortcut desired)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(shortcutPath);
        ArgumentNullException.ThrowIfNull(desired);
        ArgumentException.ThrowIfNullOrWhiteSpace(desired.TargetPath, nameof(desired));
        ArgumentException.ThrowIfNullOrWhiteSpace(desired.AppUserModelId, nameof(desired));

        ShortcutAction action;
        try
        {
            action = Decide(_store.Read(shortcutPath), desired);
        }
        catch (Exception ex)
        {
            // A damaged or foreign file with our name: replace it.
            LogUnreadable(ex);
            action = ShortcutAction.Update;
        }

        if (action != ShortcutAction.UpToDate)
        {
            _store.Write(shortcutPath, desired);
            LogWritten(action);
        }

        return action;
    }

    private static bool SamePath(string a, string b)
    {
        if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b))
        {
            return false;
        }

        try
        {
            return string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "The Start menu shortcut could not be read; it will be rewritten.")]
    private partial void LogUnreadable(Exception exception);

    [LoggerMessage(Level = LogLevel.Information, Message = "Start menu shortcut for notifications: {Action}.")]
    private partial void LogWritten(ShortcutAction action);
}
