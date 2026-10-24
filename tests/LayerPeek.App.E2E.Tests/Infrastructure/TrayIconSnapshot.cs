using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace LayerPeek.App.E2E.Tests.Infrastructure;

/// <summary>
/// Explorer's notification area entries (<c>HKCU\Control Panel\NotifyIconSettings</c>) in the state snapshot of an
/// end-to-end run (T-065), private by design (T-071, 2026-10-23): the entries of the tested exe with their key and
/// <c>IsPromoted</c>; of every other program only how many there are and a fingerprint of their <c>IsPromoted</c> values,
/// never their exe paths. The keys of the other entries (opaque numbers) are kept in memory only, to tell a changed entry
/// from a new or removed one; they are never written.
/// </summary>
internal sealed class TrayIconSnapshot
{
    private readonly Dictionary<string, int?> _others;

    private TrayIconSnapshot(string testedExe, IReadOnlyList<TrayIconEntry> tested, Dictionary<string, int?> others)
    {
        TestedExe = testedExe;
        Tested = tested;
        _others = others;
        OtherFingerprint = Fingerprint(others);
    }

    /// <summary>The exe under test.</summary>
    public string TestedExe { get; }

    /// <summary>The entries of <see cref="TestedExe"/>, ordered by key.</summary>
    public IReadOnlyList<TrayIconEntry> Tested { get; }

    /// <summary>How many entries other programs have.</summary>
    public int OtherCount => _others.Count;

    /// <summary>
    /// SHA-256 (first 8 bytes, hex) of the key and <c>IsPromoted</c> of every other entry, in key order: changes when any of
    /// them is promoted or demoted, added or removed, and tells nothing about the programs.
    /// </summary>
    public string OtherFingerprint { get; }

    /// <summary>
    /// Splits <paramref name="entries"/> (<see cref="TrayIconPromotionKind.ReadAll"/>) into the entries of
    /// <paramref name="testedExe"/> and the summary of all others.
    /// </summary>
    public static TrayIconSnapshot Of(IEnumerable<(string Key, string? ExecutablePath, int? IsPromoted)> entries, string testedExe)
    {
        List<TrayIconEntry> tested = [];
        Dictionary<string, int?> others = new(StringComparer.OrdinalIgnoreCase);
        foreach ((string key, string? path, int? promoted) in entries)
        {
            if (TrayIconPromotionKind.SamePath(path, testedExe))
            {
                tested.Add(new TrayIconEntry(key, promoted));
            }
            else
            {
                others[key] = promoted;
            }
        }

        tested.Sort(static (a, b) => StringComparer.OrdinalIgnoreCase.Compare(a.Key, b.Key));
        return new TrayIconSnapshot(testedExe, tested, others);
    }

    /// <summary>
    /// Differences of <paramref name="end"/> from this start state, like before the privacy change: every entry that existed
    /// must keep its <c>IsPromoted</c>; a new entry of the tested exe must not be promoted (the restore of the run-wide tray
    /// change removes <c>IsPromoted</c> of new entries, T-065 Q3; 0 written by Explorer is fine). New entries of other
    /// programs (Explorer creates one for any program that shows an icon) and removed entries are only noted.
    /// </summary>
    public void Compare(TrayIconSnapshot end, List<string> differences, List<string> notes)
    {
        foreach (TrayIconEntry entry in Tested)
        {
            if (!end.TryFind(entry.Key, out int? now))
            {
                notes.Add($"Tray icon entry {entry.Key} of {TestedExe} is gone (Explorer removed it).");
            }
            else if (now != entry.IsPromoted)
            {
                differences.Add($"Tray icon entry {entry.Key} of {TestedExe}: IsPromoted {Show(entry.IsPromoted)} at the start, {Show(now)} at the end.");
            }
        }

        int changed = 0;
        int gone = 0;
        foreach ((string key, int? promoted) in _others)
        {
            if (!end.TryFind(key, out int? now))
            {
                gone++;
            }
            else if (now != promoted)
            {
                changed++;
            }
        }

        if (changed != 0)
        {
            differences.Add($"Tray icon entries: IsPromoted changed on {changed} of {OtherCount} entries of other programs (fingerprint {OtherFingerprint} at the start, {end.OtherFingerprint} at the end).");
        }

        if (gone != 0)
        {
            notes.Add($"Tray icon entries: {Entries(gone)} of other programs gone (Explorer removed them).");
        }

        foreach (TrayIconEntry entry in end.Tested.Where(entry => !TryFind(entry.Key, out _)))
        {
            if (!TrayIconPromotionKind.IsNotPromoted(entry.IsPromoted))
            {
                differences.Add($"Tray icon entry {entry.Key} of {TestedExe} (IsPromoted {Show(entry.IsPromoted)}) appeared during the run.");
            }
            else
            {
                notes.Add($"Tray icon entry {entry.Key} of {TestedExe} appeared during the run (created by Explorer when the icon was first shown).");
            }
        }

        int appeared = end._others.Keys.Count(key => !TryFind(key, out _));
        if (appeared != 0)
        {
            notes.Add($"Tray icon entries: {Entries(appeared)} of other programs appeared during the run (created by Explorer when a program first showed an icon).");
        }
    }

    /// <summary>The text for the results (no paths of other programs).</summary>
    public void AppendTo(StringBuilder text)
    {
        text.AppendLine("Tray icon entries (HKCU\\Control Panel\\NotifyIconSettings):");
        text.AppendLine(CultureInfo.InvariantCulture, $"  of the tested app {TestedExe}:{(Tested.Count == 0 ? " none" : string.Empty)}");
        foreach (TrayIconEntry entry in Tested)
        {
            text.AppendLine(CultureInfo.InvariantCulture, $"    {entry.Key}: IsPromoted={Show(entry.IsPromoted)}");
        }

        text.AppendLine(CultureInfo.InvariantCulture, $"  of other programs: {Entries(OtherCount)} (paths not recorded), IsPromoted fingerprint {OtherFingerprint}");
    }

    private bool TryFind(string key, out int? isPromoted)
    {
        foreach (TrayIconEntry entry in Tested)
        {
            if (string.Equals(entry.Key, key, StringComparison.OrdinalIgnoreCase))
            {
                isPromoted = entry.IsPromoted;
                return true;
            }
        }

        return _others.TryGetValue(key, out isPromoted);
    }

    private static string Fingerprint(Dictionary<string, int?> entries)
    {
        StringBuilder text = new();
        foreach (KeyValuePair<string, int?> entry in entries.OrderBy(static entry => entry.Key, StringComparer.OrdinalIgnoreCase))
        {
            text.Append(entry.Key.ToUpperInvariant()).Append('=').Append(Show(entry.Value)).Append('\n');
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())), 0, 8);
    }

    private static string Entries(int count) => count == 1 ? "1 entry" : $"{count} entries";

    private static string Show(int? value) => value?.ToString(CultureInfo.InvariantCulture) ?? "(missing)";
}
