using System.Diagnostics.CodeAnalysis;

using LayerPeek.App.Cameras;
using LayerPeek.App.Platform;
using LayerPeek.App.Platform.Windows;

namespace LayerPeek.App.Windows;

/// <summary>
/// T-186: a window the window manager cloaks (Windows: on another virtual desktop) pauses its stream like a minimized one
/// (<see cref="CameraViewModel.SetCloaked"/>); followed from opening to closing.
/// </summary>
public sealed partial class CameraWindow
{
    private IWindowCloaking? _cloaking;
    private IDisposable? _cloakingWatch;

    /// <summary>
    /// Tells whether the shown window is cloaked; default (also for <see langword="null"/>): <see cref="WindowsWindowCloaking"/>
    /// on Windows, <see cref="NoWindowCloaking"/> elsewhere. Tests supply a fake (headless windows have no handle).
    /// </summary>
    [AllowNull]
    internal IWindowCloaking WindowCloaking
    {
        get => _cloaking ??= OperatingSystem.IsWindows() ? WindowsWindowCloaking.Shared : NoWindowCloaking.Instance;
        init => _cloaking = value;
    }

    private void StartCloakingWatch()
    {
        _cloakingWatch?.Dispose();
        _cloakingWatch = WindowCloaking.Watch(this, OnCloakedChanged);
    }

    private void StopCloakingWatch()
    {
        _cloakingWatch?.Dispose();
        _cloakingWatch = null;
    }

    private void OnCloakedChanged(bool cloaked)
    {
        if (!_closed)
        {
            _viewModel?.SetCloaked(cloaked);
        }
    }
}
