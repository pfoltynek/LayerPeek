using System.Runtime.InteropServices;

using PrusaTrayDiagnostic.App.Support;
using PrusaTrayDiagnostic.App.Tests.Tray;
using PrusaTrayDiagnostic.Core.Cameras;
using PrusaTrayDiagnostic.Core.Printers;
using PrusaTrayDiagnostic.Core.Settings;

namespace PrusaTrayDiagnostic.App.Tests.Support;

/// <summary>The text for a problem report (T-048 info.txt, T-049 clipboard): English and invariant, without secrets.</summary>
public sealed class SupportInfoTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 22, 14, 3, 12, TimeSpan.FromHours(2));

    private static readonly SupportEnvironment Environment = new(
        "1.2.3", "Microsoft Windows 10.0.26300", Architecture.X64, Architecture.X64, ".NET 11.0.0", NativeAot: true, "cs-CZ", "cs", "7.1");

    [Theory]
    [InlineData("en-US")]
    [InlineData("cs-CZ")]
    [InlineData("de-DE")]
    public void TextIsEnglishAndInvariantWhateverTheLanguage(string culture)
    {
        using CultureScope scope = new(culture);

        string text = SupportInfo.Build(Environment, Settings(), [], Now);

        Assert.Equal(
            """
            Prusa Tray Diagnostic 1.2.3
            Created: 2026-10-22 14:03:12 +02:00
            OS: Microsoft Windows 10.0.26300 (X64)
            Process: X64, .NET 11.0.0, Native AOT: yes
            UI language: cs-CZ (setting: cs)
            FFmpeg: 7.1
            Polling interval: 2.5 s, detailed logging: on
            Printers: 2

            - Workshop MK4 [mk4]
              Connection: PrusaLink, 192.168.0.226, user workshop
              Model: not detected
              Camera: on, rtsp://192.168.0.50/live
              State: no data yet

            - XL [xl]
              Connection: PrusaLink, xl.local, user maker
              Model: not detected
              Camera: none
              State: no data yet

            """.ReplaceLineEndings("\n"),
            text);
    }

    [Fact]
    public void PrinterStatesComeFromTheSnapshots()
    {
        PrinterSnapshot snapshot = PrinterSnapshot.Unavailable("XL", PrinterState.Offline, Now.AddSeconds(-42), "Connection refused\r\n(10.0.0.6:80)", PrinterProblem.NotReachable);

        string text = SupportInfo.Build(Environment, Settings(), [snapshot], Now);

        Assert.Contains("  State: Offline, problem NotReachable, 42 s ago\n  Message: Connection refused (10.0.0.6:80)\n", text, StringComparison.Ordinal);
    }

    [Fact]
    public void FFmpegThatCannotBeLoadedIsNamedByTheExceptionTypeOnly()
    {
        SupportEnvironment environment = SupportEnvironment.Current("1.0.0", languageSetting: null, static () => throw new DllNotFoundException(@"C:\Users\someone\avcodec.dll"));

        Assert.Equal("not available (DllNotFoundException)", environment.FFmpeg);
        Assert.Null(environment.LanguageSetting);
        Assert.Contains("(setting: system)", SupportInfo.Build(environment, new AppSettings(), [], Now), StringComparison.Ordinal);
    }

    private static AppSettings Settings() => new()
    {
        PollingInterval = TimeSpan.FromSeconds(2.5),
        DebugLogging = true,
        Language = "cs",
        Printers =
        [
            new PrinterDefinition
            {
                Id = "mk4",
                Name = "Workshop MK4",
                ConnectionType = ConnectionType.PrusaLink,
                Address = "192.168.0.226",
                UserName = "workshop",
                Camera = new CameraSettings(enabled: true, new Uri("rtsp://192.168.0.50/live?token=Tk9secret")),
            },
            new PrinterDefinition { Id = "xl", Name = "XL", ConnectionType = ConnectionType.PrusaLink, Address = "xl.local" },
        ],
    };
}
