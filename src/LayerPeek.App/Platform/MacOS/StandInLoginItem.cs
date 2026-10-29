namespace LayerPeek.App.Platform.MacOS;

/// <summary>
/// The Login Item of the test mode (T-019, rule "Izolace testů na macOS" in docs/architecture.md, user decision
/// 2026-10-29): "Open at Login" is offered, but registering writes only the file <paramref name="path"/> in the test data
/// folder (<c>AppEnvironment.TestLoginItemPath</c>) with the executable it stands for. <c>SMAppService</c>, the user's
/// Login Items and <c>~/Library/LaunchAgents</c> are never touched. The sweep of the test folders at the start of every test
/// run and the Debug-only cleanup mode <c>--layerpeek-e2e-clear-autostart</c> remove it after a crash.
/// </summary>
internal sealed class StandInLoginItem(string path, string executable) : ILoginItemService
{
    /// <summary>The stand-in file (tests).</summary>
    internal string Path { get; } = !string.IsNullOrWhiteSpace(path) && System.IO.Path.IsPathFullyQualified(path)
        ? path
        : throw new ArgumentException("The stand-in Login Item needs an absolute path.", nameof(path));

    public LoginItemStatus Status() => File.Exists(Path) ? LoginItemStatus.Enabled : LoginItemStatus.NotRegistered;

    public void Register()
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
        File.WriteAllText(Path, executable + "\n");
    }

    public void Unregister() => File.Delete(Path);

    /// <summary>
    /// Nothing: the test mode never opens System Settings (rule "Izolace testů na macOS"). The stand-in is never in the state
    /// <see cref="LoginItemStatus.RequiresApproval"/> either, so the settings window does not offer it.
    /// </summary>
    public void OpenSystemSettings()
    {
    }
}
