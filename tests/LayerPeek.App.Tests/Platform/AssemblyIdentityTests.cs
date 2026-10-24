using System.Reflection;

namespace LayerPeek.App.Tests.Platform;

public sealed class AssemblyIdentityTests
{
    [Fact]
    public void ExeDescribesItselfWithTheProductName()
    {
        // Windows shows the file description as the name of the auxiliary windows' taskbar button (T-043).
        Assembly app = typeof(App).Assembly;

        Assert.Equal("LayerPeek", app.GetCustomAttribute<AssemblyTitleAttribute>()?.Title);
        Assert.Equal("LayerPeek", app.GetCustomAttribute<AssemblyProductAttribute>()?.Product);
    }
}
