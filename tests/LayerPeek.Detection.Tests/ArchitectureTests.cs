using System.Reflection;

using LayerPeek.Core.Detection;
using LayerPeek.Detection;

namespace LayerPeek.Detection.Tests;

/// <summary>
/// T-153 layering (user decision 2026-11-04): App → Detection → Core. Detection knows neither the UI nor FFmpeg nor HTTP;
/// Core knows neither Detection nor any ML / CV runtime.
/// </summary>
public sealed class ArchitectureTests
{
    private static readonly string[] NeverInDetection = ["Avalonia", "FFmpeg", "LayerPeek.App", "LayerPeek.Camera", "LayerPeek.PrusaLink", "LayerPeek.PrusaConnect", "System.Net.Http", "Microsoft.AspNetCore"];

    private static readonly string[] NeverInCore = ["LayerPeek.Detection", "Avalonia", "FFmpeg", "OpenCv", "Emgu", "Onnx", "Microsoft.ML", "SkiaSharp"];

    private static string[] References(Assembly assembly) => [.. assembly.GetReferencedAssemblies().Select(static a => a.Name ?? "")];

    [Fact]
    public void DetectionReferencesCoreButNoUiDecoderOrHttp()
    {
        string[] references = References(typeof(PrintFailureDetectors).Assembly);

        Assert.Contains("LayerPeek.Core", references);
        foreach (string forbidden in NeverInDetection)
        {
            Assert.DoesNotContain(references, r => r.StartsWith(forbidden, StringComparison.OrdinalIgnoreCase));
        }
    }

    [Fact]
    public void CoreReferencesNeitherDetectionNorAnyMlOrCvRuntime()
    {
        string[] references = References(typeof(IPrintFailureDetector).Assembly);

        foreach (string forbidden in NeverInCore)
        {
            Assert.DoesNotContain(references, r => r.StartsWith(forbidden, StringComparison.OrdinalIgnoreCase));
        }
    }

    [Fact]
    public void TheRegisteredDetectorsFitTheOrchestration()
    {
        IReadOnlyList<PrintFailureDetectorRegistration> all = PrintFailureDetectors.All;

        Assert.Equal(all.Count, all.Select(static d => d.Id).Distinct(StringComparer.Ordinal).Count());
        Assert.All(all, d => Assert.Equal(all[0].FrameSpec, d.FrameSpec));
    }
}
