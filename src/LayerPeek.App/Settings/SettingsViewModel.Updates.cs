using CommunityToolkit.Mvvm.Input;

using LayerPeek.App.Resources;
using LayerPeek.Core.Updates;

namespace LayerPeek.App.Settings;

/// <summary>
/// The update check on the Startup and updates tab (T-058): "Check for updates automatically" (Q2 a, saved with the other
/// settings), the button "Check now" with its result next to it (Q4 a, like the test notification of T-101: up to date /
/// version X.Y.Z available / the check failed), and on macOS the hint to move the app to Applications first (Q8 a).
/// </summary>
public sealed partial class SettingsViewModel
{
    private bool _checkForUpdates = true;
    private UpdateCheckResultKind _updateCheckResult;
    private string? _updateCheckVersion;
    private bool _isCheckingForUpdates;
    private AsyncRelayCommand? _checkForUpdatesNowCommand;

    private enum UpdateCheckResultKind
    {
        None,
        UpToDate,
        Available,
        Failed,
    }

    /// <summary>"Check for updates automatically" (T-058 Q2 a), on by default; applied by Save.</summary>
    public bool CheckForUpdates
    {
        get => _checkForUpdates;
        set => SetGeneral(ref _checkForUpdates, value);
    }

    /// <summary>
    /// The manual check of the update offer (set by the composition root); <see langword="null"/>: no check in this window
    /// (the button is hidden).
    /// </summary>
    internal Func<CancellationToken, Task<UpdateCheckResult>>? CheckForUpdatesNow { get; init; }

    /// <summary>
    /// macOS (Q8 a): the app runs translocated or from the <c>.dmg</c> and cannot update itself there; the tab says to move it
    /// to Applications first.
    /// </summary>
    public bool ShowsMoveToApplications { get; init; }

    /// <summary>The version of the running app, for "LayerPeek X.Y.Z is up to date."</summary>
    internal string RunningVersion => _productVersion;

    /// <summary>The button "Check now" is part of the tab.</summary>
    public bool ShowsCheckForUpdatesNow => CheckForUpdatesNow is not null;

    /// <summary>Checks for a new version now, also while automatic checks are off; disabled while it runs.</summary>
    public IAsyncRelayCommand CheckForUpdatesNowCommand => _checkForUpdatesNowCommand ??= new AsyncRelayCommand(CheckForUpdatesNowAsync, () => CanCheckForUpdates);

    /// <summary>A check is running.</summary>
    public bool IsCheckingForUpdates
    {
        get => _isCheckingForUpdates;
        private set
        {
            if (SetProperty(ref _isCheckingForUpdates, value))
            {
                _checkForUpdatesNowCommand?.NotifyCanExecuteChanged();
                OnPropertyChanged(nameof(UpdateCheckResultText));
                OnPropertyChanged(nameof(ShowsUpdateCheckResult));
            }
        }
    }

    /// <summary>
    /// "Checking…", or the result of the last check in the current language; <see langword="null"/> before the first one.
    /// </summary>
    public string? UpdateCheckResultText => _isCheckingForUpdates
        ? Strings.SettingsUpdateChecking
        : _updateCheckResult switch
        {
            UpdateCheckResultKind.UpToDate => Format(Strings.SettingsUpdateUpToDate, _productVersion),
            UpdateCheckResultKind.Available => Format(Strings.SettingsUpdateAvailable, _updateCheckVersion),
            UpdateCheckResultKind.Failed => Strings.SettingsUpdateFailed,
            _ => null,
        };

    /// <summary>The text next to the button is shown.</summary>
    public bool ShowsUpdateCheckResult => UpdateCheckResultText is not null;

    /// <summary>The result is a failure (red); the others are neutral.</summary>
    public bool UpdateCheckResultIsError => !_isCheckingForUpdates && _updateCheckResult == UpdateCheckResultKind.Failed;

    private bool CanCheckForUpdates => CheckForUpdatesNow is not null && !_isCheckingForUpdates && !_disposed;

    /// <summary>The result in the new language.</summary>
    private void RefreshUpdateTexts() => OnPropertyChanged(nameof(UpdateCheckResultText));

    private async Task CheckForUpdatesNowAsync()
    {
        if (CheckForUpdatesNow is not { } check || !CanCheckForUpdates)
        {
            return;
        }

        IsCheckingForUpdates = true;
        UpdateCheckResultKind kind;
        string? version = null;
        try
        {
            UpdateCheckResult result = await check(LifetimeToken).ConfigureAwait(true);
            (kind, version) = result.Outcome switch
            {
                UpdateCheckOutcome.UpToDate => (UpdateCheckResultKind.UpToDate, null),
                UpdateCheckOutcome.UpdateAvailable when result.Release is { } release => (UpdateCheckResultKind.Available, release.Version.ToString()),
                _ => (UpdateCheckResultKind.Failed, (string?)null),
            };
        }
        catch (OperationCanceledException)
        {
            // The window closed.
            return;
        }
        catch (Exception)
        {
            // Never seen: the offer ends every failure as a result.
            kind = UpdateCheckResultKind.Failed;
        }
        finally
        {
            if (!_disposed)
            {
                IsCheckingForUpdates = false;
            }
        }

        if (_disposed)
        {
            return;
        }

        _updateCheckResult = kind;
        _updateCheckVersion = version;
        OnPropertyChanged(nameof(UpdateCheckResultText));
        OnPropertyChanged(nameof(ShowsUpdateCheckResult));
        OnPropertyChanged(nameof(UpdateCheckResultIsError));
    }
}
