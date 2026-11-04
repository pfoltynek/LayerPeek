using LayerPeek.App.Localization;
using LayerPeek.App.Resources;
using LayerPeek.Core.Detection;
using LayerPeek.Core.Printers;
using LayerPeek.Core.Settings;

namespace LayerPeek.App.Settings;

/// <summary>An item of "Repeat the alarm" (T-154): once per print and kind, or while the failure lasts.</summary>
public sealed class AlarmRepeatOption(PrintFailureAlarmRepeat value, Func<string> text) : LocalizedOption(text)
{
    public PrintFailureAlarmRepeat Value { get; } = value;
}

/// <summary>
/// The "Print failure detection" section of the Recording tab (T-154, user decisions 2026-11-04): the global switch (off
/// after installation), how often a lasting failure is announced, whether alarm snapshots are saved (on by default) and the
/// sampling interval 2–5 s (global, T-153). Whether a printer is watched, its region and the detector sensitivities are on its
/// Detection sub-tab (<see cref="PrinterEditorViewModel"/>). Saved and applied by Save like the other settings.
/// </summary>
public sealed partial class SettingsViewModel
{
    private static readonly decimal MinSampleSeconds = (decimal)PrintFailureDetectionSettings.MinSampleInterval.TotalSeconds;
    private static readonly decimal MaxSampleSeconds = (decimal)PrintFailureDetectionSettings.MaxSampleInterval.TotalSeconds;

    private bool _detectionEnabled;
    private bool _detectionSaveSnapshots = true;
    private AlarmRepeatOption? _selectedAlarmRepeat;
    private decimal? _detectionIntervalSeconds = (decimal)PrintFailureDetectionSettings.DefaultSampleInterval.TotalSeconds;
    private string? _detectionIntervalError;

    // Printers whose last frame this window wrote (UI thread): those never saved lose it when the window closes.
    private readonly HashSet<string> _lastFramesWritten = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The detector kinds of this version, in display order (T-153 registrations); default: none.</summary>
    internal IReadOnlyList<PrintFailureDetectorRegistration> DetectorRegistrations { get; init; } = [];

    /// <summary>The last camera frame of each printer for the bed region editor; <see langword="null"/>: none kept (tests).</summary>
    internal ILastCameraFrames? LastFrames { get; init; }

    public static decimal MinDetectionIntervalSeconds => MinSampleSeconds;

    public static decimal MaxDetectionIntervalSeconds => MaxSampleSeconds;

    /// <summary>The global switch: off, nothing is watched (the default).</summary>
    public bool DetectionGloballyEnabled
    {
        get => _detectionEnabled;
        set
        {
            if (_detectionEnabled == value)
            {
                return;
            }

            SetGeneral(ref _detectionEnabled, value);
            foreach (PrinterEditorViewModel printer in Printers)
            {
                printer.OnDetectionStateChanged();
            }
        }
    }

    /// <summary>Save a JPEG of the camera at every alarm next to the recordings (on by default).</summary>
    public bool DetectionSaveSnapshots
    {
        get => _detectionSaveSnapshots;
        set => SetGeneral(ref _detectionSaveSnapshots, value);
    }

    public IReadOnlyList<AlarmRepeatOption> AlarmRepeatOptions => field ??=
    [
        new AlarmRepeatOption(PrintFailureAlarmRepeat.OncePerPrint, static () => Strings.SettingsDetectionRepeatOnce),
        new AlarmRepeatOption(
            PrintFailureAlarmRepeat.WhilePersisting,
            static () => Format(Strings.SettingsDetectionRepeatWhile, (int)PrintFailureDetectionOptions.Default.RepeatInterval.TotalMinutes)),
    ];

    public AlarmRepeatOption SelectedAlarmRepeat
    {
        get => _selectedAlarmRepeat ?? AlarmRepeatOptions[0];
        set
        {
            // A combo box may write null while its items change.
            if (value is not null && !ReferenceEquals(value, SelectedAlarmRepeat))
            {
                SetGeneral(ref _selectedAlarmRepeat, value);
            }
        }
    }

    /// <summary>How often a printing printer's camera is sampled, in seconds (2–5, decimals allowed).</summary>
    public decimal? DetectionIntervalSeconds
    {
        get => _detectionIntervalSeconds;
        set => SetGeneral(ref _detectionIntervalSeconds, value);
    }

    public string? DetectionIntervalError
    {
        get => _detectionIntervalError;
        private set
        {
            if (SetProperty(ref _detectionIntervalError, value))
            {
                OnPropertyChanged(nameof(HasDetectionIntervalError));
                OnPropertyChanged(nameof(ShowsDetectionIntervalHint));
                OnPropertyChanged(nameof(DetectionIntervalHelpText));
            }
        }
    }

    public bool HasDetectionIntervalError => _detectionIntervalError is not null;

    public bool ShowsDetectionIntervalHint => _detectionIntervalError is null;

    public static string DetectionIntervalHint => Format(Strings.SettingsDetectionIntervalHint, MinSampleSeconds, MaxSampleSeconds);

    /// <summary>For screen readers: the error of the interval, else its hint.</summary>
    public string DetectionIntervalHelpText => _detectionIntervalError ?? DetectionIntervalHint;

    /// <summary>The interval as entered, when it is within 2–5 s.</summary>
    internal static bool IsValidDetectionInterval(decimal? seconds) => seconds is { } value && value >= MinSampleSeconds && value <= MaxSampleSeconds;

    private void PopulateDetection(PrintFailureDetectionSettings detection)
    {
        _detectionEnabled = detection.Enabled;
        _detectionSaveSnapshots = detection.SaveSnapshots;
        _selectedAlarmRepeat = FindAlarmRepeat(detection.AlarmRepeat);
        // Whole milliseconds (T-153 clamps and rounds); shown with up to one decimal.
        _detectionIntervalSeconds = Math.Round((decimal)detection.SampleInterval.TotalMilliseconds / 1000, 3);
    }

    /// <summary>The detection settings as edited; <see langword="null"/> while the interval is invalid.</summary>
    private PrintFailureDetectionSettings? BuildDetection() =>
        IsValidDetectionInterval(_detectionIntervalSeconds) && _detectionIntervalError is null
            ? new PrintFailureDetectionSettings
            {
                Enabled = _detectionEnabled,
                SaveSnapshots = _detectionSaveSnapshots,
                AlarmRepeat = SelectedAlarmRepeat.Value,
                SampleInterval = TimeSpan.FromMilliseconds((double)Math.Round(_detectionIntervalSeconds!.Value * 1000)),
            }
            : null;

    private void ValidateDetection() =>
        DetectionIntervalError = IsValidDetectionInterval(_detectionIntervalSeconds)
            ? null
            : Format(Strings.SettingsDetectionIntervalError, MinSampleSeconds, MaxSampleSeconds);

    private AlarmRepeatOption FindAlarmRepeat(PrintFailureAlarmRepeat repeat)
    {
        foreach (AlarmRepeatOption option in AlarmRepeatOptions)
        {
            if (option.Value == repeat)
            {
                return option;
            }
        }

        return AlarmRepeatOptions[0];
    }

    /// <summary>The language changed: the options and texts with a format.</summary>
    private void RefreshDetectionTexts()
    {
        foreach (AlarmRepeatOption option in AlarmRepeatOptions)
        {
            option.RefreshText();
        }

        OnPropertyChanged(nameof(DetectionIntervalHint));
        OnPropertyChanged(nameof(DetectionIntervalHelpText));
    }

    /// <summary>After a save: the last frames of printers that are gone are deleted (T-154).</summary>
    private void ForgetLastFramesOfRemovedPrinters(AppSettings before, AppSettings after)
    {
        if (LastFrames is not { } frames)
        {
            return;
        }

        HashSet<string> kept = new(after.Printers.Count, StringComparer.OrdinalIgnoreCase);
        foreach (PrinterDefinition printer in after.Printers)
        {
            kept.Add(printer.Id);
        }

        foreach (PrinterDefinition printer in before.Printers)
        {
            if (!kept.Contains(printer.Id))
            {
                frames.Delete(printer.Id);
            }
        }
    }

    /// <summary>UI thread: <paramref name="printerId"/> got a new last frame (a successful camera test in this window).</summary>
    internal void OnLastFrameWritten(string printerId) => _lastFramesWritten.Add(printerId);

    /// <summary>
    /// The window closes: the last frames written for printers that were never saved (a new printer tested, then cancelled
    /// or removed before Save) are deleted, so no frame of an unknown printer stays in the cache.
    /// </summary>
    private void ForgetLastFramesOfUnsavedPrinters()
    {
        if (LastFrames is not { } frames || _lastFramesWritten.Count == 0)
        {
            return;
        }

        foreach (string printerId in _lastFramesWritten)
        {
            if (!_baseline.Printers.Any(p => string.Equals(p.Id, printerId, StringComparison.OrdinalIgnoreCase)))
            {
                frames.Delete(printerId);
            }
        }

        _lastFramesWritten.Clear();
    }

    /// <summary>The Detection sub-tab of the selected printer may have become visible: its last frame is read once.</summary>
    private void OnDetectionTabMaybeShown()
    {
        if (_loaded && _selectedTabIndex == PrintersTab && _selectedPrinterTabIndex == DetectionPrinterTab)
        {
            _selectedPrinter?.OnDetectionTabShown();
        }
    }
}
