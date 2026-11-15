using System.Globalization;
using System.Text;

using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;

using LayerPeek.App.Tests.Settings;
using LayerPeek.App.UiTests.Infrastructure;
using LayerPeek.App.UiTests.Settings;

using static LayerPeek.App.Tests.Settings.SettingsTestData;

namespace LayerPeek.App.UiTests.SystemFont;

/// <summary>
/// T-214 calibration: the layout tests of this assembly only mean something if they lay San Francisco out as wide as
/// the app does on macOS. The reference is the system font that Avalonia's own font manager picks on macOS
/// (<c>.AppleSystemUIFont</c> through CoreText, what the app's default font resolves to; user decision 2026-11-14):
/// sample texts in the weights the UI uses (regular, semibold, bold) at the common UI sizes must be exactly as wide.
/// </summary>
/// <remarks>
/// AppKit lays the same texts out wider (macOS 27.2.1, 2026-11-14: 7 to 13 % regular, 14 to 21 % semibold and bold):
/// Avalonia 12.1.4 uses neither the weight nor the optical size axis of the variable font (always the default instance,
/// wght 400 / opsz 28; semibold and bold only emboldened). <see cref="AppKitWidthsForInformation"/> reports the
/// difference without failing (T-222 evaluates it).
/// </remarks>
public sealed class SystemFontCalibrationTests(HeadlessSession session)
{
    /// <summary>The family name of the macOS system font in CoreText (and in Avalonia's platform font manager).</summary>
    private const string PlatformSystemFamily = ".AppleSystemUIFont";

    /// <summary>Texts of the UI in several languages (Latin with diacritics, as in every language of the app).</summary>
    private static readonly string[] Samples =
    [
        "Settings",
        "Printers",
        "Test connection",
        "Startup and updates",
        "Nastavení tiskárny",
        "Paramètres de l’appareil photo",
        "Kamerafenster zurücksetzen",
        "Próximo intento en unos 40 s",
        "Ustawienia nagrywania",
        "Impostazioni della fotocamera",
    ];

    private static readonly double[] Sizes = [12, 13, 14, 16];

    private static readonly (string Name, FontWeight Avalonia, double AppKit)[] Weights =
    [
        ("regular", FontWeight.Normal, 0),
        ("semibold", FontWeight.SemiBold, 0.3),
        ("bold", FontWeight.Bold, 0.4),
    ];

    /// <summary>The main tabs of the settings window, whose widths the check in the running app compares (T-127).</summary>
    private static readonly string[] MainTabs =
        ["SettingsTabPrinters", "SettingsTabWidgets", "SettingsTabGeneral", "SettingsTabStartupUpdates", "SettingsTabRecording", "SettingsTabCamera"];

    [Fact]
    public Task DefaultFontOfTheTestsIsSanFrancisco() =>
        session.RunAsync(static _ =>
        {
            Assert.True(FontManager.Current.TryGetGlyphTypeface(new Typeface(HeadlessTestApp.FontFamilyName), out GlyphTypeface? typeface));
            Assert.Equal("System Font", typeface.FamilyName);
            Assert.Equal(SystemFontCollection.FamilyName, FontManager.Current.DefaultFontFamily.Name);
            return Task.CompletedTask;
        });

    [Fact]
    public Task WidthsMatchTheSystemFontOfAvaloniaOnMacOS() =>
        session.RunAsync(static _ =>
        {
            Assert.True(FontManager.Current.TryGetGlyphTypeface(new Typeface(PlatformSystemFamily), out GlyphTypeface? platform));
            Assert.Equal("System Font", platform.FamilyName);
            List<string> problems = [];
            foreach ((string name, FontWeight weight, double _) in Weights)
            {
                foreach (double size in Sizes)
                {
                    foreach (string sample in Samples)
                    {
                        double file = AvaloniaWidth(sample, size, weight);
                        double system = AvaloniaWidth(sample, size, weight, PlatformSystemFamily);
                        if (Math.Abs(file - system) > 0.01)
                        {
                            problems.Add(string.Create(CultureInfo.InvariantCulture, $"{name} {size} pt '{sample}': SFNS.ttf {file:0.00}, platform {system:0.00}"));
                        }
                    }
                }
            }

            Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
            return Task.CompletedTask;
        });

    /// <summary>
    /// Information only (never fails): how much narrower than AppKit Avalonia lays San Francisco out, per weight. Shown
    /// with <c>--output detailed</c> (standard output of the test).
    /// </summary>
    [Fact]
    public Task AppKitWidthsForInformation()
    {
        ITestOutputHelper output = TestContext.Current.TestOutputHelper!;
        return session.RunAsync(_ =>
        {
            StringBuilder report = new("Avalonia width relative to AppKit (NSFont.systemFont):");
            report.AppendLine();
            foreach ((string name, FontWeight avaloniaWeight, double appKitWeight) in Weights)
            {
                double smallest = double.MaxValue;
                double largest = double.MinValue;
                foreach (double size in Sizes)
                {
                    foreach (string sample in Samples)
                    {
                        double difference = (AvaloniaWidth(sample, size, avaloniaWeight) / CoreTextWidth.Measure(sample, size, appKitWeight)) - 1;
                        smallest = Math.Min(smallest, difference);
                        largest = Math.Max(largest, difference);
                    }
                }

                report.AppendLine(CultureInfo.InvariantCulture, $"  {name}: {smallest:+0.0%;-0.0%} to {largest:+0.0%;-0.0%}");
            }

            output.WriteLine(report.ToString());
            return Task.CompletedTask;
        });
    }

    /// <summary>
    /// Information only: the widths of the main tab headers of the settings window in English, which the check in the
    /// running app (T-127, item T-214) compares with the Accessibility frames of the same tabs.
    /// </summary>
    [Fact]
    public Task MainTabWidthsForTheCheckInTheRunningApp()
    {
        ITestOutputHelper output = TestContext.Current.TestOutputHelper!;
        return session.RunAsync(async scope =>
        {
            SettingsWindowHarness ui = await SettingsWindowHarness.OpenAsync(scope, SettingsWindowHarness.StoreWith(LinkPrinter("a", "Alpha")));
            StringBuilder report = new("Main tab headers of the settings window (English, DIP = points):");
            report.AppendLine();
            foreach (string id in MainTabs)
            {
                TabItem tab = ui.Window.Find<TabItem>(id);
                Assert.True(tab.Bounds.Width > 0, id);
                report.AppendLine(CultureInfo.InvariantCulture, $"  {id}: {tab.Bounds.Width:0.00} × {tab.Bounds.Height:0.00}");
            }

            output.WriteLine(report.ToString());
        });
    }

    private static double AvaloniaWidth(string text, double size, FontWeight weight, string family = HeadlessTestApp.FontFamilyName)
    {
        using TextLayout layout = new(text, new Typeface(family, FontStyle.Normal, weight), size, Brushes.Black);
        return layout.WidthIncludingTrailingWhitespace;
    }
}
