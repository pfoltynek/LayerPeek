using System.Globalization;
using System.Runtime.InteropServices;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using PrusaTrayDiagnostic.App.Platform;
using PrusaTrayDiagnostic.App.Resources;
using PrusaTrayDiagnostic.Camera.FFmpeg;

namespace PrusaTrayDiagnostic.App.Licenses;

/// <summary>One component in the third-party licences window; its full text is read when the entry is first expanded.</summary>
public sealed partial class LicenseItemViewModel : ObservableObject
{
    private readonly ThirdPartyComponent _component;
    private readonly IUrlLauncher _launcher;
    private bool _isExpanded;
    private string? _fullText;

    internal LicenseItemViewModel(ThirdPartyComponent component, IUrlLauncher launcher)
    {
        _component = component;
        _launcher = launcher;
        VersionText = Format(Strings.LicensesVersion, component.Version ?? RuntimeInformation.FrameworkDescription);
        LicenseText = Format(Strings.LicensesLicense, component.License);
    }

    public string Name => _component.Name;

    public string VersionText { get; }

    public string LicenseText { get; }

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

    private string ReadText()
    {
        try
        {
            return ThirdPartyLicenses.ReadText(_component);
        }
        catch (InvalidOperationException)
        {
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
public sealed partial class LicensesViewModel : ObservableObject
{
    private readonly IUrlLauncher _launcher;
    private readonly Func<string> _ffmpegVersion;
    private readonly Func<string?> _ffmpegLicenseText;
    private string _ffmpegVersionText = Strings.LicensesFFmpegLoading;
    private string? _ffmpegFullText;
    private bool _loadStarted;

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
    }

    public string Title { get; } = Strings.LicensesTitle;

    public string Intro { get; } = Strings.LicensesIntro;

    public string FFmpegHeader { get; } = Strings.LicensesFFmpegHeader;

    public string FFmpegLicenseText { get; } = Format(Strings.LicensesLicense, ThirdPartyLicenses.FFmpeg.License);

    public string FFmpegNote { get; } = Strings.LicensesFFmpegNote;

    /// <summary>Versions of the loaded FFmpeg libraries, "detecting…" before <see cref="LoadAsync"/>, or that FFmpeg is missing.</summary>
    public string FFmpegVersionText
    {
        get => _ffmpegVersionText;
        private set => SetProperty(ref _ffmpegVersionText, value);
    }

    /// <summary>The shipped FFmpeg licence (or where to find it); <see langword="null"/> before <see cref="LoadAsync"/>.</summary>
    public string? FFmpegFullText
    {
        get => _ffmpegFullText;
        private set => SetProperty(ref _ffmpegFullText, value);
    }

    public string LicenseTextHeader { get; } = Strings.LicensesShowText;

    public string ProjectText { get; } = Strings.LicensesProject;

    public string FFmpegSourceText { get; } = Strings.LicensesFFmpegSource;

    public string FFmpegBuildText { get; } = Strings.LicensesFFmpegBuild;

    public string FFmpegBuildScriptsText { get; } = Strings.LicensesFFmpegBuildScripts;

    public string CloseText { get; } = Strings.AboutClose;

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
        (string version, string? text) = await Task.Run(() =>
        {
            string version;
            try
            {
                version = _ffmpegVersion();
            }
            catch (Exception)
            {
                // Missing or broken native libraries: the camera reports it too; here it is just information.
                version = Strings.LicensesFFmpegMissing;
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
        FFmpegVersionText = version;
        FFmpegFullText = string.IsNullOrWhiteSpace(text) ? Strings.LicensesFFmpegLicenseMissing : text;
    }

    private static string Format(string format, object? arg0) => string.Format(CultureInfo.CurrentCulture, format, arg0);

    [RelayCommand]
    private void OpenFFmpegSource() => _ = _launcher.OpenAsync(ThirdPartyLicenses.FFmpeg.SourceUri);

    [RelayCommand]
    private void OpenFFmpegBuild() => _ = _launcher.OpenAsync(ThirdPartyLicenses.FFmpeg.BuildUri);

    [RelayCommand]
    private void OpenFFmpegBuildScripts() => _ = _launcher.OpenAsync(ThirdPartyLicenses.FFmpeg.BuildScriptsUri);
}
