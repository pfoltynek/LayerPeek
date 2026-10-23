using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

using Microsoft.Win32;

namespace PrusaTrayDiagnostic.App.E2E.Tests.Infrastructure;

/// <summary>
/// Light (1) or dark (0) mode of the taskbar, Start and notification area (<c>SystemUsesLightTheme</c>) and of apps
/// (<c>AppsUseLightTheme</c>); <see langword="null"/> = the value does not exist.
/// </summary>
internal sealed record ThemeState(int? SystemUsesLightTheme, int? AppsUseLightTheme)
{
    public static ThemeState Light { get; } = new(1, 1);

    public static ThemeState Dark { get; } = new(0, 0);

    /// <summary>Whether the taskbar is light (a missing value means light, the Windows default).</summary>
    public bool TaskbarIsLight => SystemUsesLightTheme != 0;

    public override string ToString() => $"SystemUsesLightTheme={Show(SystemUsesLightTheme)}, AppsUseLightTheme={Show(AppsUseLightTheme)}";

    private static string Show(int? value) => value?.ToString(CultureInfo.InvariantCulture) ?? "(missing)";
}

/// <summary>
/// The light/dark theme of Windows (T-065): <c>HKCU\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize</c>
/// plus a <c>WM_SETTINGCHANGE "ImmersiveColorSet"</c> broadcast, so Explorer and apps switch without signing out. The
/// values are not a documented API, but they are what the Settings app writes. <c>ColorPrevalence</c> (accent colour on
/// the taskbar) is never changed, only reported, because it changes the taskbar colour.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed partial class ThemeKind() : SystemChangeKind<ThemeState>("Theme", SystemChangeTraits.Theme, SystemChangeJson.Default.ThemeState)
{
    public const string SubKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    public const string SystemValue = "SystemUsesLightTheme";
    public const string AppsValue = "AppsUseLightTheme";
    public const string ColorPrevalenceValue = "ColorPrevalence";

    /// <summary>Median brightness (0..255) of a light taskbar is above this.</summary>
    public const double LightTaskbarMinimum = 150;

    /// <summary>Median brightness of a dark taskbar (without the accent colour) is below this.</summary>
    public const double DarkTaskbarMaximum = 110;

    public override Task<ThemeState> ReadStateAsync(string? key, CancellationToken cancellationToken)
    {
        using RegistryKey? personalize = Registry.CurrentUser.OpenSubKey(SubKey);
        return Task.FromResult(new ThemeState(ReadExactDword(personalize, SystemValue), ReadExactDword(personalize, AppsValue)));
    }

    /// <summary>
    /// A DWORD value or <see langword="null"/> when it is missing; a value of another type is refused (it could not be
    /// written back as it was, and "missing" would delete it).
    /// </summary>
    private static int? ReadExactDword(RegistryKey? key, string name)
    {
        if (key is null || key.GetValue(name) is not { } value)
        {
            return null;
        }

        return key.GetValueKind(name) == RegistryValueKind.DWord && value is int number
            ? number
            : throw new InvalidOperationException($"HKCU\\{SubKey}\\{name} is a {key.GetValueKind(name)}, not a DWORD; the theme is not changed.");
    }

    /// <summary><c>ColorPrevalence</c> (1 = accent colour on the taskbar in dark mode), or <see langword="null"/>.</summary>
    public static int? ColorPrevalence()
    {
        using RegistryKey? personalize = Registry.CurrentUser.OpenSubKey(SubKey);
        return ReadDword(personalize, ColorPrevalenceValue);
    }

    public override string Describe(string? key, ThemeState original) =>
        $"Theme: {original}, ColorPrevalence={ColorPrevalence()?.ToString(CultureInfo.InvariantCulture) ?? "(missing)"} (not changed). "
        + "By hand: Settings > Personalization > Colors > Choose your mode (Custom: Windows mode / app mode), "
        + $"or the DWORD values in HKCU\\{SubKey} (a missing value means light).";

    /// <summary>
    /// Waits up to <paramref name="timeout"/> until the taskbar of the primary monitor has the brightness of
    /// <paramref name="light"/>; fails with the measured value otherwise (Explorer did not switch). Returns the median
    /// brightness. A dark taskbar with the accent colour (<c>ColorPrevalence</c> = 1) only has to be darker than
    /// <paramref name="lightBrightness"/> by a margin.
    /// </summary>
    public static async Task<double> WaitForTaskbarAsync(bool light, TimeSpan timeout, double? lightBrightness = null)
    {
        double brightness = 0;
        bool accent = ColorPrevalence() == 1;
        try
        {
            await Wait.UntilAsync(
                () =>
                {
                    brightness = TaskbarBrightness();
                    return IsExpected(brightness, light, accent, lightBrightness);
                },
                timeout,
                $"the taskbar to turn {(light ? "light" : "dark")}");
        }
        catch (TimeoutException ex)
        {
            throw new InvalidOperationException(
                $"Explorer did not switch the taskbar to {(light ? "light" : "dark")} within {timeout.TotalSeconds:0} s: median brightness "
                + $"{brightness:0} (light >= {LightTaskbarMinimum}, dark <= {DarkTaskbarMaximum}; ColorPrevalence={accent}). Is the taskbar hidden automatically?",
                ex);
        }

        return brightness;
    }

    /// <summary>Whether a measured taskbar <paramref name="brightness"/> fits the expected mode.</summary>
    internal static bool IsExpected(double brightness, bool light, bool accentOnTaskbar, double? lightBrightness)
    {
        if (light)
        {
            return brightness >= LightTaskbarMinimum;
        }

        return brightness <= DarkTaskbarMaximum || (accentOnTaskbar && lightBrightness is { } reference && brightness <= reference - 30);
    }

    /// <summary>Median brightness of the taskbar of the primary monitor as on screen (icons are a small minority of it).</summary>
    public static double TaskbarBrightness()
    {
        using Bitmap bitmap = CaptureTaskbar();
        return MedianBrightness(bitmap);
    }

    /// <summary>The taskbar of the primary monitor as on screen (only the taskbar, no windows of the user).</summary>
    public static Bitmap CaptureTaskbar()
    {
        nint taskbar = Native.FindWindow("Shell_TrayWnd", null);
        if (taskbar == 0 || !Native.GetWindowRect(taskbar, out Native.Rect rect) || rect.Width <= 0 || rect.Height <= 0)
        {
            throw new InvalidOperationException("No taskbar (Shell_TrayWnd).");
        }

        return ScreenArea.CaptureBitmap(Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom));
    }

    /// <summary>Median of the brightness (0..255) of every second pixel.</summary>
    internal static double MedianBrightness(Bitmap bitmap)
    {
        List<double> values = [];
        for (int y = 0; y < bitmap.Height; y += 2)
        {
            for (int x = 0; x < bitmap.Width; x += 2)
            {
                Color c = bitmap.GetPixel(x, y);
                values.Add((0.2126 * c.R) + (0.7152 * c.G) + (0.0722 * c.B));
            }
        }

        if (values.Count == 0)
        {
            return 0;
        }

        values.Sort();
        return values[values.Count / 2];
    }

    protected override Task ApplyCoreAsync(string? key, ThemeState state, CancellationToken cancellationToken)
    {
        using (RegistryKey personalize = Registry.CurrentUser.CreateSubKey(SubKey, writable: true))
        {
            WriteDword(personalize, SystemValue, state.SystemUsesLightTheme);
            WriteDword(personalize, AppsValue, state.AppsUseLightTheme);
        }

        // What the Settings app sends: Explorer and apps re-read the mode.
        SendMessageTimeout(HwndBroadcast, WmSettingChange, 0, "ImmersiveColorSet", SmtoAbortIfHung, 5000, out _);
        return Task.CompletedTask;
    }

    private static int? ReadDword(RegistryKey? key, string name) => key?.GetValue(name) is int value ? value : null;

    private static void WriteDword(RegistryKey key, string name, int? value)
    {
        if (value is { } number)
        {
            key.SetValue(name, number, RegistryValueKind.DWord);
        }
        else
        {
            key.DeleteValue(name, throwOnMissingValue: false);
        }
    }

    private const nint HwndBroadcast = 0xFFFF;
    private const uint WmSettingChange = 0x001A;
    private const uint SmtoAbortIfHung = 0x0002;

    [LibraryImport("user32.dll", EntryPoint = "SendMessageTimeoutW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint SendMessageTimeout(nint hwnd, uint message, nint wParam, string lParam, uint flags, uint timeoutMs, out nint result);
}
