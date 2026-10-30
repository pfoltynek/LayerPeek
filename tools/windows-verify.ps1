<#
.SYNOPSIS
    User-run verification of the open Windows acceptance criteria (T-105): build and tests ("Windows unchanged" of the macOS
    tasks), AOT smoke, the end-to-end tests, a real install of the Velopack installer with a toast click and autostart after a
    real sign-in, with a report and evidence under artifacts/verify/<timestamp>/.

.DESCRIPTION
    Run it in your own Windows session. Every check says which task criterion it closes (-ListChecks). Kinds:
      - Auto: build, tests, AOT smoke (scripts/aot-smoke.ps1 backs up and restores the profile itself);
      - Permission: the end-to-end tests (tools/run-e2e.ps1 takes over the desktop for minutes; the installer test installs
        and uninstalls), the real install and uninstall in your user profile;
      - Manual: the toast click after the install, autostart after a real sign-in (two runs), the taskbar rule of T-102.
    The real install (win.install ... win.uninstall) refuses to run over an existing installation or a running LayerPeek.
    It moves %APPDATA%\LayerPeek and %LOCALAPPDATA%\LayerPeek aside first (renamed to <folder>.aot-smoke-backup next to
    them, with the journal %APPDATA%\LayerPeek.verify-install-journal.json, the mechanism of scripts/aot-smoke.ps1) and keeps
    the Start menu and Startup shortcuts and the Credential Manager targets of LayerPeek from before; win.uninstall (also in a
    later run, e.g. after the sign-out for win.autostart) uninstalls, checks that nothing is left (Uninstall key, shortcuts,
    the COM toast activator HKCU\Software\Classes\CLSID\{EC47413C-984D-447A-866A-5F780E408DE7}) and restores everything.
    -RestoreOnly finishes an interrupted restore.

    Results: PASS (automatic), FAIL, SKIP (with the reason), MANUAL (you confirmed it with y). Exit code 1 when any check
    failed, 2 when the script could not run.

.PARAMETER Unattended
    Never asks anything: Permission and Manual checks are SKIP with the reason. For test runs (also inside dotnet test).

.PARAMETER Only
    Check ids or wildcards to run (comma-separated or an array).

.PARAMETER Skip
    Check ids or wildcards not to run.

.PARAMETER ListChecks
    Lists the checks and exits.

.PARAMETER SelfTest
    Runs the engine with stand-in checks in a temporary folder and validates the check list; works on any OS.

.PARAMETER RestoreOnly
    Only restores the profile moved aside by win.install (after an interrupted run); uninstalls nothing.

.PARAMETER OutputRoot
    Folder of the run folders; default artifacts/verify in the repository.

.EXAMPLE
    pwsh tools/windows-verify.ps1 -ListChecks
    pwsh tools/windows-verify.ps1
    pwsh tools/windows-verify.ps1 -Unattended
    pwsh tools/windows-verify.ps1 -Only win.install,win.toast-click,win.autostart
#>
#Requires -Version 7.2
[CmdletBinding()]
param(
    [switch] $Unattended,
    [string[]] $Only,
    [string[]] $Skip,
    [switch] $ListChecks,
    [switch] $SelfTest,
    [switch] $RestoreOnly,
    [string] $OutputRoot
)

$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'VerifyTools.psm1') -Force
Import-Module (Join-Path $PSScriptRoot '..' 'scripts' 'ReleaseTools' 'ReleaseTools.psm1') -Force

$script:Repo = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$script:DotnetEnv = @{ DOTNET_CLI_UI_LANGUAGE = 'en'; DOTNET_NOLOGO = '1'; DOTNET_CLI_TELEMETRY_OPTOUT = '1' }
$script:Runtime = 'win-x64'
$script:ActivatorSubKey = 'Software\Classes\CLSID\{EC47413C-984D-447A-866A-5F780E408DE7}'
$script:UninstallSubKey = 'Software\Microsoft\Windows\CurrentVersion\Uninstall\LayerPeek'

function Get-AppVersion { Get-SourceVersion -PropsPath (Join-Path $script:Repo 'Directory.Build.props') }

function Get-InstallPaths {
    $local = [Environment]::GetFolderPath('LocalApplicationData')
    $roaming = [Environment]::GetFolderPath('ApplicationData')
    [pscustomobject]@{
        InstallFolder = Join-Path $local 'LayerPeek'
        InstalledExe  = Join-Path $local 'LayerPeek' 'current' 'LayerPeek.exe'
        UpdateExe     = Join-Path $local 'LayerPeek' 'Update.exe'
        Data          = Join-Path $roaming 'LayerPeek'
        StartMenu     = Join-Path ([Environment]::GetFolderPath('Programs')) 'LayerPeek.lnk'
        Startup       = Join-Path ([Environment]::GetFolderPath('Startup')) 'LayerPeek.lnk'
        Journal       = Join-Path $roaming 'LayerPeek.verify-install-journal.json'
        State         = Join-Path $roaming 'LayerPeek.verify-install-state.json'
    }
}

function Get-InstallProfile {
    <#
    .SYNOPSIS
        The places win.install moves aside, in the form of Get-SmokeProfile (Backup-SmokeProfile / Restore-SmokeProfile):
        the data folder and the whole %LOCALAPPDATA%\LayerPeek (logs of development builds; the installation goes there).
    #>
    $paths = Get-InstallPaths
    [pscustomobject]@{ Folders = @($paths.Data, $paths.InstallFolder); Preferences = @(); Defaults = $null; Journal = $paths.Journal }
}

function Test-RegistryKey([string] $SubKey) {
    $key = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($SubKey)
    if ($key) { $key.Dispose(); $true } else { $false }
}

function Get-CredentialTargets { Get-VerifyCredentialTargets @(& cmdkey.exe /list 2>$null) }

function Get-ShortcutTarget([string] $Path) {
    if (-not (Test-Path -LiteralPath $Path)) { return $null }
    (New-Object -ComObject WScript.Shell).CreateShortcut($Path).TargetPath
}

function Invoke-DotnetTest($Context, [string] $Label, [string[]] $Arguments) {
    $result = Invoke-VerifyTool -Context $Context -Label $Label -FilePath 'dotnet' -ArgumentList (@('test') + $Arguments) -Environment $script:DotnetEnv -Show
    [pscustomobject]@{ ExitCode = $result.ExitCode; Totals = (Get-VerifyTestTotals $result.Output); Skipped = @(Get-VerifySkippedTests $result.Output) }
}

function Format-Totals($Totals) { "total $($Totals.Total), passed $($Totals.Passed), failed $($Totals.Failed), skipped $($Totals.Skipped)" }

function Invoke-ChildPwsh($Context, [string] $Label, [string[]] $Arguments) {
    $pwsh = @(Get-PwshCommand)
    Invoke-VerifyTool -Context $Context -Label $Label -FilePath $pwsh[0] -ArgumentList (@($pwsh | Select-Object -Skip 1) + @('-NoProfile') + $Arguments) -Environment $script:DotnetEnv -Show
}

function Start-Simulator($Context) {
    if ($Context.Shared.ContainsKey('Simulator')) { return $Context.Shared.Simulator }
    $build = Invoke-VerifyTool -Context $Context -Label 'build-simulator' -FilePath 'dotnet' -ArgumentList @('build', (Join-Path $script:Repo 'tools' 'PrusaSimulator' 'PrusaSimulator.csproj'), '-c', 'Debug') -Environment $script:DotnetEnv
    if ($build.ExitCode -ne 0) { throw 'dotnet build of the PrusaSimulator failed.' }
    $folder = Join-Path $Context.RunFolder 'tmp' "simulator-$([guid]::NewGuid().ToString('N').Substring(0, 8))"
    $null = New-Item -ItemType Directory -Force -Path $folder
    $out = Join-Path $folder 'stdout.txt'
    $empty = Join-Path $folder 'stdin.txt'
    [System.IO.File]::WriteAllText($empty, '')
    $dll = Join-Path $script:Repo 'tools' 'PrusaSimulator' 'bin' 'Debug' 'net11.0' 'PrusaSimulator.dll'
    # Console input from an empty file: the simulator keeps running and never reads the answers meant for this script.
    $process = Start-Process -FilePath 'dotnet' -ArgumentList @("`"$dll`"", '--port', '0', '--camera-port', '0', '--scenario', 'printing') -RedirectStandardOutput $out -RedirectStandardInput $empty -NoNewWindow -PassThru
    Add-VerifyCleanup $Context 'simulator' { param($p) if (-not $p.HasExited) { $p.Kill($true); $p.WaitForExit() } } -State $process
    if (-not (Wait-VerifyCondition -TimeoutSeconds 60 -Condition { (Test-Path -LiteralPath $out) -and (Select-String -LiteralPath $out -Pattern 'Camera:' -Quiet) })) { throw 'The simulator did not start.' }
    $text = Get-Content -LiteralPath $out -Raw
    $simulator = [pscustomobject]@{
        BaseUri  = ([regex]::Match($text, 'Address:\s+(\S+)')).Groups[1].Value
        User     = ([regex]::Match($text, 'User name:\s+(\S+)')).Groups[1].Value
        Password = ([regex]::Match($text, 'Password:\s+(\S+)')).Groups[1].Value
    }
    $Context.Shared.Simulator = $simulator
    $simulator
}

function Set-SimulatorScenario($Simulator, [string] $Scenario) {
    $null = Invoke-RestMethod -Method Post -Uri "$($Simulator.BaseUri.TrimEnd('/'))/_simulator/scenario/$Scenario" -TimeoutSec 10
}

function Assert-Installed {
    $paths = Get-InstallPaths
    if (-not (Test-Path -LiteralPath $paths.State)) { Skip-VerifyCheck 'no verification install: run -Only win.install first.' }
    if (-not (Test-Path -LiteralPath $paths.InstalledExe)) { throw "The verification install is incomplete: no $($paths.InstalledExe). Run -Only win.uninstall to clean up." }
    $paths
}

function Wait-NoLayerPeek($Context, [string] $Why) {
    if (-not (Get-Process -Name 'LayerPeek' -ErrorAction SilentlyContinue)) { return }
    Write-VerifyStep $Context "LayerPeek is running: quit it (tray menu > Quit) $Why." 'Yellow'
    if (-not (Wait-VerifyCondition -TimeoutSeconds 180 -Condition { -not (Get-Process -Name 'LayerPeek' -ErrorAction SilentlyContinue) })) { Skip-VerifyCheck 'LayerPeek kept running.' }
}

function Restore-Install {
    <# .SYNOPSIS Puts back what win.install moved and kept: the folders (journal), the shortcuts, removes new credentials. Returns notes. #>
    $paths = Get-InstallPaths
    $notes = [System.Collections.Generic.List[string]]::new()
    if (Test-Path -LiteralPath $paths.State) {
        $state = Get-Content -LiteralPath $paths.State -Raw | ConvertFrom-Json
        foreach ($target in @(Get-CredentialTargets | Where-Object { @($state.CredentialsBefore) -notcontains $_ })) {
            $null = & cmdkey.exe "/delete:$target" 2>&1
            $notes.Add("removed the credential $target")
        }
        foreach ($pair in @(@($paths.StartMenu, $state.StartMenuShortcut), @($paths.Startup, $state.StartupShortcut))) {
            if ($pair[1]) { [System.IO.File]::WriteAllBytes($pair[0], [Convert]::FromBase64String($pair[1])) }
            elseif (Test-Path -LiteralPath $pair[0]) { Remove-Item -LiteralPath $pair[0] -Force }
        }
        Remove-Item -LiteralPath $paths.State -Force
    }
    if (Restore-SmokeProfile -SmokeProfile (Get-InstallProfile)) { $notes.Add('restored %APPDATA%\LayerPeek and %LOCALAPPDATA%\LayerPeek') }
    $notes.ToArray()
}

# ---------------------------------------------------------------------------------------------------------------------
# The checks
# ---------------------------------------------------------------------------------------------------------------------

$unchanged = @(
    'T-095 kritérium Windows (dotnet build / test beze změny výsledku)',
    'T-018 kritérium 4 (build a testy na Windows)',
    'T-021 kritérium 9 (Windows build a testy beze změny chování)',
    'T-026 kritérium 5 (Windows build a testy beze změny chování)',
    'T-053 kritérium 6 (Windows build a testy beze změny chování)',
    'T-097 kritérium 5 (Windows beze změny)',
    'T-100 kritérium 5 (Windows beze změny)'
)

$checks = @(
    New-VerifyCheck -Id 'win.build' -Kind Auto -Title 'dotnet build Debug and Release without warnings' -Closes $unchanged -Action {
        param($c)
        $problems = [System.Collections.Generic.List[string]]::new()
        foreach ($configuration in 'Debug', 'Release') {
            $result = Invoke-VerifyTool -Context $c -Label "build-$($configuration.ToLowerInvariant())" -FilePath 'dotnet' -ArgumentList @('build', (Join-Path $script:Repo 'LayerPeek.slnx'), '-c', $configuration) -Environment $script:DotnetEnv
            $warnings = @(Get-VerifyBuildWarnings $result.Output)
            if ($result.ExitCode -ne 0) { $problems.Add("dotnet build -c $configuration failed (exit code $($result.ExitCode))") }
            elseif ($warnings.Count -gt 0) { $problems.Add("dotnet build -c $configuration has $($warnings.Count) warning(s): $($warnings[0])") }
        }
        if ($problems.Count -gt 0) { return New-VerifyResult FAIL ($problems -join '; ') }
        New-VerifyResult PASS 'Debug and Release: 0 warnings, 0 errors.'
    }

    New-VerifyCheck -Id 'win.test' -Kind Auto -Title 'full dotnet test without failures' -Closes (@($unchanged) + 'T-018 kritérium 3 (Windows chování z T-007 beze změny, unit a UI testy)') -Action {
        param($c)
        $run = Invoke-DotnetTest $c 'dotnet-test' @('--solution', (Join-Path $script:Repo 'LayerPeek.slnx'))
        if (-not $run.Totals) { return New-VerifyResult FAIL "no test results (build failed?), exit code $($run.ExitCode)." }
        $null = Save-VerifyEvidence -Context $c -Name 'skipped.txt' -Content ($run.Skipped | ForEach-Object { "$($_.Name)`n    $($_.Reason)" })
        if ($run.Totals.Failed -gt 0 -or $run.ExitCode -ne 0) { return New-VerifyResult FAIL "$(Format-Totals $run.Totals); exit code $($run.ExitCode)." }
        New-VerifyResult PASS "$(Format-Totals $run.Totals) (skipped tests with reasons in skipped.txt)"
    }

    New-VerifyCheck -Id 'win.aot-smoke' -Kind Auto -Title 'Native AOT publish win-x64 without warnings and smoke start (scripts/aot-smoke.ps1)' `
        -Closes 'T-053 kritérium 6 (Windows beze změny: Native AOT exe startuje)', 'T-097 kritérium 5 (Windows beze změny, AOT)' -Action {
        param($c)
        $result = Invoke-ChildPwsh $c 'aot-smoke' @('-File', (Join-Path $script:Repo 'scripts' 'aot-smoke.ps1'), '-Runtime', $script:Runtime)
        $warnings = @(Get-VerifyBuildWarnings $result.Output)
        if ($result.ExitCode -ne 0) { return New-VerifyResult FAIL "aot-smoke.ps1 failed (exit code $($result.ExitCode)); needs the C++ build tools (docs/prerequisites.md)." }
        if ($warnings.Count -gt 0) { return New-VerifyResult FAIL "the AOT publish has $($warnings.Count) warning(s): $($warnings[0])" }
        New-VerifyResult PASS 'published without warnings; the AOT exe ran and created its Start menu shortcut; profile restored by aot-smoke.ps1.'
    }

    New-VerifyCheck -Id 'win.e2e' -Kind Permission -Permission 'the end-to-end tests take over the desktop for several minutes (tools/run-e2e.ps1 -NoSystemChanges: no Windows setting is changed)' `
        -Title 'end-to-end tests of the real app (tray, windows, camera, toasts in isolation)' `
        -Closes 'T-018 kritérium 3 (Windows chování z T-007 beze změny, E2E)', 'T-100 kritérium 5 (Windows beze změny, E2E)', 'T-097 kritérium 5 (Windows beze změny, E2E)', 'T-026 kritérium 5 (Windows beze změny, E2E)' -Action {
        param($c)
        $result = Invoke-ChildPwsh $c 'run-e2e' @('-File', (Join-Path $script:Repo 'tools' 'run-e2e.ps1'), '-NoSystemChanges')
        $totals = Get-VerifyRunE2ETotals $result.Output
        if ($result.ExitCode -ne 0 -or ($totals -and $totals.Failed -gt 0)) { return New-VerifyResult FAIL "run-e2e.ps1 exit code $($result.ExitCode)$(if ($totals) { "; $(Format-Totals $totals)" })." }
        New-VerifyResult PASS "$(if ($totals) { Format-Totals $totals } else { 'passed' }) (system-change tests skipped by -NoSystemChanges)."
    }

    New-VerifyCheck -Id 'win.e2e-installer' -Kind Permission -Permission 'the E2E test installs and uninstalls the release installer silently in your profile (tools/run-e2e.ps1 with its warning and restore journal)' `
        -Title 'Velopack installer E2E: silent install, toast registration, uninstall leaves nothing (incl. the COM toast activator)' `
        -Closes 'T-017 kritérium 6 (odinstalace smaže registraci COM aktivátoru toastů)' -Action {
        param($c)
        $setup = Join-Path $script:Repo 'artifacts' 'release' (Get-AppVersion) 'dist' (Get-VelopackSetupFileName -Version (Get-AppVersion) -Runtime $script:Runtime)
        if (-not (Test-Path -LiteralPath $setup)) { Skip-VerifyCheck "no installer $setup; build it first: pwsh scripts/release-build.ps1" }
        $result = Invoke-ChildPwsh $c 'run-e2e-installer' @('-File', (Join-Path $script:Repo 'tools' 'run-e2e.ps1'), '-Class', '*VelopackInstallerTests')
        $totals = Get-VerifyRunE2ETotals $result.Output
        if ($result.ExitCode -ne 0 -or -not $totals -or $totals.Failed -gt 0) { return New-VerifyResult FAIL "run-e2e.ps1 exit code $($result.ExitCode)$(if ($totals) { "; $(Format-Totals $totals)" })." }
        if ($totals.Passed -eq 0) { return New-VerifyResult FAIL "the installer test did not run: $(Format-Totals $totals) (see run-e2e-installer.txt for the skip reason)." }
        New-VerifyResult PASS "$(Format-Totals $totals): the uninstall left no Uninstall key, shortcut or HKCU\$($script:ActivatorSubKey)."
    }

    New-VerifyCheck -Id 'win.install' -Kind Permission -Permission 'a real install of the release installer into your profile (your LayerPeek data and logs are moved aside until win.uninstall)' `
        -Title 'real install with Setup.exe --silent (stays installed for win.toast-click and win.autostart)' `
        -Closes 'T-017 kritérium 6 (podklad: instalace pro kliknutí na toast)', 'T-017 kritérium 7 (podklad: instalace pro autostart)' -Action {
        param($c)
        $paths = Get-InstallPaths
        if (Test-Path -LiteralPath $paths.State) { return New-VerifyResult PASS 'already installed by an earlier run of this script (win.uninstall removes it).' }
        $version = Get-AppVersion
        $setup = Join-Path $script:Repo 'artifacts' 'release' $version 'dist' (Get-VelopackSetupFileName -Version $version -Runtime $script:Runtime)
        if (-not (Test-Path -LiteralPath $setup)) { Skip-VerifyCheck "no installer $setup; build it first: pwsh scripts/release-build.ps1" }
        if ((Test-Path -LiteralPath $paths.UpdateExe) -or (Test-RegistryKey $script:UninstallSubKey)) { Skip-VerifyCheck 'LayerPeek is installed on this machine; the check never touches a real installation.' }
        Wait-NoLayerPeek $c 'before the install'
        $state = [ordered]@{
            Installed          = [DateTimeOffset]::Now.ToString('o')
            CredentialsBefore  = @(Get-CredentialTargets)
            StartMenuShortcut  = if (Test-Path -LiteralPath $paths.StartMenu) { [Convert]::ToBase64String([System.IO.File]::ReadAllBytes($paths.StartMenu)) } else { $null }
            StartupShortcut    = if (Test-Path -LiteralPath $paths.Startup) { [Convert]::ToBase64String([System.IO.File]::ReadAllBytes($paths.Startup)) } else { $null }
        }
        [System.IO.File]::WriteAllText($paths.State, ($state | ConvertTo-Json -Depth 4))
        Backup-SmokeProfile -SmokeProfile (Get-InstallProfile)
        $install = Invoke-VerifyTool -Context $c -Label 'setup' -FilePath $setup -ArgumentList @('--silent')
        if ($install.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $paths.InstalledExe)) { return New-VerifyResult FAIL "Setup.exe --silent ended with $($install.ExitCode); run -Only win.uninstall to clean up." }
        $shortcut = Get-ShortcutTarget $paths.StartMenu
        $null = Save-VerifyEvidence -Context $c -Name 'install.txt' -Content @("Start menu shortcut -> $shortcut", "Uninstall key: $(Test-RegistryKey $script:UninstallSubKey)", "COM toast activator: $(Test-RegistryKey $script:ActivatorSubKey)")
        if (-not (Test-RegistryKey $script:ActivatorSubKey)) { return New-VerifyResult FAIL "the install did not register the toast activator HKCU\$($script:ActivatorSubKey)." }
        New-VerifyResult PASS "installed $version into $($paths.InstallFolder); Start menu shortcut and toast activator registered. Uninstall with -Only win.uninstall."
    }

    New-VerifyCheck -Id 'win.toast-click' -Kind Manual -Title 'after the install, a click on a toast in the notification center starts the app and opens the printer web' `
        -Closes 'T-017 kritérium 6 (po instalaci kliknutí na toast otevře web tiskárny, i když aplikace neběží)' -Action {
        param($c)
        $paths = Assert-Installed
        Wait-NoLayerPeek $c 'so that the installed app starts fresh'
        $simulator = Start-Simulator $c
        Set-SimulatorScenario $simulator 'printing'
        $null = Start-Process -FilePath $paths.InstalledExe -PassThru
        Write-VerifyStep $c "The installed LayerPeek starts (empty profile). Add the simulator printer in Settings: address $(([uri] $simulator.BaseUri).Authority), user $($simulator.User), password $($simulator.Password) (test values of the simulator, not a secret)."
        $null = Read-VerifyText -Context $c -Question 'Press Enter when the printer shows as printing'
        Set-SimulatorScenario $simulator 'paused'
        $toast = Confirm-VerifyStep $c 'The printer is paused now: did a toast "paused" appear?'
        Get-Process -Name 'LayerPeek' -ErrorAction SilentlyContinue | Stop-Process -Force
        Write-VerifyStep $c 'LayerPeek was ended. Open the notification center (Win+N) and click the LayerPeek toast. Waiting up to 2 minutes for the app to start.' 'Yellow'
        $started = Wait-VerifyCondition -TimeoutSeconds 120 -Condition { Get-Process -Name 'LayerPeek' -ErrorAction SilentlyContinue }
        $web = $started -and (Confirm-VerifyStep $c 'Did the click start LayerPeek and open the printer web in the browser?')
        $null = Save-VerifyEvidence -Context $c -Name 'processes.txt' -Content (Get-Process -Name 'LayerPeek' -ErrorAction SilentlyContinue | ForEach-Object { "$($_.Id) $($_.StartTime.ToString('o')) $($_.Path)" })
        if (-not $toast) { return New-VerifyResult FAIL 'no toast appeared (user).' }
        if (-not $started) { return New-VerifyResult FAIL 'the click did not start LayerPeek within 2 minutes.' }
        if (-not $web) { return New-VerifyResult FAIL 'the printer web did not open (user).' }
        New-VerifyResult MANUAL 'the toast click started the installed app (not running before) and opened the printer web.'
    }

    New-VerifyCheck -Id 'win.autostart' -Kind Manual -Title 'autostart after a real sign-out and sign-in (installed app, two runs)' `
        -Closes 'T-017 kritérium 7 (autostart po přihlášení funguje na Windows)' -Action {
        param($c)
        $paths = Assert-Installed
        $target = Get-ShortcutTarget $paths.Startup
        if (-not (Read-VerifyAnswer -Context $c -Question 'Have you switched on autostart in the installed LayerPeek (Settings > Startup and updates), saved, and signed out and in since then?')) {
            Write-VerifyStep $c 'Do now: start LayerPeek from the Start menu, Settings > Startup and updates > start with Windows, Save; sign out and in; then run: pwsh tools/windows-verify.ps1 -Only win.autostart' 'Yellow'
            Skip-VerifyCheck 'pending: switch on autostart, sign out and in, then run -Only win.autostart again.'
        }
        $explorer = Get-Process -Name 'explorer' -ErrorAction SilentlyContinue | Sort-Object StartTime | Select-Object -First 1
        $app = Get-Process -Name 'LayerPeek' -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $paths.InstalledExe } | Sort-Object StartTime | Select-Object -First 1
        $null = Save-VerifyEvidence -Context $c -Name 'autostart.txt' -Content @("Startup shortcut -> $target", "explorer started $($explorer.StartTime.ToString('o'))", "LayerPeek: $(if ($app) { "$($app.Id) started $($app.StartTime.ToString('o'))" } else { 'not running' })")
        if ($target -ne $paths.InstalledExe) { return New-VerifyResult FAIL "the Startup shortcut points to '$target', expected $($paths.InstalledExe)." }
        if (-not $app) { return New-VerifyResult FAIL 'the installed LayerPeek is not running after the sign-in.' }
        if ($explorer -and $app.StartTime -lt $explorer.StartTime.AddSeconds(-5)) { return New-VerifyResult FAIL 'LayerPeek runs since before the sign-in (not started by autostart).' }
        if (-not (Confirm-VerifyStep $c 'Did the tray icon appear by itself after the sign-in?')) { return New-VerifyResult FAIL 'no tray icon after the sign-in (user).' }
        New-VerifyResult MANUAL "started by the Startup shortcut at the sign-in ($($app.StartTime.ToString('HH:mm:ss')), explorer $($explorer.StartTime.ToString('HH:mm:ss')))."
    }

    New-VerifyCheck -Id 'win.uninstall' -Kind Permission -Permission 'uninstalling the verification install and restoring your LayerPeek data, logs, shortcuts and credentials' `
        -Title 'uninstall leaves no Uninstall key, shortcuts or COM toast activator; the profile is restored' `
        -Closes 'T-017 kritérium 6 (odinstalace smaže registraci COM aktivátoru toastů)' -Action {
        param($c)
        $paths = Get-InstallPaths
        if (-not (Test-Path -LiteralPath $paths.State) -and -not (Test-Path -LiteralPath $paths.Journal)) { Skip-VerifyCheck 'no verification install (win.install) to remove.' }
        Wait-NoLayerPeek $c 'before the uninstall'
        $problems = [System.Collections.Generic.List[string]]::new()
        $key = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($script:UninstallSubKey)
        if ($key) {
            $quiet = [string] $key.GetValue('QuietUninstallString')
            $key.Dispose()
            # "C:\...\Update.exe" --uninstall --silent, as Settings > Apps runs it.
            if ($quiet -notmatch '^\s*"(?<exe>[^"]+)"\s*(?<arguments>.*)$') { return New-VerifyResult FAIL "unexpected QuietUninstallString: $quiet" }
            $result = Invoke-VerifyTool -Context $c -Label 'uninstall' -FilePath $Matches.exe -ArgumentList @($Matches.arguments -split '\s+' | Where-Object { $_ })
            if ($result.ExitCode -ne 0) { $problems.Add("the uninstall ended with $($result.ExitCode)") }
            $null = Wait-VerifyCondition -TimeoutSeconds 60 -Condition { -not (Test-Path -LiteralPath $paths.UpdateExe) }
        }
        $left = @(
            if (Test-RegistryKey $script:UninstallSubKey) { "HKCU\$($script:UninstallSubKey)" }
            if (Test-RegistryKey $script:ActivatorSubKey) { "HKCU\$($script:ActivatorSubKey)" }
            if ((Get-ShortcutTarget $paths.StartMenu) -like "$($paths.InstallFolder)*") { $paths.StartMenu }
            if ((Get-ShortcutTarget $paths.Startup) -like "$($paths.InstallFolder)*") { $paths.Startup }
            if (Test-Path -LiteralPath $paths.UpdateExe) { $paths.UpdateExe }
        )
        $null = Save-VerifyEvidence -Context $c -Name 'left.txt' -Content $left
        foreach ($item in $left) { $problems.Add("left after the uninstall: $item") }
        $notes = @(Restore-Install)
        $null = Save-VerifyEvidence -Context $c -Name 'restore.txt' -Content $notes
        if ($problems.Count -gt 0) { return New-VerifyResult FAIL ($problems -join '; ') }
        New-VerifyResult PASS "uninstalled: no Uninstall key, no shortcut into the installation, no HKCU\$($script:ActivatorSubKey); $($notes -join '; ')."
    }

    New-VerifyCheck -Id 'win.taskbar' -Kind Manual -Title 'taskbar presence rule of T-102 (only when T-102 is done)' `
        -Closes 'T-102 (pravidlo přítomnosti na hlavním panelu, Windows)' -Action {
        param($c)
        $task = Get-ChildItem -LiteralPath (Join-Path $script:Repo 'tasks' 'done') -Filter 'T-102-*.md' -ErrorAction SilentlyContinue | Select-Object -First 1
        if (-not $task) { Skip-VerifyCheck 'T-102 is not merged (no tasks/done/T-102-*.md); nothing to verify yet.' }
        $criteria = @(Get-Content -LiteralPath $task.FullName | Where-Object { $_ -match '^\s*- \[[ x]\]' })
        $null = Save-VerifyEvidence -Context $c -Name 'criteria.txt' -Content $criteria
        Write-VerifyStep $c "Check the taskbar rule of $($task.Name) (criteria in criteria.txt) with LayerPeek running."
        if (-not (Confirm-VerifyStep $c 'Does the taskbar presence follow the rule of T-102 on Windows?')) { return New-VerifyResult FAIL 'the user answered no.' }
        New-VerifyResult MANUAL "confirmed against $($task.Name)."
    }
)

# ---------------------------------------------------------------------------------------------------------------------
# Main
# ---------------------------------------------------------------------------------------------------------------------

if ($ListChecks) {
    Format-VerifyCheckList -Checks $checks | Write-Output
    exit 0
}

if ($SelfTest) {
    $problems = @(Invoke-VerifySelfTest -TempRoot ([System.IO.Path]::GetTempPath())) + @(Test-VerifyCheckDefinitions -Checks $checks -TasksRoot (Join-Path $script:Repo 'tasks') -AllowMissingTasks 'T-102')
    foreach ($check in $checks) { if ($check.Id -notlike 'win.*') { $problems += "The check $($check.Id) is not a win.* check." } }
    if ($problems.Count -gt 0) {
        Write-Output 'SELF-TEST FAILED:'
        $problems | ForEach-Object { Write-Output "  $_" }
        exit 1
    }
    Write-Output "SELF-TEST PASSED: engine, both modes and $($checks.Count) check definitions."
    exit 0
}

if (-not $IsWindows) {
    Write-Host 'tools/windows-verify.ps1 runs on Windows only (use tools/macos-verify.ps1 on macOS).' -ForegroundColor Red
    exit 2
}

if ($RestoreOnly) {
    $notes = @(Restore-Install)
    Write-Host $(if ($notes.Count) { $notes -join "`n" } else { 'Nothing to restore.' })
    exit 0
}

try { $selected = @(Select-VerifyChecks -Checks $checks -Only $Only -Skip $Skip) }
catch { Write-Host $_.Exception.Message -ForegroundColor Red; exit 2 }
if ($selected.Count -eq 0) { Write-Host 'No check selected.' -ForegroundColor Red; exit 2 }

$root = if ($OutputRoot) { $OutputRoot } else { Join-Path $script:Repo 'artifacts' 'verify' }
$runFolder = New-VerifyRunFolder -Root $root
$context = New-VerifyContext -RunFolder $runFolder -Unattended:$Unattended
Write-Host "LayerPeek Windows verification ($($context.Unattended ? 'unattended' : 'interactive')), $($selected.Count) check(s); report in $runFolder"
$pending = Test-Path -LiteralPath (Get-InstallPaths).State

$results = Invoke-VerifyRun -Checks $selected -Context $context
$info = Get-VerifyRunInfo -RepoRoot $script:Repo -Script 'tools/windows-verify.ps1' -Unattended:$Unattended -Selected @($selected.Id)
$report = Write-VerifyReport -Folder $runFolder -Info $info -Results $results
Write-Host ''
Format-VerifySummary -Results $results | ForEach-Object { Write-Host $_ }
Write-Host "Report: $($report.Markdown)"
if (Test-Path -LiteralPath (Get-InstallPaths).State) {
    Write-Host 'The verification install stays (with your profile moved aside) until: pwsh tools/windows-verify.ps1 -Only win.uninstall' -ForegroundColor Yellow
}
elseif ($pending) { Write-Host 'The verification install was removed and your profile restored.' }
exit (Get-VerifyExitCode -Results $results)
