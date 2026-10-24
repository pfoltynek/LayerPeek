using System.Collections.Concurrent;
using System.Globalization;
using System.Xml.Linq;

using LayerPeek.Tests.Shared;

namespace LayerPeek.App.E2E.Tests.Infrastructure;

/// <summary>
/// UI texts of the app read from its <c>Strings*.resx</c> in the worktree (the tests do not reference the app), so that
/// the expected texts of a check follow the translations instead of being copied into the tests.
/// </summary>
internal static class AppStrings
{
    private static readonly string ResourcesDirectory = Path.Combine(TestWorktree.Root, "src", "LayerPeek.App", "Resources");

    private static readonly ConcurrentDictionary<string, IReadOnlyDictionary<string, string>> Tables = new(StringComparer.Ordinal);

    /// <summary>The text <paramref name="key"/> in <paramref name="language"/> (<c>en</c> = the neutral <c>Strings.resx</c>).</summary>
    public static string Get(string language, string key) =>
        Table(language).TryGetValue(key, out string? text)
            ? text
            : throw new KeyNotFoundException($"'{key}' is not in {FileOf(language)}.");

    /// <summary><see cref="Get"/> formatted like the app does (<c>string.Format</c> with the culture of the language).</summary>
    public static string Format(string language, string key, params object[] arguments) =>
        string.Format(CultureInfo.GetCultureInfo(language), Get(language, key), arguments);

    private static IReadOnlyDictionary<string, string> Table(string language) => Tables.GetOrAdd(language, static language =>
        XDocument.Load(FileOf(language)).Root!
            .Elements("data")
            .ToDictionary(
                static data => (string)data.Attribute("name")!,
                static data => (string?)data.Element("value") ?? string.Empty,
                StringComparer.Ordinal));

    private static string FileOf(string language) =>
        Path.Combine(ResourcesDirectory, language == "en" ? "Strings.resx" : $"Strings.{language}.resx");
}
