using System.Globalization;
using System.Reflection;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using PrusaTrayDiagnostic.App.Licenses;
using PrusaTrayDiagnostic.App.Localization;
using PrusaTrayDiagnostic.App.Platform;
using PrusaTrayDiagnostic.App.Resources;
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
    public const string LicenseName = "Apache License 2.0";

    private readonly IUrlLauncher _launcher;
    private readonly string _version;

    // The licences window opened from this one (at most one is open; a closed one is harmless to refresh).
    private LicensesViewModel? _licenses;

    internal AboutViewModel(IUrlLauncher launcher)
        : this(launcher, typeof(AboutViewModel).Assembly)
    {
    }

    internal AboutViewModel(IUrlLauncher launcher, Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(launcher);
        ArgumentNullException.ThrowIfNull(assembly);
        _launcher = launcher;
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
    }

    private static string Format(string format, object? arg0) =>
        string.Format(CultureInfo.CurrentCulture, format, arg0);

    /// <summary>Opens the project page in the browser (the launcher validates the address and logs failures).</summary>
    [RelayCommand]
    private void OpenRepository() => _ = _launcher.OpenAsync(RepositoryUri);
}
