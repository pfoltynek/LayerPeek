using System.Globalization;
using System.Text.RegularExpressions;

using PrusaTrayDiagnostic.App.Resources;

namespace PrusaTrayDiagnostic.App.Tests.Localization;

/// <summary>
/// XAML texts are bound through <c>LocalizedStrings</c> so that open windows follow a language switch (T-029). The
/// indexer keys are plain strings in XAML, so this test is what catches a typo or a removed resource.
/// </summary>
public sealed partial class XamlLocalizationTests
{
    [Fact]
    public void EveryLocalizedXamlKeyExists()
    {
        List<string> keys = [];
        foreach (string file in XamlFiles())
        {
            foreach (Match match in LocalizedBinding().Matches(File.ReadAllText(file)))
            {
                keys.Add(match.Groups["key"].Value);
            }
        }

        Assert.NotEmpty(keys);
        foreach (string key in keys)
        {
            Assert.True(Strings.ResourceManager.GetString(key, CultureInfo.InvariantCulture) is not null, $"XAML uses the unknown text '{key}'.");
        }
    }

    [Fact]
    public void EveryLocalizedStringsUseHasTheCheckedForm()
    {
        foreach (string file in XamlFiles())
        {
            string xaml = File.ReadAllText(file);
            // A binding written in another form would escape EveryLocalizedXamlKeyExists, so a typo could ship unnoticed.
            Assert.True(
                Regex.Count(xaml, @"LocalizedStrings\.Instance") == LocalizedBinding().Count(xaml),
                $"{Path.GetFileName(file)} binds LocalizedStrings in a form other than {{Binding [Key], Source={{x:Static loc:LocalizedStrings.Instance}}}}.");
        }
    }

    [Fact]
    public void NoXamlTextIsEvaluatedOnlyOnce()
    {
        foreach (string file in XamlFiles())
        {
            // {x:Static res:Strings.X} is read when the window is created and would not follow a language switch.
            Assert.DoesNotMatch(@"x:Static \w+:Strings\.", File.ReadAllText(file));
        }
    }

    private static string[] XamlFiles()
    {
        string[] files = Directory.GetFiles(Path.Combine(RepositoryRoot(), "src", "PrusaTrayDiagnostic.App"), "*.axaml", SearchOption.AllDirectories);
        Assert.NotEmpty(files);
        return files;
    }

    private static string RepositoryRoot()
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "PrusaTrayDiagnostic.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException("Repository root not found.");
    }

    [GeneratedRegex(@"\{Binding \[(?<key>[^\]]+)\], Source=\{x:Static loc:LocalizedStrings\.Instance\}\}")]
    private static partial Regex LocalizedBinding();
}
