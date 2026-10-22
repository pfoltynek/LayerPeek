using System.Drawing;
using System.Text.Json;

using PrusaTrayDiagnostic.App.E2E.Tests.Infrastructure;

namespace PrusaTrayDiagnostic.App.E2E.Tests;

/// <summary>
/// Unit tests of the pure parts of the system change kinds of T-065 (restore plan, state JSON, taskbar brightness): they
/// run in every <c>dotnet test</c> and read or write nothing of the system.
/// </summary>
public sealed class SystemChangeKindTests
{
    private const string Exe = @"D:\work\src\PrusaTrayDiagnostic.App\bin\Debug\net11.0\PrusaTrayDiagnostic.exe";

    [Fact]
    public void TrayRestoreWritesBackOriginalValuesIncludingMissingAndKeepsNewEntriesWithoutPromotion()
    {
        TrayIconPromotionState original = new(Exe, [new("100", null), new("200", 0), new("300", 1)]);
        TrayIconPromotionState current = new(Exe, [new("100", 1), new("200", 1), new("300", 1), new("400", 1), new("500", null)]);

        List<(TrayIconAction Action, string Key, int? Value)> plan = TrayIconPromotionKind.PlanRestore(original, current);

        // T-065 Q3 (2026-10-22): an entry created during the change stays, only its IsPromoted goes.
        Assert.Equal(
            [(TrayIconAction.DeletePromoted, "100", null), (TrayIconAction.SetPromoted, "200", 0), (TrayIconAction.DeletePromoted, "400", null)],
            plan);
    }

    [Fact]
    public void TrayRestoreNeverDeletesAnEntry() =>
        Assert.Equal([TrayIconAction.SetPromoted, TrayIconAction.DeletePromoted], Enum.GetValues<TrayIconAction>());

    [Fact]
    public void TrayStateWithANewUnpromotedEntryIsRestored()
    {
        TrayIconPromotionState original = new(Exe, [new("100", null)]);

        Assert.True(SystemChanges.TrayIconPromotion.Matches(original, new(Exe, [new("100", null), new("400", null)])));
        // IsPromoted = 0 that Explorer itself may write to a new entry: not promoted either.
        Assert.True(SystemChanges.TrayIconPromotion.Matches(original, new(Exe, [new("100", null), new("400", 0)])));
        Assert.False(SystemChanges.TrayIconPromotion.Matches(original, new(Exe, [new("100", null), new("400", 1)])));
    }

    [Fact]
    public void TrayRestoreOfAnUnchangedStateDoesNothing()
    {
        TrayIconPromotionState state = new(Exe, [new("100", null), new("200", 0)]);

        Assert.Empty(TrayIconPromotionKind.PlanRestore(state, state with { Entries = [new("200", 0), new("100", null)] }));
    }

    [Fact]
    public void TrayEntryThatExplorerRemovedCountsAsRestored()
    {
        TrayIconPromotionState original = new(Exe, [new("100", 1), new("200", null)]);
        TrayIconPromotionState current = new(Exe, [new("200", null)]);

        // Nothing to write into, and nothing of it can stay promoted: a journal entry that could never be restored would
        // block every later run.
        Assert.Empty(TrayIconPromotionKind.PlanRestore(original, current));
        Assert.True(SystemChanges.TrayIconPromotion.Matches(original, current));
    }

    [Fact]
    public void TrayStatesMatchIgnoringOrderAndKeyCase()
    {
        TrayIconPromotionState original = new(Exe, [new("Abc", null), new("200", 0)]);

        Assert.True(SystemChanges.TrayIconPromotion.Matches(original, new(Exe, [new("200", 0), new("abc", null)])));
        Assert.False(SystemChanges.TrayIconPromotion.Matches(original, new(Exe, [new("200", 0), new("abc", 1)])));
        Assert.False(SystemChanges.TrayIconPromotion.Matches(original, new(Exe, [new("200", 1), new("abc", null)])));
    }

    [Fact]
    public void SnapshotComparisonFindsChangedStateAndNotesNewEntries()
    {
        const string Other = @"C:\Program Files\Other\other.exe";
        SystemStateSnapshot start = Snapshot(new ThemeState(1, null), [("100", Exe, null), ("200", Other, 1)]);
        SystemStateSnapshot end = Snapshot(new ThemeState(0, null), [("100", Exe, 1), ("200", Other, 1), ("300", Exe, null), ("400", Exe, 0), ("500", Other, 1)]);
        List<string> notes = [];

        List<string> differences = start.Compare(end, Exe, notes);

        Assert.Equal(2, differences.Count);
        Assert.Contains("Theme", differences[0], StringComparison.Ordinal);
        Assert.Contains("100", differences[1], StringComparison.Ordinal);
        Assert.Equal(3, notes.Count);
    }

    [Fact]
    public void SnapshotComparisonFindsAPromotedNewEntryOfTheTestedExeAndALeftJournal()
    {
        SystemStateSnapshot start = Snapshot(new ThemeState(1, 1), []);
        SystemStateSnapshot end = Snapshot(new ThemeState(1, 1), [("300", Exe, 1)]) with { JournalEntries = ["Theme.json"] };
        List<string> notes = [];

        List<string> differences = start.Compare(end, Exe, notes);

        Assert.Equal(2, differences.Count);
        Assert.Contains("300", differences[0], StringComparison.Ordinal);
        Assert.Contains("Theme.json", differences[1], StringComparison.Ordinal);
        Assert.Empty(notes);
    }

    [Fact]
    public void SnapshotComparisonOfAnUnchangedSystemIsEmpty()
    {
        SystemStateSnapshot start = Snapshot(new ThemeState(null, 0), [("100", Exe, null)]) with { DoNotDisturb = DoNotDisturbState.On };
        List<string> notes = [];

        Assert.Empty(start.Compare(start with { DoNotDisturb = DoNotDisturbState.On }, Exe, notes));
        Assert.Contains("Do Not Disturb", Assert.Single(start.Compare(start with { DoNotDisturb = DoNotDisturbState.Off }, Exe, notes)), StringComparison.Ordinal);
        Assert.Empty(notes);
    }

    [Theory]
    [InlineData("DoNotDisturbToggle", true)]
    [InlineData("QuietHoursButton", true)]
    [InlineData("doNotDisturb", true)]
    [InlineData("FocusAssistTimer", false)]
    [InlineData("", false)]
    public void DoNotDisturbToggleIsRecognizedByItsAutomationId(string automationId, bool expected) =>
        Assert.Equal(expected, DoNotDisturbKind.IsCandidateId(automationId));

    private static SystemStateSnapshot Snapshot(ThemeState theme, IReadOnlyList<(string Key, string? ExecutablePath, int? IsPromoted)> trayIcons) =>
        new(theme, null, null, trayIcons, []);

    [Fact]
    public void TrayStateSurvivesTheJournalJson()
    {
        TrayIconPromotionState state = new(Exe, [new("100", null), new("200", 1)]);

        JsonElement json = SystemChanges.TrayIconPromotion.Serialize(state);
        TrayIconPromotionState back = SystemChanges.TrayIconPromotion.Deserialize(json);

        Assert.True(SystemChanges.TrayIconPromotion.Matches(state, back));
        Assert.Equal(Exe, back.ExecutablePath);
        Assert.Equal(JsonValueKind.Null, json.GetProperty("entries")[0].GetProperty("isPromoted").ValueKind);
    }

    [Fact]
    public void ThemeStateKeepsMissingValuesThroughTheJournalJson()
    {
        ThemeState state = new(null, 0);

        ThemeState back = SystemChanges.Theme.Deserialize(SystemChanges.Theme.Serialize(state));

        Assert.Equal(state, back);
        Assert.True(back.TaskbarIsLight);
        Assert.Contains("SystemUsesLightTheme=(missing)", back.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void KnownFolderPrefixOfExplorerIsExpanded()
    {
        string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);

        Assert.Equal(
            Path.Join(programFiles, @"Vendor\App.exe"),
            TrayIconPromotionKind.ExpandKnownFolder(@"{905E63B6-C1BF-494E-B29C-65B732D3D21A}\Vendor\App.exe"));
        Assert.Equal(Exe, TrayIconPromotionKind.ExpandKnownFolder(Exe));
    }

    [Theory]
    [InlineData(240, true, false, null, true)]
    [InlineData(120, true, false, null, false)]
    [InlineData(30, false, false, null, true)]
    [InlineData(130, false, false, null, false)]
    // Accent colour on a dark taskbar: only darker than the light one by a margin.
    [InlineData(130, false, true, 240.0, true)]
    [InlineData(230, false, true, 240.0, false)]
    [InlineData(130, false, true, null, false)]
    public void TaskbarBrightnessIsClassified(double brightness, bool light, bool accent, double? lightBrightness, bool expected) =>
        Assert.Equal(expected, ThemeKind.IsExpected(brightness, light, accent, lightBrightness));

    [Fact]
    public void MedianBrightnessIgnoresAFewIcons()
    {
        using Bitmap bitmap = new(40, 10);
        using (Graphics graphics = Graphics.FromImage(bitmap))
        {
            graphics.Clear(Color.FromArgb(32, 32, 32));
            graphics.FillRectangle(Brushes.White, 0, 0, 8, 10);
        }

        Assert.InRange(ThemeKind.MedianBrightness(bitmap), 31, 33);
    }
}
