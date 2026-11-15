using System.Globalization;
using System.Text;

using FlaUI.Core.AutomationElements;
using FlaUI.Core.Patterns;

using LayerPeek.App.E2E.Tests.Infrastructure;
using LayerPeek.Simulator.Printers;

namespace LayerPeek.App.E2E.Tests;

/// <summary>
/// The password box of the settings window does not give the password away through UI Automation (T-054, finding I4 of
/// the security review 2026-10-21): any process of the same user can read UIA, not only a screen reader. A known password
/// typed into the PrusaLink password box must not appear in its <c>ValuePattern.Value</c>, <c>Name</c>, <c>HelpText</c>,
/// <c>LegacyIAccessiblePattern.Value</c> / <c>Name</c> / <c>Description</c> / <c>Help</c> or <c>TextPattern</c> text
/// (when offered), and no other element of the window (raw view) holds it; <c>IsPassword</c> is read and reported. The
/// password really reached the box: the connection test logs in to a simulated printer that requires it. The camera has
/// no password box (T-014: credentials in the camera address are refused, so there is nothing to check there).
/// Regression check, e.g. after an Avalonia update; every reading is saved to the results folder.
/// </summary>
public sealed class PasswordAutomationTests(E2EFixture fixture)
{
    private const string PrinterId = "e2e-password";
    private const string PasswordBox = "PrinterPassword";

    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(30);

    [Fact]
    public async Task PasswordBoxDoesNotExposeThePasswordThroughUiAutomation()
    {
        E2EFixture.Require();
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        // Unique and long, so that a hit (also of a part of it) can only mean a leak.
        string password = "E2E-pw-" + Guid.NewGuid().ToString("N")[..16];
        await using SimulatedPrinter printer = await SimulatedPrinter.StartAsync(
            new SimulatedPrinterOptions { Scenario = PrinterScenario.Idle, Password = password },
            cancellationToken);
        // Saved without a password: only the one typed below can log in.
        string settingsJson = TestSettings.Json("en", [new TestPrinter(PrinterId, "E2E password printer", printer.Address())]);
        await using AppInstance app = await AppInstance.StartAsync(fixture, "password-automation", settingsJson);

        StringBuilder report = new();
        report.AppendLine(CultureInfo.InvariantCulture, $"Password box readings through UI Automation (T-054), {E2EFixture.AppPath}");
        List<string> leaks = [];

        // 1. Typed, not saved yet.
        nint settings = await app.OpenAsync("settings", AppWindowKind.Settings);
        AutomationElement window = app.Element(settings);
        (await Ui.ItemsAsync(window, "SettingsPrinterList", 1))[0].Patterns.SelectionItem.Pattern.Select();
        await Ui.SelectAsync(window, "PrinterTabConnection");
        AutomationElement box = await Ui.FindAsync(window, PasswordBox);
        Assert.True(box.Properties.IsEnabled.ValueOrDefault, "The password box is disabled (no secret store in the test mode?).");
        box.Patterns.Value.Pattern.SetValue(password);

        // The password reached the box: the simulated printer accepts the login only with it.
        await Ui.InvokeAsync(window, "PrinterTestConnection");
        string success = AppStrings.Get("en", "SettingsTestSuccess");
        string successStart = success[..success.IndexOf("{0}", StringComparison.Ordinal)];
        string result = string.Empty;
        await Wait.UntilAsync(
            () => (result = Ui.Text(window, "PrinterTestResult")).StartsWith(successStart, StringComparison.Ordinal),
            TestTimeout,
            "the connection test with the typed password");
        Screenshots.Save(settings, "password-1-typed-and-tested");

        Inspect(box, "1 typed", password, report, leaks);
        RecordIsPassword(box, report);
        ScanWindow(app, window, "1 typed", password, report, leaks);

        // Control: a unique text in a plain text box is found by the same scan, so the scan really reads the values
        // (a broken read would let the check above pass without looking).
        string canary = "E2E-canary-" + Guid.NewGuid().ToString("N")[..16];
        await Ui.SetTextAsync(window, "PrinterUserName", canary);
        List<string> canaryHits = [];
        ScanWindow(app, window, "control", canary, report, canaryHits);
        foreach (string source in new[] { "ValuePattern.Value", "LegacyIAccessible.Value" })
        {
            Assert.Contains(canaryHits, hit => hit.Contains("'PrinterUserName'", StringComparison.Ordinal) && hit.EndsWith(" " + source, StringComparison.Ordinal));
        }

        report.AppendLine(CultureInfo.InvariantCulture, $"[control] a plain text in PrinterUserName found by the scan: {string.Join("; ", canaryHits)}");
        await Ui.SetTextAsync(window, "PrinterUserName", "maker");

        // 2. Saved and the settings opened again: whatever the box shows for the saved password must not be it either.
        await Ui.InvokeAsync(window, "SettingsSave");
        await Wait.UntilAsync(() => !Native.IsWindowVisible(settings), TimeSpan.FromSeconds(20), "the settings to save and close");
        settings = await app.OpenAsync("settings", AppWindowKind.Settings);
        window = app.Element(settings);
        (await Ui.ItemsAsync(window, "SettingsPrinterList", 1))[0].Patterns.SelectionItem.Pattern.Select();
        await Ui.SelectAsync(window, "PrinterTabConnection");
        box = await Ui.FindAsync(window, PasswordBox);
        Screenshots.Save(settings, "password-2-saved-reopened");

        Inspect(box, "2 saved, reopened", password, report, leaks);
        RecordIsPassword(box, report);
        ScanWindow(app, window, "2 saved, reopened", password, report, leaks);

        // The password was really saved: the box stays untouched (empty keeps the saved password) and the simulated
        // printer, which still requires the password, accepts the login of a fresh connection test.
        Assert.False(Ui.IsShown(window, "PrinterTestResult"), "The reopened settings already show a connection test result.");
        await Ui.InvokeAsync(window, "PrinterTestConnection");
        await Wait.UntilAsync(
            () => (result = Ui.Text(window, "PrinterTestResult")).StartsWith(successStart, StringComparison.Ordinal),
            TestTimeout,
            "the connection test with the saved password");
        report.AppendLine("[2 saved, reopened] connection test with the untouched box: success (the password was saved)");

        string text = report.ToString();
        Screenshots.SaveText(text, "password-automation-readings");
        TestContext.Current.TestOutputHelper?.WriteLine(text);
        await app.CloseWindowAsync(settings);
        await app.QuitAsync();

        Assert.True(leaks.Count == 0, "The password is exposed through UI Automation: " + string.Join("; ", leaks));
    }

    /// <summary>
    /// Reads every listed UIA source of the password box into the report; a source holding the password is a leak. The
    /// read of <c>ValuePattern.Value</c> must succeed (a masked or empty value), so a broken read cannot pass silently.
    /// </summary>
    private static void Inspect(AutomationElement box, string state, string password, StringBuilder report, List<string> leaks)
    {
        report.AppendLine().AppendLine(CultureInfo.InvariantCulture, $"[{state}] {PasswordBox}");
        foreach ((string source, string? value) in Read(box))
        {
            if (source == "ValuePattern.Value" || source == "ValuePattern")
            {
                Assert.True(value is not null, $"{state}: {PasswordBox} {source} could not be read.");
            }

            report.AppendLine(CultureInfo.InvariantCulture, $"  {source} = {Describe(value, password)}");
            if (Holds(value, password))
            {
                leaks.Add($"{state}: {PasswordBox} {source}");
            }
        }
    }

    /// <summary>
    /// Records <c>IsPassword</c> of the box. Measured with Avalonia 12.1.6: <c>False</c>, the Win32 automation node of
    /// Avalonia does not answer the property at all and its peers have no API for it (follow-up task T-234). It is no
    /// leak of the password, so it is only reported; T-234 decides whether this test requires <c>True</c> once fixed.
    /// </summary>
    private static void RecordIsPassword(AutomationElement box, StringBuilder report)
    {
        bool? isPassword = Try<bool?>(() => box.Properties.IsPassword.ValueOrDefault);
        report.AppendLine(CultureInfo.InvariantCulture, $"  IsPassword = {isPassword?.ToString() ?? "(not readable)"}");
    }

    /// <summary>Every element of the window in the raw view: each one holding <paramref name="secret"/> in any UIA source is added to <paramref name="hits"/>.</summary>
    private static void ScanWindow(AppInstance app, AutomationElement window, string state, string secret, StringBuilder report, List<string> hits)
    {
        FlaUI.Core.ITreeWalker walker = app.Automation.TreeWalkerFactory.GetRawViewWalker();
        int count = 0;
        int skipped = 0;
        Stack<AutomationElement> pending = new([window]);
        while (pending.Count > 0)
        {
            AutomationElement element = pending.Pop();
            count++;
            foreach ((string source, string? value) in Read(element))
            {
                if (Holds(value, secret))
                {
                    string id = Try(() => element.Properties.AutomationId.ValueOrDefault) ?? "?";
                    string type = Try(() => element.Properties.ControlType.ValueOrDefault.ToString()) ?? "?";
                    hits.Add($"{state}: element '{id}' ({type}) {source}");
                }
            }

            // A failed navigation (the element went away meanwhile) skips the rest of that level: counted, not hidden.
            for (AutomationElement? child = Navigate(() => walker.GetFirstChild(element), ref skipped); child is not null; child = Navigate(() => walker.GetNextSibling(child), ref skipped))
            {
                pending.Push(child);
            }
        }

        report.AppendLine(CultureInfo.InvariantCulture, $"[{state}] raw view of the window: {count} elements scanned, {skipped} navigation(s) failed (subtrees skipped)");
        Assert.True(count > 10, $"{state}: only {count} elements in the raw view of the settings window.");
        // Threshold 5 %: an element can go away between the reads (Avalonia removes hidden elements, e.g. a tooltip or the
        // progress bar of the connection test), so a few failures are tolerated; a broken walker fails on most elements,
        // and more than 1 in 20 would leave too much of the window unchecked.
        Assert.True(skipped * 20 < count, $"{state}: {skipped} of {count} navigations in the raw view failed; too much of the window was not scanned.");
    }

    /// <summary>One step of the tree walker: <see langword="null"/> at the end of a level; a failed step counts in <paramref name="failed"/>.</summary>
    private static AutomationElement? Navigate(Func<AutomationElement?> step, ref int failed)
    {
        try
        {
            return step();
        }
        catch (Exception ex) when (IsReadFailure(ex))
        {
            failed++;
            return null;
        }
    }

    /// <summary>The UIA sources the task lists (and a few more texts), each read on its own; a failed read is reported.</summary>
    private static IEnumerable<(string Source, string? Value)> Read(AutomationElement element)
    {
        yield return ("Name", Try(() => element.Properties.Name.ValueOrDefault));
        yield return ("HelpText", Try(() => element.Properties.HelpText.ValueOrDefault));
        yield return ("ItemStatus", Try(() => element.Properties.ItemStatus.ValueOrDefault));

        if (Try(() => element.Patterns.Value.TryGetPattern(out IValuePattern? value) ? value : null) is { } valuePattern)
        {
            yield return ("ValuePattern.Value", Try(() => valuePattern.Value.ValueOrDefault));
        }
        else
        {
            yield return ("ValuePattern", null);
        }

        if (Try(() => element.Patterns.LegacyIAccessible.TryGetPattern(out ILegacyIAccessiblePattern? legacy) ? legacy : null) is { } legacyPattern)
        {
            yield return ("LegacyIAccessible.Value", Try(() => legacyPattern.Value.ValueOrDefault));
            yield return ("LegacyIAccessible.Name", Try(() => legacyPattern.Name.ValueOrDefault));
            yield return ("LegacyIAccessible.Description", Try(() => legacyPattern.Description.ValueOrDefault));
            yield return ("LegacyIAccessible.Help", Try(() => legacyPattern.Help.ValueOrDefault));
        }
        else
        {
            yield return ("LegacyIAccessiblePattern", null);
        }

        if (Try(() => element.Patterns.Text.TryGetPattern(out ITextPattern? text) ? text : null) is { } textPattern)
        {
            yield return ("TextPattern.DocumentRange.GetText", Try(() => textPattern.DocumentRange.GetText(-1)));
            yield return ("TextPattern.GetSelection", Try(() => string.Concat(textPattern.GetSelection().Select(static range => range.GetText(-1)))));
        }
        else
        {
            yield return ("TextPattern", null);
        }
    }

    /// <summary>The secret or any 6-character part of it (a partial leak counts too).</summary>
    private static bool Holds(string? value, string secret)
    {
        if (string.IsNullOrEmpty(value))
        {
            return false;
        }

        for (int start = 0; start + 6 <= secret.Length; start++)
        {
            if (value.AsSpan().Contains(secret.AsSpan(start, 6), StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>What the report shows: never the password itself, only whether a source holds it.</summary>
    private static string Describe(string? value, string password) => value switch
    {
        null => "(not offered or not readable)",
        _ when Holds(value, password) => $"LEAK: holds the password ({value.Length} chars)",
        "" => "(empty)",
        _ => $"'{value}'",
    };

    private static T? Try<T>(Func<T> read)
    {
        try
        {
            return read();
        }
        catch (Exception ex) when (IsReadFailure(ex))
        {
            return default;
        }
    }

    /// <summary>A UIA read that failed because the element or the value is not available (not a bug of the test).</summary>
    private static bool IsReadFailure(Exception ex) =>
        ex is System.Runtime.InteropServices.COMException or InvalidOperationException or NotSupportedException or TimeoutException
            or FlaUI.Core.Exceptions.ElementNotAvailableException or FlaUI.Core.Exceptions.PropertyNotSupportedException
            or FlaUI.Core.Exceptions.PatternNotSupportedException;
}
