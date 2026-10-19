using System.Globalization;
using System.Runtime.InteropServices;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using PrusaTrayDiagnostic.App.Localization;
using PrusaTrayDiagnostic.App.Platform;
using PrusaTrayDiagnostic.App.Resources;
using PrusaTrayDiagnostic.Camera.FFmpeg;

namespace PrusaTrayDiagnostic.App.Licenses;

/// <summary>One component in the third-party licences window; its full text is read when the entry is first expanded.</summary>
public sealed partial class LicenseItemViewModel : ObservableObject, ILanguageAware
{
    private readonly ThirdPartyComponent _component;
    private readonly IUrlLauncher _launcher;
    private bool _isExpanded;
    private string? _fullText;

    // The text could not be read: the "unavailable" notice is shown (in the current language).
    private bool _textUnavailable;

    internal LicenseItemViewModel(ThirdPartyComponent component, IUrlLauncher launcher)
    {
        _component = component;
        _launcher = launcher;
        FormatTexts();
    }

    /// <summary>The product name, with its translated description where it has one (<see cref="ThirdPartyComponent.DisplayNameKey"/>).</summary>
    public string Name { get; private set; } = string.Empty;

    public string VersionText { get; private set; } = string.Empty;

    public string LicenseText { get; private set; } = string.Empty;

    public string ProjectUrl => _component.ProjectUri.AbsoluteUri;

    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (SetProperty(ref _isExpanded, value) && value && _fullText is null)
            {
                FullText = ReadText();
            }
        }
    }

    /// <summary>The licence and notice texts; <see langword="null"/> until the entry was expanded.</summary>
    public string? FullText
    {
        get => _fullText;
        private set => SetProperty(ref _fullText, value);
    }

    /// <summary>UI thread: the language changed; the name, version, licence and a "text unavailable" notice are formatted again.</summary>
    public void RefreshTexts()
    {
        FormatTexts();
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(VersionText));
        OnPropertyChanged(nameof(LicenseText));
        if (_textUnavailable)
        {
            FullText = Strings.LicensesTextUnavailable;
        }
    }

    private void FormatTexts()
    {
        Name = _component.DisplayNameKey is { } key ? Strings.ResourceManager.GetString(key, Strings.Culture) ?? _component.Name : _component.Name;
        VersionText = Format(Strings.LicensesVersion, _component.Version ?? RuntimeInformation.FrameworkDescription);
        LicenseText = Format(Strings.LicensesLicense, _component.License);
    }

    private string ReadText()
    {
        try
        {
            return ThirdPartyLicenses.ReadText(_component);
        }
        catch (InvalidOperationException)
        {
            _textUnavailable = true;
            return Strings.LicensesTextUnavailable;
        }
    }

    private static string Format(string format, object? arg0) => string.Format(CultureInfo.CurrentCulture, format, arg0);

    /// <summary>Opens the project page through the launcher (validated http(s), failures logged).</summary>
    [RelayCommand]
    private void OpenProject() => _ = _launcher.OpenAsync(_component.ProjectUri);
}

/// <summary>
/// The third-party licences window (opened from About, T-014): FFmpeg (LGPL, dynamically linked, replaceable, with links
/// to its source and the exact build used) and every other shipped component from <see cref="ThirdPartyLicenses"/>.
/// The FFmpeg version is read off the UI thread (it loads the native libraries); a missing FFmpeg is shown, not thrown.
/// UI thread only.
/// </summary>
public sealed partial class LicensesViewModel : ObservableObject, ILanguageAware
{
    private readonly IUrlLauncher _launcher;
    private readonly Func<string> _ffmpegVersion;
    private readonly Func<string?> _ffmpegLicenseText;
    private bool _loadStarted;

    // What LoadAsync found; the texts are formatted from it in the current language (T-029).
    private bool _loaded;
    private string? _loadedVersion;
    private string? _loadedLicenseText;

    internal LicensesViewModel(IUrlLauncher launcher)
        : this(launcher, static () => FFmpegCameraFeedFactory.LibraryVersion, ReadShippedFFmpegLicense)
    {
    }

    /// <param name="ffmpegVersion">Version and licence of the loaded FFmpeg; throws when FFmpeg is missing.</param>
    /// <param name="ffmpegLicenseText">The shipped FFmpeg licence text; <see langword="null"/> when it is missing.</param>
    internal LicensesViewModel(IUrlLauncher launcher, Func<string> ffmpegVersion, Func<string?> ffmpegLicenseText)
    {
        ArgumentNullException.ThrowIfNull(launcher);
        ArgumentNullException.ThrowIfNull(ffmpegVersion);
        ArgumentNullException.ThrowIfNull(ffmpegLicenseText);
        _launcher = launcher;
        _ffmpegVersion = ffmpegVersion;
        _ffmpegLicenseText = ffmpegLicenseText;
        Components = [.. ThirdPartyLicenses.All.Select(component => new LicenseItemViewModel(component, launcher))];
        FormatTexts();
    }

    public string Title { get; private set; } = string.Empty;

    public string Intro { get; private set; } = string.Empty;

    public string FFmpegHeader { get; private set; } = string.Empty;

    public string FFmpegLicenseText { get; private set; } = string.Empty;

    public string FFmpegNote { get; private set; } = string.Empty;

    /// <summary>Versions of the loaded FFmpeg libraries, "detecting…" before <see cref="LoadAsync"/>, or that FFmpeg is missing.</summary>
    public string FFmpegVersionText =>
        !_loaded ? Strings.LicensesFFmpegLoading
        : _loadedVersion ?? Strings.LicensesFFmpegMissing;

    /// <summary>The shipped FFmpeg licence (or where to find it); <see langword="null"/> before <see cref="LoadAsync"/>.</summary>
    public string? FFmpegFullText =>
        !_loaded ? null
        : _loadedLicenseText ?? Strings.LicensesFFmpegLicenseMissing;

    public string LicenseTextHeader { get; private set; } = string.Empty;

    public string ProjectText { get; private set; } = string.Empty;

    public string FFmpegSourceText { get; private set; } = string.Empty;

    public string FFmpegBuildText { get; private set; } = string.Empty;

    public string FFmpegBuildScriptsText { get; private set; } = string.Empty;

    public string CloseText { get; private set; } = string.Empty;

    /// <summary>
    /// UI thread: the language changed (T-029); every text of the window and of its components is formatted again (the
    /// FFmpeg version is not read again).
    /// </summary>
    public void RefreshTexts()
    {
        foreach (LicenseItemViewModel component in Components)
        {
            component.RefreshTexts();
        }

        FormatTexts();
        OnPropertyChanged(string.Empty);
    }

    private void FormatTexts()
    {
        Title = Strings.LicensesTitle;
        Intro = Strings.LicensesIntro;
        FFmpegHeader = Strings.LicensesFFmpegHeader;
        FFmpegLicenseText = Format(Strings.LicensesLicense, ThirdPartyLicenses.FFmpeg.License);
        FFmpegNote = Strings.LicensesFFmpegNote;
        LicenseTextHeader = Strings.LicensesShowText;
        ProjectText = Strings.LicensesProject;
        FFmpegSourceText = Strings.LicensesFFmpegSource;
        FFmpegBuildText = Strings.LicensesFFmpegBuild;
        FFmpegBuildScriptsText = Strings.LicensesFFmpegBuildScripts;
        CloseText = Strings.AboutClose;
    }

    public IReadOnlyList<LicenseItemViewModel> Components { get; }

    /// <summary><c>FFmpeg-LICENSE.txt</c> next to the application; <see langword="null"/> when it is missing or unreadable.</summary>
    internal static string? ReadShippedFFmpegLicense()
    {
        try
        {
            return File.ReadAllText(Path.Combine(AppContext.BaseDirectory, ThirdPartyLicenses.FFmpeg.LicenseFileName));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Reads the FFmpeg version and licence text on the thread pool; call once after the window opened. Never throws.</summary>
    public async Task LoadAsync()
    {
        if (_loadStarted)
        {
            return;
        }

        _loadStarted = true;
        (string? version, string? text) = await Task.Run(() =>
        {
            string? version;
            try
            {
                version = _ffmpegVersion();
            }
            catch (Exception)
            {
                // Missing or broken native libraries: the camera reports it too; here it is just information.
                version = null;
            }

            string? text;
            try
            {
                text = _ffmpegLicenseText();
            }
            catch (Exception)
            {
                text = null;
            }

            return (version, text);
        }).ConfigureAwait(true);
        _loaded = true;
        _loadedVersion = version;
        _loadedLicenseText = string.IsNullOrWhiteSpace(text) ? null : text;
        OnPropertyChanged(nameof(FFmpegVersionText));
        OnPropertyChanged(nameof(FFmpegFullText));
    }

    private static string Format(string format, object? arg0) => string.Format(CultureInfo.CurrentCulture, format, arg0);

    [RelayCommand]
    private void OpenFFmpegSource() => _ = _launcher.OpenAsync(ThirdPartyLicenses.FFmpeg.SourceUri);

    [RelayCommand]
    private void OpenFFmpegBuild() => _ = _launcher.OpenAsync(ThirdPartyLicenses.FFmpeg.BuildUri);

    [RelayCommand]
    private void OpenFFmpegBuildScripts() => _ = _launcher.OpenAsync(ThirdPartyLicenses.FFmpeg.BuildScriptsUri);
}
