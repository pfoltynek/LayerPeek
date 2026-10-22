using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

using Microsoft.Win32;

namespace PrusaTrayDiagnostic.App.E2E.Tests.Infrastructure;

/// <summary>One entry of Explorer's notification area settings: the subkey name and <c>IsPromoted</c> (null = missing).</summary>
internal sealed record TrayIconEntry(string Key, int? IsPromoted);

/// <summary>The entries of one executable in <c>HKCU\Control Panel\NotifyIconSettings</c>.</summary>
internal sealed record TrayIconPromotionState(string ExecutablePath, IReadOnlyList<TrayIconEntry> Entries)
{
    public override string ToString() =>
        Entries.Count == 0
            ? "no entries"
            : string.Join(", ", Entries.Select(static entry => $"{entry.Key}: IsPromoted={entry.IsPromoted?.ToString(CultureInfo.InvariantCulture) ?? "(missing)"}"));
}

/// <summary>One step of a restore of <see cref="TrayIconPromotionKind"/>.</summary>
internal enum TrayIconAction
{
    /// <summary>Write the original <c>IsPromoted</c>.</summary>
    SetPromoted,

    /// <summary>Delete <c>IsPromoted</c> (it did not exist, or the whole entry was created during the change).</summary>
    DeletePromoted,
}

/// <summary>
/// The test tray icon on the taskbar instead of the overflow flyout (T-065): <c>IsPromoted = 1</c> in Explorer's
/// entries of the tested exe in <c>HKCU\Control Panel\NotifyIconSettings</c> (undocumented, Windows 11 22H2+; the
/// mechanism of T-061, which the test mode switches off). The key of a change is the exe path. The restore writes back
/// the original <c>IsPromoted</c> of every entry that existed (also "missing") and removes <c>IsPromoted</c> from the
/// entries Explorer created for the exe after the original was read. No entry is ever deleted (user decision 2026-10-22,
/// T-065 Q3: Explorer may not create a deleted entry again in the same session, so the icon could not be promoted any
/// more) and entries of other programs are never touched. An entry Explorer created may afterwards carry
/// <c>IsPromoted = 0</c> written by Explorer itself; that counts as restored (not promoted), as does an original entry
/// Explorer removed meanwhile (nothing of it can stay promoted).
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed partial class TrayIconPromotionKind() : SystemChangeKind<TrayIconPromotionState>("TrayIconPromotion", changesSystem: true, SystemChangeJson.Default.TrayIconPromotionState)
{
    public const string SubKey = @"Control Panel\NotifyIconSettings";
    private const string ExecutablePathValue = "ExecutablePath";
    private const string IsPromotedValue = "IsPromoted";

    public override Task<TrayIconPromotionState> ReadStateAsync(string? key, CancellationToken cancellationToken)
    {
        string exe = ExecutableOf(key);
        List<TrayIconEntry> entries = [.. ReadAll().Where(entry => SamePath(entry.ExecutablePath, exe)).Select(static entry => new TrayIconEntry(entry.Key, entry.IsPromoted))];
        return Task.FromResult(new TrayIconPromotionState(exe, entries));
    }

    /// <summary>Every entry of every program: key, expanded exe path and <c>IsPromoted</c>.</summary>
    public static List<(string Key, string? ExecutablePath, int? IsPromoted)> ReadAll()
    {
        List<(string, string?, int?)> all = [];
        using RegistryKey? parent = Registry.CurrentUser.OpenSubKey(SubKey);
        if (parent is null)
        {
            return all;
        }

        foreach (string name in parent.GetSubKeyNames().Order(StringComparer.OrdinalIgnoreCase))
        {
            using RegistryKey? entry = parent.OpenSubKey(name);
            if (entry is null)
            {
                continue;
            }

            string? path = entry.GetValue(ExecutablePathValue) as string;
            all.Add((name, path is null ? null : ExpandKnownFolder(path), entry.GetValue(IsPromotedValue) is int promoted ? promoted : null));
        }

        return all;
    }

    /// <summary>
    /// Every original entry that still exists has its original <c>IsPromoted</c>; every entry created since is not
    /// promoted (<c>IsPromoted</c> missing, or 0 written by Explorer).
    /// </summary>
    public override bool Matches(TrayIconPromotionState original, TrayIconPromotionState current) =>
        current.Entries.All(now => Find(original, now.Key) is { } before ? now.IsPromoted == before.IsPromoted : IsNotPromoted(now.IsPromoted));

    /// <summary>Whether an entry created during a change is as Explorer keeps a new entry: not promoted.</summary>
    internal static bool IsNotPromoted(int? isPromoted) => isPromoted is null or 0;

    public override string Describe(string? key, TrayIconPromotionState original) =>
        $"Tray icon entries of '{original.ExecutablePath}' in HKCU\\{SubKey}: {original}; entries of that exe created later lose IsPromoted (the entries stay). "
        + "By hand: Settings > Personalization > Taskbar > Other system tray icons (turn the test icon off), "
        + $"or in HKCU\\{SubKey} set IsPromoted of the entries of that exe back to the values above (delete it where it is missing above or the entry is newer).";

    /// <summary>
    /// The steps that bring <paramref name="current"/> back to <paramref name="original"/>: the original <c>IsPromoted</c>
    /// of every original entry, none on an entry created since (it stays, Q3); an original entry that is gone has nothing
    /// to write into.
    /// </summary>
    internal static List<(TrayIconAction Action, string Key, int? Value)> PlanRestore(TrayIconPromotionState original, TrayIconPromotionState current)
    {
        List<(TrayIconAction, string, int?)> plan = [];
        foreach (TrayIconEntry now in current.Entries)
        {
            int? wanted = Find(original, now.Key)?.IsPromoted;
            if (now.IsPromoted != wanted)
            {
                plan.Add(wanted is { } value ? (TrayIconAction.SetPromoted, now.Key, value) : (TrayIconAction.DeletePromoted, now.Key, null));
            }
        }

        return plan;
    }

    private static TrayIconEntry? Find(TrayIconPromotionState state, string key) =>
        state.Entries.FirstOrDefault(entry => string.Equals(entry.Key, key, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Applies the <c>IsPromoted</c> values of <paramref name="state"/> to its entries (entries not in the state are left
    /// alone); used to promote (all 1).
    /// </summary>
    protected override Task ApplyCoreAsync(string? key, TrayIconPromotionState state, CancellationToken cancellationToken)
    {
        foreach (TrayIconEntry entry in state.Entries)
        {
            Write(entry.IsPromoted is { } value ? TrayIconAction.SetPromoted : TrayIconAction.DeletePromoted, entry.Key, entry.IsPromoted);
        }

        return Task.CompletedTask;
    }

    protected override async Task RestoreCoreAsync(string? key, TrayIconPromotionState original, CancellationToken cancellationToken)
    {
        TrayIconPromotionState current = await ReadStateAsync(key, cancellationToken).ConfigureAwait(false);
        foreach ((TrayIconAction action, string entry, int? value) in PlanRestore(original, current))
        {
            Write(action, entry, value);
        }
    }

    private static void Write(TrayIconAction action, string entry, int? value)
    {
        if (entry.Length == 0 || entry.Contains('\\', StringComparison.Ordinal) || entry.Contains('/', StringComparison.Ordinal))
        {
            throw new ArgumentException($"Not a subkey name: '{entry}'.", nameof(entry));
        }

        string path = SubKey + @"\" + entry;
        // Only IsPromoted of an existing entry: no entry is ever created or deleted.
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(path, writable: true);
        if (action == TrayIconAction.SetPromoted)
        {
            key?.SetValue(IsPromotedValue, value ?? 0, RegistryValueKind.DWord);
        }
        else
        {
            key?.DeleteValue(IsPromotedValue, throwOnMissingValue: false);
        }
    }

    private static string ExecutableOf(string? key) =>
        key is { Length: > 0 } ? Path.GetFullPath(key) : throw new ArgumentException("The key of a tray icon change is the exe path.", nameof(key));

    private static bool SamePath(string? path, string exe)
    {
        if (path is null)
        {
            return false;
        }

        try
        {
            return string.Equals(Path.GetFullPath(path), exe, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    /// <summary>Explorer stores paths in known folders as <c>{FOLDERID}\rest</c> (e.g. Program Files).</summary>
    internal static string ExpandKnownFolder(string path)
    {
        const int GuidLength = 38;
        if (path.Length <= GuidLength + 1 || path[0] != '{' || path[GuidLength] != '\\'
            || !Guid.TryParseExact(path.AsSpan(0, GuidLength), "B", out Guid folderId))
        {
            return path;
        }

        int result = SHGetKnownFolderPath(folderId, 0, 0, out nint buffer);
        try
        {
            return result == 0 && Marshal.PtrToStringUni(buffer) is { Length: > 0 } folder ? Path.Join(folder, path.AsSpan(GuidLength + 1)) : path;
        }
        finally
        {
            Marshal.FreeCoTaskMem(buffer);
        }
    }

    [LibraryImport("shell32.dll")]
    private static partial int SHGetKnownFolderPath(in Guid folderId, uint flags, nint token, out nint path);
}

/// <summary>Promotion with the run-wide tray icon change of <see cref="E2EFixture.TrayIconPromotion"/>.</summary>
[SupportedOSPlatform("windows")]
internal static class TrayIconPromotionExtensions
{
    /// <summary>
    /// Waits until Explorer has an entry for the exe (it creates it a few seconds after the icon is first shown) and
    /// promotes every entry of the exe to the taskbar.
    /// </summary>
    public static async Task<TrayIconPromotionState> PromoteAsync(this SystemChange<TrayIconPromotionState> change, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        TrayIconPromotionState current = await change.ReadAsync(cancellationToken);
        await Wait.UntilAsync(
            async () => (current = await change.ReadAsync(cancellationToken)).Entries.Count > 0,
            timeout,
            $"Explorer's notification area entry of '{change.Key}'");
        TrayIconPromotionState promoted = current with { Entries = [.. current.Entries.Select(static entry => entry with { IsPromoted = 1 })] };
        await change.ApplyAsync(promoted, cancellationToken);
        return promoted;
    }
}
