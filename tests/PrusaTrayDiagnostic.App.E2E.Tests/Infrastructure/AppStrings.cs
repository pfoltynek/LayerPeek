using System.Xml.Linq;

using PrusaTrayDiagnostic.Tests.Shared;

namespace PrusaTrayDiagnostic.App.E2E.Tests.Infrastructure;

/// <summary>
/// UI texts of the app straight from its resource files (<c>Strings.resx</c>, <c>Strings.&lt;language&gt;.resx</c>, T-029):
/// the tests do not reference the app, and a text compared by its key follows every translation change.
/// </summary>
internal static class AppStrings
{
    private static readonly string Resources = Path.Combine(TestWorktree.Root, "src", "PrusaTrayDiagnostic.App", "Resources");

    /// <summary>The text of <paramref name="key"/> in <paramref name="language"/> ("en" is the neutral file).</summary>
    public static string Get(string language, string key)
    {
        string file = Path.Combine(Resources, language == "en" ? "Strings.resx" : $"Strings.{language}.resx");
        return XDocument.Load(file).Root?.Elements("data")
                .FirstOrDefault(data => (string?)data.Attribute("name") == key)?
                .Element("value")?.Value
            ?? throw new InvalidOperationException($"'{key}' is not in '{file}'.");
    }
}
