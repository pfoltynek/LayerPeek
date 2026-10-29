using System.Drawing;
using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;

using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;

using LayerPeek.Simulator.Camera;
using LayerPeek.Simulator.Printers;

namespace LayerPeek.App.E2E.Tests.Infrastructure;

/// <summary>What a <see cref="WidgetRun"/> starts with (T-093).</summary>
internal sealed record WidgetRunOptions
{
    /// <summary>The print cycle of the simulator; <see langword="null"/> = its default (90 s heating, 30 min print).</summary>
    public SimulationTiming? Timing { get; init; }

    public PrinterScenario Scenario { get; init; } = PrinterScenario.Printing;

    /// <summary>Whether the simulated job files have G-code previews (<c>--no-previews</c> when not).</summary>
    public bool Previews { get; init; } = true;

    public string Language { get; init; } = "en";

    /// <summary>
    /// The printer's cool-down thresholds (T-079, T-109), 30–100 °C, used for both the nozzle and the bed;
    /// <see langword="null"/> = the defaults 40 / 40 °C.
    /// </summary>
    public int? CooldownThresholdCelsius { get; init; }

    /// <summary>The <c>camera.window</c> section; <see langword="null"/> = the defaults (normal window in the centre).</summary>
    public JsonObject? CameraWindow { get; init; }

    /// <summary>The <c>camera.overlay</c> section as written by an older or this version; <see langword="null"/> = none.</summary>
    public JsonObject? Overlay { get; init; }

    /// <summary>More files of the data folder (e.g. a <c>camera-windows.json</c> of version 0.1.0).</summary>
    public IReadOnlyDictionary<string, string>? DataFiles { get; init; }
}

/// <summary>
/// The camera overlay widgets on the real app (T-093): a simulated PrusaLink printer (T-045) with its G-code previews
/// (T-086), the simulated RTSP camera, and the app in the isolated test mode with that printer and camera. Disposing quits
/// the app first, then stops the simulators.
/// </summary>
internal sealed class WidgetRun : IAsyncDisposable
{
    public const string PrinterId = "e2e-widgets";

    /// <summary>A short print cycle (real seconds): heating, a print of a minute, cooling while FINISHED, a short IDLE.</summary>
    public static SimulationTiming ShortCycle { get; } = new()
    {
        HeatUp = TimeSpan.FromSeconds(15),
        Print = TimeSpan.FromSeconds(60),
        FinishedHold = TimeSpan.FromSeconds(110),
        IdleHold = TimeSpan.FromSeconds(20),
    };

    private WidgetRun(SimulatedPrinter printer, RtspCamera camera)
    {
        Printer = printer;
        Camera = camera;
    }

    public SimulatedPrinter Printer { get; }

    public RtspCamera Camera { get; }

    public AppInstance App { get; private set; } = null!;

    public static async Task<WidgetRun> StartAsync(E2EFixture fixture, string name, WidgetRunOptions options)
    {
        SimulatedPrinter printer = await SimulatedPrinter.StartAsync(
            new SimulatedPrinterOptions { Scenario = options.Scenario, Timing = options.Timing ?? SimulationTiming.Default, Previews = options.Previews },
            TestContext.Current.CancellationToken);
        RtspCamera? camera = null;
        try
        {
            camera = Simulators.StartCamera();
            WidgetRun run = new(printer, camera);
            run.App = await AppInstance.StartAsync(
                fixture,
                name,
                SettingsJson(printer.Address(), camera.StreamUri, options),
                Simulators.Secrets(PrinterId, printer),
                dataFiles: options.DataFiles);
            return run;
        }
        catch
        {
            if (camera is not null)
            {
                await camera.DisposeAsync();
            }

            await printer.DisposeAsync();
            throw;
        }
    }

    /// <summary>The <c>settings.json</c> (format version 1, as the tests of the earlier tasks) with the options of the run.</summary>
    public static string SettingsJson(string address, Uri camera, WidgetRunOptions options)
    {
        JsonObject printer = new TestPrinter(PrinterId, "Core One", address, Camera: camera, CameraWindow: options.CameraWindow).ToJson();
        if (options.CooldownThresholdCelsius is { } threshold)
        {
            printer["cooldownNozzleThresholdCelsius"] = threshold;
            printer["cooldownBedThresholdCelsius"] = threshold;
        }

        if (options.Overlay is not null)
        {
            printer["camera"]!["overlay"] = options.Overlay.DeepClone();
        }

        JsonObject settings = JsonNode.Parse(TestSettings.Json(options.Language, []))!.AsObject();
        settings["printers"] = new JsonArray(printer);
        return settings.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
    }

    /// <summary>Opens the camera window of the printer.</summary>
    public Task<nint> OpenCameraAsync() => App.OpenAsync($"camera:{PrinterId}", AppWindowKind.Camera);

    /// <summary>
    /// Runs <paramref name="body"/>; when it fails, every visible window of the app is captured into the results (with
    /// <paramref name="name"/>) before the failure goes on, so that a failed run shows what the app showed.
    /// </summary>
    public Task GuardAsync(string name, Func<Task> body) => FailureScreenshots.GuardAsync(App, name, body);

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (App is not null)
            {
                await App.DisposeAsync();
            }
        }
        finally
        {
            try
            {
                await Camera.DisposeAsync();
            }
            finally
            {
                await Printer.DisposeAsync();
            }
        }
    }
}

/// <summary>Screenshots of every visible window of an app when a test step fails (T-093).</summary>
internal static class FailureScreenshots
{
    public static async Task GuardAsync(AppInstance app, string name, Func<Task> body)
    {
        try
        {
            await body();
        }
        catch (Exception failure) when (failure is not OperationCanceledException)
        {
            Capture(app, name);
            throw;
        }
    }

    /// <summary>Captures every visible window of <paramref name="app"/> as <c>failure-name-N</c>; never throws.</summary>
    public static void Capture(AppInstance app, string name)
    {
        try
        {
            if (app.Process.HasExited)
            {
                Screenshots.SaveText($"The app exited with {app.Process.ExitCode}.", $"failure-{name}");
                return;
            }

            int index = 0;
            foreach (nint hwnd in app.Windows())
            {
                try
                {
                    Screenshots.Save(hwnd, $"failure-{name}-{index}");
                    Screenshots.SaveText(Tree(app.Element(hwnd)), $"failure-{name}-{index}-uia");
                    index++;
                }
                catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or System.ComponentModel.Win32Exception)
                {
                    // A window closing right now: the others are still worth having.
                }
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            // Nothing more to capture; the test's own failure is what matters.
        }
    }

    /// <summary>The UI Automation tree of a window: control type, automation id, name, bounds, off screen.</summary>
    public static string Tree(AutomationElement root)
    {
        StringBuilder text = new();
        Append(root, 0);
        return text.ToString();

        void Append(AutomationElement element, int depth)
        {
            try
            {
                text.Append(' ', depth * 2)
                    .Append(element.Properties.ControlType.ValueOrDefault)
                    .Append(" '").Append(element.Properties.AutomationId.ValueOrDefault).Append("' \"")
                    .Append(element.Properties.Name.ValueOrDefault).Append("\" ")
                    .Append(element.BoundingRectangle.ToString())
                    .AppendLine(element.Properties.IsOffscreen.ValueOrDefault ? " offscreen" : string.Empty);
                foreach (AutomationElement child in element.FindAllChildren())
                {
                    Append(child, depth + 1);
                }
            }
            catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or FlaUI.Core.Exceptions.PropertyNotSupportedException or TimeoutException)
            {
                text.Append(' ', depth * 2).AppendLine("(gone)");
            }
        }
    }
}

/// <summary>The widget cards, anchors and the mini preview strip of a camera window through UI Automation (T-093).</summary>
internal static class CameraWidgets
{
    public const string CardPrefix = "CameraOverlayWidget-";

    public const string Status = "status";
    public const string Times = "times";
    public const string Temperatures = "temperatures";
    public const string PrintName = "print-name";
    public const string Cooldown = "cooldown";

    public static readonly string[] Anchors = ["TopLeft", "TopCenter", "TopRight", "BottomLeft", "BottomCenter", "BottomRight"];

    /// <summary>The widgets of the default layout (T-076 3.3) that show with a running print, and their anchors.</summary>
    public static IReadOnlyDictionary<string, string> DefaultPrinting { get; } = new Dictionary<string, string>
    {
        [Status] = "TopLeft",
        [PrintName] = "TopCenter",
        [Times] = "BottomLeft",
        [Temperatures] = "BottomRight",
    };

    /// <summary>The card of <paramref name="widgetId"/> when it is shown.</summary>
    public static AutomationElement? Card(AutomationElement window, string widgetId) =>
        Ui.TryFind(window, CardPrefix + widgetId) is { } card && !card.Properties.IsOffscreen.ValueOrDefault ? card : null;

    public static bool IsShown(AutomationElement window, string widgetId) => Card(window, widgetId) is not null;

    /// <summary>The anchor whose items hold the shown card of <paramref name="widgetId"/>, or <see langword="null"/>.</summary>
    public static string? AnchorOf(AutomationElement window, string widgetId)
    {
        foreach (string anchor in Anchors)
        {
            if (Ui.TryFind(window, "CameraOverlayAnchor" + anchor) is { } items
                && items.FindFirstDescendant(cf => cf.ByAutomationId(CardPrefix + widgetId)) is { } card
                && !card.Properties.IsOffscreen.ValueOrDefault)
            {
                return anchor;
            }
        }

        return null;
    }

    /// <summary>The text (UIA name) of a part of a card, e.g. <c>cooldown-Title</c>.</summary>
    public static string Part(AutomationElement window, string widgetId, string part) => Ui.Text(window, $"{CardPrefix}{widgetId}-{part}");

    /// <summary>
    /// Where the card of <paramref name="widgetId"/> is wrong for <paramref name="anchor"/> in <paramref name="area"/> (the
    /// image area of the window or the monitor in full screen, physical pixels), or <see langword="null"/>: inside the area,
    /// on the anchor's side (left third / centred / right third) and in its half (top / bottom).
    /// </summary>
    public static string? PlacementProblem(Rectangle card, Rectangle area, string anchor)
    {
        if (!area.Contains(card))
        {
            return $"the card {card} is not inside {area}";
        }

        int centerX = card.Left + (card.Width / 2);
        int centerY = card.Top + (card.Height / 2);
        int areaCenterX = area.Left + (area.Width / 2);
        int areaCenterY = area.Top + (area.Height / 2);
        string? horizontal = anchor switch
        {
            _ when anchor.EndsWith("Left", StringComparison.Ordinal) => card.Left - area.Left <= area.Width / 3 && centerX < areaCenterX ? null : "not on the left",
            _ when anchor.EndsWith("Right", StringComparison.Ordinal) => area.Right - card.Right <= area.Width / 3 && centerX > areaCenterX ? null : "not on the right",
            _ => Math.Abs(centerX - areaCenterX) <= 3 ? null : $"not centred ({centerX - areaCenterX} px off)",
        };
        string? vertical = anchor.StartsWith("Top", StringComparison.Ordinal)
            ? (centerY < areaCenterY ? null : "not in the top half")
            : (centerY > areaCenterY ? null : "not in the bottom half");
        return horizontal is null && vertical is null ? null : $"the card {card} in {area} is {string.Join(", ", new[] { horizontal, vertical }.Where(static p => p is not null))}";
    }

    /// <summary>
    /// Every shown widget of <paramref name="expected"/> is in its anchor (UIA tree) and placed there (bounds); no top card
    /// overlaps the window buttons (T-076: the anchors at the top stay clear of them). Returns the problems.
    /// </summary>
    public static List<string> LayoutProblems(AutomationElement window, Rectangle area, IReadOnlyDictionary<string, string> expected)
    {
        List<string> problems = [];
        Rectangle? buttons = Ui.TryFind(window, "CameraWindowButtons") is { } panel ? panel.BoundingRectangle : null;
        foreach ((string widget, string anchor) in expected)
        {
            AutomationElement? card = Card(window, widget);
            if (card is null)
            {
                problems.Add($"{widget}: not shown");
                continue;
            }

            string? actual = AnchorOf(window, widget);
            if (actual != anchor)
            {
                problems.Add($"{widget}: in the anchor {actual ?? "(none)"}, expected {anchor}");
            }

            Rectangle bounds = card.BoundingRectangle;
            if (PlacementProblem(bounds, area, anchor) is { } problem)
            {
                problems.Add($"{widget} ({anchor}): {problem}");
            }

            if (buttons is { IsEmpty: false } b && anchor.StartsWith("Top", StringComparison.Ordinal) && b.IntersectsWith(bounds))
            {
                problems.Add($"{widget} ({anchor}): the card {bounds} overlaps the window buttons {b}");
            }
        }

        return problems;
    }

    /// <summary>The image area of a camera window (its client area without the mini strip), physical screen pixels.</summary>
    public static Rectangle ImageArea(nint hwnd, AutomationElement window)
    {
        Native.Rect client = Native.VisibleClientRect(hwnd);
        Rectangle area = new(client.Left, client.Top, client.Width, client.Height);
        if (Ui.TryFind(window, "CameraMiniInfoStrip") is { } strip && !strip.Properties.IsOffscreen.ValueOrDefault && strip.BoundingRectangle.Height > 0)
        {
            area.Height = strip.BoundingRectangle.Top - area.Top;
        }

        return area;
    }

    /// <summary>The shown elements whose automation id starts with <see cref="CardPrefix"/> (no card at all in the mini preview).</summary>
    public static List<string> ShownCards(AutomationElement window) =>
        [.. window.FindAllDescendants()
            .Select(static element => (Id: element.Properties.AutomationId.ValueOrDefault ?? string.Empty, Element: element))
            .Where(static pair => pair.Id.StartsWith(CardPrefix, StringComparison.Ordinal) && !pair.Element.Properties.IsOffscreen.ValueOrDefault)
            .Select(static pair => pair.Id)];

    /// <summary>The visible summary text of the mini strip (its text block) and its name for screen readers.</summary>
    public static (string Visible, string Accessible) MiniSummary(AutomationElement window)
    {
        AutomationElement? summary = Ui.TryFind(window, "CameraMiniSummary");
        if (summary is null)
        {
            return (string.Empty, string.Empty);
        }

        string visible = string.Join(" ", summary.FindAllDescendants(cf => cf.ByControlType(ControlType.Text))
            .Select(static text => text.Properties.Name.ValueOrDefault ?? string.Empty)
            .Where(static text => text.Length > 0));
        return (visible, summary.Properties.Name.ValueOrDefault ?? string.Empty);
    }

    /// <summary>A toggle of the mini strip is on.</summary>
    public static bool IsOn(AutomationElement window, string automationId) =>
        Ui.TryFind(window, automationId)?.Patterns.Toggle.Pattern.ToggleState.ValueOrDefault == ToggleState.On;

    /// <summary>A description of the cards for a report: id, anchor, bounds.</summary>
    public static string Describe(AutomationElement window) => string.Join(
        Environment.NewLine,
        ShownCards(window)
            .Select(id => string.Create(CultureInfo.InvariantCulture, $"  {id}: {Ui.TryFind(window, id)?.BoundingRectangle}")));
}
