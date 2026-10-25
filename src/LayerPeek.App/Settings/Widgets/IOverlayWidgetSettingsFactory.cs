using System.ComponentModel;

using CommunityToolkit.Mvvm.ComponentModel;

using LayerPeek.Core.Events;

namespace LayerPeek.App.Settings.Widgets;

/// <summary>
/// The settings panel of one overlay widget on the Widgets tab of the settings window (T-092, design 6), declared by its
/// registration (<see cref="Cameras.Overlay.IOverlayWidgetDefinition.Settings"/>). One instance created in the composition
/// root: no reflection, no <c>Activator</c> (Native AOT). Its view is a <c>DataTemplate</c> whose <c>x:DataType</c> is the
/// type of the panel's view model, in <c>Settings/Widgets/WidgetSettingsTemplates.axaml</c>. UI thread only.
/// </summary>
public interface IOverlayWidgetSettingsFactory
{
    /// <summary>
    /// The view model of the panel for the printer of <paramref name="host"/>: created when the panel is shown (another
    /// widget or another printer selected) and disposed when it is replaced or the window closes.
    /// </summary>
    OverlayWidgetSettingsViewModel Create(IOverlayWidgetSettingsHost host);

    /// <summary>
    /// The widget's settings of <paramref name="printer"/> have an error that is shown (the "!" on the widget's row, without
    /// creating the panel). The values and their validation belong to the printer (<see cref="PrinterEditorViewModel"/>).
    /// </summary>
    bool HasError(PrinterEditorViewModel printer);
}

/// <summary>
/// What a widget settings panel may use of the settings window (T-092, design 6), for one printer: implemented by the
/// settings window's view model (<see cref="SettingsViewModel"/>). <see cref="INotifyPropertyChanged.PropertyChanged"/>
/// announces changes of <see cref="NotificationsEnabled"/> and of the notification switches
/// (<see cref="IsNotificationOn"/>), among other properties of the window. UI thread only.
/// </summary>
public interface IOverlayWidgetSettingsHost : INotifyPropertyChanged
{
    /// <summary>
    /// The printer of the panel as edited in the window. Its values and validation belong to it (e.g.
    /// <see cref="PrinterEditorViewModel.CooldownThresholdCelsius"/>), so that Save, Cancel, the unsaved state and the error
    /// markers work like for every other field.
    /// </summary>
    PrinterEditorViewModel Printer { get; }

    /// <summary>How many printers the window edits (an "… for all printers" action needs at least two).</summary>
    int PrinterCount { get; }

    /// <summary>The camera of <see cref="Printer"/> is on (as edited).</summary>
    bool IsCameraEnabled { get; }

    /// <summary>The master switch of the notifications on the General tab (as edited).</summary>
    bool NotificationsEnabled { get; }

    /// <summary>The switch of the notification <paramref name="kind"/> on the General tab (as edited).</summary>
    bool IsNotificationOn(PrintEventKind kind);

    /// <summary>
    /// Applies <paramref name="copy"/> to every other printer of the window (also unsaved ones), saved with the other
    /// changes, and shows <paramref name="done"/> in the status line. Nothing happens with a single printer.
    /// </summary>
    /// <param name="copy">Copies the value of <see cref="Printer"/> to the given printer.</param>
    /// <param name="done">The status text in the current language (formatted again after a language switch).</param>
    void ApplyToOtherPrinters(Action<PrinterEditorViewModel> copy, Func<string> done);

    /// <summary>Shows <paramref name="text"/> (formatted again after a language switch) in the status line of the window.</summary>
    void ShowStatus(Func<string> text);

    /// <summary>Switches to the General tab and moves the keyboard focus to the switch of the notification <paramref name="kind"/>.</summary>
    void ShowNotificationSetting(PrintEventKind kind);
}

/// <summary>
/// The view model of a widget settings panel (T-092, design 6): the panel of one widget for one printer. It shows and edits
/// values of <see cref="Host"/>'s printer; it holds no values of its own. Disposed when the panel is replaced (another
/// widget or printer selected, the window closed): subscriptions end there.
/// </summary>
public abstract class OverlayWidgetSettingsViewModel : ObservableObject, IDisposable
{
    protected OverlayWidgetSettingsViewModel(IOverlayWidgetSettingsHost host)
    {
        ArgumentNullException.ThrowIfNull(host);
        Host = host;
    }

    /// <summary>The printer whose settings the panel shows.</summary>
    public PrinterEditorViewModel Printer => Host.Printer;

    public bool IsDisposed { get; private set; }

    protected IOverlayWidgetSettingsHost Host { get; }

    public void Dispose()
    {
        if (!IsDisposed)
        {
            IsDisposed = true;
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }

    /// <summary>UI thread: the language changed; texts the panel formats itself are formatted again.</summary>
    internal virtual void RefreshTexts()
    {
    }

    protected virtual void Dispose(bool disposing)
    {
    }
}
