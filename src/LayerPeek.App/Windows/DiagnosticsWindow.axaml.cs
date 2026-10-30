using Avalonia.Controls;
using Avalonia.Interactivity;

namespace LayerPeek.App.Windows;

/// <summary>
/// Diagnostics of the configured printers (T-011). Closing it never quits the app (<c>ShutdownMode.OnExplicitShutdown</c>);
/// its view model is disposed by <see cref="AuxiliaryWindowSlot"/> when it closes, and the chart unsubscribes when detached.
/// Opens at the size of the settings window (or its own remembered size, T-094), shrunk to the work area of its screen like it (T-041,
/// <see cref="AuxiliaryWindowPlacement"/>).
/// </summary>
public sealed partial class DiagnosticsWindow : Window
{
    private readonly WorkAreaFitter _fitter;

    public DiagnosticsWindow()
    {
        InitializeComponent();
        _fitter = new WorkAreaFitter(this);
        // DataContext (DiagnosticsViewModel) is set by the creator.
        MacShortcuts.HandleCloseKeys(this, () => UsesMacShortcuts, Close, CloseFromCommandPeriod);
    }

    /// <summary>
    /// The macOS shortcuts (T-100): ⌘W closes the window, ⌘. is Close like Esc (an open drop-down is closed first, user
    /// decision 2026-10-30). Default: on macOS only; tests set it.
    /// </summary>
    internal bool UsesMacShortcuts { get; set; } = MacShortcuts.IsPlatformDefault;

    /// <summary>The fitting and remembering of the window size (T-094).</summary>
    internal WorkAreaFitter Sizing => _fitter;

    /// <summary>⌘. (macOS, T-100): what Esc does: an open drop-down (the printer selector) closes first, otherwise the window.</summary>
    private void CloseFromCommandPeriod()
    {
        if (!MacShortcuts.TryCloseOpenDropDown(this))
        {
            Close();
        }
    }

    /// <summary>Before showing (T-094): opens with the size and maximized state remembered in <paramref name="store"/> and remembers them on closing.</summary>
    internal void RememberSizeIn(IAuxiliaryWindowStateStore store) => _fitter.Remember(store, AuxiliaryWindowKeys.Diagnostics);

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        _fitter.CorrectAfterOpening();
    }

    private void OnCloseClicked(object? sender, RoutedEventArgs e) => Close();
}
