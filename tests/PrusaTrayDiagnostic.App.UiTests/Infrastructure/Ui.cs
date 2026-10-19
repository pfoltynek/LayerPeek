using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Avalonia.VisualTree;

using PrusaTrayDiagnostic.App.Tests.Tray;

namespace PrusaTrayDiagnostic.App.UiTests.Infrastructure;

/// <summary>Finding controls and simulating user input in a headless window. UI thread only.</summary>
public static class Ui
{
    /// <summary>
    /// Runs everything queued on the UI thread (bindings, posted updates), lays the window out and renders a frame, so
    /// that the visual tree and hit testing reflect the current state. Deterministic: no waiting on real time.
    /// </summary>
    public static void Settle(this TopLevel topLevel)
    {
        ArgumentNullException.ThrowIfNull(topLevel);
        Dispatcher.UIThread.RunJobs();
        topLevel.UpdateLayout();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>The single control of type <typeparamref name="T"/> with the automation id <paramref name="automationId"/>.</summary>
    public static T Find<T>(this Visual root, string automationId)
        where T : Control
    {
        ArgumentNullException.ThrowIfNull(root);
        List<T> matches = [.. FindAll<T>(root, automationId)];
        return matches.Count switch
        {
            1 => matches[0],
            0 => throw new InvalidOperationException($"No {typeof(T).Name} with automation id '{automationId}' is in the visual tree."),
            _ => throw new InvalidOperationException($"{matches.Count} controls of type {typeof(T).Name} have the automation id '{automationId}'."),
        };
    }

    /// <summary>Whether a visible control of type <typeparamref name="T"/> with the automation id exists.</summary>
    public static bool IsShown<T>(this Visual root, string automationId)
        where T : Control =>
        FindAll<T>(root, automationId).Any(static control => control.IsEffectivelyVisible);

    /// <summary>Clicks the centre of <paramref name="control"/> with the left mouse button, like the user would.</summary>
    public static void Click(this Window window, Control control)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(control);
        window.Settle();
        if (!control.IsEffectivelyVisible)
        {
            throw new InvalidOperationException($"{Describe(control)} is not visible and cannot be clicked.");
        }

        // Scrolled into the viewport like the user would (the printer form is in a scroll viewer).
        control.BringIntoView();
        window.Settle();

        Point centre = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)
            ?? throw new InvalidOperationException($"{Describe(control)} is not in the window.");
        window.MouseMove(centre);
        window.MouseDown(centre, MouseButton.Left);
        window.MouseUp(centre, MouseButton.Left);
        window.Settle();
    }

    /// <summary>Focuses <paramref name="box"/>, selects its text with Ctrl+A and types <paramref name="text"/> over it.</summary>
    public static void TypeOver(this Window window, TextBox box, string text)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(box);
        Focus(window, box);
        window.KeyPressQwerty(PhysicalKey.A, RawInputModifiers.Control);
        window.KeyReleaseQwerty(PhysicalKey.A, RawInputModifiers.Control);
        window.KeyTextInput(text);
        window.Settle();
    }

    /// <summary>Focuses <paramref name="box"/> and deletes its whole text (Ctrl+A, Backspace).</summary>
    public static void ClearText(this Window window, TextBox box)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(box);
        Focus(window, box);
        window.KeyPressQwerty(PhysicalKey.A, RawInputModifiers.Control);
        window.KeyReleaseQwerty(PhysicalKey.A, RawInputModifiers.Control);
        window.KeyPressQwerty(PhysicalKey.Backspace, RawInputModifiers.None);
        window.KeyReleaseQwerty(PhysicalKey.Backspace, RawInputModifiers.None);
        window.Settle();
    }

    /// <summary>
    /// Runs dispatcher jobs and polls <paramref name="condition"/> until it holds (camera tests, T-014). Only for work that
    /// runs on the thread pool (the fake stream, the camera test); UI work is settled with <see cref="Settle"/> instead.
    /// </summary>
    public static async Task UntilAsync(Func<bool> condition, string description)
    {
        DateTime deadline = DateTime.UtcNow + TestTimeouts.Wait;
        while (true)
        {
            Dispatcher.UIThread.RunJobs();
            if (condition())
            {
                return;
            }

            if (DateTime.UtcNow > deadline)
            {
                Assert.Fail($"Timed out waiting for: {description}");
            }

            await Task.Delay(10);
        }
    }

    /// <summary>Texts of the visible text blocks under <paramref name="root"/>.</summary>
    public static List<string> VisibleTexts(Visual root) =>
        [.. root.GetVisualDescendants().OfType<TextBlock>().Where(static t => t.IsEffectivelyVisible && t.Text is not null).Select(static t => t.Text!)];

    public static bool ShowsText(Visual root, string text) => VisibleTexts(root).Contains(text);

    /// <summary>The first control of type <typeparamref name="T"/> under <paramref name="root"/> that matches.</summary>
    public static T Find<T>(Visual root, Func<T, bool> predicate)
        where T : Visual =>
        root.GetVisualDescendants().OfType<T>().FirstOrDefault(predicate)
            ?? throw new InvalidOperationException($"No {typeof(T).Name} matches.");

    /// <summary>The pixel at (<paramref name="x"/>, <paramref name="y"/>) of a captured frame (BGRA or RGBA, read per format).</summary>
    public static unsafe (byte R, byte G, byte B) Pixel(WriteableBitmap frame, int x, int y)
    {
        ArgumentNullException.ThrowIfNull(frame);
        using ILockedFramebuffer buffer = frame.Lock();
        byte* row = (byte*)buffer.Address + ((long)y * buffer.RowBytes) + (x * 4);
        return buffer.Format == PixelFormat.Rgba8888 ? (row[0], row[1], row[2]) : (row[2], row[1], row[0]);
    }

    /// <summary>A left click (down + up) at <paramref name="point"/> in client coordinates, without moving the mouse first.</summary>
    public static void Click(TopLevel topLevel, Point point)
    {
        ArgumentNullException.ThrowIfNull(topLevel);
        topLevel.MouseDown(point, MouseButton.Left);
        topLevel.MouseUp(point, MouseButton.Left);
    }

    /// <summary>The centre of <paramref name="control"/> in the coordinates of <paramref name="window"/>.</summary>
    public static Point CentreOf(Control control, Visual window)
    {
        ArgumentNullException.ThrowIfNull(control);
        return control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)
            ?? throw new InvalidOperationException("The control is not in the window.");
    }

    private static void Focus(Window window, TextBox box)
    {
        window.Settle();
        if (!box.Focus())
        {
            throw new InvalidOperationException($"{Describe(box)} cannot get the keyboard focus (disabled or hidden).");
        }
    }

    private static IEnumerable<T> FindAll<T>(Visual root, string automationId)
        where T : Control =>
        root.GetSelfAndVisualDescendants().OfType<T>().Where(control => AutomationProperties.GetAutomationId(control) == automationId);

    private static string Describe(Control control) =>
        $"{control.GetType().Name} '{AutomationProperties.GetAutomationId(control) ?? control.Name}'";
}
