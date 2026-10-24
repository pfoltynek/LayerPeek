using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;

namespace LayerPeek.App.E2E.Tests.Infrastructure;

/// <summary>
/// Layout problems of the settings and diagnostics windows found so far (T-033, T-041, T-044, T-071): something that
/// scrolls, or a text outside the client area. Recorded per state and asserted at the end of a test, so that one
/// overflowing state does not hide the states after it (every state is still captured into the results).
/// </summary>
internal sealed class LayoutRecorder
{
    private static readonly TimeSpan Settle = TimeSpan.FromMilliseconds(400);

    private readonly List<string> _problems = [];

    /// <summary>Selects a tab, lets the layout settle, records what scrolls and captures the window.</summary>
    public async Task CheckTabAsync(AutomationElement window, nint hwnd, string tab, string screenshot)
    {
        await Ui.SelectAsync(window, tab);
        await CheckAsync(window, hwnd, screenshot);
    }

    /// <summary>Lets the layout settle, records what scrolls and captures the window.</summary>
    public async Task CheckAsync(AutomationElement window, nint hwnd, string screenshot)
    {
        await Task.Delay(Settle, TestContext.Current.CancellationToken);
        Screenshots.Save(hwnd, screenshot);
        Record(screenshot, WindowChecks.ScrollingProblems(window));
    }

    /// <summary>Records texts on screen that lie (partly) outside the client area of the window.</summary>
    public void CheckTextsInside(AutomationElement window, nint hwnd, string state) =>
        Record(state, TextsOutside(window, Native.VisibleClientRect(hwnd)));

    public void Record(string state, IReadOnlyCollection<string> problems)
    {
        if (problems.Count > 0)
        {
            _problems.Add($"{state}: {string.Join("; ", problems)}");
        }
    }

    public void AssertNothingFound() => Assert.True(_problems.Count == 0, string.Join(Environment.NewLine, _problems));

    /// <summary>Text elements on screen whose bounds are not inside <paramref name="client"/> (1 px tolerance).</summary>
    public static List<string> TextsOutside(AutomationElement window, Native.Rect client)
    {
        List<string> problems = [];
        foreach (AutomationElement text in window.FindAllDescendants(cf => cf.ByControlType(ControlType.Text)))
        {
            try
            {
                if (text.Properties.IsOffscreen.ValueOrDefault)
                {
                    continue;
                }

                System.Drawing.Rectangle bounds = text.Properties.BoundingRectangle.ValueOrDefault;
                if (bounds.Width > 0 && (bounds.Left < client.Left - 1 || bounds.Top < client.Top - 1 || bounds.Right > client.Right + 1 || bounds.Bottom > client.Bottom + 1))
                {
                    problems.Add($"{WindowChecks.Describe(text)} '{text.Properties.Name.ValueOrDefault}' at {bounds} is outside the client area {client}");
                }
            }
            catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or FlaUI.Core.Exceptions.PropertyNotSupportedException)
            {
                // The element went away meanwhile (a value being refreshed); the next check sees the new state.
            }
        }

        return problems;
    }
}
