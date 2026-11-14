using System.Globalization;
using System.Text;

using Avalonia.Media;
using Avalonia.Media.TextFormatting;

using LayerPeek.App.UiTests.Infrastructure;

namespace LayerPeek.App.UiTests.SystemFont;

/// <summary>
/// T-214 calibration: the layout tests of this assembly only mean something if they lay San Francisco out as wide as
/// the app does on macOS. SFNS.ttf is a variable font; the widths of sample texts in the weights the UI uses (regular,
/// semibold, bold) at the common UI sizes are compared with AppKit's (<see cref="CoreTextWidth"/>, the reference of the
/// task) and with the system font that Avalonia's own Skia font manager picks on macOS (<c>.AppleSystemUIFont</c>
/// through CoreText, what the app's default font resolves to).
/// </summary>
/// <remarks>
/// Measured on macOS 27.2.1 (2026-11-14): Avalonia 12.1.4 uses neither the weight nor the optical size axis of the
/// variable font (always the default instance: wght 400, opsz 28; semibold and bold only emboldened, same advances), so
/// it lays every text out 7 to 13 % (regular) and 14 to 21 % (semibold, bold) narrower than AppKit, while it matches the
/// platform system font of Avalonia exactly. Whether the AppKit tolerance or the Avalonia reference applies is open in
/// T-214 (Otevřené otázky); until then <see cref="WidthsMatchAppKitWithinTolerance"/> stays red.
/// </remarks>
public sealed class SystemFontCalibrationTests(HeadlessSession session)
{
    /// <summary>The largest relative difference of a width from AppKit's that still counts as the same layout.</summary>
    private const double Tolerance = 0.02;

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
    public Task WidthsMatchAppKitWithinTolerance() =>
        session.RunAsync(static _ =>
        {
            StringBuilder report = new();
            List<string> problems = [];
            foreach ((string name, FontWeight avaloniaWeight, double appKitWeight) in Weights)
            {
                double worst = 0;
                foreach (double size in Sizes)
                {
                    foreach (string sample in Samples)
                    {
                        double avalonia = AvaloniaWidth(sample, size, avaloniaWeight);
                        double appKit = CoreTextWidth.Measure(sample, size, appKitWeight);
                        double difference = (avalonia - appKit) / appKit;
                        worst = Math.Abs(difference) > Math.Abs(worst) ? difference : worst;
                        if (Math.Abs(difference) > Tolerance)
                        {
                            problems.Add(string.Create(CultureInfo.InvariantCulture, $"{name} {size} pt '{sample}': Avalonia {avalonia:0.00}, AppKit {appKit:0.00} ({difference:+0.0%;-0.0%})"));
                        }
                    }
                }

                report.AppendLine(CultureInfo.InvariantCulture, $"{name}: largest difference {worst:+0.00%;-0.00%}");
            }

            TestContext.Current.SendDiagnosticMessage(report.ToString());
            Assert.True(problems.Count == 0, report + string.Join(Environment.NewLine, problems));
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

    /// <summary>The family name of the macOS system font in CoreText (and in Avalonia's platform font manager).</summary>
    private const string PlatformSystemFamily = ".AppleSystemUIFont";

    private static double AvaloniaWidth(string text, double size, FontWeight weight, string family = HeadlessTestApp.FontFamilyName)
    {
        using TextLayout layout = new(text, new Typeface(family, FontStyle.Normal, weight), size, Brushes.Black);
        return layout.WidthIncludingTrailingWhitespace;
    }
}
