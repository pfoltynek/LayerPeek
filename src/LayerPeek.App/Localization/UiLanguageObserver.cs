using LayerPeek.App.Tray;
using LayerPeek.Core.Settings;

namespace LayerPeek.App.Localization;

/// <summary>
/// Applies the language of saved settings (T-029): settings reach the observers both when monitoring restarts
/// (<see cref="IMonitoringObserver.ApplySettings"/>) and when only other settings changed
/// (<see cref="IMonitoringObserver.UpdateSettings"/>, e.g. only the language). The switch runs on the UI thread; settings
/// arriving in quick succession apply only the latest language.
/// </summary>
internal sealed class UiLanguageObserver : IMonitoringObserver
{
    private readonly UiLanguage _language;
    private readonly IUiDispatcher _dispatcher;
    private readonly Action _apply;
    private readonly Lock _gate = new();

    // Guarded by _gate.
    private string? _latest;
    private bool _applyPending;

    public UiLanguageObserver(UiLanguage language, IUiDispatcher dispatcher)
    {
        ArgumentNullException.ThrowIfNull(language);
        ArgumentNullException.ThrowIfNull(dispatcher);
        _language = language;
        _dispatcher = dispatcher;
        _apply = Apply;
    }

    public void ApplySettings(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        Schedule(settings.Language);
    }

    public void UpdateSettings(AppSettings settings) => ApplySettings(settings);

    /// <summary>The language stays as it is.</summary>
    public void ReportMonitoringFailed()
    {
    }

    private void Schedule(string? code)
    {
        lock (_gate)
        {
            _latest = code;
            if (_applyPending)
            {
                return;
            }

            _applyPending = true;
        }

        _dispatcher.Post(_apply);
    }

    private void Apply()
    {
        string? code;
        lock (_gate)
        {
            _applyPending = false;
            code = _latest;
        }

        _language.Apply(code);
    }
}
