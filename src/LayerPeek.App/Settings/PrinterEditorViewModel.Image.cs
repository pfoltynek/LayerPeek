using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using LayerPeek.App.Printers;
using LayerPeek.App.Resources;
using LayerPeek.Core.Images;
using LayerPeek.Core.Printers;

namespace LayerPeek.App.Settings;

/// <summary>A model in the "Choose model" submenu of the printer image (T-039); the product name is the same in every language.</summary>
public sealed class PrinterModelOption : ObservableObject
{
    private bool _isChecked;

    internal PrinterModelOption(PrinterModel model, Action<PrinterModel> choose)
    {
        Model = model;
        Text = PrinterModels.GetProductName(model);
        Command = new RelayCommand(() => choose(model));
    }

    public PrinterModel Model { get; }

    public string Text { get; }

    public IRelayCommand Command { get; }

    /// <summary>The printer shows this model because the user chose it.</summary>
    public bool IsChecked
    {
        get => _isChecked;
        internal set => SetProperty(ref _isChecked, value);
    }

    /// <summary>Raises the change of <see cref="IsChecked"/> even when it stayed (the menu may have toggled its item).</summary>
    internal void Refresh() => OnPropertyChanged(nameof(IsChecked));
}

/// <summary>
/// The printer image (T-039): which picture the printer shows and the menu of the image button (automatic, a chosen model,
/// a custom image, removing it). The detected model belongs to the address and connection type it was detected for: a
/// different address or connection type in the form shows no detected model until a test or monitoring detects it again
/// (user decision 2026-10-20). A custom image chosen here stays in memory until the settings are saved; Cancel drops it.
/// </summary>
public sealed partial class PrinterEditorViewModel
{
    // The detected model and the printer it was detected for (address, connection type).
    private PrinterModelCode? _detectedModel;
    private string _detectedAddress = string.Empty;
    private ConnectionType _detectedConnectionType;

    private PrinterImageChoice _imageChoice = PrinterImageChoice.Automatic;

    // The stored custom image (loaded with the settings, or saved since) and a new one imported in this window.
    private byte[]? _storedCustomImage;
    private byte[]? _importedCustomImage;

    private bool _isImportingImage;
    private Func<string>? _imageErrorSource;
    private string? _imageError;
    private PrinterPicture _picture = PrinterPicture.Generic;
    private PrinterModelOption[] _modelOptions = [];

    /// <summary>What the image button, the printer list and (after saving) the tray show.</summary>
    public PrinterPicture Picture => _picture;

    /// <summary>Accessible name of the image button: "Printer image: MK4S (detected)".</summary>
    public string ImageName => Format(Strings.SettingsImageButtonName, ImageDescription());

    /// <summary>
    /// The automatic choice: "Automatic by model (MK4S)" / "(not detected yet)" for PrusaLink; "Generic illustration" for
    /// Prusa Connect, which detects no model (T-039 decision).
    /// </summary>
    public string AutomaticImageText
    {
        get
        {
            if (!IsPrusaLink)
            {
                return Strings.SettingsImageGeneric;
            }

            string model = EffectiveDetectedModel is not { } code ? Strings.SettingsImageNotDetected
                : PrinterModels.FromCode(code) is var detected && detected != PrinterModel.Unknown ? PrinterModels.GetProductName(detected)
                : Strings.SettingsImageDescriptionGeneric;
            return Format(Strings.SettingsImageAutomatic, model);
        }
    }

    public bool IsAutomaticImage => _imageChoice.Mode == PrinterImageMode.Automatic;

    public bool IsCustomImage => _imageChoice.Mode == PrinterImageMode.Custom;

    /// <summary>Every model a user can choose (<see cref="PrinterModels.Selectable"/>), in display order.</summary>
    public IReadOnlyList<PrinterModelOption> ModelOptions => _modelOptions;

    public IRelayCommand UseAutomaticImageCommand { get; private set; } = null!;

    /// <summary>Opens the file dialog and imports the chosen PNG/JPG (checked, downscaled, kept in memory until saved).</summary>
    public IAsyncRelayCommand ChooseCustomImageCommand { get; private set; } = null!;

    /// <summary>Back to the automatic image; the stored file is deleted after saving.</summary>
    public IRelayCommand RemoveCustomImageCommand { get; private set; } = null!;

    /// <summary>Why the chosen file could not be used; cleared by the next choice.</summary>
    public string? ImageError => _imageError;

    public bool HasImageError => _imageError is not null;

    public bool IsImportingImage
    {
        get => _isImportingImage;
        private set
        {
            if (SetProperty(ref _isImportingImage, value))
            {
                ChooseCustomImageCommand.NotifyCanExecuteChanged();
            }
        }
    }

    /// <summary>The detected model, when it was detected for the address and connection type in the form; saved with the printer.</summary>
    internal PrinterModelCode? EffectiveDetectedModel =>
        _detectedModel is { } code
        && _detectedConnectionType == _connectionType.Value
        && string.Equals(_detectedAddress.Trim(), _address.Trim(), StringComparison.OrdinalIgnoreCase)
            ? code
            : null;

    internal PrinterImageChoice ImageChoice => _imageChoice;

    /// <summary>A custom image of this printer is stored (loaded with the settings or saved since).</summary>
    internal bool HasStoredCustomImage => _storedCustomImage is not null;

    /// <summary>A custom image imported in this window and not saved yet (only while the custom image is chosen).</summary>
    internal byte[]? PendingImage => _imageChoice.Mode == PrinterImageMode.Custom ? _importedCustomImage : null;

    /// <summary>Called by the constructor.</summary>
    private void InitializeImage(PrinterDefinition? saved, byte[]? storedCustomImage)
    {
        _detectedModel = saved?.DetectedModel;
        _detectedAddress = saved?.Address ?? string.Empty;
        _detectedConnectionType = saved?.ConnectionType ?? ConnectionType.PrusaLink;
        _imageChoice = saved?.Image ?? PrinterImageChoice.Automatic;
        _storedCustomImage = storedCustomImage;
        UseAutomaticImageCommand = new RelayCommand(() => SetImageChoice(PrinterImageChoice.Automatic));
        ChooseCustomImageCommand = new AsyncRelayCommand(ChooseCustomImageAsync, () => !_isImportingImage && !_owner.IsDisposed && _owner.ImageFilePicker is not null);
        RemoveCustomImageCommand = new RelayCommand(() => SetImageChoice(PrinterImageChoice.Automatic), () => IsCustomImage);
        _modelOptions = [.. PrinterModels.Selectable.Select(model => new PrinterModelOption(model, ChooseModel))];
        UpdatePicture();
    }

    /// <summary>The address or connection type changed: the detected model may no longer apply.</summary>
    private void OnMachineChanged() => UpdatePicture();

    /// <summary>A successful connection test reported the model (or that the firmware reports none) for the tested values.</summary>
    private void OnModelTested(PrinterModelCode? code, PrinterDefinition tested)
    {
        if (_detectedModel == code
            && _detectedConnectionType == tested.ConnectionType
            && string.Equals(_detectedAddress.Trim(), tested.Address, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _detectedModel = code;
        _detectedAddress = tested.Address;
        _detectedConnectionType = tested.ConnectionType;
        UpdatePicture();
        _owner.OnPrinterEdited(nameChanged: false);
    }

    /// <summary>After a successful save: a new image is now the stored one; a removed one is gone.</summary>
    private void MarkImageSaved()
    {
        _storedCustomImage = _imageChoice.Mode == PrinterImageMode.Custom ? _importedCustomImage ?? _storedCustomImage : null;
        _importedCustomImage = null;
    }

    private void RefreshImageTexts()
    {
        if (_imageErrorSource is { } source)
        {
            _imageError = source();
            OnPropertyChanged(nameof(ImageError));
        }

        OnPropertyChanged(nameof(ImageName));
        OnPropertyChanged(nameof(AutomaticImageText));
    }

    private void ChooseModel(PrinterModel model) => SetImageChoice(PrinterImageChoice.ForModel(model));

    private void SetImageChoice(PrinterImageChoice choice)
    {
        if (_owner.IsDisposed)
        {
            return;
        }

        SetImageError(null);
        if (choice.Mode != PrinterImageMode.Custom)
        {
            _importedCustomImage = null;
        }

        _imageChoice = choice;
        UpdatePicture();
        _owner.OnPrinterEdited(nameChanged: false);
    }

    private async Task ChooseCustomImageAsync()
    {
        if (_isImportingImage || _owner.IsDisposed || _owner.ImageFilePicker is not { } picker)
        {
            return;
        }

        IsImportingImage = true;
        SetImageError(null);
        try
        {
            Stream? stream;
            try
            {
                stream = await picker.PickAsync(_owner.ImageStartDirectory).ConfigureAwait(true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                SetImageError(static () => Strings.SettingsImageErrorUnreadable);
                return;
            }

            if (stream is null)
            {
                // The user cancelled the dialog: nothing changes.
                return;
            }

            PrinterImageImportResult result;
            await using (stream.ConfigureAwait(true))
            {
                result = await _owner.ImageImporter.ImportAsync(stream, _owner.LifetimeToken).ConfigureAwait(true);
            }

            if (_owner.IsDisposed)
            {
                return;
            }

            if (result is { Status: PrinterImageImportStatus.Imported, Png: { } png })
            {
                _importedCustomImage = png;
                SetImageChoice(PrinterImageChoice.Custom);
            }
            else
            {
                SetImageError(ImportErrorText(result.Status));
            }
        }
        catch (OperationCanceledException) when (_owner.IsDisposed)
        {
            // The window closed during the import.
        }
        catch (Exception)
        {
            // A command handler must never throw into the UI; the path is not shown or logged.
            SetImageError(static () => Strings.SettingsImageErrorUndecodable);
        }
        finally
        {
            IsImportingImage = false;
            // The menu checked "Custom image…" (a radio item) when it was clicked; a cancelled or failed import must show the
            // choice that still applies.
            RefreshChoiceChecks();
        }
    }

    /// <summary>Tells the menu again which choice is checked (it toggles radio items itself when they are clicked).</summary>
    private void RefreshChoiceChecks()
    {
        OnPropertyChanged(nameof(IsAutomaticImage));
        OnPropertyChanged(nameof(IsCustomImage));
        foreach (PrinterModelOption option in _modelOptions)
        {
            option.Refresh();
        }
    }

    private static Func<string> ImportErrorText(PrinterImageImportStatus status) => status switch
    {
        PrinterImageImportStatus.TooLarge => static () => Format(Strings.SettingsImageErrorTooLarge, PrinterImageFormats.MaxSourceBytes / (1024 * 1024)),
        PrinterImageImportStatus.UnsupportedFormat => static () => Strings.SettingsImageErrorFormat,
        PrinterImageImportStatus.NotFound or PrinterImageImportStatus.Unreadable => static () => Strings.SettingsImageErrorUnreadable,
        _ => static () => Strings.SettingsImageErrorUndecodable,
    };

    /// <summary>The window shows the error in its status line (T-039 layout: the form has no room for another line).</summary>
    private void SetImageError(Func<string>? source)
    {
        Func<string>? previous = _imageErrorSource;
        _imageErrorSource = source;
        if (source is not null || previous is not null)
        {
            _owner.ShowImageError(previous, source);
        }

        string? text = source?.Invoke();
        if (_imageError == text)
        {
            return;
        }

        bool had = _imageError is not null;
        _imageError = text;
        OnPropertyChanged(nameof(ImageError));
        if (had != (text is not null))
        {
            OnPropertyChanged(nameof(HasImageError));
        }
    }

    private void UpdatePicture()
    {
        byte[]? custom = _imageChoice.Mode == PrinterImageMode.Custom ? _importedCustomImage ?? _storedCustomImage : null;
        PrinterPicture picture = PrinterPicture.For(_imageChoice, _connectionType.Value, EffectiveDetectedModel, custom);
        if (!picture.Equals(_picture))
        {
            _picture = picture;
            OnPropertyChanged(nameof(Picture));
        }

        OnPropertyChanged(nameof(ImageName));
        OnPropertyChanged(nameof(AutomaticImageText));
        OnPropertyChanged(nameof(IsAutomaticImage));
        OnPropertyChanged(nameof(IsCustomImage));
        foreach (PrinterModelOption option in _modelOptions)
        {
            option.IsChecked = _imageChoice.Mode == PrinterImageMode.Model && _imageChoice.Model == option.Model;
        }

        RemoveCustomImageCommand?.NotifyCanExecuteChanged();
    }

    private string ImageDescription() =>
        _picture.IsCustom ? Strings.SettingsImageDescriptionCustom
        : _imageChoice.Mode == PrinterImageMode.Model ? PrinterModels.GetProductName(_imageChoice.Model)
        : _picture.Image.Kind == PrinterImageKind.Model ? Format(Strings.SettingsImageDescriptionDetected, PrinterModels.GetProductName(_picture.Image.Model))
        : Strings.SettingsImageDescriptionGeneric;
}
