// TEMPORARY T-122 diagnostics - remove before finishing.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace LayerPeek.App.Windows;

public sealed partial class CameraWindow
{
    private static readonly string TracePath = Path.Combine(Path.GetTempPath(), "layerpeek-t122-trace.log");

    internal static void Trace(string text)
    {
        try
        {
            File.AppendAllText(TracePath, $"{DateTime.Now:HH:mm:ss.fff} {text}{Environment.NewLine}");
        }
        catch (IOException)
        {
        }
    }

    private static string Id(object? o) => o is Control c ? $"{c.GetType().Name}/{c.Name}/{Avalonia.Automation.AutomationProperties.GetAutomationId(c)}" : "null";

    private string FocusText() => $"active={IsActive} focus={Id(FocusManager?.GetFocusedElement())}";

    private void AttachTrace()
    {
        AddHandler(GotFocusEvent, (_, e) => Trace($"GotFocus {Id(e.Source)} {e.NavigationMethod}"), RoutingStrategies.Bubble, true);
        AddHandler(LostFocusEvent, (_, e) => Trace($"LostFocus {Id(e.Source)}\n{Environment.StackTrace}"), RoutingStrategies.Bubble, true);
        AddHandler(KeyDownEvent, (_, e) => Trace($"KeyDown {e.Key} handled={e.Handled} {FocusText()}"), RoutingStrategies.Tunnel, true);
        AddHandler(KeyUpEvent, (_, e) => Trace($"KeyUp {e.Key} handled={e.Handled} {FocusText()}"), RoutingStrategies.Tunnel, true);
        if (OperatingSystem.IsWindows())
        {
            Win32Properties.AddWndProcHookCallback(this, (nint hwnd, uint msg, nint w, nint l, ref bool handled) =>
            {
                string? name = msg switch
                {
                    0x6 => "WM_ACTIVATE",
                    0x7 => "WM_SETFOCUS",
                    0x8 => "WM_KILLFOCUS",
                    0x1C => "WM_ACTIVATEAPP",
                    0x86 => "WM_NCACTIVATE",
                    0x18 => "WM_SHOWWINDOW",
                    0x100 => "WM_KEYDOWN",
                    0x101 => "WM_KEYUP",
                    0x104 => "WM_SYSKEYDOWN",
                    0x105 => "WM_SYSKEYUP",
                    0x102 => "WM_CHAR",
                    _ => null,
                };
                if (name is not null)
                {
                    Trace($"{name} w=0x{w:X} l=0x{l:X} {FocusText()}");
                }

                return 0;
            });
        }
    }
}
