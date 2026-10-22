using System.Globalization;
using System.Reflection;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using PrusaTrayDiagnostic.App.Licenses;
using PrusaTrayDiagnostic.App.Localization;
using PrusaTrayDiagnostic.App.Platform;
using PrusaTrayDiagnostic.App.Resources;
using PrusaTrayDiagnostic.App.Support;
using PrusaTrayDiagnostic.App.Tray;

namespace PrusaTrayDiagnostic.App.Windows;

/// <summary>Content of the About window in the current language; <see cref="RefreshTexts"/> after a language switch.</summary>
public sealed partial class AboutViewModel : ObservableObject, ILanguageAware
{
    /// <summary>Project page opened by the repository link.</summary>
    public static readonly Uri RepositoryUri = new("https://github.com/pfoltynek/PrusaTrayDiagnostic");

    /// <summary>Used when the assembly carries no copyright attribute (it is generated from Directory.Build.props).</summary>
    internal const string DefaultCopyright = "Copyright © Petr Foltýnek";

    /// <summary>License of the project (<c>LICENSE</c> in the repository root); a name, not translated.</summary>
    public const string LicenseName = "GNU General Public License v3.0 only";

    private readonly IUrlLauncher _launcher;
    private readonly string _version;
    private readonly SupportActions? _support;

    // The outcome of the last support action, formatted again after a language switch; null: none shown.
    private Func<string>? _supportStatus;

    // The licences window opened from this one (at most one is open; a closed one is harmless to refresh).
    private LicensesViewModel? _licenses;

    /// <param name="support">The support actions (T-048, T-049); <see langword="null"/>: the buttons do nothing (designer, tests).</param>
    internal AboutViewModel(IUrlLauncher launcher, SupportActions? support = null)
        : this(launcher, typeof(AboutViewModel).Assembly, support)
    {
    }

    internal AboutViewModel(IUrlLauncher launcher, Assembly assembly, SupportActions? support = null)
    {
        ArgumentNullException.ThrowIfNull(launcher);
        ArgumentNullException.ThrowIfNull(assembly);
        _launcher = launcher;
        _support = support;
        _version = ProductVersion(assembly);
        Copyright = ProductCopyright(assembly);
        FormatTexts();
    }

    public string Title { get; private set; } = string.Empty;

    public string AppName { get; } = TrayToolTipFormatter.AppName;

    public string VersionText { get; private set; } = string.Empty;

    public string Description { get; private set; } = string.Empty;

    public string Disclaimer { get; private set; } = string.Empty;

    public string LicenseText { get; private set; } = string.Empty;

    /// <summary>Copyright notice from the assembly metadata (<c>Copyright</c> in Directory.Build.props); not translated.</summary>
    public string Copyright { get; }

    public string RepositoryText { get; private set; } = string.Empty;

    public string CloseText { get; private set; } = string.Empty;

    public string SupportHeading { get; private set; } = string.Empty;

    public string SaveBundleText { get; private set; } = string.Empty;

    public string SaveBundleTip { get; private set; } = string.Empty;

    public string OpenLogFolderText { get; private set; } = string.Empty;

    /// <summary>The outcome of the last support action (saving, saved, an error), or <see langword="null"/>.</summary>
    public string? SupportStatusText { get; private set; }

    public bool SupportStatusIsError { get; private set; }

    /// <summary>The save dialog of the bundle; set by the window (it owns the dialog).</summary>
    internal IBundleFilePicker? BundlePicker { get; set; }

    /// <summary>UI thread: the language changed; every text is formatted again, also in the licences window.</summary>
    public void RefreshTexts()
    {
        FormatTexts();
        OnPropertyChanged(string.Empty);
        _licenses?.RefreshTexts();
    }

    public string ThirdPartyLicensesText { get; private set; } = string.Empty;

    /// <summary>The user wants the third-party licences; the window opens (or activates) the licences window.</summary>
    public event EventHandler? LicensesRequested;

    /// <summary>Content of a new licences window (T-014); it follows language switches of this window (<see cref="RefreshTexts"/>).</summary>
    internal LicensesViewModel CreateLicensesViewModel() => _licenses = new(_launcher);

    [RelayCommand]
    private void ShowLicenses() => LicensesRequested?.Invoke(this, EventArgs.Empty);

    /// <summary><see cref="AssemblyCopyrightAttribute"/> of <paramref name="assembly"/>, or <see cref="DefaultCopyright"/>.</summary>
    internal static string ProductCopyright(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        // Attribute metadata only, like ProductVersion: safe under Native AOT.
        string? copyright = assembly.GetCustomAttribute<AssemblyCopyrightAttribute>()?.Copyright;
        return string.IsNullOrWhiteSpace(copyright) ? DefaultCopyright : copyright;
    }

    /// <summary>Informational version without the source revision suffix ("1.2.0+abc123" → "1.2.0").</summary>
    internal static string ProductVersion(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        // Attributes survive Native AOT; this reads assembly metadata, not members by name.
        string? informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(informational))
        {
            int plus = informational.IndexOf('+', StringComparison.Ordinal);
            return plus > 0 ? informational[..plus] : informational;
        }

        return assembly.GetName().Version?.ToString(3) ?? "?";
    }

    private void FormatTexts()
    {
        Title = Strings.AboutTitle;
        VersionText = Format(Strings.AboutVersion, _version);
        Description = Strings.AboutDescription;
        Disclaimer = Strings.AboutDisclaimer;
        LicenseText = Format(Strings.AboutLicense, LicenseName);
        RepositoryText = Strings.AboutRepository;
        CloseText = Strings.AboutClose;
        ThirdPartyLicensesText = Strings.AboutThirdPartyLicenses;
        SupportHeading = Strings.SupportHeading;
        SaveBundleText = Strings.SupportSaveBundle;
        SaveBundleTip = Strings.SupportSaveBundleTip;
        OpenLogFolderText = Strings.SupportOpenLogFolder;
        SupportStatusText = _supportStatus?.Invoke();
    }

    /// <summary>
    /// "Save diagnostic bundle…" (T-048): the save dialog, then the ZIP off the UI thread. A cancelled dialog creates
    /// nothing; a closed window lets the save finish (its outcome is logged). Runs once at a time (the command is disabled
    /// meanwhile).
    /// </summary>
    [RelayCommand]
    private async Task SaveBundleAsync()
    {
        if (_support is null || BundlePicker is not { } picker)
        {
            return;
        }

        IBundleTarget? target;
        try
        {
            target = await picker.PickAsync(_support.DefaultBundleFileName()).ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            ShowSupportStatus(static () => Strings.SupportBundleFailed, isError: true);
            return;
        }

        if (target is null)
        {
            return;
        }

        await using (target.ConfigureAwait(true))
        {
            ShowSupportStatus(static () => Strings.SupportBundleSaving, isError: false);
            bool saved = await _support.SaveBundleAsync(target).ConfigureAwait(true);
            ShowSupportStatus(saved ? static () => Strings.SupportBundleSaved : static () => Strings.SupportBundleFailed, isError: !saved);
        }
    }

    /// <summary>"Open log folder" (T-048): Explorer / Finder with the log folder, created when missing.</summary>
    [RelayCommand]
    private async Task OpenLogFolderAsync()
    {
        if (_support is null)
        {
            return;
        }

        if (!SaveBundleCommand.IsRunning)
        {
            // A running save keeps its "Saving…" until its own outcome replaces it.
            ShowSupportStatus(null, isError: false);
        }

        if (!await _support.OpenLogFolderAsync().ConfigureAwait(true))
        {
            ShowSupportStatus(static () => Strings.SupportLogFolderFailed, isError: true);
        }
    }

    /// <summary>Shows the outcome of a support action (localized, formatted again after a language switch); <see langword="null"/> clears it.</summary>
    internal void ShowSupportStatus(Func<string>? status, bool isError)
    {
        _supportStatus = status;
        SupportStatusText = status?.Invoke();
        SupportStatusIsError = isError;
        OnPropertyChanged(nameof(SupportStatusText));
        OnPropertyChanged(nameof(SupportStatusIsError));
    }

    private static string Format(string format, object? arg0) =>
        string.Format(CultureInfo.CurrentCulture, format, arg0);

    /// <summary>Opens the project page in the browser (the launcher validates the address and logs failures).</summary>
    [RelayCommand]
    private void OpenRepository() => _ = _launcher.OpenAsync(RepositoryUri);
}
