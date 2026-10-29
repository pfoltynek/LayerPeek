using LayerPeek.App.Windows;

namespace LayerPeek.App.Settings;

/// <summary>
/// "Settings…" and "Add printer…" of the tray menu (T-015, T-042) and a click on the test notification (T-101) on the
/// single settings window: opens it or activates the open one. Kept apart from <c>App</c> so that both paths are testable with a real window. UI thread only.
/// </summary>
internal static class SettingsWindowCommand
{
    /// <param name="slot">The slot of the settings window (one instance, closed when quitting).</param>
    /// <param name="create">
    /// Creates the window and its view model; one editing session per window: the slot disposes the view model (cancelling a
    /// running test) when the window closes.
    /// </param>
    /// <param name="addPrinter">
    /// "Add printer…": the window (new or already open) shows the Printers tab with a new printer
    /// (<see cref="SettingsViewModel.AddPrinterFromTray"/>; a new window adds it once its settings are loaded).
    /// </param>
    /// <param name="generalTab">
    /// A click on the test notification (T-101): the window (new or already open) shows the General tab, where the test came
    /// from; the edits of an open window are kept.
    /// </param>
    public static void Show(AuxiliaryWindowSlot slot, Func<(IAuxiliaryWindow Window, SettingsViewModel ViewModel)> create, bool addPrinter, bool generalTab = false)
    {
        ArgumentNullException.ThrowIfNull(slot);
        ArgumentNullException.ThrowIfNull(create);
        SettingsViewModel? created = null;
        slot.ShowOrActivate(
            () =>
            {
                (IAuxiliaryWindow window, SettingsViewModel viewModel) = create();
                created = viewModel;
                return (window, viewModel);
            },
            owned =>
            {
                // Already open: the editing session goes on, with a new printer when it has none yet.
                if (addPrinter && owned is SettingsViewModel open)
                {
                    open.AddPrinterFromTray();
                }

                if (generalTab && owned is SettingsViewModel shown)
                {
                    shown.ShowGeneralTab();
                }
            });

        if (addPrinter)
        {
            created?.AddPrinterFromTray();
        }

        if (generalTab)
        {
            created?.ShowGeneralTab();
        }

        // Loads after the window is shown (reading the file and the credential store runs off the UI thread); never throws.
        _ = created?.LoadAsync();
    }
}
