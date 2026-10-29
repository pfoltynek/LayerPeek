using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

using LayerPeek.App.Composition;
using LayerPeek.App.Platform;
using LayerPeek.App.Platform.MacOS;

namespace LayerPeek.App.Tests.Composition;

/// <summary>
/// "Open at Login" on macOS (T-019): who gets which Login Item. The test mode always the stand-in in its data folder (user
/// decision 2026-10-29), production only the app in its LayerPeek.app in a permanent place, a test host never. The real
/// Login Item is never created here: its factory fails the test when it is called where it must not be.
/// </summary>
public sealed class MacAutostartCompositionTests : IDisposable
{
    private const string Installed = "/Applications/LayerPeek.app/Contents/MacOS/LayerPeek";

    private readonly string _data = TestDirectories.NewDirectoryPath();

    public void Dispose() => TestDirectories.Delete(_data);

    private static readonly NullLogger<LoginItemAutostart> Logger = new();

    private static ILoginItemService Forbidden() => throw new InvalidOperationException("The real Login Item must not be created here.");

    [Fact]
    public void TheAppInItsBundleGetsTheRealLoginItem()
    {
        FakeLoginItem real = new();

        AutostartOption option = AppServices.CreateMacAutostart(AppEnvironment.Production, Logger, Installed, static _ => false, () => real);

        LoginItemAutostart autostart = Assert.IsType<LoginItemAutostart>(option.Autostart);
        Assert.Same(real, autostart.Service);
        Assert.False(option.NeedsInstallation);
    }

    [Theory]
    [InlineData("/Users/u/src/LayerPeek/src/LayerPeek.App/bin/Debug/net11.0/LayerPeek", false)]
    [InlineData("/private/var/folders/xy/T/AppTranslocation/0D1B/d/LayerPeek.app/Contents/MacOS/LayerPeek", false)]
    [InlineData("/Volumes/LayerPeek/LayerPeek.app/Contents/MacOS/LayerPeek", true)]
    public void ElsewhereTheOptionIsDisabledWithTheReason(string executable, bool readOnly)
    {
        AutostartOption option = AppServices.CreateMacAutostart(AppEnvironment.Production, Logger, executable, _ => readOnly, Forbidden);

        Assert.Equal(new AutostartOption(null, NeedsInstallation: true), option);
    }

    [Theory]
    [InlineData("/Users/u/src/LayerPeek/tests/LayerPeek.App.Tests/bin/Debug/net11.0/LayerPeek.App.Tests")]
    [InlineData("/usr/local/share/dotnet/dotnet")]
    [InlineData("")]
    [InlineData(null)]
    public void ATestHostNeverGetsIt(string? executable)
    {
        Assert.Equal(new AutostartOption(null), AppServices.CreateMacAutostart(AppEnvironment.Production, Logger, executable, static _ => false, Forbidden));
#if DEBUG
        Assert.Equal(new AutostartOption(null), AppServices.CreateMacAutostart(AppEnvironment.ForTests(_data), Logger, executable, static _ => false, Forbidden));
#endif
    }

#if DEBUG
    /// <summary>User decision 2026-10-29: offered in the test mode, but only the stand-in file in the test data folder.</summary>
    [Theory]
    [InlineData(Installed)]
    [InlineData("/Users/u/src/LayerPeek/src/LayerPeek.App/bin/Debug/net11.0/LayerPeek")]
    public void TheTestModeGetsOnlyTheStandIn(string executable)
    {
        AutostartOption option = AppServices.CreateMacAutostart(AppEnvironment.ForTests(_data), Logger, executable, static _ => false, Forbidden);

        LoginItemAutostart autostart = Assert.IsType<LoginItemAutostart>(option.Autostart);
        StandInLoginItem standIn = Assert.IsType<StandInLoginItem>(autostart.Service);
        Assert.Equal(Path.Combine(_data, "LoginItems", "LayerPeek.loginitem"), standIn.Path);

        autostart.SetEnabled(true);
        Assert.True(autostart.IsEnabled());
        Assert.Equal(executable + "\n", File.ReadAllText(standIn.Path));
        autostart.SetEnabled(false);
        Assert.False(File.Exists(standIn.Path));
    }

    /// <summary>The test host on macOS (also in the test mode) gets no autostart from the container: nothing can reach SMAppService.</summary>
    [Fact]
    public async Task TheContainerOfTheTestHostHasNoAutostart()
    {
        Assert.SkipUnless(OperatingSystem.IsMacOS(), "The macOS branch of the container.");
        await using ServiceProvider production = AppServices.Create();
        await using ServiceProvider testMode = AppServices.Create(environment: AppEnvironment.ForTests(_data));

        Assert.Equal(new AutostartOption(null), production.GetRequiredService<AutostartOption>());
        Assert.Equal(new AutostartOption(null), testMode.GetRequiredService<AutostartOption>());
    }

#endif
    private sealed class FakeLoginItem : ILoginItemService
    {
        public LoginItemStatus Status() => LoginItemStatus.NotRegistered;

        public void Register()
        {
        }

        public void Unregister()
        {
        }
    }
}
