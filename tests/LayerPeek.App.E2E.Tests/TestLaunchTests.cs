using System.Diagnostics;

using LayerPeek.App.E2E.Tests.Infrastructure;

namespace LayerPeek.App.E2E.Tests;

/// <summary>
/// Unit tests of the start guard of the app under test (T-074 safety fix): only a start with the run marker, the run's test
/// data folder and an instance suffix passes. They start nothing and run in every <c>dotnet test</c>.
/// </summary>
public sealed class TestLaunchTests
{
    private const string Run = @"C:\Temp\LayerPeek.App.E2E.abc\run-0123456789ab";
    private const string Exe = @"C:\does-not-exist\LayerPeek.exe";

    private static Func<string, string?> Variables(params (string Name, string? Value)[] variables) =>
        name => variables.FirstOrDefault(pair => pair.Name == name).Value;

    private static readonly (string, string?) Data = (AppSwitches.DataDirectoryVariable, Run + @"\data");
    private static readonly (string, string?) Suffix = (AppSwitches.SuffixVariable, "e2e-0123456789ab");

    [Fact]
    public void CompleteTestModePasses()
    {
        Assert.Null(TestLaunch.Problem([AppSwitches.MarkerArgument + Run], Variables(Data, Suffix)));
        Assert.Null(TestLaunch.Problem([AppSwitches.ClearToastsArgument, AppSwitches.MarkerArgument + Run + @"\"], Variables(Data, Suffix)));
    }

    /// <summary>The path of T-074: a start with the suffix (and forward-only) but neither data folder nor marker.</summary>
    [Fact]
    public void SuffixAndForwardOnlyWithoutDataFolderAndMarkerAreRefused()
    {
        Assert.NotNull(TestLaunch.Problem([], Variables(Suffix, (AppSwitches.ForwardOnlyVariable, "1"))));
    }

    [Fact]
    public void EachMissingPartIsRefused()
    {
        Assert.Contains("run marker", TestLaunch.Problem([], Variables(Data, Suffix)), StringComparison.Ordinal);
        Assert.Contains("run marker", TestLaunch.Problem([AppSwitches.MarkerArgument + Run, AppSwitches.MarkerArgument + Run], Variables(Data, Suffix)), StringComparison.Ordinal);
        Assert.Contains("absolute", TestLaunch.Problem([AppSwitches.MarkerArgument + "run-1"], Variables(Data, Suffix)), StringComparison.Ordinal);
        Assert.Contains(AppSwitches.DataDirectoryVariable, TestLaunch.Problem([AppSwitches.MarkerArgument + Run], Variables(Suffix)), StringComparison.Ordinal);
        Assert.Contains(AppSwitches.DataDirectoryVariable, TestLaunch.Problem([AppSwitches.MarkerArgument + Run], Variables((AppSwitches.DataDirectoryVariable, " "), Suffix)), StringComparison.Ordinal);
        Assert.Contains(AppSwitches.SuffixVariable, TestLaunch.Problem([AppSwitches.MarkerArgument + Run], Variables(Data)), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(@"data")]
    [InlineData(@"C:\Temp\LayerPeek.App.E2E.abc\other-run\data")]
    [InlineData(@"C:\Users\someone\AppData\Roaming\LayerPeek")]
    public void DataFolderOutsideTheRunIsRefused(string data)
    {
        Assert.NotNull(TestLaunch.Problem([AppSwitches.MarkerArgument + Run], Variables((AppSwitches.DataDirectoryVariable, data), Suffix)));
    }

    [Fact]
    public void StartInfoWithoutTestModeIsRefusedBeforeAnyProcessExists()
    {
        ProcessStartInfo start = new(Exe) { UseShellExecute = false };
        start.Environment.Remove(AppSwitches.DataDirectoryVariable);
        start.Environment[AppSwitches.SuffixVariable] = "e2e-0123456789ab";
        start.Environment[AppSwitches.ForwardOnlyVariable] = "1";

        Assert.Throws<InvalidOperationException>(() => TestLaunch.Start(start));
    }

    [Fact]
    public void StartInfoThroughTheShellOrWithAnArgumentStringIsRefused()
    {
        ProcessStartInfo shell = new(Exe) { UseShellExecute = true };
        Assert.Throws<InvalidOperationException>(() => TestLaunch.Verify(shell));

        ProcessStartInfo text = new(Exe, AppSwitches.MarkerArgument + Run) { UseShellExecute = false };
        text.Environment[AppSwitches.DataDirectoryVariable] = Run + @"\data";
        text.Environment[AppSwitches.SuffixVariable] = "e2e-0123456789ab";
        Assert.Throws<InvalidOperationException>(() => TestLaunch.Verify(text));
    }

    [Fact]
    public void ShellStartNeedsTheTestModeInItsVariables()
    {
        // A removed variable (null) is missing for the started app, whatever the test process has.
        Assert.Throws<InvalidOperationException>(() => TestLaunch.VerifyShellStart(
            [AppSwitches.MarkerArgument + Run],
            new Dictionary<string, string?> { [AppSwitches.DataDirectoryVariable] = null, [AppSwitches.SuffixVariable] = "e2e-1" }));

        TestLaunch.VerifyShellStart(
            [AppSwitches.MarkerArgument + Run],
            new Dictionary<string, string?> { [AppSwitches.DataDirectoryVariable] = Run + @"\data", [AppSwitches.SuffixVariable] = "e2e-1" });
    }
}
