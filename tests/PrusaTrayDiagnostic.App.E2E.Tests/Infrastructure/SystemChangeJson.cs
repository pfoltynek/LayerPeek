using System.Text.Json.Serialization;

namespace PrusaTrayDiagnostic.App.E2E.Tests.Infrastructure;

/// <summary>Source-generated JSON of the system change journal (T-065) and the states of its kinds (T-065, T-066).</summary>
[JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(SystemChangeEntry))]
[JsonSerializable(typeof(ThemeState))]
[JsonSerializable(typeof(TrayIconPromotionState))]
[JsonSerializable(typeof(DoNotDisturbState))]
[JsonSerializable(typeof(ProbeState))]
[JsonSerializable(typeof(DisplayScaleState))]
[JsonSerializable(typeof(DisplayTopologyState))]
internal sealed partial class SystemChangeJson : JsonSerializerContext;
