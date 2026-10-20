using System.Reflection;

namespace PrusaTrayDiagnostic.App.Tests.Platform;

public sealed class AssemblyIdentityTests
{
    [Fact]
    public void ExeDescribesItselfWithTheProductName()
    {
        // Windows shows the file description as the name of the auxiliary windows' taskbar button (T-043).
        Assembly app = typeof(App).Assembly;

        Assert.Equal("Prusa Tray Diagnostic", app.GetCustomAttribute<AssemblyTitleAttribute>()?.Title);
        Assert.Equal("Prusa Tray Diagnostic", app.GetCustomAttribute<AssemblyProductAttribute>()?.Product);
    }
}
