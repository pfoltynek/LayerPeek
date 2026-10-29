using System.Runtime.Versioning;

namespace LayerPeek.App.Platform.MacOS;

/// <summary>
/// The shell side of auxiliary windows on macOS (T-019): nothing to prepare (no taskbar), and a window asked to come to
/// the front (a second start of the app, a reopen) brings the agent app with it (<see cref="MacAppActivation"/>), which
/// is not active on its own because it has no Dock icon.
/// </summary>
[SupportedOSPlatform("macos")]
internal sealed class MacWindowShell : IWindowShell
{
    public void Prepare(nint handle)
    {
    }

    public void Release(nint handle)
    {
    }

    public void RequestAttention(nint handle) => MacAppActivation.Activate();
}
