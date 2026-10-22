using System.Reflection;
using System.Reflection.Emit;

using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.VisualTree;

using PrusaTrayDiagnostic.App.Localization;
using PrusaTrayDiagnostic.App.Resources;
using PrusaTrayDiagnostic.App.Tests.Tray.Menu;
using PrusaTrayDiagnostic.App.UiTests.Infrastructure;
using PrusaTrayDiagnostic.App.Windows;
using PrusaTrayDiagnostic.Core.Settings;

namespace PrusaTrayDiagnostic.App.UiTests.About;

/// <summary>
/// The About window with the support actions (T-048 design): the same size in every state and language, within the
/// smallest supported screen (T-033: client at most 780 × 508), the status in at most three lines, no button text cut.
/// </summary>
public sealed class AboutWindowLayoutTests(HeadlessSession session)
{
    public static TheoryData<string> Languages => [.. AppLanguages.Supported];

    /// <summary>Version 1.0.0 whatever the product version is, so that a release does not change the snapshots.</summary>
    private static readonly Assembly FixedProduct = AssemblyBuilder.DefineDynamicAssembly(
        new AssemblyName("FixedProduct"),
        AssemblyBuilderAccess.Run,
        [
            new CustomAttributeBuilder(typeof(AssemblyInformationalVersionAttribute).GetConstructor([typeof(string)])!, ["1.0.0"]),
            new CustomAttributeBuilder(typeof(AssemblyCopyrightAttribute).GetConstructor([typeof(string)])!, [AboutViewModel.DefaultCopyright]),
        ]);

    /// <summary>The states of the status line, the longest texts included.</summary>
    private static readonly (string Name, Func<string>? Status, bool IsError)[] States =
    [
        ("default", null, false),
        ("saving", static () => Strings.SupportBundleSaving, false),
        ("saved", static () => Strings.SupportBundleSaved, false),
        ("bundle failed", static () => Strings.SupportBundleFailed, true),
        ("log folder failed", static () => Strings.SupportLogFolderFailed, true),
    ];

    [Theory]
    [MemberData(nameof(Languages))]
    public Task FitsTheSmallestScreenWithTheSameSizeInEveryState(string language) =>
        session.RunAsync(async scope =>
        {
            Assert.True(new UiLanguage().Apply(language));
            (AboutWindow window, AboutViewModel viewModel) = Open(scope);
            List<string> problems = [];
            Size? first = null;
            foreach ((string name, Func<string>? status, bool isError) in States)
            {
                viewModel.ShowSupportStatus(status, isError);
                window.Settle();
                string context = $"{language}, {name}";
                Size size = window.ClientSize;
                first ??= size;
                if (size != first)
                {
                    problems.Add($"{context}: {size} instead of {first}.");
                }

                if (size.Width > 780 || size.Height > 508)
                {
                    problems.Add($"{context}: {size} does not fit 780 × 508.");
                }

                TextBlock statusText = window.Find<TextBlock>("SupportStatus");
                if (statusText.IsEffectivelyVisible && statusText.TextLayout.TextLines.Count > 3)
                {
                    problems.Add($"{context}: the status has {statusText.TextLayout.TextLines.Count} lines.");
                }

                problems.AddRange(CutButtonTexts(window, context));
            }

            Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
            await Task.CompletedTask;
        });

    [Fact]
    public Task ErrorStatusIsRedAndTheOthersAreNot() =>
        session.RunAsync(async scope =>
        {
            (AboutWindow window, AboutViewModel viewModel) = Open(scope);
            TextBlock status = window.Find<TextBlock>("SupportStatus");

            viewModel.ShowSupportStatus(static () => Strings.SupportBundleSaved, isError: false);
            window.Settle();
            Color? normal = (status.Foreground as ISolidColorBrush)?.Color;
            viewModel.ShowSupportStatus(static () => Strings.SupportBundleFailed, isError: true);
            window.Settle();

            Assert.Equal(Color.Parse("#C42B1C"), (status.Foreground as ISolidColorBrush)?.Color);
            Assert.NotEqual(normal, (status.Foreground as ISolidColorBrush)?.Color);
            Assert.Equal(AutomationLiveSetting.Polite, AutomationProperties.GetLiveSetting(status));
            await Task.CompletedTask;
        });

    [Fact]
    public Task TabMovesFromTheLinksThroughTheSupportActionsToClose() =>
        session.RunAsync(async scope =>
        {
            (AboutWindow window, _) = Open(scope);
            List<string> order = [];
            for (int i = 0; i < 10; i++)
            {
                window.KeyPressQwerty(PhysicalKey.Tab, RawInputModifiers.None);
                window.KeyReleaseQwerty(PhysicalKey.Tab, RawInputModifiers.None);
                window.Settle();
                if ((window.FocusManager?.GetFocusedElement() as Control) is { } focused)
                {
                    order.Add(AutomationProperties.GetAutomationId(focused) ?? (focused as ContentControl)?.Content as string ?? focused.GetType().Name);
                }
            }

            string[] expected = [Strings.AboutRepository, Strings.AboutThirdPartyLicenses, "SupportSaveBundle", "SupportOpenLogFolder", "AboutClose"];
            int start = order.IndexOf(Strings.AboutRepository);
            Assert.True(start >= 0, string.Join(", ", order));
            Assert.Equal(expected, order.Skip(start).Take(expected.Length));
            await Task.CompletedTask;
        });

    [Fact]
    public Task SupportGroupLooksAsDesigned() =>
        session.RunAsync(async scope =>
        {
            (AboutWindow window, _) = Open(scope);

            SnapshotAssert.Matches(window, "about-support");
            await Task.CompletedTask;
        });

    [Fact]
    public Task LongestErrorInGermanLooksAsDesigned() =>
        session.RunAsync(async scope =>
        {
            Assert.True(new UiLanguage().Apply("de"));
            (AboutWindow window, AboutViewModel viewModel) = Open(scope);
            viewModel.ShowSupportStatus(static () => Strings.SupportBundleFailed, isError: true);
            window.Settle();

            SnapshotAssert.Matches(window, "about-support-error-de");
            await Task.CompletedTask;
        });

    private static (AboutWindow Window, AboutViewModel ViewModel) Open(UiTestScope scope)
    {
        AboutViewModel viewModel = new(new FakeUrlLauncher(), FixedProduct);
        AboutWindow window = scope.Track(new AboutWindow { DataContext = viewModel });
        window.Show();
        window.Settle();
        return (window, viewModel);
    }

    /// <summary>Button texts that do not get the width they need (a cut translation).</summary>
    private static IEnumerable<string> CutButtonTexts(Window window, string context)
    {
        foreach (Button button in window.GetVisualDescendants().OfType<Button>().Where(static b => b.IsEffectivelyVisible))
        {
            foreach (TextBlock block in button.GetVisualDescendants().OfType<TextBlock>().Where(static t => t.IsEffectivelyVisible && !string.IsNullOrEmpty(t.Text)))
            {
                TextBlock probe = new() { Text = block.Text, FontFamily = block.FontFamily, FontSize = block.FontSize, FontWeight = block.FontWeight };
                probe.Measure(Size.Infinity);
                if (probe.DesiredSize.Width > block.Bounds.Width + 0.5)
                {
                    yield return $"{context}: '{block.Text}' needs {probe.DesiredSize.Width:0.#} DIP but has {block.Bounds.Width:0.#}.";
                }
            }
        }
    }
}
