using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Avalonia.VisualTree;

using PrusaTrayDiagnostic.App.Tests.Tray;

namespace PrusaTrayDiagnostic.App.UiTests;

/// <summary>Helpers for headless UI tests (UI thread only).</summary>
internal static class Ui
{
    /// <summary>Runs dispatcher jobs and polls <paramref name="condition"/> (work runs on the thread pool) until it holds.</summary>
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

    public static T Find<T>(Visual root, Func<T, bool> predicate)
        where T : Visual =>
        root.GetVisualDescendants().OfType<T>().FirstOrDefault(predicate)
            ?? throw new InvalidOperationException($"No {typeof(T).Name} matches.");

    /// <summary>The pixel at (<paramref name="x"/>, <paramref name="y"/>) of a captured frame (BGRA or RGBA, read per format).</summary>
    public static unsafe (byte R, byte G, byte B) Pixel(WriteableBitmap frame, int x, int y)
    {
        using ILockedFramebuffer buffer = frame.Lock();
        byte* row = (byte*)buffer.Address + ((long)y * buffer.RowBytes) + (x * 4);
        return buffer.Format == PixelFormat.Rgba8888 ? (row[0], row[1], row[2]) : (row[2], row[1], row[0]);
    }

    /// <summary>A left click (down + up) at <paramref name="point"/> in client coordinates.</summary>
    public static void Click(TopLevel topLevel, Point point)
    {
        Avalonia.Headless.HeadlessWindowExtensions.MouseDown(topLevel, point, Avalonia.Input.MouseButton.Left);
        Avalonia.Headless.HeadlessWindowExtensions.MouseUp(topLevel, point, Avalonia.Input.MouseButton.Left);
    }

    /// <summary>The centre of <paramref name="control"/> in the coordinates of its window.</summary>
    public static Point CentreOf(Control control, Visual window) =>
        control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)
            ?? throw new InvalidOperationException("The control is not in the window.");
}
