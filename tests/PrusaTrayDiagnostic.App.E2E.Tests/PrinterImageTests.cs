using System.Drawing;
using System.Globalization;
using System.Text;

using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;

using PrusaTrayDiagnostic.App.E2E.Tests.Infrastructure;
using PrusaTrayDiagnostic.Simulator.Printers;

namespace PrusaTrayDiagnostic.App.E2E.Tests;

/// <summary>
/// The printer image of T-039 in the real app (T-069): the image button's menu (automatic, a model, a custom image), a
/// phone photo with an EXIF orientation chosen in the real open dialog (which starts in the test's data folder, T-067),
/// Cancel and Save, the image in the diagnostics header, and the tray menu icon changing after the save without the
/// printer going back to "Connecting…". Pixel checks: the illustrations from the repository's assets, the photo by the
/// colours of its quadrants.
/// </summary>
public sealed class PrinterImageTests(E2EFixture fixture)
{
    private const string PrinterId = "e2e-image";

    /// <summary>Same tolerance as the menu icons (<see cref="TrayMenuDetailTests"/>), see the notes of T-069.</summary>
    private const double MeanTolerance = 6;

    private const int MaxTolerance = 48;

    [Fact]
    public async Task CustomImageFromAPhoneIsShownUpright()
    {
        E2EFixture.Require();
        DateTime started = DateTime.Now;
        await using SimulatedPrinter printer = await Simulators.StartModelAsync(SimulatedModel.Mk4S);
        await using AppInstance app = await StartAsync("image-custom", printer);
        string photo = WritePhoto(app);

        // The menu of the image button: automatic (checked), the models, a custom image; choosing a model changes the preview.
        (nint settings, AutomationElement window) = await OpenPrinterTabAsync(app);
        AutomationElement button = await Ui.FindAsync(window, "PrinterImageButton");
        Assert.Equal("Printer image: MK4S (detected)", button.Name);
        using Bitmap automatic = CaptureElement(settings, button);
        AutomationElement popup = await OpenImageMenuAsync(app, button);
        AutomationElement automaticItem = await Ui.FindAsync(popup, "PrinterImageAutomatic");
        Assert.Equal(ToggleState.On, automaticItem.Patterns.Toggle.Pattern.ToggleState.ValueOrDefault);
        Assert.False((await Ui.FindAsync(popup, "PrinterImageRemoveCustom")).IsEnabled, "Remove custom image is enabled without one.");
        AutomationElement models = await Ui.FindAsync(popup, "PrinterImageModels");
        models.Patterns.ExpandCollapse.Pattern.Expand();
        AutomationElement[] modelItems = [];
        await Wait.UntilAsync(() => (modelItems = models.FindAllChildren(cf => cf.ByControlType(ControlType.MenuItem))).Length > 0, TimeSpan.FromSeconds(10), "the model list");
        string[] modelNames = [.. modelItems.Select(static item => item.Name)];
        Assert.Contains("Core One", modelNames);
        Assert.Contains("MK4S", modelNames);
        Assert.Contains("XL", modelNames);
        Assert.Contains("MINI", modelNames);
        SaveAppPopups(app, settings, "image-custom-1-menu");
        modelItems.First(static item => item.Name == "XL").Patterns.Invoke.Pattern.Invoke();
        await Wait.UntilAsync(() => button.Name == "Printer image: XL", TimeSpan.FromSeconds(10), "the XL preview");
        await Task.Delay(300, TestContext.Current.CancellationToken);
        using (Bitmap xl = CaptureElement(settings, button))
        {
            Assert.True(Screenshots.Difference(automatic, xl) > 0.05, "Choosing XL did not change the preview.");
        }

        // A custom image from the real open dialog: the photo is shown upright.
        await ImportPhotoAsync(app, window, photo, "image-custom-2");
        await AssertUprightAsync(settings, button, "the preview");
        Assert.Equal("Printer image: custom image", button.Name);
        Screenshots.Save(settings, "image-custom-3-preview");

        // Cancel: nothing changes.
        await Ui.InvokeAsync(window, "SettingsCancel");
        await Wait.UntilAsync(() => !Native.IsWindowVisible(settings), TimeSpan.FromSeconds(10), "the settings to close");
        Assert.Empty(SavedImages(app));
        Assert.DoesNotContain("\"image\"", ReadSettings(app), StringComparison.Ordinal);

        // Again with Save: the photo is stored upright in printer-images/ of the test folder.
        (settings, window) = await OpenPrinterTabAsync(app);
        await ImportPhotoAsync(app, window, photo, "image-custom-4");
        await AssertUprightAsync(settings, await Ui.FindAsync(window, "PrinterImageButton"), "the preview");
        await Ui.InvokeAsync(window, "SettingsSave");
        await Wait.UntilAsync(() => !Native.IsWindowVisible(settings), TimeSpan.FromSeconds(20), "the settings to save and close");
        string saved = Assert.Single(SavedImages(app));
        using (Bitmap stored = new(saved))
        {
            Assert.True(stored.Height > stored.Width, $"The saved image is not upright: {stored.Size}.");
            Assert.Equal(PixelChecks.UprightQuadrants, PixelChecks.ReadQuadrants(stored, new Rectangle(Point.Empty, stored.Size))?.Colors);
        }

        Assert.Contains("\"Custom\"", ReadSettings(app), StringComparison.Ordinal);
        AssertNothingOutsideTheTestFolder(started, Path.GetFileNameWithoutExtension(photo));
    }

    [Fact]
    public async Task DiagnosticsHeaderShowsThePrinterImage()
    {
        E2EFixture.Require();
        await using SimulatedPrinter printer = await Simulators.StartModelAsync(SimulatedModel.Mk4S);
        await using AppInstance app = await StartAsync("image-diagnostics", printer);
        nint diagnostics = await app.OpenAsync($"diagnostics:{PrinterId}", AppWindowKind.Diagnostics);
        await Task.Delay(500, TestContext.Current.CancellationToken);

        // The illustration of the model, pixel for pixel on the window background. The image itself is no UI Automation
        // element (a drawing control); it is found left of the printer name (48 + 12 DIP in the header grid).
        Rectangle area = await HeaderImageAreaAsync(app, diagnostics);
        using (Bitmap capture = Screenshots.Capture(diagnostics))
        {
            Screenshots.Save(capture, "image-diagnostics-1-illustration");
            Native.GetWindowRect(diagnostics, out Native.Rect rect);
            double scaling = Native.GetDpiForWindow(diagnostics) / 96.0;
            int size = (int)Math.Round(48 * scaling);
            int pixels = PixelChecks.IllustrationPixelSize(48, scaling);
            Rectangle estimate = new(area.Left - rect.Left + ((area.Width - size) / 2), area.Top - rect.Top + ((area.Height - size) / 2), size, size);
            // The background of the window right of the name's column start is the header background.
            Color background = PixelChecks.Median(capture, new Rectangle(estimate.Right + 2, estimate.Top + 2, 4, 4));
            StringBuilder report = new();
            Dictionary<string, PixelMatch> matches = [];
            using (Bitmap asset = PixelChecks.LoadIllustration("mk4", pixels))
            using (Bitmap expected = PixelChecks.ComposeOn(asset, background))
            {
                matches["mk4"] = PixelChecks.Compare(capture, estimate.Location, expected, slack: SearchSlack);
            }

            Point found = new(estimate.Left + matches["mk4"].Offset.X, estimate.Top + matches["mk4"].Offset.Y);
            foreach (string id in PixelChecks.IllustrationIds.Where(static id => id != "mk4"))
            {
                using Bitmap asset = PixelChecks.LoadIllustration(id, pixels);
                using Bitmap expected = PixelChecks.ComposeOn(asset, background);
                matches[id] = PixelChecks.Compare(capture, found, expected);
            }

            report.AppendLine(CultureInfo.InvariantCulture, $"Header image near {estimate} (found at {found}), scaling {scaling:0.##}, asset {pixels} px, background {background}: {string.Join("; ", matches.Select(static m => $"{m.Key} {m.Value}"))}");
            Screenshots.SaveText(report.ToString(), "image-diagnostics-report");
            if (pixels == size)
            {
                PixelMatch match = matches["mk4"];
                Assert.True(match.Mean <= MeanTolerance && match.Max <= MaxTolerance, $"The header does not show the MK4 illustration.{Environment.NewLine}{report}");
                Assert.True(matches.Where(static m => m.Key != "mk4").All(m => m.Value.Mean > match.Mean), $"Another illustration matches better.{Environment.NewLine}{report}");
            }
        }

        // After a custom image is saved: the photo, upright.
        await app.CloseWindowAsync(diagnostics);
        await SaveCustomImageAsync(app, "image-diagnostics-2");
        diagnostics = await app.OpenAsync($"diagnostics:{PrinterId}", AppWindowKind.Diagnostics);
        await Task.Delay(500, TestContext.Current.CancellationToken);
        await AssertUprightAsync(diagnostics, await HeaderImageAreaAsync(app, diagnostics), "the diagnostics header");
        Screenshots.Save(diagnostics, "image-diagnostics-3-custom");
    }

    /// <summary>How far (pixels) the header image is searched around its expected place.</summary>
    private const int SearchSlack = 12;

    /// <summary>
    /// The screen area of the header image of the diagnostics window, with <see cref="SearchSlack"/> around it: the 48 DIP
    /// column left of the printer name (12 DIP gap), its top about at the top of the name (the image is top-aligned, the name block is centred in a 48 DIP row).
    /// </summary>
    private static async Task<Rectangle> HeaderImageAreaAsync(AppInstance app, nint diagnostics)
    {
        AutomationElement name = await Ui.FindAsync(app.Element(diagnostics), "DiagnosticsPrinterName");
        double scaling = Native.GetDpiForWindow(diagnostics) / 96.0;
        int size = (int)Math.Round(48 * scaling);
        Rectangle bounds = name.BoundingRectangle;
        int left = bounds.Left - (int)Math.Round(60 * scaling);
        int top = bounds.Top - (int)Math.Round(4 * scaling);
        Rectangle area = new(left, top, size, size);
        area.Inflate(SearchSlack, SearchSlack);
        return area;
    }

    [Fact]
    public async Task SavedImageChangesTheMenuWithoutReconnecting()
    {
        E2EFixture.Require();
        await using SimulatedPrinter printer = await Simulators.StartModelAsync(SimulatedModel.Mk4S);
        await using AppInstance app = await StartAsync("image-menu", printer);
        string connecting = TrayMenu.Resource("StateConnecting");

        TrayMenuWindow menu = await TrayMenu.OpenWithMouseAsync(app);
        await Wait.UntilAsync(() => menu.Items()[0].Name.EndsWith(": Ready", StringComparison.Ordinal), TimeSpan.FromSeconds(30), "the printer ready in the menu");
        await Task.Delay(400, TestContext.Current.CancellationToken);
        using Bitmap before = CaptureElement(menu.Handle, IconOf(menu));
        Screenshots.Save(menu.Handle, "image-menu-1-before");
        await TrayMenu.CloseAsync(menu);

        await SaveCustomImageAsync(app, "image-menu-2");
        DateTime saved = DateTime.UtcNow;

        // Read the row again and again for 10 s after the save: never "Connecting…".
        menu = await TrayMenu.OpenWithMouseAsync(app);
        List<string> names = [];
        while (DateTime.UtcNow - saved < TimeSpan.FromSeconds(10))
        {
            string name = menu.Items()[0].Name;
            if (names.Count == 0 || names[^1] != name)
            {
                names.Add(name);
            }

            await Task.Delay(100, TestContext.Current.CancellationToken);
        }

        Screenshots.SaveText(string.Join(Environment.NewLine, names.Select(TrayMenu.CodePoints)), "image-menu-names-after-save");
        Assert.All(names, name => Assert.DoesNotContain(connecting, name, StringComparison.Ordinal));
        AutomationElement icon = IconOf(menu);
        using Bitmap after = CaptureElement(menu.Handle, icon);
        Screenshots.Save(menu.Handle, "image-menu-3-after");
        Assert.True(Screenshots.Difference(before, after) > 0.2, "The menu icon did not change after the save.");
        Assert.Equal(PixelChecks.UprightQuadrants, PixelChecks.ReadQuadrants(after, new Rectangle(Point.Empty, after.Size))?.Colors);
        await TrayMenu.CloseAsync(menu);
    }

    /// <summary>The app with one MK4S printer, once it has detected the model (the automatic image is its illustration).</summary>
    private async Task<AppInstance> StartAsync(string name, SimulatedPrinter printer)
    {
        AppInstance app = await AppInstance.StartAsync(
            fixture,
            name,
            TestSettings.Json("en", [new TestPrinter(PrinterId, "MK4 workshop", printer.Address())]),
            Simulators.Secrets(PrinterId, printer));
        try
        {
            await Simulators.WaitForDetectedModelsAsync(app, PrinterId);
            return app;
        }
        catch
        {
            await app.DisposeAsync();
            throw;
        }
    }

    /// <summary>The phone photo (EXIF orientation 6) in the app's data folder, where the open dialog starts (T-067).</summary>
    private static string WritePhoto(AppInstance app)
    {
        string photo = Path.Combine(app.DataDirectory, $"phone-photo-{Guid.NewGuid().ToString("N")[..8]}.jpg");
        File.WriteAllBytes(photo, PixelChecks.PhonePhotoJpeg());
        return photo;
    }

    private static async Task<(nint Hwnd, AutomationElement Window)> OpenPrinterTabAsync(AppInstance app)
    {
        nint settings = await app.OpenAsync("settings", AppWindowKind.Settings);
        AutomationElement window = app.Element(settings);
        (await Ui.ItemsAsync(window, "SettingsPrinterList", 1))[0].Patterns.SelectionItem.Pattern.Select();
        await Ui.SelectAsync(window, "PrinterTabConnection");
        return (settings, window);
    }

    /// <summary>Opens the image button's menu (a popup window of the app) and returns it.</summary>
    private static async Task<AutomationElement> OpenImageMenuAsync(AppInstance app, AutomationElement button)
    {
        button.Patterns.Invoke.Pattern.Invoke();
        AutomationElement? popup = null;
        await Wait.UntilAsync(
            () => (popup = app.Windows().Select(app.Element).FirstOrDefault(static window => Ui.TryFind(window, "PrinterImageCustom") is not null)) is not null,
            TimeSpan.FromSeconds(10),
            "the image menu");
        return popup!;
    }

    /// <summary>
    /// "Custom image…" → the real open dialog (in the data folder, T-067) → the photo selected in the file list (typing a
    /// path into the dialog through UI Automation is not reliable, T-064) → Open.
    /// </summary>
    private static async Task ImportPhotoAsync(AppInstance app, AutomationElement window, string photo, string screenshot)
    {
        AutomationElement popup = await OpenImageMenuAsync(app, await Ui.FindAsync(window, "PrinterImageButton"));
        (await Ui.FindAsync(popup, "PrinterImageCustom")).Patterns.Invoke.Pattern.Invoke();
        nint dialog = 0;
        await Wait.UntilAsync(() => (dialog = app.Windows().FirstOrDefault(static hwnd => Native.ClassName(hwnd) == "#32770")) != 0, TimeSpan.FromSeconds(20), "the open dialog");
        AutomationElement dialogElement = app.Element(dialog);
        string file = Path.GetFileNameWithoutExtension(photo);
        AutomationElement? item = null;
        await Wait.UntilAsync(
            () => (item = dialogElement.FindAllDescendants(cf => cf.ByControlType(ControlType.ListItem)).FirstOrDefault(candidate => candidate.Name.StartsWith(file, StringComparison.OrdinalIgnoreCase))) is not null,
            TimeSpan.FromSeconds(10),
            $"'{file}' in the open dialog (it must open in the test's data folder)");
        item!.Patterns.SelectionItem.Pattern.Select();
        await Wait.UntilAsync(() => item.Patterns.SelectionItem.Pattern.IsSelected.ValueOrDefault, TimeSpan.FromSeconds(5), "the photo selected");
        Screenshots.Save(dialog, $"{screenshot}-open-dialog");
        await Ui.InvokeAsync(dialogElement, "1");
        await Wait.UntilAsync(() => !Native.IsWindowVisible(dialog), TimeSpan.FromSeconds(10), "the open dialog to close");
    }

    /// <summary>Imports the photo in the settings and saves.</summary>
    private static async Task SaveCustomImageAsync(AppInstance app, string screenshot)
    {
        string photo = WritePhoto(app);
        (nint settings, AutomationElement window) = await OpenPrinterTabAsync(app);
        await ImportPhotoAsync(app, window, photo, screenshot);
        await AssertUprightAsync(settings, await Ui.FindAsync(window, "PrinterImageButton"), "the preview");
        await Ui.InvokeAsync(window, "SettingsSave");
        await Wait.UntilAsync(() => !Native.IsWindowVisible(settings), TimeSpan.FromSeconds(20), "the settings to save and close");
        Assert.Single(SavedImages(app));
    }

    /// <summary>Waits until the element shows the photo upright (blue, red / yellow, green).</summary>
    private static Task AssertUprightAsync(nint hwnd, AutomationElement element, string what) => AssertUprightAsync(hwnd, element.BoundingRectangle, what);

    /// <summary>Waits until the screen area <paramref name="area"/> of the window shows the photo upright.</summary>
    private static async Task AssertUprightAsync(nint hwnd, Rectangle area, string what)
    {
        QuadrantColor[]? colors = null;
        try
        {
            await Wait.UntilAsync(
                () =>
                {
                    using Bitmap capture = CaptureArea(hwnd, area);
                    colors = PixelChecks.ReadQuadrants(capture, new Rectangle(Point.Empty, capture.Size))?.Colors;
                    return colors is not null && colors.SequenceEqual(PixelChecks.UprightQuadrants);
                },
                TimeSpan.FromSeconds(10),
                $"the photo upright in {what}");
        }
        catch (TimeoutException ex)
        {
            Screenshots.Save(hwnd, "image-not-upright");
            throw new InvalidOperationException($"{what}: quadrants {(colors is null ? "(no photo)" : string.Join(", ", colors))}, expected {string.Join(", ", PixelChecks.UprightQuadrants)}.", ex);
        }
    }

    /// <summary>The element's area of its window (captured with PrintWindow: only the app's own window).</summary>
    private static Bitmap CaptureElement(nint hwnd, AutomationElement element) => CaptureArea(hwnd, element.BoundingRectangle);

    /// <summary>A screen area of the app's window (captured with PrintWindow: only the app's own window).</summary>
    private static Bitmap CaptureArea(nint hwnd, Rectangle bounds)
    {
        using Bitmap window = Screenshots.Capture(hwnd);
        Native.GetWindowRect(hwnd, out Native.Rect rect);
        Rectangle area = Rectangle.Intersect(new Rectangle(bounds.Left - rect.Left, bounds.Top - rect.Top, bounds.Width, bounds.Height), new Rectangle(Point.Empty, window.Size));
        return window.Clone(area, window.PixelFormat);
    }

    private static AutomationElement IconOf(TrayMenuWindow menu) =>
        menu.Items()[0].FindFirstChild(cf => cf.ByControlType(ControlType.Image)) ?? throw new InvalidOperationException("The printer row has no icon.");

    /// <summary>The popups of the app (menus of the image button) captured window by window.</summary>
    private static void SaveAppPopups(AppInstance app, nint except, string name)
    {
        foreach (nint hwnd in app.Windows().Where(hwnd => hwnd != except))
        {
            Screenshots.Save(hwnd, name);
        }
    }

    private static string[] SavedImages(AppInstance app)
    {
        string folder = Path.Combine(app.DataDirectory, "printer-images");
        return Directory.Exists(folder) ? Directory.GetFiles(folder) : [];
    }

    private static string ReadSettings(AppInstance app) => app.ReadSettings();

    /// <summary>No copy of the photo and no saved image in the user's usual folders (the dialog started in the test folder).</summary>
    private static void AssertNothingOutsideTheTestFolder(DateTime since, string photoName)
    {
        string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string[] folders =
        [
            Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
            Path.Combine(profile, "Downloads"),
        ];
        string[] found = [.. folders.Where(static folder => folder.Length > 0 && Directory.Exists(folder))
            .SelectMany(folder => Directory.GetFiles(folder, $"{photoName}*"))
            .Concat(folders.Where(static folder => folder.Length > 0 && Directory.Exists(folder)).SelectMany(static folder => Directory.GetFiles(folder, "*.png")).Where(file => File.GetCreationTime(file) >= since && Path.GetFileName(file).StartsWith(PrinterId, StringComparison.OrdinalIgnoreCase)))];
        Assert.True(found.Length == 0, $"Files of the test outside its folder: {string.Join(", ", found)}");
    }
}
