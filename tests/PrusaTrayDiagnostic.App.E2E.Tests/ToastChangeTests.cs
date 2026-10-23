using System.Drawing;
using System.Drawing.Imaging;
using System.Text.Json.Nodes;

using PrusaTrayDiagnostic.App.E2E.Tests.Infrastructure;

namespace PrusaTrayDiagnostic.App.E2E.Tests;

/// <summary>
/// Unit tests of the pure parts of the toast tests of T-068 (registry restore plan, states, which UI texts belong to the
/// test toast, the icon search, resource texts): they run in every <c>dotnet test</c> and read or write nothing of the
/// system.
/// </summary>
public sealed class ToastChangeTests
{
    private static readonly RegistryValueState EnabledOff = new("Enabled", "DWord", "0");
    private static readonly RegistryValueState BannerOff = new("ShowBanner", "DWord", "0");
    private static readonly RegistryValueState Sound = new("Sound", "String", "x");

    [Fact]
    public void KeyThatDidNotExistIsDeletedAsAWhole()
    {
        RegistryKeySnapshot current = new(true, [EnabledOff, Sound], ["sub"]);

        Assert.Equal([(RegistryKeyStep.DeleteKey, (RegistryValueState?)null)], RegistryKeys.Plan(RegistryKeySnapshot.Missing, current));
        Assert.Empty(RegistryKeys.Plan(RegistryKeySnapshot.Missing, RegistryKeySnapshot.Missing));
    }

    [Fact]
    public void ExistingKeyGetsItsValuesBackAndLosesNewOnes()
    {
        RegistryKeySnapshot original = new(true, [EnabledOff, Sound], []);
        RegistryKeySnapshot current = new(true, [EnabledOff with { Data = "1" }, BannerOff], []);

        List<(RegistryKeyStep Action, RegistryValueState? Value)> plan = RegistryKeys.Plan(original, current);

        Assert.Equal(
            [(RegistryKeyStep.DeleteValue, BannerOff), (RegistryKeyStep.SetValue, EnabledOff), (RegistryKeyStep.SetValue, Sound)],
            plan);
    }

    [Fact]
    public void DeletedKeyIsCreatedWithItsValues()
    {
        RegistryKeySnapshot original = new(true, [EnabledOff], []);

        Assert.Equal([(RegistryKeyStep.CreateKey, (RegistryValueState?)null), (RegistryKeyStep.SetValue, EnabledOff)], RegistryKeys.Plan(original, RegistryKeySnapshot.Missing));
    }

    [Fact]
    public void UnchangedKeyNeedsNothingWhateverTheOrderAndCase()
    {
        RegistryKeySnapshot original = new(true, [EnabledOff, Sound], ["A"]);
        RegistryKeySnapshot current = new(true, [Sound, EnabledOff with { Name = "ENABLED" }], ["a"]);

        Assert.Empty(RegistryKeys.Plan(original, current));
        Assert.True(original.SameAs(current));
    }

    [Fact]
    public void KeyStatesDifferInExistenceKindDataAndSubkeys()
    {
        RegistryKeySnapshot state = new(true, [EnabledOff], []);

        Assert.False(state.SameAs(RegistryKeySnapshot.Missing));
        Assert.False(state.SameAs(state with { Values = [EnabledOff with { Kind = "QWord" }] }));
        Assert.False(state.SameAs(state with { Values = [EnabledOff with { Data = "1" }] }));
        Assert.False(state.SameAs(state with { SubKeys = ["new"] }));
        Assert.False(state.SameAs(state with { Values = [EnabledOff, Sound] }));
    }

    [Fact]
    public void NotificationsOfTheTestAppAreSwitchedOnOnlyWhereTheyAreOff()
    {
        RegistryKeySnapshot original = new(true, [EnabledOff, BannerOff, Sound, new("ShowInActionCenter", "DWord", "0"), new("Other", "String", "0")], []);

        RegistryKeySnapshot on = ToastAppSettingsKind.WithNotificationsOn(original);

        Assert.Equal("1", on.Value("Enabled")?.Data);
        Assert.Equal("1", on.Value("ShowBanner")?.Data);
        Assert.Equal("x", on.Value("Sound")?.Data);
        // Only the two switches; a string "0" is not a switch.
        Assert.Equal("0", on.Value("ShowInActionCenter")?.Data);
        Assert.Equal("0", on.Value("Other")?.Data);
        // A missing key means Windows' defaults (on): it stays missing.
        Assert.Same(RegistryKeySnapshot.Missing, ToastAppSettingsKind.WithNotificationsOn(RegistryKeySnapshot.Missing));
    }

    [Fact]
    public void ToastStatesSurviveTheJournal()
    {
        RegistryKeySnapshot key = new(true, [EnabledOff, Sound], ["sub"]);
        Assert.True(key.SameAs(SystemChanges.ToastAppSettings.Deserialize(SystemChanges.ToastAppSettings.Serialize(key))));
        Assert.True(RegistryKeySnapshot.Missing.SameAs(SystemChanges.ToastAppSettings.Deserialize(SystemChanges.ToastAppSettings.Serialize(RegistryKeySnapshot.Missing))));

        foreach (ToastsEnabledState state in new ToastsEnabledState[] { new(null), new(0), new(1) })
        {
            Assert.Equal(state, SystemChanges.ToastsEnabled.Deserialize(SystemChanges.ToastsEnabled.Serialize(state)));
        }

        Assert.Equal(new TestToastsState(true), SystemChanges.TestToasts.Deserialize(SystemChanges.TestToasts.Serialize(new TestToastsState(true))));
        Assert.Equal(new TestToastsState(false, true), SystemChanges.TestToasts.Deserialize(SystemChanges.TestToasts.Serialize(new TestToastsState(false, true))));
        // An entry journaled before T-068 knows no activator.
        using System.Text.Json.JsonDocument old = System.Text.Json.JsonDocument.Parse("""{"shortcutExists":true}""");
        Assert.Equal(new TestToastsState(true, false), SystemChanges.TestToasts.Deserialize(old.RootElement));
    }

    [Fact]
    public void TestToastsAreRestoredOnlyWhenTheActivatorIsGoneToo()
    {
        // T-068: a registered activator of the test app would let a test toast start the Debug exe.
        Assert.False(SystemChanges.TestToasts.Matches(new TestToastsState(false), new TestToastsState(false, ActivatorRegistered: true)));
        Assert.True(SystemChanges.TestToasts.Matches(new TestToastsState(true, ActivatorRegistered: true), new TestToastsState(false)));
    }

    [Fact]
    public void TheActivatorKeysAreThoseTheAppWrites()
    {
        Assert.Equal(@"Software\Classes\CLSID\{1708A774-C00D-4A8B-B9E5-1BEB8527DD24}", TestToastsKind.ActivatorSubKey(AppSwitches.TestToastActivatorClsid));
        Assert.Equal(@"Software\Classes\CLSID\{E8389DCA-851B-498C-8082-69B1AB263C14}", TestToastsKind.ActivatorSubKey(AppSwitches.ProductionToastActivatorClsid));
        Assert.NotEqual(AppSwitches.TestToastActivatorClsid, AppSwitches.ProductionToastActivatorClsid);
    }

    [Fact]
    public void NotificationsAreOnUnlessSwitchedOff()
    {
        Assert.True(new ToastsEnabledState(null).IsOn);
        Assert.True(new ToastsEnabledState(1).IsOn);
        Assert.False(new ToastsEnabledState(0).IsOn);
    }

    [Fact]
    public void TestToastsAreRestoredOnlyWhenTheShortcutIsGone()
    {
        // A shortcut before the change is a leftover of a crashed run: it goes as well.
        Assert.True(SystemChanges.TestToasts.Matches(new TestToastsState(true), new TestToastsState(false)));
        Assert.True(SystemChanges.TestToasts.Matches(new TestToastsState(false), new TestToastsState(false)));
        Assert.False(SystemChanges.TestToasts.Matches(new TestToastsState(false), new TestToastsState(true)));
    }

    [Fact]
    public void ToastKindsAreRegisteredAndNeedTheWarning()
    {
        Assert.Same(SystemChanges.ToastsEnabled, SystemChanges.Kinds["ToastsEnabled"]);
        Assert.Same(SystemChanges.ToastAppSettings, SystemChanges.Kinds["ToastSettings"]);
        Assert.Same(SystemChanges.TestToasts, SystemChanges.Kinds["ToastHistory"]);
        Assert.True(SystemChanges.ToastsEnabled.ChangesSystem);
        Assert.True(SystemChanges.ToastAppSettings.ChangesSystem);
        Assert.True(SystemChanges.TestToasts.ChangesSystem);
        // The toasts are cleared before the key Windows created for them goes (entries are restored in file name order).
        Assert.True(string.CompareOrdinal(SystemChanges.Journal.FileOf("ToastHistory", "x"), SystemChanges.Journal.FileOf("ToastSettings", null)) < 0);
    }

    [Fact]
    public void TestAppSettingsKeyIsTheTestAppsOnly()
    {
        Assert.EndsWith(@"\Notifications\Settings\PrusaTrayDiagnostic.E2E", ToastAppSettingsKind.SubKey, StringComparison.Ordinal);
        Assert.Equal("Prusa Tray Diagnostic E2E", ToastTexts.AppName);
    }

    [Fact]
    public void OnlyTheTextsOfTheTestToastBelongToIt()
    {
        ToastTexts texts = new("PTD E2E 1234abcd", "Print paused", [".bgcode"]);

        Assert.True(texts.OnlyOurs([
            ("Text", "PTD E2E 1234abcd", "Title"),
            ("Text", "Print paused", ""),
            ("Text", "benchy_0.4n_0.2mm_PLA.bgcode", ""),
            ("Text", "Prusa Tray Diagnostic E2E", ""),
            ("ListItem", "Prusa Tray Diagnostic E2E, PTD E2E 1234abcd, Print paused", ""),
            ("Image", "Some icon", ""),
            ("Button", "Close", ""),
            ("Group", string.Empty, ""),
        ]));
        // Another notification, a time stamp or a window title: not ours.
        Assert.False(texts.OnlyOurs([("Text", "PTD E2E 1234abcd", "Title"), ("Text", "New message from Alice", "")]));
        Assert.False(texts.OnlyOurs([("Text", "PTD E2E 1234abcd", "Title"), ("Text", "10:42", "TimeStamp")]));
        // A long button name is content (e.g. a notification presented as a button), not a control label.
        Assert.False(texts.OnlyOurs([("Button", new string('x', ToastTexts.MaxControlLabel + 1), "")]));
        // The buttons of the toast itself (Windows 11 26300) have long localized labels; they are controls by their id.
        Assert.True(texts.OnlyOurs([.. ToastTexts.ChromeButtonIds.Select(static id => ("Button", new string('x', 60), id))]));
    }

    [Fact]
    public void AppIconIsFoundInAToastHeaderAtItsSize()
    {
        Color background = Color.FromArgb(44, 44, 44);
        using Bitmap area = new(72, 40, PixelFormat.Format32bppArgb);
        using (Graphics graphics = Graphics.FromImage(area))
        using (Bitmap icon = ToastIcon.Reference(24))
        {
            graphics.Clear(background);
            graphics.DrawImage(icon, new Rectangle(11, 7, 24, 24));
        }

        ToastIconMatch? match = ToastIcon.Find(area, ToastIcon.CandidateSizes(1.5));

        Assert.NotNull(match);
        Assert.True(match.IsMatch, match.ToString());
        Assert.Equal(new Rectangle(11, 7, 24, 24), match.Location);
        Assert.True(match.Score < 0.02, match.ToString());
    }

    [Fact]
    public void AppIconIsFoundInARealToastHeader()
    {
        // The header of a real banner (Windows 11 26300, 100 %): the icon is 12 px; a 16 px reference differed by 15.8 %
        // and its dark body on the dark toast left too little contrast for a plain colour comparison.
        using Bitmap area = new(Path.Combine(AppContext.BaseDirectory, "Fixtures", "toast-header-icon-area-26300-100pct.png"));

        ToastIconMatch? match = ToastIcon.Find(area, ToastIcon.CandidateSizes(1));

        Assert.NotNull(match);
        Assert.True(match.IsMatch, match.ToString());
        Assert.Equal(12, match.Size);
    }

    [Fact]
    public void EmptyOrOtherHeaderIsNoMatch()
    {
        using Bitmap empty = new(72, 40, PixelFormat.Format32bppArgb);
        using (Graphics graphics = Graphics.FromImage(empty))
        {
            graphics.Clear(Color.FromArgb(243, 243, 243));
        }

        Assert.False(ToastIcon.Find(empty, ToastIcon.CandidateSizes(1))?.IsMatch ?? false);

        using Bitmap other = new(72, 40, PixelFormat.Format32bppArgb);
        using (Graphics graphics = Graphics.FromImage(other))
        {
            graphics.Clear(Color.FromArgb(243, 243, 243));
            graphics.FillEllipse(Brushes.Magenta, 10, 8, 16, 16);
        }

        Assert.False(ToastIcon.Find(other, ToastIcon.CandidateSizes(1))?.IsMatch ?? false);
    }

    [Fact]
    public void IconSizesFollowTheScale()
    {
        Assert.Equal([12, 16, 20, 24, 32], ToastIcon.CandidateSizes(1));
        Assert.Equal([18, 24, 30, 36, 48], ToastIcon.CandidateSizes(1.5));
        Assert.Equal([15, 20, 25, 30, 40], ToastIcon.CandidateSizes(1.25));
    }

    [Fact]
    public void OnlyTheTestAppsNotificationSettingsKeyMayBeTouched()
    {
        Assert.Equal(ToastAppSettingsKind.SettingsSubKey + @"\PrusaTrayDiagnostic.E2E", ToastAppSettingsKind.TestAppKey("PrusaTrayDiagnostic.E2E"));
        // An empty id would make the key the parent of every app's settings, which this kind deletes as a whole.
        Assert.Throws<InvalidOperationException>(() => ToastAppSettingsKind.TestAppKey(string.Empty));
        Assert.Throws<InvalidOperationException>(() => ToastAppSettingsKind.TestAppKey(ToastSystemState.ProductionAppUserModelId));
        Assert.Throws<InvalidOperationException>(() => ToastAppSettingsKind.TestAppKey(@"PrusaTrayDiagnostic\x.E2E"));
    }

    [Fact]
    public void TheBodyIsOneWholeLineOfAnElement()
    {
        // Windows 11 26300 shows the event text and the file name in one element, one per line.
        Assert.True(ToastMatch.IsLineOf("Print paused\nbenchy_0.4n_0.2mm_PLA.bgcode", "Print paused"));
        Assert.True(ToastMatch.IsLineOf("Print paused\r\nbenchy.bgcode", "Print paused"));
        Assert.True(ToastMatch.IsLineOf("Print paused", "Print paused"));
        Assert.False(ToastMatch.IsLineOf("Print paused again\nbenchy.bgcode", "Print paused"));
        Assert.False(ToastMatch.IsLineOf("New notification from Prusa Tray Diagnostic E2E, PTD E2E 1234abcd, Print paused", "Print paused"));
    }

    [Fact]
    public void ARegionWithOtherNotificationsIsNotOneToast()
    {
        ToastTexts texts = new("PTD E2E 1234abcd", "Print paused", [".bgcode"]);

        // The toast with its time stamp in the notification center.
        Assert.True(texts.IsOneToast([("ListItem", "Prusa Tray Diagnostic E2E, PTD E2E 1234abcd, Print paused", ""), ("Text", "PTD E2E 1234abcd", "Title"), ("Text", "Print paused", ""), ("Text", "10:42", "TimeStamp")]));
        // As seen on Windows 11 26300: the toast in the notification center with its own (localized, long) buttons, and the banner.
        Assert.True(texts.IsOneToast([
            ("ListItem", "PTD E2E 1234abcd, Print paused", ""),
            ("Text", "PTD E2E 1234abcd", "Title"),
            ("Text", "Print paused", ""),
            ("Button", new string('x', 43), "ExpandButton"),
            ("Text", "16:57", "TimeStamp"),
            ("Button", new string('x', 30), "SettingsButton"),
            ("Button", new string('x', 45), "DismissButton"),
        ]));
        // A chrome id does not make a text a control.
        Assert.False(texts.IsOneToast([("Text", "PTD E2E 1234abcd", "Title"), ("Text", "Print paused", ""), ("Text", "Alice wrote something long here", "DismissButton")]));
        // A container that also holds another app's notification.
        Assert.False(texts.IsOneToast([("Group", "PTD E2E 1234abcd", ""), ("Text", "Print paused", ""), ("Text", "New message from Alice about the meeting", "")]));
        Assert.False(texts.IsOneToast([("Group", "PTD E2E 1234abcd", ""), ("Text", "Print paused", ""), ("Text", "10:42", ""), ("Text", "Mail", ""), ("Text", "Hi", "")]));
    }

    [Fact]
    public void NamesThatAreNotTheToastsOwnAreNeverReported()
    {
        ToastElement own = new("Text", "PTD E2E 1234abcd", "Title", "TextBlock", new Rectangle(1, 2, 3, 4), IsOurs: true);
        ToastElement other = new("Text", "Alice: secret", "Body", "TextBlock", new Rectangle(1, 2, 3, 4), IsOurs: false);

        Assert.Contains("'PTD E2E 1234abcd'", own.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("Alice", other.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("secret", new ToastMatch(null!, null!, 0, [own, other]).Describe(), StringComparison.Ordinal);
        Assert.Contains("(other name, 13 chars)", other.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void OnlyAUiaTimeoutCountsAsABusyPoll()
    {
        Assert.True(Toasts.IsUiaTimeoutCode(unchecked((int)0x80131505)));
        Assert.True(Toasts.IsUiaTimeout(new TimeoutException()));
        Assert.False(Toasts.IsUiaTimeoutCode(unchecked((int)0x80040201)));
        Assert.False(Toasts.IsUiaTimeout(new InvalidOperationException()));
    }

    [Fact]
    public void ElementsMoveWithTheBannerThatWasStillSlidingIn()
    {
        // Run 2026-10-23: the banner was found mid-animation, so the icon was searched where the app name had moved to.
        ToastElement region = new("Group", "PTD E2E 1234abcd", "", "", new Rectangle(2400, 1250, 364, 128), IsOurs: true);
        ToastElement name = new("Text", "Prusa Tray Diagnostic E2E", "SenderName", "TextBlock", new Rectangle(2440, 1263, 138, 18), IsOurs: true);
        ToastElement unplaced = new("Button", "", "DismissButton", "", Rectangle.Empty, IsOurs: false);
        ToastMatch toast = new(null!, null!, 0, [region, name, unplaced]);

        IReadOnlyList<ToastElement> settled = toast.ElementsAt(new Rectangle(2180, 1250, 364, 128));

        Assert.Equal(new Rectangle(2180, 1250, 364, 128), settled[0].Bounds);
        Assert.Equal(new Rectangle(2220, 1263, 138, 18), settled[1].Bounds);
        Assert.Equal(Rectangle.Empty, settled[2].Bounds);
        (Rectangle area, string how) = Toasts.IconSearchArea(toast, new Rectangle(2180, 1250, 364, 128), 1);
        Assert.Equal(Rectangle.FromLTRB(2180, 1250, 2220, 1299), area);
        Assert.StartsWith("left of the app name", how, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ClearToastsModeRunsOnlyADebugBuildOfTheApp()
    {
        string folder = UnitTestDirectories.Create();
        try
        {
            string exe = Path.Combine(folder, "PrusaTrayDiagnostic.exe");
            File.WriteAllBytes(exe, [0x4D, 0x5A]);
            // A Release build: the assembly does not know the mode.
            File.WriteAllText(Path.Combine(folder, "PrusaTrayDiagnostic.dll"), "no test mode here");
            Assert.NotNull(ClearToasts.NotADebugBuild(exe));
            await Assert.ThrowsAsync<InvalidOperationException>(() => ClearToasts.RunAsync(exe, TimeSpan.FromSeconds(1)));

            File.WriteAllText(Path.Combine(folder, "PrusaTrayDiagnostic.dll"), "x " + AppSwitches.ClearToastsArgument + " x", System.Text.Encoding.Unicode);
            Assert.Null(ClearToasts.NotADebugBuild(exe));

            // Another exe, or one that is gone.
            string other = Path.Combine(folder, "Other.exe");
            File.Copy(exe, other);
            Assert.NotNull(ClearToasts.NotADebugBuild(other));
            Assert.NotNull(ClearToasts.NotADebugBuild(Path.Combine(folder, "missing", "PrusaTrayDiagnostic.exe")));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void TestAppsPlayNoSound()
    {
        JsonNode? settings = JsonNode.Parse(TestSettings.Json("en", []));

        Assert.Equal("Off", settings?["notifications"]?["sound"]?.GetValue<string>());
    }

    /// <summary>
    /// User decision 2026-10-22: every test that starts a test app with toasts (its shortcut, its notifications) changes the
    /// system and needs the warning; only the child entry of the crash test is skipped outside its child process.
    /// </summary>
    [Fact]
    public void EveryTestThatStartsATestAppWithToastsNeedsTheWarning()
    {
        string[] tests =
        [
            .. typeof(ToastNotificationTests).GetMethods().Where(static method => method.IsDefined(typeof(FactAttribute), inherit: false)).Select(static method => method.Name),
            nameof(TestModeOsIntegrationTests.TestToastsUseTheirOwnShortcutAndQuitRemovesIt),
            nameof(TestModeOsIntegrationTests.ClearToastsModeRemovesWhatACrashedTestAppLeftAndStartsNoApp),
        ];
        Assert.Equal(7, tests.Length);

        foreach (string name in tests)
        {
            System.Reflection.MethodInfo method = typeof(ToastNotificationTests).GetMethod(name) ?? typeof(TestModeOsIntegrationTests).GetMethod(name)!;
            Assert.Contains(
                method.GetCustomAttributes(typeof(TraitAttribute), inherit: false).OfType<TraitAttribute>(),
                trait => trait.Name == SystemChanges.Trait && trait.Value == SystemChangeTraits.Toasts);
        }

        // The toast tests switch Do Not Disturb off when it is on: the warning must list it too.
        foreach (System.Reflection.MethodInfo method in typeof(ToastNotificationTests).GetMethods().Where(static method => method.IsDefined(typeof(FactAttribute), inherit: false)))
        {
            Assert.Contains(
                method.GetCustomAttributes(typeof(TraitAttribute), inherit: false).OfType<TraitAttribute>(),
                trait => trait.Name == SystemChanges.Trait && trait.Value == SystemChangeTraits.DoNotDisturb);
        }

        // Without the switch the test mode shows no toast and creates no shortcut: it runs in every end-to-end run.
        Assert.DoesNotContain(
            typeof(TestModeOsIntegrationTests).GetMethod(nameof(TestModeOsIntegrationTests.WithoutTheSwitchTheTestModeCreatesNoShortcut))!.GetCustomAttributes(typeof(TraitAttribute), inherit: false).OfType<TraitAttribute>(),
            static trait => trait.Name == SystemChanges.Trait);
    }

    [Fact]
    public void ToastTextsComeFromTheResourcesOfTheApp()
    {
        Assert.Equal("Print paused", AppStrings.Get("en", "NotificationPrintPaused"));
        Assert.Equal("Der Drucker meldet einen Fehler", AppStrings.Get("de", "NotificationError"));
        Assert.Throws<KeyNotFoundException>(() => AppStrings.Get("en", "NoSuchKey"));
    }
}
