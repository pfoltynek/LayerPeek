using System.Text.Json.Serialization;

namespace PrusaTrayDiagnostic.App.E2E.Tests.Infrastructure;

/// <summary>Source-generated JSON of the system change journal (T-065) and the states of its kinds.</summary>
[JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(SystemChangeEntry))]
[JsonSerializable(typeof(ThemeState))]
[JsonSerializable(typeof(TrayIconPromotionState))]
[JsonSerializable(typeof(DoNotDisturbState))]
[JsonSerializable(typeof(ProbeState))]
internal sealed partial class SystemChangeJson : JsonSerializerContext;
