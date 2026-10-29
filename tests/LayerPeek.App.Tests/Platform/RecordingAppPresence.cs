using LayerPeek.App.Platform;

namespace LayerPeek.App.Tests.Platform;

/// <summary>Records what the app presence (Dock icon of macOS, T-102) was told, in order.</summary>
internal sealed class RecordingAppPresence : IAppPresence
{
    public List<string> Calls { get; } = [];

    public void SetVisible(bool visible, bool keepActive) =>
        Calls.Add(visible ? "show" : keepActive ? "hide keep-active" : "hide");
}
