namespace PrusaTrayDiagnostic.App.Tray;

/// <summary>The platform tray icon the presenter writes to (Avalonia <c>TrayIcon</c> in the app, a fake in tests).</summary>
internal interface ITrayIconTarget<in TIcon>
    where TIcon : class
{
    void SetIcon(TIcon icon);

    void SetToolTipText(string text);
}
