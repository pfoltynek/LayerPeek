using Avalonia.Media;

using CommunityToolkit.Mvvm.Input;

using LayerPeek.App.Resources;
using LayerPeek.Core.Cameras;
using LayerPeek.Core.Detection;
using LayerPeek.Core.Printers;

namespace LayerPeek.App.Settings;

/// <summary>
/// The Detection sub-tab of the printer (T-154, user decisions 2026-11-04): whether its prints are watched, the bed region
/// and every detector kind with its sensitivity (T-153 <see cref="PrinterDetectionSettings"/>). Off by default. The fields are
/// disabled (values kept) while the camera is off; the global switch is on the Recording tab (its state is a hint here).
/// <para>
/// <b>Region:</b> the editor shows the snapshot of the last camera test of this window (the Camera sub-tab or "Load frame"
/// here), otherwise the last frame saved for the printer (<see cref="ILastCameraFrames"/>, read once when the sub-tab is
/// first shown for the printer). A successful test stores its frame as the new last frame. A region that was never edited
/// stays unset and follows the model (<see cref="DetectionRegion.DefaultFor(PrinterImageChoice, PrinterModelCode?)"/>);
/// the first edit makes it the printer's own. "Whole frame" sets the whole frame, "Default for Core One" (Core One only) the
/// region of its standard camera mount.
/// </para>
/// </summary>
public sealed partial class PrinterEditorViewModel
{
    private bool _detectionEnabled;
    private DetectionRegion? _detectionRegion;
    private List<DetectorSettingsViewModel> _detectors = [];

    // Settings of detectors this version does not offer (a newer version wrote them): kept as they were.
    private Dictionary<string, DetectorSettings> _otherDetectors = new(StringComparer.Ordinal);

    // The last saved frame (T-154): loaded at most once per window, owned here (a bitmap, disposed when replaced or detached).
    private IImage? _lastFrame;
    private bool _lastFrameRequested;

    /// <summary>Watch the prints of this printer (with the global switch and the camera on).</summary>
    public bool DetectionEnabled
    {
        get => _detectionEnabled;
        set
        {
            if (SetProperty(ref _detectionEnabled, value))
            {
                _owner.OnPrinterEdited(nameChanged: false);
            }
        }
    }

    /// <summary>The detector kinds of this version, each with this printer's settings.</summary>
    public IReadOnlyList<DetectorSettingsViewModel> Detectors => _detectors;

    public bool HasDetectors => _detectors.Count > 0;

    /// <summary>The bed region shown and edited: the printer's own, or the default of its model while it has none.</summary>
    public DetectionRegion DetectionRegion
    {
        get => _detectionRegion ?? DefaultDetectionRegion;
        set
        {
            // Every edit makes the region the printer's own (also one equal to the default).
            DetectionRegion edited = value == default ? DetectionRegion.Full : value;
            if (_detectionRegion == edited)
            {
                return;
            }

            _detectionRegion = edited;
            OnDetectionRegionChanged();
            _owner.OnPrinterEdited(nameChanged: false);
        }
    }

    /// <summary>"Default for Core One" is offered: the printer is a Core One (its chosen image model, else the detected one).</summary>
    public bool IsCoreOne => DetectionRegion.IsCoreOne(_imageChoice, EffectiveDetectedModel);

    /// <summary>"Whole frame".</summary>
    public IRelayCommand DetectionFullFrameCommand { get; private set; } = null!;

    /// <summary>"Default for Core One".</summary>
    public IRelayCommand DetectionCoreOneCommand { get; private set; } = null!;

    /// <summary>The frame under the region: this window's camera snapshot, else the last saved frame; <see langword="null"/>: black.</summary>
    public IImage? RegionEditorImage => _cameraPreview ?? _lastFrame;

    public bool HasRegionEditorImage => RegionEditorImage is not null;

    /// <summary>The editor shows the last saved frame (the caption says so: the camera may have moved since).</summary>
    public bool ShowsLastSavedFrame => _cameraPreview is null && _lastFrame is not null;

    /// <summary>The camera is off: the sub-tab says so, its fields are disabled.</summary>
    public bool ShowsDetectionCameraOff => !_cameraEnabled;

    /// <summary>The camera is on, but the global switch (Recording tab) is off.</summary>
    public bool ShowsDetectionGloballyOff => _cameraEnabled && !_owner.DetectionGloballyEnabled;

    /// <summary>Neither of the two: the general hint (when detection runs, the image stays on this computer).</summary>
    public bool ShowsDetectionRunsHint => _cameraEnabled && _owner.DetectionGloballyEnabled;

    /// <summary>For screen readers: the editor with the region in whole percent of the frame.</summary>
    public string DetectionRegionName
    {
        get
        {
            DetectionRegion region = DetectionRegion;
            return Format(
                Strings.SettingsDetectionRegionName,
                Percent(region.X),
                Percent(region.Y),
                Percent(region.Width),
                Percent(region.Height));
        }
    }

    /// <summary>The automation name of the sub-tab (no field of it can be invalid).</summary>
    public static string DetectionTabName => Strings.SettingsPrinterTabDetection;

    private DetectionRegion DefaultDetectionRegion => DetectionRegion.DefaultFor(_imageChoice, EffectiveDetectedModel);

    /// <summary>The detection settings as edited (always valid).</summary>
    internal PrinterDetectionSettings BuildDetection()
    {
        Dictionary<string, DetectorSettings> detectors = new(_otherDetectors, StringComparer.Ordinal);
        foreach (DetectorSettingsViewModel detector in _detectors)
        {
            // Only what differs from the detector's own default is kept (T-167: a detector may be off by default).
            DetectorSettings settings = detector.ToSettings();
            if (settings == detector.Registration.DefaultSettings)
            {
                detectors.Remove(detector.Id);
            }
            else
            {
                detectors[detector.Id] = settings;
            }
        }

        return new PrinterDetectionSettings { Enabled = _detectionEnabled, Region = _detectionRegion, Detectors = detectors };
    }

    /// <summary>
    /// The Detection sub-tab is shown for this printer: the last saved frame is read once (off the UI thread) unless this
    /// window already has a snapshot of the camera.
    /// </summary>
    internal void OnDetectionTabShown()
    {
        if (_lastFrameRequested || _owner.LastFrames is not { } frames || _owner.IsDisposed)
        {
            return;
        }

        _lastFrameRequested = true;
        _ = LoadLastFrameAsync(frames);
    }

    /// <summary>From the constructor: the saved detection settings and the detectors of this version.</summary>
    private void InitializeDetection(PrinterDefinition? saved)
    {
        PrinterDetectionSettings detection = saved?.Detection ?? PrinterDetectionSettings.Default;
        _detectionEnabled = detection.Enabled;
        _detectionRegion = detection.Region;
        _otherDetectors = new Dictionary<string, DetectorSettings>(detection.Detectors, StringComparer.Ordinal);
        List<DetectorSettingsViewModel> detectors = new(_owner.DetectorRegistrations.Count);
        foreach (PrintFailureDetectorRegistration registration in _owner.DetectorRegistrations)
        {
            _otherDetectors.Remove(registration.Id);
            detectors.Add(new DetectorSettingsViewModel(registration, detection.For(registration), () => _owner.OnPrinterEdited(nameChanged: false)));
        }

        _detectors = detectors;
        UpdateOfferedDetectors();
        DetectionFullFrameCommand = new RelayCommand(() => DetectionRegion = DetectionRegion.Full, () => _owner.IsEditable);
        DetectionCoreOneCommand = new RelayCommand(() => DetectionRegion = DetectionRegion.CoreOneBed, () => _owner.IsEditable);
    }

    /// <summary>The model (image choice or detected) changed: an unset region follows it, "Default for Core One" too.</summary>
    private void OnDetectionModelChanged()
    {
        OnPropertyChanged(nameof(IsCoreOne));
        UpdateOfferedDetectors();
        if (_detectionRegion is null)
        {
            OnDetectionRegionChanged();
        }
    }

    /// <summary>The detectors offered for the model (image choice, else the detected one) are shown, the others hidden (T-167).</summary>
    private void UpdateOfferedDetectors()
    {
        PrinterModel model = DetectionRegion.ModelOf(_imageChoice, EffectiveDetectedModel);
        foreach (DetectorSettingsViewModel detector in _detectors)
        {
            detector.IsOffered = detector.Registration.IsOfferedFor(model);
        }
    }

    private void OnDetectionRegionChanged()
    {
        OnPropertyChanged(nameof(DetectionRegion));
        OnPropertyChanged(nameof(DetectionRegionName));
    }

    /// <summary>The camera switch or the global switch changed: the hint of the sub-tab follows.</summary>
    internal void OnDetectionStateChanged()
    {
        OnPropertyChanged(nameof(ShowsDetectionCameraOff));
        OnPropertyChanged(nameof(ShowsDetectionGloballyOff));
        OnPropertyChanged(nameof(ShowsDetectionRunsHint));
    }

    /// <summary>The camera snapshot or the last frame changed: the editor's frame follows.</summary>
    private void OnRegionEditorImageChanged()
    {
        OnPropertyChanged(nameof(RegionEditorImage));
        OnPropertyChanged(nameof(HasRegionEditorImage));
        OnPropertyChanged(nameof(ShowsLastSavedFrame));
    }

    /// <summary>A successful camera test: its frame is the new last frame of the printer (written on the thread pool).</summary>
    private void SaveLastFrame(CameraImage image)
    {
        if (_owner.LastFrames is { } frames)
        {
            frames.Save(Id, image);
            _owner.OnLastFrameWritten(Id);
        }
    }

    private async Task LoadLastFrameAsync(ILastCameraFrames frames)
    {
        IImage? frame;
        try
        {
            frame = await frames.LoadAsync(Id, _owner.LifetimeToken).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (frame is null)
        {
            return;
        }

        if (_owner.IsDisposed || _detached)
        {
            (frame as IDisposable)?.Dispose();
            return;
        }

        SetLastFrame(frame);
    }

    private void SetLastFrame(IImage? frame)
    {
        IImage? old = _lastFrame;
        if (ReferenceEquals(old, frame))
        {
            return;
        }

        _lastFrame = frame;
        OnRegionEditorImageChanged();
        (old as IDisposable)?.Dispose();
    }

    /// <summary>The language changed: the detector texts and the editor's name.</summary>
    private void RefreshDetectionTexts()
    {
        foreach (DetectorSettingsViewModel detector in _detectors)
        {
            detector.RefreshTexts();
        }

        OnPropertyChanged(nameof(DetectionRegionName));
    }

    private void OnDetectionEditableChanged()
    {
        DetectionFullFrameCommand.NotifyCanExecuteChanged();
        DetectionCoreOneCommand.NotifyCanExecuteChanged();
    }

    private static int Percent(double share) => (int)Math.Round(share * 100, MidpointRounding.AwayFromZero);
}
