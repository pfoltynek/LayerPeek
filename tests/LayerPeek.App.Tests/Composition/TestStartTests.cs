#if DEBUG
using LayerPeek.App.Composition;
using LayerPeek.App.SingleInstance;

namespace LayerPeek.App.Tests.Composition;

/// <summary>
/// Which starts a test made (T-074 safety fix): an argument of the test infrastructure or the forward-only switch. Such a
/// start without the test data folder is refused by <c>Program.Main</c> instead of running in production mode.
/// </summary>
public sealed class TestStartTests
{
    private static Func<string, string?> Variables(params (string Name, string Value)[] variables)
    {
        Dictionary<string, string> values = variables.ToDictionary(static pair => pair.Name, static pair => pair.Value, StringComparer.Ordinal);
        return name => values.GetValueOrDefault(name);
    }

    [Fact]
    public void ThePrefixCoversEveryTestArgumentOfTheApp()
    {
        Assert.Equal("--layerpeek-e2e-", AppEnvironment.TestArgumentPrefix);
        Assert.StartsWith(AppEnvironment.TestArgumentPrefix, AppEnvironment.ClearToastsArgument, StringComparison.Ordinal);
        Assert.StartsWith(AppEnvironment.TestArgumentPrefix, LayerPeek.App.Notifications.ToastActivatorCommandLine.TestEnvironmentPrefix, StringComparison.Ordinal);
    }

    [Fact]
    public void NormalStartIsNoTestStart()
    {
        Assert.False(AppEnvironment.IsTestStart([], Variables()));
        Assert.False(AppEnvironment.IsTestStart(["-ToastActivated", "-Embedding"], Variables()));
        // The instance suffix alone stays a developer switch (a second build next to the user's instance).
        Assert.False(AppEnvironment.IsTestStart([], Variables((InstanceTransports.SuffixVariable, "dev"))));
    }

    [Theory]
    [InlineData(@"--layerpeek-e2e-run=C:\Temp\LayerPeek.App.E2E\run-1")]
    [InlineData(@"--LayerPeek-E2E-run=C:\Temp\x")]
    [InlineData("--layerpeek-e2e-env=LAYERPEEK_INSTANCE_SUFFIX=e2e-1")]
    [InlineData("--layerpeek-e2e-clear-toasts")]
    [InlineData("--layerpeek-e2e-anything-new")]
    public void TestArgumentMakesATestStart(string argument)
    {
        Assert.True(AppEnvironment.IsTestStart(["-ToastActivated", argument], Variables()));
    }

    /// <summary>The path found in T-074: the two-process test set only the suffix and the forward-only switch.</summary>
    [Fact]
    public void ForwardOnlySwitchMakesATestStart()
    {
        Assert.True(AppEnvironment.IsTestStart([], Variables((InstanceTransports.SuffixVariable, "test-1"), (InstanceTransports.ForwardOnlyVariable, "1"))));
        Assert.True(AppEnvironment.IsTestStart([], Variables((InstanceTransports.ForwardOnlyVariable, "0"))));
    }
}
#endif
