using System.ComponentModel;

namespace LayerPeek.App.Tray;

/// <summary>What the tray icon shows; <see cref="INotifyPropertyChanged.PropertyChanged"/> is raised on the UI thread.</summary>
internal interface ITrayStatusSource : INotifyPropertyChanged
{
    TrayIconKey IconKey { get; }

    string ToolTipText { get; }
}
