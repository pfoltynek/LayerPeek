using System.Runtime.InteropServices;

namespace PrusaTrayDiagnostic.App.E2E.Tests.Infrastructure;

/// <summary>
/// The open File Explorer windows (tabs) and their folders, through the shell's own list (<c>Shell.Application</c>), so
/// that a test finds exactly the window the app opened and closes it again (T-064). On Windows 11 a folder can open as a
/// tab in a window of the user: such a window is never closed (decided 2026-10-22), only a window holding nothing else.
/// </summary>
internal static class ExplorerWindows
{
    /// <summary>Whether an Explorer window (tab) shows <paramref name="folder"/>.</summary>
    public static bool Shows(string folder) => Visit(folder, close: false).Found > 0;

    /// <summary>
    /// The windows showing only <paramref name="folder"/> (no other tab): a test may capture them. A window that also holds
    /// tabs of the user is never captured (T-069: no screenshot of the user's windows).
    /// </summary>
    public static IReadOnlyList<nint> WindowsShowingOnly(string folder) => Visit(folder, close: false).Sole;

    /// <summary>
    /// Closes the windows of <paramref name="folder"/> until none has appeared for <paramref name="quiet"/>: Explorer may
    /// open the window a moment after a test gave up waiting for it.
    /// </summary>
    /// <returns>The number of tabs of the folder left open because they share a window with the user's tabs.</returns>
    public static async Task<int> CloseUntilGoneAsync(string folder, TimeSpan quiet)
    {
        DateTime lastSeen = DateTime.UtcNow;
        int shared = 0;
        while (DateTime.UtcNow - lastSeen < quiet)
        {
            (int found, int closed, _) = Visit(folder, close: true);
            shared = found - closed;
            if (closed > 0)
            {
                lastSeen = DateTime.UtcNow;
            }

            await Task.Delay(200);
        }

        return shared;
    }

    /// <summary>
    /// The windows (tabs) showing <paramref name="folder"/>; with <paramref name="close"/> those that are the only tab of their
    /// window are closed. Every COM object is released.
    /// </summary>
    private static (int Found, int Closed, List<nint> Sole) Visit(string folder, bool close)
    {
        string wanted = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));
        Type shellType = Type.GetTypeFromProgID("Shell.Application") ?? throw new InvalidOperationException("The Windows shell is not available.");
        object shell = Activator.CreateInstance(shellType)!;
        object? windows = null;
        List<object> items = [];
        try
        {
            windows = ((dynamic)shell).Windows();
            foreach (object window in (System.Collections.IEnumerable)windows!)
            {
                items.Add(window);
            }

            // Tabs of one window share its handle.
            Dictionary<long, int> tabsPerWindow = [];
            List<(object Window, long Handle)> matching = [];
            foreach (object item in items)
            {
                if (TryHandle(item) is not { } handle)
                {
                    // A window that is just closing; Explorer windows otherwise always have their handle.
                    continue;
                }

                tabsPerWindow[handle] = tabsPerWindow.GetValueOrDefault(handle) + 1;
                if (Shows((dynamic)item, wanted))
                {
                    matching.Add((item, handle));
                }
            }

            int closed = 0;
            List<nint> sole = [.. matching.Where(m => tabsPerWindow[m.Handle] == 1).Select(static m => (nint)m.Handle)];
            foreach ((object window, long handle) in matching)
            {
                if (close && tabsPerWindow[handle] == 1)
                {
                    try
                    {
                        ((dynamic)window).Quit();
                        closed++;
                    }
                    catch (COMException)
                    {
                        // Closed by the user meanwhile.
                    }
                }
            }

            return (matching.Count, closed, sole);
        }
        finally
        {
            foreach (object item in items)
            {
                Marshal.FinalReleaseComObject(item);
            }

            if (windows is not null)
            {
                Marshal.FinalReleaseComObject(windows);
            }

            Marshal.FinalReleaseComObject(shell);
        }
    }

    private static long? TryHandle(object window)
    {
        try
        {
            return Convert.ToInt64(((dynamic)window).HWND, System.Globalization.CultureInfo.InvariantCulture);
        }
        catch (COMException)
        {
            return null;
        }
    }

    private static bool Shows(dynamic window, string folder)
    {
        string? location;
        try
        {
            location = window.LocationURL;
        }
        catch (COMException)
        {
            return false;
        }

        return Uri.TryCreate(location, UriKind.Absolute, out Uri? uri) && uri.IsFile
            && string.Equals(Path.TrimEndingDirectorySeparator(uri.LocalPath), folder, StringComparison.OrdinalIgnoreCase);
    }
}
