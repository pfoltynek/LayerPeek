using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;

using FlaUI.Core.AutomationElements;

using LayerPeek.App.E2E.Tests.Infrastructure;
using LayerPeek.Simulator.Printers;

namespace LayerPeek.App.E2E.Tests;

/// <summary>
/// The log of the real app and the support actions of the About window (T-064, for T-024, T-048 and T-049): the record
/// format and a logged exception with its stack trace (also against the Native AOT exe with
/// <c>tools/run-e2e.ps1 -PublishAot</c>), the log folder in Explorer, the diagnostic bundle saved through the real save
/// dialog, and the report copied into the clipboard.
/// </summary>
public sealed partial class LogAndSupportTests(E2EFixture fixture)
{
    private const string PrinterId = "e2e-support";

    [Fact]
    public async Task LogHasRecordsAndAnExceptionWithItsStackTrace()
    {
        E2EFixture.Require();
        // A printer whose status is broken JSON: the client logs the malformed response with the exception (T-064 scenario).
        await using SimulatedPrinter printer = await Simulators.StartPrinterAsync(PrinterScenario.Malformed);
        await using AppInstance app = await StartAsync($"log{(E2EFixture.IsNativeAot ? "-aot" : string.Empty)}", printer);

        string warning = $"[pid {app.Process.Id}] LayerPeek.PrusaLink.PrusaLinkClient: Printer '{PrinterId}' sent a malformed response.";
        await Wait.UntilAsync(() => app.ReadLog().Contains(warning, StringComparison.Ordinal), TimeSpan.FromSeconds(30), "the malformed response in the log");

        string[] lines = app.ReadLog().Split('\n');
        Screenshots.SaveText(string.Join('\n', lines), $"log{(E2EFixture.IsNativeAot ? "-aot" : string.Empty)}");
        // Every line is a record (time with offset, level, PID, category) or an indented continuation.
        Assert.All(lines.Where(static line => line.Length > 0), static line => Assert.Matches(RecordOrContinuation(), line));
        Assert.Contains(lines, line => line.Contains(" INF [pid ", StringComparison.Ordinal) && line.Contains("LayerPeek", StringComparison.Ordinal));
        int record = Array.FindIndex(lines, line => line.EndsWith(warning, StringComparison.Ordinal));
        Assert.NotEqual(-1, record);
        Assert.Contains(" WRN ", lines[record], StringComparison.Ordinal);
        string[] exception = [.. lines.Skip(record + 1).TakeWhile(static line => line.StartsWith("    ", StringComparison.Ordinal))];
        Assert.NotEmpty(exception);
        string details = string.Join('\n', exception);
        Assert.Contains("Exception", exception[0], StringComparison.Ordinal);
        // The whole stack trace with method names, also in the Native AOT build (no line numbers there).
        Assert.Contains(exception, static line => line.TrimStart().StartsWith("at ", StringComparison.Ordinal));
        Assert.Contains("LayerPeek.PrusaLink.PrusaLinkClient", details, StringComparison.Ordinal);
        Assert.Contains("System.Text.Json", details, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OpenLogFolderShowsTheFolderInExplorerEvenWhenItWasMissing()
    {
        E2EFixture.Require();
        await using SimulatedPrinter printer = await Simulators.StartPrinterAsync(PrinterScenario.Idle);
        await using AppInstance app = await StartAsync("log-folder", printer);
        string logs = Path.Combine(app.DataDirectory, "logs");
        int shared = 0;
        try
        {
            nint about = await app.OpenAsync("about", AppWindowKind.Other);
            AutomationElement window = app.Element(about);
            // The app writes per batch and keeps no handle: the folder can go, and the action creates it again.
            await Wait.UntilAsync(() => TryDelete(logs), TimeSpan.FromSeconds(10), "the log folder deleted");

            await Ui.InvokeAsync(window, "SupportOpenLogFolder");

            await Wait.UntilAsync(() => ExplorerWindows.Shows(logs), TimeSpan.FromSeconds(20), "an Explorer window with the log folder");
            Assert.True(Directory.Exists(logs));
            Assert.Equal(string.Empty, Ui.Text(window, "SupportStatus"));
            // Only the Explorer window of the log folder itself (T-069: never the desktop or a window with the user's tabs).
            foreach (nint explorer in ExplorerWindows.WindowsShowingOnly(logs))
            {
                Screenshots.Save(explorer, "support-log-folder");
            }
        }
        finally
        {
            // Also a window that Explorer opens only after a timeout above. A tab in a window of the user is left alone
            // (decided 2026-10-22): closing it could close the user's other tabs.
            shared = await ExplorerWindows.CloseUntilGoneAsync(logs, TimeSpan.FromSeconds(3));
            if (shared > 0)
            {
                TestContext.Current.SendDiagnosticMessage($"The log folder opened as a tab in your Explorer window; that tab was left open: {logs}");
            }
        }

        Assert.True(shared > 0 || !ExplorerWindows.Shows(logs), "The Explorer window of the log folder is still open.");
    }

    [Fact]
    public async Task SaveDiagnosticBundleThroughTheRealSaveDialog()
    {
        E2EFixture.Require();
        await using SimulatedPrinter printer = await Simulators.StartPrinterAsync(PrinterScenario.Printing);
        await using AppInstance app = await StartAsync("bundle", printer, camera: "rtsp://camuser:CamPass9@127.0.0.1:1/live?token=Tok9Secret");
        nint about = await app.OpenAsync("about", AppWindowKind.Other);
        AutomationElement window = app.Element(about);
        DateTime started = DateTime.Now;
        string bundle;
        try
        {
            await Ui.InvokeAsync(window, "SupportSaveBundle");
            nint dialog = await WaitForSaveDialogAsync(app);
            Screenshots.Save(dialog, "support-save-dialog");
            // The test mode opens the dialog in its data folder (never the user's): Save with the suggested name, like a user.
            await Ui.InvokeAsync(app.Element(dialog), "1");

            await Wait.UntilAsync(() => Ui.Text(window, "SupportStatus") == "The diagnostic bundle has been saved.", TimeSpan.FromSeconds(30), "the bundle saved");
            bundle = Assert.Single(Directory.GetFiles(app.DataDirectory, "LayerPeek-diagnostics-*.zip"));
            Assert.Matches(BundleName(), Path.GetFileName(bundle));
        }
        finally
        {
            // Should the dialog ever have opened elsewhere, a bundle saved there by this test goes again.
            RemoveStray(started);
        }

        Screenshots.Save(about, "support-bundle-saved");
        using ZipArchive zip = ZipFile.OpenRead(bundle);
        Dictionary<string, string> entries = zip.Entries.ToDictionary(static entry => entry.FullName, static entry => Read(entry));
        Assert.Equal(["info.txt", "logs/LayerPeek.log", "settings.json"], entries.Keys.Order(StringComparer.Ordinal));
        Assert.Contains($"- E2E support printer [{PrinterId}]", entries["info.txt"], StringComparison.Ordinal);
        Assert.Contains("Saving a diagnostic bundle.", entries["logs/LayerPeek.log"], StringComparison.Ordinal);
        Assert.Contains("\"address\": \"" + printer.Address() + "\"", entries["settings.json"], StringComparison.Ordinal);
        foreach ((string name, string content) in entries)
        {
            foreach (string secret in new[] { printer.Password, "CamPass9", "camuser", "Tok9Secret" })
            {
                Assert.False(content.Contains(secret, StringComparison.Ordinal), $"{name} contains '{secret}'.");
            }
        }
    }

    [Fact]
    public async Task CopyInfoPutsTheReportIntoTheClipboardAndSaysCopied()
    {
        E2EFixture.Require();
        await using SimulatedPrinter printer = await Simulators.StartPrinterAsync(PrinterScenario.Printing);
        await using AppInstance app = await StartAsync("copy", printer);
        nint about = await app.OpenAsync("about", AppWindowKind.Other);
        AutomationElement window = app.Element(about);
        AutomationElement copy = await Ui.FindAsync(window, "SupportCopyInfo");
        // The user's clipboard text comes back after the test (decided 2026-10-22). Writing and reading a probe first proves
        // that the restore works on this machine before anything of the user's is replaced for good.
        string? saved = ClipboardText.Get();
        const string Probe = "LayerPeek E2E clipboard probe";
        ClipboardText.Set(Probe);
        Assert.Equal(Probe, ClipboardText.Get());
        string text;
        try
        {
            ClipboardText.Set(null);
            await Ui.InvokeAsync(window, "SupportCopyInfo");

            // What a screen reader reads: the name of the button says "Copied" for two seconds (announced: live region).
            await Wait.UntilAsync(() => copy.Properties.Name.Value == "✓ Copied", TimeSpan.FromSeconds(20), "the button saying Copied");
            Screenshots.Save(about, "support-copied");
            Assert.Equal(PoliteLiveSetting, LiveSetting(copy));
            text = await Wait.StableAsync(() => ClipboardText.Get() ?? string.Empty, TimeSpan.FromSeconds(10), "the report in the clipboard");
            // The test mode keeps its copies out of the clipboard history (Win+V) and the cloud clipboard.
            Assert.True(ClipboardText.HasFormat(ClipboardText.ExcludeFromHistoryFormat), "The copy is not kept out of the clipboard history.");
            await Wait.UntilAsync(() => copy.Properties.Name.Value == "Copy info to clipboard", TimeSpan.FromSeconds(10), "the button text back");
            Assert.Equal(string.Empty, Ui.Text(window, "SupportStatus"));
        }
        finally
        {
            ClipboardText.Set(saved);
        }

        Assert.Equal(saved, ClipboardText.Get());
        Screenshots.SaveText(text, "support-clipboard");
        Assert.StartsWith("LayerPeek ", text, StringComparison.Ordinal);
        Assert.Contains($"- E2E support printer [{PrinterId}]", text, StringComparison.Ordinal);
        Assert.Matches(LastLogLines(), text);
        Assert.DoesNotContain(printer.Password, text, StringComparison.Ordinal);
        Assert.True(Encoding.UTF8.GetByteCount(text) <= 64 * 1024);
    }

    /// <summary>UIA_LiveSettingPropertyId and its value Polite.</summary>
    private const int LiveSettingProperty = 30135;

    private const int PoliteLiveSetting = 1;

    private static int LiveSetting(AutomationElement element) =>
        (int)((FlaUI.UIA3.UIA3FrameworkAutomationElement)element.FrameworkAutomationElement).NativeElement.GetCurrentPropertyValue(LiveSettingProperty);

    private Task<AppInstance> StartAsync(string name, SimulatedPrinter printer, string? camera = null) =>
        AppInstance.StartAsync(
            fixture,
            name,
            TestSettings.Json("en", [new TestPrinter(PrinterId, "E2E support printer", printer.Address(), DisabledCamera: camera)]),
            Simulators.Secrets(PrinterId, printer));

    /// <summary>The save dialog the app opened (a standard dialog window of the app process).</summary>
    private static async Task<nint> WaitForSaveDialogAsync(AppInstance app)
    {
        nint dialog = 0;
        await Wait.UntilAsync(
            () => (dialog = app.Windows().FirstOrDefault(static hwnd => Native.ClassName(hwnd) == "#32770")) != 0,
            TimeSpan.FromSeconds(20),
            "the save dialog");
        return dialog;
    }

    /// <summary>
    /// A bundle the save dialog put into one of the user's usual folders instead of the test folder (created during this
    /// test, with the app's name pattern) goes again.
    /// </summary>
    private static void RemoveStray(DateTime since)
    {
        string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string[] folders =
        [
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
            Path.Combine(profile, "Downloads"),
        ];
        foreach (string folder in folders.Where(static folder => folder.Length > 0 && Directory.Exists(folder)))
        {
            foreach (string file in Directory.GetFiles(folder, "LayerPeek-diagnostics-*.zip"))
            {
                if (File.GetCreationTime(file) >= since.AddSeconds(-1) && BundleName().IsMatch(Path.GetFileName(file)))
                {
                    try
                    {
                        File.Delete(file);
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        // Never hides the failure of the test itself; the leftover is named in the output.
                        TestContext.Current.SendDiagnosticMessage($"A bundle of this test could not be removed: {file}");
                    }
                }
            }
        }
    }

    private static bool TryDelete(string folder)
    {
        try
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }

            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A file still delete-pending or briefly locked (antivirus): tried again.
            return false;
        }
    }

    private static string Read(ZipArchiveEntry entry)
    {
        using StreamReader reader = new(entry.Open(), Encoding.UTF8);
        return reader.ReadToEnd();
    }

    [GeneratedRegex(@"^(\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d{3} [+-]\d{2}:\d{2} (TRC|DBG|INF|WRN|ERR|CRT) \[pid \d+\] \S+: .*|    .*)$")]
    private static partial Regex RecordOrContinuation();

    [GeneratedRegex(@"\nLast log lines \(\d+\):\n\d{4}-\d{2}-\d{2} ")]
    private static partial Regex LastLogLines();

    [GeneratedRegex(@"^LayerPeek-diagnostics-\d{8}-\d{6}\.zip$")]
    private static partial Regex BundleName();
}
