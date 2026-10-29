<#
.SYNOPSIS
    Native AOT smoke check: publishes the app, starts the native executable and verifies it keeps running.

.DESCRIPTION
    Catches failures that only show up in the AOT-published binary (e.g. missing compiled XAML),
    which dotnet build / dotnet test cannot detect. Publishes to a temporary directory outside the
    repository; the process and the directory are always cleaned up. With -PublishDir it checks an existing publish
    folder instead (used by scripts/release-build.ps1) and leaves that folder as it is.
    On Windows it also checks that the app created its Start menu shortcut for notifications (T-010)
    and restores the previous shortcut (or removes it) afterwards.
    The Release exe has no test mode, so it would write into the real data and log folders of the app
    (macOS ~/Library/Application Support/LayerPeek and ~/Library/Logs/LayerPeek, Windows %APPDATA%\LayerPeek and
    %LOCALAPPDATA%\LayerPeek\logs) and on macOS into what the system keeps under the bundle identifier (T-019:
    ~/Library/Saved Application State/<id>.savedState, ~/Library/Caches/<id>, ~/Library/HTTPStorages/<id> and the defaults
    domains <id> and LayerPeek in ~/Library/Preferences). Right before the exe starts, the script writes a journal and moves
    the existing folders aside (renamed to <folder>.aot-smoke-backup next to them, T-095) and exports and deletes the
    existing defaults domains (through defaults, never as files: cfprefsd owns them); afterwards it deletes what the exe
    created and moves or imports them back. The app therefore starts with an empty profile and the user's data is never
    touched. After a crash of the script the next run (or -RestoreOnly) finishes the restore first.
    On macOS it also checks that the app runs without a Dock icon (LaunchServices type UIElement, T-019), and when the exe
    is the one inside LayerPeek.app (-PublishDir <app>/Contents/MacOS), it removes the LaunchServices registration of the
    bundle that the start created (only when the bundle was not registered before).
    Toasts the app shows during the run are cleared afterwards only when the notification center provably held no
    toasts of the app before the run and no other instance started meanwhile; otherwise they are left there (the
    user's own toasts are never removed) and a warning is printed. Refuses to run while LayerPeek is already running.

.PARAMETER Runtime
    Runtime identifier (win-x64, win-arm64, osx-arm64). Defaults to the current OS/architecture.

.PARAMETER PublishDir
    Existing Native AOT publish folder to check instead of publishing. The folder is not modified or deleted
    (the app's output is captured in a separate temporary folder).

.PARAMETER Seconds
    How long the app must stay alive after start. Default 10.

.PARAMETER SkipShellIntegration
    For automated tests of this script only (scripts/ReleaseTools/ReleaseTools.Tests.ps1): skips the running-instance
    check, the Start menu shortcut check and its backup/restore, and the toast history handling, so that nothing of the
    real user profile is read or changed. Requires -ProfileRoot. A real smoke check must not use it.

.PARAMETER RestoreOnly
    Only finishes the restore of an interrupted smoke run (moves the backed up data and log folders back, deletes what
    the exe created); publishes and starts nothing.

.PARAMETER ProfileRoot
    For automated tests of this script only: a stand-in profile folder whose data/LayerPeek and logs/LayerPeek take the
    place of the real data and log folders of the app.

.EXAMPLE
    pwsh scripts/aot-smoke.ps1
    pwsh scripts/aot-smoke.ps1 -Runtime osx-arm64 -Seconds 15
    pwsh scripts/aot-smoke.ps1 -PublishDir artifacts/release/0.1.0/publish/win-x64
    pwsh scripts/aot-smoke.ps1 -RestoreOnly
#>
#Requires -Version 7.2
[CmdletBinding()]
param(
    [string] $Runtime,
    [string] $PublishDir,
    [ValidateRange(1, 600)] [int] $Seconds = 10,
    [switch] $SkipShellIntegration,
    [switch] $RestoreOnly,
    [string] $ProfileRoot
)

$ErrorActionPreference = 'Stop'
# Arguments of every smoke start of the app (T-020): skip the notification center of macOS for this run.
$SmokeAppArguments = @('--layerpeek-smoke-skip-notification-center')

if (-not $Runtime) {
    $os = if ($IsWindows) { 'win' } elseif ($IsMacOS) { 'osx' } else { throw 'Unsupported OS; pass -Runtime explicitly.' }
    $arch = [System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString().ToLowerInvariant()
    $Runtime = "$os-$arch"
}

# A running instance of the app (there is no single-instance guard) would share the Start menu shortcut and the
# notification history with the smoke run, and its toasts could be cleared below: refuse instead.
if (-not $SkipShellIntegration -and (Get-Process -Name 'LayerPeek' -ErrorAction SilentlyContinue)) {
    Write-Host 'AOT smoke check NOT RUN: LayerPeek is already running. Quit it and run the check again.' -ForegroundColor Red
    exit 1
}
# The tests of this script must never move the real data of the app.
if ($SkipShellIntegration -and -not $ProfileRoot) {
    Write-Host 'AOT smoke check NOT RUN: -SkipShellIntegration (tests) needs a stand-in -ProfileRoot.' -ForegroundColor Red
    exit 1
}

Import-Module (Join-Path $PSScriptRoot 'ReleaseTools' 'ReleaseTools.psm1') -Force
# The data and log folders of the app: moved aside while the Release exe (no test mode) runs, then moved back.
$smokeProfile = Get-SmokeProfile -ProfileRoot $ProfileRoot
try {
    if (Restore-SmokeProfile -SmokeProfile $smokeProfile) {
        Write-Host 'Restored the data and log folders of the app (and its macOS preferences, saved state and caches) from an interrupted smoke run.' -ForegroundColor Yellow
    }
    elseif ($RestoreOnly) {
        Write-Host 'Nothing to restore: no interrupted smoke run.'
    }
}
catch {
    Write-Host "AOT smoke check NOT RUN: the folders of an interrupted smoke run could not be restored: $_" -ForegroundColor Red
    exit 1
}
if ($RestoreOnly) { exit 0 }
# Physical path: dotnet restore breaks on symlinked paths (Resolve-PhysicalPath).
$project = Resolve-PhysicalPath (Join-Path $PSScriptRoot '..' 'src' 'LayerPeek.App')
# The smoke run owns (and deletes) only the folders it creates: the temporary publish folder and the log folder.
$ownsPublishDir = -not $PublishDir
$publishFolder = if ($ownsPublishDir) {
    Join-Path ([System.IO.Path]::GetTempPath()) "layerpeek-aot-smoke-$([guid]::NewGuid().ToString('N'))"
}
elseif (Test-Path -LiteralPath $PublishDir -PathType Container) {
    (Resolve-Path -LiteralPath $PublishDir).ProviderPath
}
else {
    Write-Host "AOT smoke check NOT RUN: publish folder not found: $PublishDir" -ForegroundColor Red
    exit 1
}
$logDir = Join-Path ([System.IO.Path]::GetTempPath()) "layerpeek-aot-smoke-log-$([guid]::NewGuid().ToString('N'))"
$exeName = if ($Runtime.StartsWith('win')) { 'LayerPeek.exe' } else { 'LayerPeek' }
$process = $null
$profileMoved = $false
$exitCode = 1
# The app creates its Start menu shortcut for notifications (T-010) pointing to the temporary exe: restore the previous state afterwards.
$shortcut = if ($IsWindows -and -not $SkipShellIntegration) { Join-Path ([Environment]::GetFolderPath('Programs')) 'LayerPeek.lnk' } else { $null }
$shortcutBackup = if ($shortcut -and (Test-Path -LiteralPath $shortcut)) { [System.IO.File]::ReadAllBytes($shortcut) } else { $null }
# Toasts the app shows during the run (it uses the real settings, so e.g. a printer error at start) stay in the
# notification center. Windows PowerShell 5.1 is used because pwsh cannot load WinRT types.
# Returns the number of the app's toasts in the notification center, or $null when it cannot be read reliably
# (then nothing is ever cleared).
function Get-ToastHistoryCount {
    try {
        $lines = @(powershell.exe -NoProfile -NonInteractive -Command '$ErrorActionPreference = ''Stop''; [void][Windows.UI.Notifications.ToastNotificationManager, Windows.UI.Notifications, ContentType=WindowsRuntime]; @([Windows.UI.Notifications.ToastNotificationManager]::History.GetHistory("LayerPeek")).Count' 2>$null)
        $count = 0
        if ($LASTEXITCODE -eq 0 -and $lines.Count -eq 1 -and [int]::TryParse([string]$lines[0], [ref]$count) -and $count -ge 0) {
            return $count
        }
    }
    catch {
    }
    return $null
}
function Clear-ToastHistory {
    powershell.exe -NoProfile -NonInteractive -Command '$ErrorActionPreference = ''Stop''; [void][Windows.UI.Notifications.ToastNotificationManager, Windows.UI.Notifications, ContentType=WindowsRuntime]; [Windows.UI.Notifications.ToastNotificationManager]::History.Clear("LayerPeek")' 2>$null | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "clearing the notification history failed with exit code $LASTEXITCODE" }
}
$toastsBefore = if ($IsWindows -and -not $SkipShellIntegration) { Get-ToastHistoryCount } else { $null }
# macOS (T-019): the exe inside LayerPeek.app registers the bundle with LaunchServices when it starts.
$lsregister = '/System/Library/Frameworks/CoreServices.framework/Frameworks/LaunchServices.framework/Support/lsregister'
$bundle = $null
$bundleWasRegistered = $true
if ($IsMacOS -and -not $SkipShellIntegration -and -not $ownsPublishDir) {
    $contents = Split-Path -Parent (Resolve-Path -LiteralPath $PublishDir -ErrorAction SilentlyContinue).ProviderPath
    if ((Split-Path -Leaf $PublishDir) -ceq 'MacOS' -and $contents -and (Split-Path -Leaf $contents) -ceq 'Contents' -and (Split-Path -Parent $contents).EndsWith('.app')) {
        $bundle = Split-Path -Parent $contents
        $bundleWasRegistered = Get-LaunchServicesRegistration -DumpOutput @(& $lsregister -dump 2>$null) -BundlePath $bundle
    }
}

try {
    if ($ownsPublishDir) {
        Write-Host "Publishing $Runtime to $publishFolder ..."
        dotnet publish $project -c Release -r $Runtime -o $publishFolder
        if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE." }
    }
    else {
        Write-Host "Checking the existing publish folder $publishFolder ..."
    }

    $exe = Join-Path $publishFolder $exeName
    if (-not (Test-Path -LiteralPath $exe -PathType Leaf)) { throw "Published executable not found: $exe" }

    # Outside the publish folder, so that an existing (release) publish folder stays unchanged.
    $null = New-Item -ItemType Directory -Path $logDir
    $stdout = Join-Path $logDir 'smoke-stdout.txt'
    $stderr = Join-Path $logDir 'smoke-stderr.txt'
    Write-Host "Starting $exeName, expecting it to run for $Seconds s ..."
    # Last step before the start: the folders stay moved aside as briefly as possible. Set before the call, so that a
    # backup that fails half way is restored too.
    $profileMoved = $true
    Backup-SmokeProfile -SmokeProfile $smokeProfile
    # T-020 (user decision 2026-10-29): the run never opens the notification center of macOS, so nothing lands in the
    # notification settings of the developer (AppEnvironment.SkipNotificationCenterArgument; ignored on Windows).
    $process = Start-Process -FilePath $exe -ArgumentList $SmokeAppArguments -PassThru -RedirectStandardOutput $stdout -RedirectStandardError $stderr

    if ($process.WaitForExit($Seconds * 1000)) {
        $output = (Get-Content $stdout, $stderr -Raw -ErrorAction SilentlyContinue) -join [Environment]::NewLine
        throw "App exited after start with code $($process.ExitCode).$([Environment]::NewLine)$output"
    }

    if ($IsMacOS -and -not $SkipShellIntegration) {
        # T-019: a menu bar app without a Dock icon (LSUIElement in Info.plist, the accessory policy of Avalonia).
        $type = Get-LaunchServicesAppType -ListOutput @(& /usr/bin/lsappinfo list 2>$null) -ProcessId $process.Id
        if ($type -cne 'UIElement') { throw "The app is not an agent app without a Dock icon: LaunchServices type '$type', expected 'UIElement'." }
        Write-Host 'The app runs without a Dock icon (LaunchServices type UIElement).'
    }

    if ($shortcut) {
        # Proves the source-generated COM interop (IShellLinkW, IPropertyStore) works in the AOT binary.
        $target = if (Test-Path -LiteralPath $shortcut) { (New-Object -ComObject WScript.Shell).CreateShortcut($shortcut).TargetPath } else { $null }
        if ($target -ne $exe) { throw "Start menu shortcut for notifications was not created for $exe (found: '$target')." }
        Write-Host "Start menu shortcut for notifications points to the published exe."
    }

    Write-Host "AOT smoke check PASSED: app is still running after $Seconds s." -ForegroundColor Green
    $exitCode = 0
}
catch {
    Write-Host "AOT smoke check FAILED: $_" -ForegroundColor Red
    $exitCode = 1
}
finally {
    if ($process -and -not $process.HasExited) {
        $process.Kill($true)
        $process.WaitForExit()
    }
    # Right after the exe ended: nothing writes into the folders any more.
    if ($profileMoved -and $IsMacOS -and -not $ProfileRoot) {
        # T-019: what the exe wrote into its preferences (key names only), before the restore removes it.
        foreach ($preference in $smokeProfile.Preferences) {
            $read = @(& /usr/bin/defaults read $preference.Domain 2>$null)
            if ($LASTEXITCODE -eq 0) { Write-Host "Preferences the app wrote ($($preference.Domain)): $((Get-SmokePreferenceKeys $read) -join ', ')" }
        }
    }
    if ($profileMoved) {
        try {
            $null = Restore-SmokeProfile -SmokeProfile $smokeProfile
        }
        catch {
            Write-Host "The data and log folders of the app were NOT restored: $_ Run 'pwsh scripts/aot-smoke.ps1 -RestoreOnly' (the backup is next to them, *.aot-smoke-backup)." -ForegroundColor Red
            $exitCode = 1
        }
    }
    if ($bundle -and -not $bundleWasRegistered -and $process -and (Get-LaunchServicesRegistration -DumpOutput @(& $lsregister -dump 2>$null) -BundlePath $bundle)) {
        # The registration this start created (the user's LaunchServices database keeps no trace of the smoke run).
        & $lsregister -u $bundle 2>$null
        if (Get-LaunchServicesRegistration -DumpOutput @(& $lsregister -dump 2>$null) -BundlePath $bundle) {
            Write-Host "The LaunchServices registration of $bundle could not be removed (lsregister -u $bundle)." -ForegroundColor Yellow
        }
        else {
            Write-Host "Removed the LaunchServices registration of $bundle that the start created."
        }
    }
    if ($ownsPublishDir -and (Test-Path -LiteralPath $publishFolder)) {
        Remove-Item -LiteralPath $publishFolder -Recurse -Force -ErrorAction SilentlyContinue
    }
    if (Test-Path -LiteralPath $logDir) {
        Remove-Item -LiteralPath $logDir -Recurse -Force -ErrorAction SilentlyContinue
    }
    # Shortcut first: it must be restored even when the toast cleanup below fails.
    if ($shortcut) {
        if ($shortcutBackup) {
            [System.IO.File]::WriteAllBytes($shortcut, $shortcutBackup)
        }
        elseif (Test-Path -LiteralPath $shortcut) {
            Remove-Item -LiteralPath $shortcut -Force -ErrorAction SilentlyContinue
        }
    }
    # Toasts are cleared only when the history was known to be empty before the run and no other instance of the app
    # started meanwhile; in every other case they are left alone (the user's own toasts are never removed).
    if ($process -and $IsWindows -and -not $SkipShellIntegration) {
        try {
            if ($toastsBefore -ne 0) {
                $reason = if ($null -eq $toastsBefore) { 'its previous content could not be read' } else { 'it already held toasts of the app' }
                Write-Host "Toasts shown during the smoke run (if any) were left in the notification center: $reason." -ForegroundColor Yellow
            }
            elseif (Get-Process -Name 'LayerPeek' -ErrorAction SilentlyContinue) {
                Write-Host 'Toasts were not cleared: another LayerPeek instance started during the smoke run.' -ForegroundColor Yellow
            }
            else {
                Clear-ToastHistory
            }
        }
        catch {
            Write-Host "Toasts shown during the smoke run were not cleared: $_" -ForegroundColor Yellow
        }
    }
}

exit $exitCode
