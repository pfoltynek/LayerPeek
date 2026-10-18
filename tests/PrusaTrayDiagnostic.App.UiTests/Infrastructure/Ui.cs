using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;

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
