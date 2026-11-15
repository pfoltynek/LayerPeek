<#
.SYNOPSIS
    User-run verification of the open macOS acceptance criteria (T-105): automatic checks, checks that need a permission
    (each asked separately) and guided manual steps, with a report and evidence under artifacts/verify/<timestamp>/.

.DESCRIPTION
    Run it in your own Terminal session on the Mac (not in an agent sandbox): there the login Keychain is unlocked, the
    privacy permissions (TCC) are yours to give, the display can sleep and sudo works. Every check says which task
    criterion it closes (-ListChecks). The checks use the Debug app in the isolated test mode as the test bundle
    "LayerPeek E2E.app" (identifier io.github.pfoltynek.LayerPeek.E2E, built in artifacts/verify-bundle/ and removed at
    the end) and the PrusaSimulator on 127.0.0.1; the production app, its profile and its permissions are never used by an
    automatic check. Only the guided manual checks ask you to use /Applications/LayerPeek.app; they offer to move your
    LayerPeek profile aside and restore it afterwards (the journal of scripts/aot-smoke.ps1, so that
    'pwsh scripts/aot-smoke.ps1 -RestoreOnly' finishes an interrupted restore).

    Before the first check the script saves the state of the watched profile places (tools/profile-snapshot.ps1) and
    compares it after the cleanup (check mac.profile-unchanged). The cleanup at the end removes what the run created:
    the test apps, the notifications of the test bundle (--layerpeek-e2e-clear-toasts), the stand-in Login Items of the
    test data folders (--layerpeek-e2e-clear-autostart), the LaunchServices registration, Preferences and caches of the
    test bundle, Keychain items with the test prefix LayerPeek.Tests and the temporary folders. Known leftovers that no
    tool can remove: the entry "LayerPeek E2E" in System Settings > Notifications and (after mac.keys, mac.menu-items,
    mac.update-check, mac.recording, mac.dashcam, mac.timelapse, mac.timelapse-layers, mac.notifications-content,
    mac.file-dialogs, mac.dock-windows, mac.mini-after-fullscreen, mac.camera-live, mac.camera-next-attempt,
    mac.detection-region, mac.appkit-language, mac.hw-decoding-switch or mac.keys-czech-layout) Terminal in
    Privacy & Security > Accessibility (switch it off there yourself).

    Results: PASS (checked automatically), FAIL, SKIP (with the reason), MANUAL (you confirmed it with y). Exit code 1
    when any check failed, 2 when the script could not run. The checks that need a visible desktop (mac.menubar,
    mac.settings-front, mac.camera, mac.update-check, ...) report SKIP with the reason and the failure text instead of
    FAIL when the screen was locked (or the session not on the console) before or after them: nothing can be judged
    there.

.PARAMETER Unattended
    Never asks anything and never requests a permission: Permission and Manual checks are SKIP with the reason, Auto
    checks that would need an answer too. For test runs (also inside dotnet test).

.PARAMETER Allow
    Ids or wildcards of Permission checks you allow in advance (T-140): they run without the question, also with
    -Unattended, e.g. -Unattended -Allow mac.keys,mac.display-sleep,mac.socket-other-user. What they need once:
    mac.keys - Accessibility for the terminal; mac.menu-items - Accessibility for the terminal and an unlocked screen;
    mac.update-check - Accessibility for the terminal and an unlocked screen
    (Screen Recording optional, for window captures); mac.socket-other-user - the sudoers rule of
    tools/macos-verify-sudoers.ps1; mac.display-sleep - a password delay after the display sleeps longer than a minute
    (System Settings > Lock Screen), otherwise the locked screen ends it with SKIP.

.PARAMETER Preset
    A fixed list of checks (T-208), e.g. release: every Auto and Permission check that runs without a human, before each
    release. Implies -Unattended and -Allow for the Permission checks of the preset; -Skip still applies, -Only does not.
    The preconditions (unlocked screen on the console, Accessibility, Screen Recording, no Screen Sharing, the output of
    scripts/release-build.ps1 for this version, no running LayerPeek) are checked
    once at the start: each one that is missing is printed once with the checks it ends with SKIP. Without a caffeinate
    parent the script keeps the Mac and the display awake itself (caffeinate -d -i -u -w). With -ListChecks it lists the checks of the
    preset. What it needs once: Accessibility for the terminal, optionally Screen Recording, the sudoers rule of
    tools/macos-verify-sudoers.sh, the allowed notifications of "LayerPeek E2E" and a password delay after the display
    sleeps (docs/macos-manual-testing.md, "Regresní běh před vydáním").

.PARAMETER Only
    Check ids or wildcards to run (comma-separated or an array), e.g. -Only mac.keychain,mac.camera*.

.PARAMETER Skip
    Check ids or wildcards not to run.

.PARAMETER ListChecks
    Lists the checks (id, kind, the criteria they close) and exits.

.PARAMETER SelfTest
    Runs the engine with stand-in checks and a stand-in prompt in a temporary folder and validates the check list; touches
    nothing of the system or the profile. Works on any OS.

.PARAMETER OutputRoot
    Folder of the run folders; default artifacts/verify in the repository.

.EXAMPLE
    pwsh tools/macos-verify.ps1 -ListChecks
    pwsh tools/macos-verify.ps1                              # everything, interactive
    pwsh tools/macos-verify.ps1 -Unattended                  # only what runs without you
    pwsh tools/macos-verify.ps1 -Unattended -Allow mac.keys,mac.display-sleep,mac.socket-other-user
    pwsh tools/macos-verify.ps1 -Unattended -Only mac.update-check -Allow mac.update-check
    pwsh tools/macos-verify.ps1 -Only mac.keychain,mac.notifications
    pwsh tools/macos-verify.ps1 -ListChecks -Preset release
    caffeinate -d -u -i pwsh tools/macos-verify.ps1 -Preset release   # the regression run before a release (T-208)
#>
#Requires -Version 7.2
[CmdletBinding()]
param(
    [switch] $Unattended,
    [string[]] $Allow,
    [string[]] $Only,
    [string[]] $Skip,
    [string] $Preset,
    [switch] $ListChecks,
    [switch] $SelfTest,
    [string] $OutputRoot
)

$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'VerifyTools.psm1') -Force
Import-Module (Join-Path $PSScriptRoot '..' 'scripts' 'ReleaseTools' 'ReleaseTools.psm1') -Force

$script:Repo = if ($IsMacOS) { Resolve-PhysicalPath (Join-Path $PSScriptRoot '..') } else { [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..')) }
$script:Lsregister = '/System/Library/Frameworks/CoreServices.framework/Frameworks/LaunchServices.framework/Support/lsregister'
$script:BundleId = Get-MacBundleIdentifier (Join-Path $script:Repo 'build' 'macos' 'Info.plist')
$script:TestBundleId = "$($script:BundleId).E2E"
$script:DotnetEnv = @{ DOTNET_CLI_UI_LANGUAGE = 'en'; DOTNET_NOLOGO = '1'; DOTNET_CLI_TELEMETRY_OPTOUT = '1' }
$script:PrinterId = 'verify'
# The timelapse checks (T-211): a simulated print of 12 layers, a frame every 5 s (the minimum), and the seconds per span
# the script cannot time exactly (the poll that notices a state, the start, the key frame each frame waits for).
$script:TimelapseLayers = 12
$script:TimelapseInterval = 5
$script:TimelapseSlack = 7
$script:Corners = @('TopLeft', 'TopRight', 'BottomLeft', 'BottomRight', 'Center')

# ---------------------------------------------------------------------------------------------------------------------
# Fixtures shared by the checks (built once per run, cleaned up at the end)
# ---------------------------------------------------------------------------------------------------------------------

function New-ShortId { [guid]::NewGuid().ToString('N').Substring(0, 8) }

function Get-AppVersion { Get-SourceVersion -PropsPath (Join-Path $script:Repo 'Directory.Build.props') }

function Get-ReleaseApp {
    <# .SYNOPSIS The release bundle of scripts/release-build.ps1 for this version, or $null. #>
    $app = Join-Path $script:Repo 'artifacts' 'release' (Get-AppVersion) 'app' 'osx-arm64' 'LayerPeek.app'
    if (Test-Path -LiteralPath $app) { $app } else { $null }
}

function Get-ReleaseBuildFiles {
    <#
    .SYNOPSIS
        The macOS downloads of scripts/release-build.ps1 for this version (T-217): the .dmg, the .tar.gz and
        SHA256SUMS-macos.txt in artifacts/release/<version>/dist, the commit of build-info.json, and Missing (the names of
        the absent files).
    #>
    $version = Get-AppVersion
    $names = Get-MacReleaseFileNames -Version $version -Runtime 'osx-arm64'
    $folder = Join-Path $script:Repo 'artifacts' 'release' $version
    $dist = Join-Path $folder 'dist'
    $files = [pscustomobject]@{
        Version   = $version
        DiskImage = Join-Path $dist $names.DiskImage
        Archive   = Join-Path $dist $names.Archive
        Sums      = Join-Path $dist (Get-ChecksumFileName 'macos')
        Commit    = 'unknown commit'
        Missing   = @()
    }
    $files.Missing = @($files.DiskImage, $files.Archive, $files.Sums | Where-Object { -not (Test-Path -LiteralPath $_ -PathType Leaf) } | ForEach-Object { Split-Path -Leaf $_ })
    try {
        $info = Get-Content -LiteralPath (Join-Path $folder 'build-info.json') -Raw | ConvertFrom-Json
        $files.Commit = "commit $("$($info.Commit)".Substring(0, [Math]::Min(12, "$($info.Commit)".Length)))$(if ($info.Dirty) { ', dirty tree' })"
    }
    catch { }
    $files
}

function Get-ReleaseBuildProblem {
    <# .SYNOPSIS Why the release downloads cannot be checked (with how to build them), or $null. #>
    $files = Get-ReleaseBuildFiles
    if ($files.Missing.Count -gt 0) { "no $($files.Missing -join ', ') in artifacts/release/$($files.Version)/dist; build it first: caffeinate -d -u -i pwsh scripts/release-build.ps1 -SkipFeedSignature (about 20 min)" }
}

function Get-RunningAppProblem {
    <# .SYNOPSIS Why the release app cannot be started now (scripts/aot-smoke.ps1 refuses beside a running LayerPeek), or $null. #>
    if (Get-Process -Name 'LayerPeek' -ErrorAction SilentlyContinue) { 'LayerPeek is running; quit it first (the release app is never started beside another instance)' }
}

function Dismount-VerifyImage([string] $MountPoint) {
    <# .SYNOPSIS Detaches the disk image attached at the mount point, if any (forced when the normal detach fails). #>
    if (-not (@(& /usr/bin/hdiutil info 2>$null) -match [regex]::Escape($MountPoint))) { return }
    $null = & /usr/bin/hdiutil detach $MountPoint -quiet 2>&1
    if ($LASTEXITCODE -ne 0) { $null = & /usr/bin/hdiutil detach $MountPoint -force -quiet 2>&1 }
}

function Get-Helper($Context) {
    if (-not $Context.Shared.ContainsKey('Helper')) {
        $folder = Join-Path $Context.RunFolder 'tmp'
        $null = New-Item -ItemType Directory -Force -Path $folder
        $binary = Join-Path $folder 'macos-verify-helper'
        $result = Invoke-VerifyTool -Context $Context -Label 'swiftc-helper' -FilePath '/usr/bin/xcrun' -ArgumentList @('swiftc', '-O', '-o', $binary, (Join-Path $PSScriptRoot 'macos-verify-helper.swift'))
        if ($result.ExitCode -ne 0) { throw "The helper did not compile (Xcode Command Line Tools installed? xcode-select --install): $($result.Output -join ' ')" }
        $Context.Shared.Helper = $binary
    }
    $Context.Shared.Helper
}

function Invoke-Helper($Context, [string[]] $Arguments, [switch] $AllowFailure) {
    $output = @(& (Get-Helper $Context) @Arguments 2>&1 | ForEach-Object { "$_" })
    if ($LASTEXITCODE -ne 0 -and -not $AllowFailure) { throw "macos-verify-helper $($Arguments -join ' ') failed: $($output -join ' ')" }
    $output
}

function Get-Screens($Context) { @((Invoke-Helper $Context @('screens')) -join "`n" | ConvertFrom-Json) }

function Get-ProcessWindows($Context, [int] $ProcessId) { @((Invoke-Helper $Context @('windows', "$ProcessId")) -join "`n" | ConvertFrom-Json) }

function Get-StatusItems($Context, [int] $ProcessId) {
    <# .SYNOPSIS The status items of a process from the Accessibility API, or $null without Accessibility for the terminal. #>
    if (-not (Test-Accessibility $Context)) { return $null }
    @((Invoke-Helper $Context @('statusitems', "$ProcessId")) -join "`n" | ConvertFrom-Json)
}

function Test-StatusItem($Context, [int] $ProcessId) {
    <#
    .SYNOPSIS
        Whether the process shows a status item in the menu bar: a window on layer 25 (macOS 15), or an item of its
        AXExtrasMenuBar (macOS 27 draws status items in no window of the process, T-135; needs Accessibility).
    #>
    if (Test-VerifyMenuBarItem @(Get-ProcessWindows $Context $ProcessId)) { return $true }
    [bool] (@(Get-StatusItems $Context $ProcessId) | Where-Object { $_ -and $_.width -gt 0 -and $_.height -gt 0 })
}

function Get-StatusMenu($Context, [int] $ProcessId) {
    <# .SYNOPSIS The menu of the process's status item (helper 'menu': opened, read with its submenus, closed again; T-209). #>
    (Invoke-Helper $Context @('menu', "$ProcessId")) -join "`n" | ConvertFrom-Json -Depth 30
}

function Invoke-MenuPress($Context, [int] $ProcessId, [string] $Path) {
    <# .SYNOPSIS Chooses a menu item by its path of titles (helper 'menupress'); ExitCode and Output (the titles of the level on a miss). #>
    $output = @(& (Get-Helper $Context) 'menupress' "$ProcessId" $Path 2>&1 | ForEach-Object { "$_" })
    [pscustomobject]@{ ExitCode = $LASTEXITCODE; Output = $output }
}

function Get-OpenMenuWindows($Context, [int] $ProcessId) {
    <# .SYNOPSIS The process's on-screen windows on the pop-up menu level (101): an open menu. #>
    @(Get-ProcessWindows $Context $ProcessId | Where-Object { $_.onscreen -and $_.layer -eq 101 })
}

function Get-SessionLimitation($Context) {
    <# .SYNOPSIS Why window checks cannot be judged in this session (locked screen, not on the console), or $null. #>
    try { Get-VerifySessionLimitation ((Invoke-Helper $Context @('session')) -join "`n" | ConvertFrom-Json) } catch { $null }
}

function Invoke-SessionCheck($Context, [scriptblock] $Body) {
    <#
    .SYNOPSIS
        Runs a check that needs an unlocked session on the console (front app, status item, full-screen Space). Its FAIL
        becomes a SKIP with the reason (and the FAIL text) when the screen was locked before or after; see
        Resolve-VerifySessionResult.
    #>
    $before = Get-SessionLimitation $Context
    $result = @(& $Body | Where-Object { $_.PSObject.TypeNames -contains 'LayerPeek.VerifyResult' })[-1]
    if (-not $result) { $result = New-VerifyResult FAIL 'The check returned no result (a bug of the check).' }
    Resolve-VerifySessionResult -Result $result -Limitations @($before, (Get-SessionLimitation $Context))
}

function Test-Accessibility($Context) { (Invoke-Helper $Context @('trusted')) -contains 'true' }

function Get-DotnetRoot {
    # "Microsoft.NETCore.App 11.0.0 [<root>/shared/Microsoft.NETCore.App]"
    foreach ($line in @(& dotnet --list-runtimes 2>$null)) {
        if ($line -match '^Microsoft\.NETCore\.App \S+ \[(?<path>.+)\]$') { return Split-Path -Parent (Split-Path -Parent $Matches.path) }
    }
    throw 'dotnet --list-runtimes lists no Microsoft.NETCore.App.'
}

function Get-DebugApp($Context) {
    if (-not $Context.Shared.ContainsKey('DebugApp')) {
        $result = Invoke-VerifyTool -Context $Context -Label 'build-debug-app' -FilePath 'dotnet' -ArgumentList @('build', (Join-Path $script:Repo 'src' 'LayerPeek.App' 'LayerPeek.App.csproj'), '-c', 'Debug') -Environment $script:DotnetEnv
        if ($result.ExitCode -ne 0) { throw 'dotnet build of the Debug app failed (see build-debug-app.txt).' }
        $Context.Shared.DebugApp = Join-Path $script:Repo 'src' 'LayerPeek.App' 'bin' 'Debug' 'net11.0'
    }
    $Context.Shared.DebugApp
}

function Get-SimulatorDll($Context) {
    if (-not $Context.Shared.ContainsKey('SimulatorDll')) {
        $result = Invoke-VerifyTool -Context $Context -Label 'build-simulator' -FilePath 'dotnet' -ArgumentList @('build', (Join-Path $script:Repo 'tools' 'PrusaSimulator' 'PrusaSimulator.csproj'), '-c', 'Debug') -Environment $script:DotnetEnv
        if ($result.ExitCode -ne 0) { throw 'dotnet build of the PrusaSimulator failed.' }
        $Context.Shared.SimulatorDll = Join-Path $script:Repo 'tools' 'PrusaSimulator' 'bin' 'Debug' 'net11.0' 'PrusaSimulator.dll'
    }
    $Context.Shared.SimulatorDll
}

function Get-RegisteredTestBundles {
    <# .SYNOPSIS The paths LaunchServices knows with the test bundle identifier (the rule of MacNotificationsE2ETests.RegisteredPaths). #>
    $paths = [System.Collections.Generic.List[string]]::new()
    $path = $null
    foreach ($raw in @(& $script:Lsregister -dump 2>$null)) {
        $line = "$raw".Trim()
        if ($line.StartsWith('----')) { $path = $null }
        elseif ($line.StartsWith('path:')) { $path = ($line.Substring(5).Trim() -replace '\s+\(0x[0-9a-fA-F]+\)$', '') }
        elseif ($path -and $line -match '^identifier:\s*(?<id>\S+)$' -and $Matches.id -ceq $script:TestBundleId -and -not $paths.Contains($path)) { $paths.Add($path) }
    }
    $paths.ToArray()
}

function Get-TestBundle($Context) {
    <# .SYNOPSIS "LayerPeek E2E.app" of the Debug app (as MacNotificationsE2ETests builds it), signed ad hoc and registered. #>
    if ($Context.Shared.ContainsKey('Bundle')) { Register-TestBundle $Context.Shared.Bundle; return $Context.Shared.Bundle }
    $source = Get-DebugApp $Context
    $folder = Join-Path $script:Repo 'artifacts' 'verify-bundle' (Split-Path -Leaf $Context.RunFolder)
    $app = Join-Path $folder 'LayerPeek E2E.app'
    $macOS = Join-Path $app 'Contents' 'MacOS'
    $bundle = [pscustomobject]@{ Path = $app; Executable = Join-Path $macOS 'LayerPeek'; Folder = $folder }
    Add-VerifyCleanup $Context 'test bundle' { param($b) Remove-TestBundle $b } -State $bundle -RunLast
    $null = New-Item -ItemType Directory -Force -Path $macOS
    $root = (Resolve-Path -LiteralPath $source).ProviderPath
    foreach ($file in Get-ChildItem -LiteralPath $root -Recurse -File) {
        $relative = [System.IO.Path]::GetRelativePath($root, $file.FullName)
        # The native libraries of other platforms are not needed (and not worth signing).
        if ($relative.StartsWith('runtimes/') -and -not $relative.StartsWith('runtimes/osx')) { continue }
        $target = Join-Path $macOS $relative
        $null = New-Item -ItemType Directory -Force -Path (Split-Path -Parent $target)
        [System.IO.File]::Copy($file.FullName, $target)
    }
    $plist = ConvertTo-VerifyTestInfoPlist -ProductionText (Get-Content -LiteralPath (Join-Path $script:Repo 'build' 'macos' 'Info.plist') -Raw) -ProductionIdentifier $script:BundleId
    [System.IO.File]::WriteAllText((Join-Path $app 'Contents' 'Info.plist'), $plist, [System.Text.UTF8Encoding]::new($false))
    $identifier = (& /usr/bin/plutil -extract CFBundleIdentifier raw (Join-Path $app 'Contents' 'Info.plist')) -join ''
    if ($identifier -cne $script:TestBundleId) { throw "The test bundle got the identifier '$identifier'; refusing to run it." }
    $files = @(Get-ChildItem -LiteralPath $macOS -Recurse -File | Where-Object FullName -NE $bundle.Executable | ForEach-Object FullName)
    for ($i = 0; $i -lt $files.Count; $i += 40) {
        $chunk = @($files[$i..([math]::Min($i + 39, $files.Count - 1))])
        $null = & /usr/bin/codesign --force --sign - --timestamp=none @chunk 2>&1
        if ($LASTEXITCODE -ne 0) { throw 'codesign of the test bundle files failed.' }
    }
    $null = & /usr/bin/codesign --force --sign - --timestamp=none $app 2>&1
    $null = & /usr/bin/codesign --verify --strict $app 2>&1
    if ($LASTEXITCODE -ne 0) { throw 'The signature of the test bundle does not verify.' }
    $Context.Shared.Bundle = $bundle
    Register-TestBundle $bundle
    $bundle
}

function Register-TestBundle($Bundle) {
    # Another registration of the test identifier (the E2E tests, a crashed run) makes the notification center refuse this one.
    foreach ($stale in Get-RegisteredTestBundles) { if ($stale -cne $Bundle.Path) { $null = & $script:Lsregister -u $stale 2>&1 } }
    $null = & $script:Lsregister -f $Bundle.Path 2>&1
}

function Get-BundleProcessIds($Bundle) {
    $ids = foreach ($line in @(& /bin/ps -axww -o 'pid=,command=' 2>$null)) {
        if ("$line" -match '^\s*(?<pid>\d+)\s+(?<command>.+)$') {
            $command = $Matches.command
            if ($command.StartsWith($Bundle.Executable) -or $command.StartsWith("/private$($Bundle.Executable)")) { [int] $Matches.pid }
        }
    }
    @($ids)
}

function Invoke-BundleTool($Bundle, [string[]] $Arguments, [hashtable] $Environment = @{}) {
    $saved = @{}
    $all = @{ DOTNET_ROOT = (Get-DotnetRoot) } + $Environment
    foreach ($name in $all.Keys) { $saved[$name] = [Environment]::GetEnvironmentVariable($name); [Environment]::SetEnvironmentVariable($name, $all[$name]) }
    try { $output = @(& $Bundle.Executable @Arguments 2>&1 | ForEach-Object { "$_" }); $code = $LASTEXITCODE }
    finally { foreach ($name in $saved.Keys) { [Environment]::SetEnvironmentVariable($name, $saved[$name]) } }
    [pscustomobject]@{ ExitCode = $code; Output = $output }
}

function Get-TestNotificationStatus($Context) {
    $bundle = Get-TestBundle $Context
    $result = Invoke-BundleTool $bundle @('--layerpeek-e2e-notification-status')
    $status = Get-VerifyNotificationStatus $result.Output
    if (-not $status) { throw "The status mode of the test bundle answered: $($result.Output -join ' ')" }
    $status
}

function Remove-TestBundle($Bundle) {
    foreach ($id in Get-BundleProcessIds $Bundle) { Stop-Process -Id $id -Force -ErrorAction SilentlyContinue }
    if (Test-Path -LiteralPath $Bundle.Executable) { $null = Invoke-BundleTool $Bundle @('--layerpeek-e2e-clear-toasts') }
    $null = & $script:Lsregister -u $Bundle.Path 2>&1
    $null = & /usr/bin/defaults read $script:TestBundleId 2>&1
    if ($LASTEXITCODE -eq 0) { $null = & /usr/bin/defaults delete $script:TestBundleId 2>&1 }
    $library = Join-Path ([Environment]::GetFolderPath('UserProfile')) 'Library'
    # These places belong to the test identifier only ('defaults delete' leaves an empty plist behind).
    foreach ($place in @("Preferences/$($script:TestBundleId).plist", "Saved Application State/$($script:TestBundleId).savedState", "Caches/$($script:TestBundleId)", "HTTPStorages/$($script:TestBundleId)")) {
        $path = Join-Path $library $place
        if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path -Recurse -Force }
    }
    if (Test-Path -LiteralPath $Bundle.Folder) { Remove-Item -LiteralPath $Bundle.Folder -Recurse -Force }
    $parent = Split-Path -Parent $Bundle.Folder
    if ((Test-Path -LiteralPath $parent) -and -not (Get-ChildItem -LiteralPath $parent -Force)) { Remove-Item -LiteralPath $parent -Force }
}

function Get-Simulator($Context, [double] $Speed = 1, [string] $Clip, [int] $Layers, [double] $Fps, [string] $Instance) {
    <#
    .SYNOPSIS
        The PrusaSimulator (one printer, camera) on 127.0.0.1 with free ports, started once per run, time speed-up
        (-Speed, e.g. 20 for a print that moves in seconds, T-209), camera clip (-Clip, default the built-in one) and the
        layers of a print (-Layers: the Z axis rises in that many 0.2 mm steps, T-211; default smoothly), camera frame
        rate (-Fps, default the simulator's 25) and -Instance: a name for a second simulator of the same options (T-216).
    #>
    $key = if ($Speed -eq 1) { 'Simulator' } else { "Simulator-x$Speed" }
    if ($Clip) { $key += "-$Clip" }
    if ($Layers) { $key += "-layers$Layers" }
    if ($Fps) { $key += "-fps$Fps" }
    if ($Instance) { $key += "-$Instance" }
    if ($Context.Shared.ContainsKey($key)) { return $Context.Shared[$key] }
    $dll = Get-SimulatorDll $Context
    $folder = Join-Path $Context.RunFolder 'tmp' "simulator-$(New-ShortId)"
    $null = New-Item -ItemType Directory -Force -Path $folder
    $out = Join-Path $folder 'stdout.txt'
    $start = [System.Diagnostics.ProcessStartInfo]::new('/bin/sh')
    foreach ($argument in @('-c', 'exec dotnet "$0" "$@" >"$LPV_OUT" 2>&1 </dev/null', $dll, '--port', '0', '--camera-port', '0', '--scenario', 'printing', '--speed', "$Speed")) { $start.ArgumentList.Add($argument) }
    if ($Clip) { foreach ($argument in '--camera-clip', $Clip) { $start.ArgumentList.Add($argument) } }
    if ($Layers) { foreach ($argument in '--layers', "$Layers") { $start.ArgumentList.Add($argument) } }
    if ($Fps) { foreach ($argument in '--fps', (Format-VerifyInvariantNumber $Fps)) { $start.ArgumentList.Add($argument) } }
    $start.Environment['LPV_OUT'] = $out
    $start.UseShellExecute = $false
    $process = [System.Diagnostics.Process]::Start($start)
    Add-VerifyCleanup $Context 'simulator' { param($p) if (-not $p.HasExited) { $p.Kill($true); $p.WaitForExit() } } -State $process -RunLast
    $ready = Wait-VerifyCondition -TimeoutSeconds 60 -Condition { (Test-Path -LiteralPath $out) -and (Select-String -LiteralPath $out -Pattern 'Camera:' -Quiet) }
    if (-not $ready) { throw "The simulator did not start: $(Get-Content -LiteralPath $out -Raw -ErrorAction SilentlyContinue)" }
    $text = Get-Content -LiteralPath $out -Raw
    $simulator = [pscustomobject]@{
        BaseUri  = ([regex]::Match($text, 'Address:\s+(\S+)')).Groups[1].Value
        User     = ([regex]::Match($text, 'User name:\s+(\S+)')).Groups[1].Value
        Password = ([regex]::Match($text, 'Password:\s+(\S+)')).Groups[1].Value
        Camera   = ([regex]::Match($text, 'Camera:\s+(\S+)')).Groups[1].Value
        Output   = $out
    }
    $simulator | Add-Member -NotePropertyName Address -NotePropertyValue (([uri] $simulator.BaseUri).Authority)
    $simulator | Add-Member -NotePropertyName CameraPort -NotePropertyValue (([uri] $simulator.Camera).Port)
    $Context.Shared[$key] = $simulator
    $simulator
}

function Set-SimulatorScenario($Simulator, [string] $Scenario) {
    $null = Invoke-RestMethod -Method Post -Uri "$($Simulator.BaseUri.TrimEnd('/'))/_simulator/scenario/$Scenario" -TimeoutSec 10
}

function Get-SimulatorPrinterState($Simulator) {
    <# .SYNOPSIS The printer state the simulator reports to the app now (PRINTING, PAUSED, FINISHED, ...; T-211), or $null. #>
    try { (Invoke-RestMethod -Uri "$($Simulator.BaseUri.TrimEnd('/'))/api/v1/status" -Headers @{ 'X-Api-Key' = $Simulator.Password } -TimeoutSec 10).printer.state }
    catch { $null }
}

function Set-SimulatorCamera($Simulator, [ValidateSet('offline', 'online')] [string] $State) {
    <# .SYNOPSIS Switches the simulator's camera off (drops the clients, refuses new ones) or on again (T-210). #>
    $null = Invoke-RestMethod -Method Post -Uri "$($Simulator.BaseUri.TrimEnd('/'))/_simulator/camera/$State" -TimeoutSec 10
}

function Get-SamplesFolder {
    <# .SYNOPSIS The local real camera samples (never in the repository): $env:LAYERPEEK_SAMPLES or ~/LayerPeek-samples. #>
    if ($env:LAYERPEEK_SAMPLES) { $env:LAYERPEEK_SAMPLES } else { Join-Path ([Environment]::GetFolderPath('UserProfile')) 'LayerPeek-samples' }
}

function Get-Buddy3DClip {
    <#
    .SYNOPSIS
        The 60 s clip of a real Buddy3D camera from the samples, or $null (user decision 2026-11-13, T-210 question 2: a
        simulator recording of this clip stands in for a recording of the real camera).
    #>
    $clip = Join-Path (Get-SamplesFolder) 'clips' 'coreone-20261103-1905-60s.mp4'
    if (Test-Path -LiteralPath $clip -PathType Leaf) { $clip } else { $null }
}

function New-TestData {
    <#
    .SYNOPSIS
        A test data folder (settings.json, test-secrets.txt) of the isolated test mode and its instance suffix. -Camera adds
        the camera of the printer, -Window its camera.window section. Removed at the end (after the autostart stand-ins).
    #>
    param($Context, $Simulator, [string] $Camera, [System.Collections.IDictionary] $Window, [System.Collections.IDictionary] $CameraOptions,
        [string] $RecordingFolder, [System.Collections.IDictionary] $RecordingOptions, [int] $PollingSeconds = 5, [switch] $SharedTemp, [switch] $Notifications,
        [ValidateSet('Off', 'ImportantOnly', 'All')] [string] $Sound = 'Off', [switch] $Detection, [System.Collections.IDictionary] $NotificationOptions,
        [string] $Language = 'en', [System.Collections.IDictionary] $SettingsOptions, [System.Collections.IDictionary] $PrinterOptions)
    $base = if ($SharedTemp) { '/tmp' } else { [System.IO.Path]::GetTempPath() }
    $data = Join-Path $base "lpv-$(New-ShortId)"
    $null = New-Item -ItemType Directory -Force -Path $data
    $data = (Resolve-Path -LiteralPath $data).ProviderPath
    $suffix = "v$(New-ShortId)"
    $settings = [ordered]@{
        version                = 1
        pollingIntervalSeconds = $PollingSeconds
        language               = $Language
        notifications          = [ordered]@{ enabled = [bool] $Notifications; remainingTimeMinutes = 0; sound = $Sound }
        printers               = @()
    }
    # -NotificationOptions (T-215): e.g. offline = $false next to the master switch.
    if ($NotificationOptions) { foreach ($name in $NotificationOptions.Keys) { $settings.notifications[$name] = $NotificationOptions[$name] } }
    # -SettingsOptions (T-216): top-level values, e.g. debugLogging, cameraHardwareDecoding.
    if ($SettingsOptions) { foreach ($name in $SettingsOptions.Keys) { $settings[$name] = $SettingsOptions[$name] } }
    if ($Simulator) {
        $printer = [ordered]@{ id = $script:PrinterId; name = "LP verify $(New-ShortId)"; connectionType = 'PrusaLink'; address = $Simulator.Address; userName = $Simulator.User }
        if ($Camera) {
            $printer.camera = [ordered]@{ enabled = $true; streamUrl = $Camera }
            if ($Window) { $printer.camera.window = $Window }
            # T-210: e.g. recordPrints, dashcam.
            if ($CameraOptions) { foreach ($name in $CameraOptions.Keys) { $printer.camera[$name] = $CameraOptions[$name] } }
        }
        # -Detection (T-212): print failure detection on, for the detector of the test mode (LAYERPEEK_TEST_FAKE_DETECTOR).
        if ($Detection) { $printer.detection = [ordered]@{ enabled = $true } }
        # -PrinterOptions (T-216): e.g. image = @{ mode = 'Model'; model = 'CoreOne' }.
        if ($PrinterOptions) { foreach ($name in $PrinterOptions.Keys) { $printer[$name] = $PrinterOptions[$name] } }
        $settings.printers = @($printer)
        [System.IO.File]::WriteAllText((Join-Path $data 'test-secrets.txt'), "printer/$($script:PrinterId)`t$($Simulator.Password)`n")
    }
    # -RecordingFolder: the recordings go into this folder of the test data (T-210), never into ~/Movies/LayerPeek.
    # An absolute -RecordingFolder is taken as it is (T-212: a folder on a volume that is not mounted).
    $recordings = if (-not $RecordingFolder) { $null } elseif ([System.IO.Path]::IsPathRooted($RecordingFolder)) { $RecordingFolder } else { Join-Path $data $RecordingFolder }
    if ($recordings) { $settings.recording = [ordered]@{ folder = $recordings } }
    if ($Detection) { $settings.detection = [ordered]@{ enabled = $true } }
    # -RecordingOptions (T-211): e.g. timelapseIntervalSeconds, timelapseTrigger, next to the folder.
    if ($RecordingOptions) {
        if (-not $settings.Contains('recording')) { $settings.recording = [ordered]@{} }
        foreach ($name in $RecordingOptions.Keys) { $settings.recording[$name] = $RecordingOptions[$name] }
    }
    [System.IO.File]::WriteAllText((Join-Path $data 'settings.json'), ($settings | ConvertTo-Json -Depth 8))
    $testData = [pscustomobject]@{
        Context = $Context
        Data   = $data
        Recordings = if ($recordings) { $recordings } else { Join-Path $data 'Recordings' }
        Suffix = $suffix
        Socket = Get-VerifyInstanceSocketPath -DataDirectory $data -Suffix $suffix -TempDirectory ([System.IO.Path]::GetTempPath()) -UserId ([int] (& /usr/bin/id -u))
        Logs   = Join-Path $data 'logs'
    }
    Add-VerifyCleanup $Context "test data $data" {
        param($s)
        # The stand-in Login Item of the test mode (T-019), then the folder.
        if ($s.Shared.ContainsKey('Bundle') -and (Test-Path -LiteralPath $s.Shared.Bundle.Executable)) { $null = Invoke-BundleTool $s.Shared.Bundle @('--layerpeek-e2e-clear-autostart') @{ LAYERPEEK_TEST_DATA_DIR = $s.Data; LAYERPEEK_INSTANCE_SUFFIX = $s.Suffix } }
        if (Test-Path -LiteralPath $s.Data) { Remove-Item -LiteralPath $s.Data -Recurse -Force }
    } -State ([pscustomobject]@{ Shared = $Context.Shared; Data = $data; Suffix = $suffix })
    $testData
}

function Add-TestPrinter($TestData, $Simulator, [string] $Id) {
    <#
    .SYNOPSIS
        Adds a printer (-Id) of another simulator with its camera to the settings.json and test-secrets.txt of a test data
        folder before the app starts (T-216: two cameras); returns its name.
    #>
    $file = Join-Path $TestData.Data 'settings.json'
    $settings = Get-Content -LiteralPath $file -Raw | ConvertFrom-Json -AsHashtable
    $name = "LP verify $(New-ShortId)"
    $settings.printers = @($settings.printers) + @([ordered]@{ id = $Id; name = $name; connectionType = 'PrusaLink'; address = $Simulator.Address; userName = $Simulator.User; camera = [ordered]@{ enabled = $true; streamUrl = $Simulator.Camera } })
    [System.IO.File]::WriteAllText($file, ($settings | ConvertTo-Json -Depth 8))
    [System.IO.File]::AppendAllText((Join-Path $TestData.Data 'test-secrets.txt'), "printer/$Id`t$($Simulator.Password)`n")
    $name
}

function Read-TestLog($TestData) {
    if (-not (Test-Path -LiteralPath $TestData.Logs)) { return '' }
    (Get-ChildItem -LiteralPath $TestData.Logs -File | Sort-Object Name | ForEach-Object {
            $stream = [System.IO.FileStream]::new($_.FullName, 'Open', 'Read', 'ReadWrite, Delete')
            try { [System.IO.StreamReader]::new($stream).ReadToEnd() } finally { $stream.Dispose() }
        }) -join "`n"
}

function Start-TestApp {
    <#
    .SYNOPSIS
        Starts the test bundle's executable in the test mode as a child of this script (its responsible process is the
        terminal: no Local Network question for 127.0.0.1 or the LAN) and waits for its instance socket.
    #>
    param($Context, $TestData, [hashtable] $Environment = @{}, [switch] $NoWait)
    $bundle = Get-TestBundle $Context
    $start = [System.Diagnostics.ProcessStartInfo]::new('/bin/sh')
    foreach ($argument in @('-c', 'exec "$0" >>"$LPV_OUT" 2>&1 </dev/null', $bundle.Executable)) { $start.ArgumentList.Add($argument) }
    foreach ($name in @($start.Environment.Keys | Where-Object { $_ -like 'LAYERPEEK_*' })) { $null = $start.Environment.Remove($name) }
    $start.Environment['DOTNET_ROOT'] = Get-DotnetRoot
    $start.Environment['LAYERPEEK_TEST_DATA_DIR'] = $TestData.Data
    $start.Environment['LAYERPEEK_INSTANCE_SUFFIX'] = $TestData.Suffix
    $start.Environment['LPV_OUT'] = Join-Path $TestData.Data 'stdout.txt'
    foreach ($name in $Environment.Keys) { $start.Environment[$name] = $Environment[$name] }
    $start.UseShellExecute = $false
    $process = [System.Diagnostics.Process]::Start($start)
    Add-VerifyCleanup $Context "test app $($process.Id)" { param($p) if (-not $p.HasExited) { $p.Kill(); $p.WaitForExit() } } -State $process
    if (-not $NoWait) {
        if (-not (Wait-VerifyCondition -TimeoutSeconds 30 -Condition { $process.HasExited -or (Test-Path -LiteralPath $TestData.Socket) })) { throw "The test app did not open its instance channel within 30 s. Log:`n$(Read-TestLog $TestData)" }
        if ($process.HasExited) { throw "The test app ended with $($process.ExitCode). Log:`n$(Read-TestLog $TestData)" }
        Start-Sleep -Seconds 2
        if ((Read-TestLog $TestData) -match 'No active display') {
            # T-096: the app waits for a display before its UI starts; nothing of the UI can be checked meanwhile.
            $null = Stop-TestApp $TestData $process
            Skip-VerifyCheck 'every display is asleep (the app waits for one before its UI starts): wake the display and run the script under caffeinate -d -u.'
        }
    }
    $process
}

function Start-TestAppViaOpen {
    <#
    .SYNOPSIS
        Starts the test bundle through LaunchServices (open -n), as macOS requires for notifications; returns the pid.
        -NoWait returns right after the start (no wait for the instance socket, no skip while every display sleeps).
    #>
    param($Context, $TestData, [hashtable] $Environment = @{}, [switch] $NoWait)
    $bundle = Get-TestBundle $Context
    $before = @(Get-BundleProcessIds $bundle)
    $arguments = @('-n', '-F', '-g', '--stdout', (Join-Path $TestData.Data 'stdout.txt'), '--stderr', (Join-Path $TestData.Data 'stderr.txt'))
    $all = [ordered]@{ LAYERPEEK_TEST_DATA_DIR = $TestData.Data; LAYERPEEK_INSTANCE_SUFFIX = $TestData.Suffix; DOTNET_ROOT = (Get-DotnetRoot) }
    foreach ($name in $Environment.Keys) { $all[$name] = $Environment[$name] }
    foreach ($name in $all.Keys) { $arguments += @('--env', "$name=$($all[$name])") }
    & /usr/bin/open @arguments $bundle.Path
    if ($LASTEXITCODE -ne 0) { throw 'open of the test bundle failed.' }
    $found = 0
    $null = Wait-VerifyCondition -TimeoutSeconds 20 -Condition { $script:candidate = @(Get-BundleProcessIds $bundle | Where-Object { $before -notcontains $_ }); $script:candidate.Count -gt 0 }
    if ($script:candidate.Count -gt 0) { $found = $script:candidate[0] }
    if (-not $found) { throw 'The test app did not start through open.' }
    Add-VerifyCleanup $Context "test app $found" { param($id) Stop-Process -Id $id -Force -ErrorAction SilentlyContinue } -State $found
    if ($NoWait) { return $found }
    $null = Wait-VerifyCondition -TimeoutSeconds 30 -Condition { Test-Path -LiteralPath $TestData.Socket }
    Start-Sleep -Seconds 2
    if ((Read-TestLog $TestData) -match 'No active display') {
        Skip-VerifyCheck 'every display is asleep (the app waits for one before its UI starts): wake the display and run the script under caffeinate -d -u.'
    }
    $found
}

function Send-TestCommand($TestData, [string] $Command) {
    if (-not (Send-VerifyInstanceCommand -SocketPath $TestData.Socket -Command $Command)) { throw "The test app did not acknowledge the command '$Command'." }
}

function Stop-TestApp($TestData, $Process) {
    <#
    .SYNOPSIS
        Quits through the test command (like the menu item Quit); kills after 20 s. Keeps the app's log as evidence of the
        running check. Returns the exit code.
    #>
    $code = if ($Process.HasExited) { $Process.ExitCode }
    else {
        $null = Send-VerifyInstanceCommand -SocketPath $TestData.Socket -Command 'quit'
        if ($Process.WaitForExit(20000)) { $Process.ExitCode } else { $Process.Kill(); $Process.WaitForExit(); -1 }
    }
    $null = Save-VerifyEvidence -Context $TestData.Context -Name "app-$($TestData.Suffix).log" -Content (Read-TestLog $TestData)
    $code
}

function Get-FrontProcessId {
    $asn = Get-VerifyFrontAsn @(& /usr/bin/lsappinfo front 2>$null)
    if (-not $asn) { return $null }
    Get-VerifyLsappinfoPid @(& /usr/bin/lsappinfo info -only pid $asn 2>$null)
}

function Get-FrontAppText {
    <# .SYNOPSIS "name (pid)" of the front app for a FAIL text, e.g. "loginwindow (179)" with a locked screen. #>
    $frontId = Get-FrontProcessId
    if (-not $frontId) { return 'none' }
    $name = (Get-Process -Id $frontId -ErrorAction SilentlyContinue).ProcessName
    "$(if ($name) { $name } else { '?' }) ($frontId)"
}

function Invoke-FinderToFront {
    <#
    .SYNOPSIS
        Brings Finder (always running) to the front as "the other app"; asks again every 3 s for up to 12 s, because a
        single 'open -a Finder' is sometimes lost while another app is activating. Returns whether Finder is in front.
    #>
    for ($attempt = 0; $attempt -lt 4; $attempt++) {
        & /usr/bin/open -a Finder
        $finder = @(Get-Process -Name 'Finder' -ErrorAction SilentlyContinue | ForEach-Object Id)
        if (Wait-VerifyCondition -TimeoutSeconds 3 -Condition { $finder -contains (Get-FrontProcessId) }) { return $true }
    }
    $false
}

function Get-CameraWindow($Context, [int] $ProcessId) {
    # The normal window is layer 0; the mini preview (always on top) a floating level; menu bar items (25) never count.
    @(Get-ProcessWindows $Context $ProcessId | Where-Object { $_.onscreen -and $_.layer -ge 0 -and $_.layer -lt 25 -and $_.width -ge 80 -and $_.height -ge 45 } |
            Sort-Object { $_.width * $_.height } -Descending) | Select-Object -First 1
}

function Test-FullScreen($Window, $Screens) {
    [bool] ($Screens | Where-Object { [math]::Abs($_.frame.x - $Window.x) -le 2 -and [math]::Abs($_.frame.y - $Window.y) -le 2 -and [math]::Abs($_.frame.width - $Window.width) -le 2 -and [math]::Abs($_.frame.height - $Window.height) -le 2 })
}

function Get-RtspConnections([int] $ProcessId, [int] $Port) {
    @(& /usr/sbin/lsof -a -n -P -p $ProcessId "-iTCP:$Port" -sTCP:ESTABLISHED 2>$null | Select-Object -Skip 1)
}

function Invoke-Decode($Context, [string] $File, [string] $Evidence) {
    <#
    .SYNOPSIS
        The helper's 'decode' of a recording (T-210, AVFoundation as QuickTime Player plays it): Report (its JSON, $null when
        it printed none), ExitCode, Error (stderr) and Problems (Get-VerifyDecodeProblems without expectations). The JSON is
        kept as evidence -Evidence.
    #>
    $errors = Join-Path $Context.RunFolder 'tmp' "decode-$(New-ShortId).txt"
    $text = @(& (Get-Helper $Context) 'decode' $File 2>$errors) -join "`n"
    $code = $LASTEXITCODE
    $stderr = if (Test-Path -LiteralPath $errors) { (Get-Content -LiteralPath $errors -Raw); Remove-Item -LiteralPath $errors -Force } else { '' }
    $report = $null
    if ($text) { try { $report = $text | ConvertFrom-Json -Depth 10 } catch { $report = $null } }
    $null = Save-VerifyEvidence -Context $Context -Name $Evidence -Content @($text, $(if ($stderr) { "stderr: $stderr" }))
    [pscustomobject]@{ Report = $report; ExitCode = $code; Error = "$stderr".Trim(); Problems = @(Get-VerifyDecodeProblems -Report $report -ExitCode $code) }
}

function Get-RecordingFiles([string] $Folder) {
    <# .SYNOPSIS The .mp4 files below a recordings folder in the order they were created (name: "-2" sorts before ".mp4"). #>
    if (-not (Test-Path -LiteralPath $Folder)) { return @() }
    @(Get-ChildItem -LiteralPath $Folder -Recurse -File -Filter '*.mp4' | Sort-Object CreationTimeUtc, Name)
}

function Get-MoviesRecordingsState {
    <# .SYNOPSIS The files of ~/Movies/LayerPeek (path and size), or 'missing': a run must leave them as they were (T-210). #>
    $folder = Join-Path ([Environment]::GetFolderPath('UserProfile')) 'Movies' 'LayerPeek'
    if (-not (Test-Path -LiteralPath $folder)) { return 'missing' }
    (@(Get-ChildItem -LiteralPath $folder -Recurse -Force | ForEach-Object { "$([System.IO.Path]::GetRelativePath($folder, $_.FullName)) $(if ($_.PSIsContainer) { 'dir' } else { $_.Length })" }) | Sort-Object) -join "`n"
}

function Wait-RecordingEvent($TestData, [string] $Kind, [int] $Skip, [double] $TimeoutSeconds, [string] $ExcludeFile) {
    <#
    .SYNOPSIS
        Waits for the recording event -Kind of the app's log (Get-VerifyRecordingEvents) after the first -Skip of that kind;
        returns it, or $null after -TimeoutSeconds. -ExcludeFile (a wildcard) leaves out the events of such files: a
        timelapse file logs "writes" like a recording (2026-11-15: the "continuation" of a print with both was its timelapse).
    #>
    $script:recordingEvent = $null
    $null = Wait-VerifyCondition -TimeoutSeconds $TimeoutSeconds -IntervalMilliseconds 1000 -Condition {
        $script:recordingEvent = @(Get-VerifyRecordingEvents (Read-TestLog $TestData) | Where-Object { $_.Kind -ceq $Kind -and -not ($ExcludeFile -and $_.File -like $ExcludeFile) }) | Select-Object -Skip $Skip -First 1
        $null -ne $script:recordingEvent
    }
    $script:recordingEvent
}

function Get-RecordingEventCount($TestData, [string] $Kind) { @(Get-VerifyRecordingEvents (Read-TestLog $TestData) | Where-Object Kind -CEQ $Kind).Count }

function Test-Recordings($Context, [System.IO.FileInfo[]] $Files, [double] $MinSeconds = -1, [double] $MaxSeconds = -1) {
    <# .SYNOPSIS Decodes every file (helper 'decode'); returns problems ("name: problem") and notes ("name: frames, duration"). #>
    $problems = [System.Collections.Generic.List[string]]::new()
    $notes = [System.Collections.Generic.List[string]]::new()
    foreach ($file in $Files) {
        $result = Invoke-Decode $Context $file.FullName "decode-$($file.BaseName).json"
        foreach ($problem in Get-VerifyDecodeProblems -Report $result.Report -ExitCode $result.ExitCode -MinSeconds $MinSeconds -MaxSeconds $MaxSeconds) { $problems.Add("$($file.Name): $problem") }
        if ($result.Error -and -not $result.Report) { $problems.Add("$($file.Name): $($result.Error)") }
        if ($result.Report) { $notes.Add("$($file.Name): $($result.Report.decodedFrames) frames, $([math]::Round([double] $result.Report.duration, 1)) s") }
    }
    [pscustomobject]@{ Problems = $problems.ToArray(); Notes = $notes.ToArray() }
}

function Get-TimelapseFiles([string] $Folder) {
    <# .SYNOPSIS The timelapse files (…_timelapse.mp4, T-211) below a recordings folder, oldest first. #>
    @(Get-RecordingFiles $Folder | Where-Object { (ConvertFrom-VerifyRecordingFileName $_.Name).Kind -ceq 'timelapse' })
}

function Test-Timelapse($Context, [System.IO.FileInfo] $File, $Range) {
    <#
    .SYNOPSIS
        Decodes a timelapse (helper 'decode', T-210) and compares its decoded frames with -Range (Min, Max; none: only the
        decode). Problems and a Note ("name: N frames, D s").
    #>
    $result = Invoke-Decode $Context $File.FullName "decode-$($File.BaseName).json"
    $problems = [System.Collections.Generic.List[string]]::new()
    foreach ($problem in $result.Problems) { $problems.Add("$($File.Name): $problem") }
    if ($result.Error -and -not $result.Report) { $problems.Add("$($File.Name): $($result.Error)") }
    $note = $null
    if ($result.Report) {
        $frames = [int] $result.Report.decodedFrames
        $expected = if (-not $Range) { '' } elseif ($Range.Min -eq $Range.Max) { ", expected $($Range.Min)" } else { ", expected $($Range.Min) to $($Range.Max)" }
        if ($Range -and ($frames -lt $Range.Min -or $frames -gt $Range.Max)) { $problems.Add("$($File.Name): $frames decoded frame(s)$expected") }
        $note = "$($File.Name): $frames frames$expected, $([math]::Round([double] $result.Report.duration, 2)) s"
    }
    [pscustomobject]@{ Problems = $problems.ToArray(); Note = $note }
}

function Get-FileDialogMedia($Context) {
    <#
    .SYNOPSIS
        The media of mac.file-dialogs (T-213): a print recording with a camera outage (Recording …mp4, Continuation
        …-2.mp4) and a timelapse. Those of mac.recording-files and mac.timelapse-files of this run when they ran, otherwise
        one print of its own (simulator x20, recording and timelapse together, the camera off for 10 s). Made once per run.
    #>
    if ($Context.Shared.ContainsKey('FileDialogMedia')) { return $Context.Shared.FileDialogMedia }
    if ($Context.Shared.FileDialogRecording -and $Context.Shared.FileDialogTimelapse) {
        $media = [pscustomobject]@{
            Recording    = $Context.Shared.FileDialogRecording.Recording
            Continuation = $Context.Shared.FileDialogRecording.Continuation
            Timelapse    = $Context.Shared.FileDialogTimelapse
            Source       = 'the recording of mac.recording-files and the timelapse of mac.timelapse-files of this run'
        }
        $Context.Shared.FileDialogMedia = $media
        return $media
    }
    $simulator = Get-Simulator $Context -Speed 20 -Clip (Get-Buddy3DClip)
    Set-SimulatorScenario $simulator 'idle'
    $data = New-TestData -Context $Context -Simulator $simulator -Camera $simulator.Camera -CameraOptions ([ordered]@{ recordPrints = $true; timelapsePrints = $true }) -RecordingFolder 'Recordings' -RecordingOptions ([ordered]@{ timelapseIntervalSeconds = $script:TimelapseInterval }) -PollingSeconds 1
    $app = Start-TestApp -Context $Context -TestData $data
    try {
        Start-Sleep -Seconds 4
        Set-SimulatorScenario $simulator 'printing'
        $first = Wait-RecordingEvent $data 'Writes' 0 60 -ExcludeFile '*_timelapse*'
        if (-not $first) { throw 'The print of the file dialog media recorded nothing within 60 s.' }
        Start-Sleep -Seconds 25
        Set-SimulatorCamera $simulator 'offline'
        Start-Sleep -Seconds 10
        Set-SimulatorCamera $simulator 'online'
        $second = Wait-RecordingEvent $data 'Writes' 1 60 -ExcludeFile '*_timelapse*'
        if (-not $second) { throw 'The print of the file dialog media wrote no second file within 60 s after the camera came back.' }
        Start-Sleep -Seconds 25
    }
    # Quitting completes the recording and the timelapse.
    finally { $null = Stop-TestApp $data $app; Set-SimulatorCamera $simulator 'online'; Set-SimulatorScenario $simulator 'idle' }
    $all = @(Get-RecordingFiles $data.Recordings)
    $recording = @($all | Where-Object Name -CEQ $first.File)
    $continuation = @($all | Where-Object Name -CEQ $second.File)
    # The outage may split the timelapse as well: the longest one.
    $timelapse = @(Get-TimelapseFiles $data.Recordings | Sort-Object Length -Descending)
    if ($recording.Count -ne 1 -or $continuation.Count -ne 1 -or $timelapse.Count -eq 0) { throw "The print of the file dialog media left $(@($all.Name) -join ', ') (expected $($first.File), $($second.File) and a timelapse)." }
    $media = [pscustomobject]@{ Recording = $recording[0]; Continuation = $continuation[0]; Timelapse = $timelapse[0]; Source = 'a print of its own (mac.recording-files and mac.timelapse-files did not run before)' }
    $Context.Shared.FileDialogMedia = $media
    $media
}

function Get-StatusToolTipLines($Context, [int] $ProcessId) {
    <# .SYNOPSIS The lines of the status item's tooltip (AXHelp from 'statusitems'), or an empty list. #>
    $tip = @(Get-StatusItems $Context $ProcessId | Where-Object { $_.width -gt 0 }) | Select-Object -First 1 | ForEach-Object help
    @("$tip" -split "`n" | Where-Object { $_ })
}

function Find-MenuEntry($Items, [string] $Title) {
    <#
    .SYNOPSIS
        The item of the helper's 'menu' JSON (or a submenu) with that exact title, or $null; no-break spaces count as
        plain spaces (NSMenu shows them so to the Accessibility API, ConvertTo-VerifyMenuTitle).
    #>
    $wanted = ConvertTo-VerifyMenuTitle $Title
    @(Get-VerifyMenuEntries $Items | Where-Object { (ConvertTo-VerifyMenuTitle $_.title) -ceq $wanted }) | Select-Object -First 1
}

function Get-AppTexts([string] $Language) {
    <# .SYNOPSIS The UI texts of the app in a language (Strings.<lang>.resx over the English Strings.resx), by resource name. #>
    $folder = Join-Path $script:Repo 'src' 'LayerPeek.App' 'Resources'
    $texts = Get-VerifyResxStrings (Join-Path $folder 'Strings.resx')
    if ($Language -and $Language -ne 'en') {
        $translated = Get-VerifyResxStrings (Join-Path $folder "Strings.$Language.resx")
        foreach ($name in $translated.Keys) { $texts[$name] = $translated[$name] }
    }
    $texts
}

function Get-AxTree($Context, [int] $ProcessId) { @((Invoke-Helper $Context @('ax', "$ProcessId")) -join "`n" | ConvertFrom-Json -Depth 100) }

function Find-AxElement($Nodes, [string] $Identifier) {
    <# .SYNOPSIS The first node of the helper's Accessibility tree with that identifier (AutomationId), depth first; $null when none. #>
    foreach ($node in @($Nodes)) {
        if (-not $node) { continue }
        if ($node.identifier -ceq $Identifier) { return $node }
        $inner = Find-AxElement $node.children $Identifier
        if ($inner) { return $inner }
    }
    $null
}

function Get-AxElement($Context, [int] $ProcessId, [string] $Identifier) { Find-AxElement (Get-AxTree $Context $ProcessId) $Identifier }

function Get-AxNodes($Nodes) {
    <# .SYNOPSIS Every node of the helper's Accessibility tree, depth first. #>
    foreach ($node in @($Nodes)) {
        if (-not $node) { continue }
        $node
        Get-AxNodes $node.children
    }
}

function Get-AxSelectedTab($Context, [int] $ProcessId, [string] $TabsIdentifier) {
    <#
    .SYNOPSIS
        The tabs of a TabControl (identifiers of its tab items, in order) and the selected one from the Accessibility tree
        (T-140: instead of asking whether the tab moved). Avalonia gives a tab item no value; the selected one is the one
        that shows the selection pipe of the Fluent theme (PART_SelectedPipe, hidden - so not in the tree - in the others).
    #>
    $tabs = Get-AxElement $Context $ProcessId $TabsIdentifier
    $items = @(Get-VerifyAxTabItems $tabs)
    [pscustomobject]@{
        Tabs     = [string[]] @($items.identifier)
        Selected = [string] (@($items | Where-Object { Test-VerifyAxTabSelected $_ }) | Select-Object -First 1).identifier
        Tree     = $tabs
    }
}

function Get-AxFocusedId($Context, [int] $ProcessId) { [string] (@(Invoke-Helper $Context @('focusedid', "$ProcessId")) | Select-Object -First 1) }

function Get-AxCenter($Node) { @([math]::Round($Node.frame.x + $Node.frame.width / 2, 1), [math]::Round($Node.frame.y + $Node.frame.height / 2, 1)) }

function Get-AppType([int] $ProcessId) { Get-LaunchServicesAppType -ListOutput @(& /usr/bin/lsappinfo list 2>$null) -ProcessId $ProcessId }

function Set-SimulatorPrinter($Simulator, [ValidateSet('offline', 'online')] [string] $State) {
    <# .SYNOPSIS Switches the simulated printer off (its API stops answering) or on again (T-151, T-215). #>
    $null = Invoke-RestMethod -Method Post -Uri "$($Simulator.BaseUri.TrimEnd('/'))/_simulator/$State" -TimeoutSec 10
}

function Invoke-HelperExit($Context, [string[]] $Arguments, [string] $Evidence) {
    <# .SYNOPSIS The helper with its exit code (ExitCode, Output); -Evidence keeps the command, its exit code and its output. #>
    $output = @(& (Get-Helper $Context) @Arguments 2>&1 | ForEach-Object { "$_" })
    $code = $LASTEXITCODE
    if ($Evidence) { $null = Save-VerifyEvidence -Context $Context -Name $Evidence -Content (@("$($Arguments -join ' '): exit code $code") + $output) }
    [pscustomobject]@{ ExitCode = $code; Output = $output }
}

function Get-AxWindows($Context, [int] $ProcessId) {
    <# .SYNOPSIS The windows of a process from the Accessibility API (helper 'axwindows': title, frame, main, focused, minimized, buttons). #>
    @((Invoke-Helper $Context @('axwindows', "$ProcessId")) -join "`n" | ConvertFrom-Json)
}

function Test-AxWindowFocused($Context, [int] $ProcessId, [string] $Title) {
    <# .SYNOPSIS Whether the process is the front app and its window with that title is its main and key window. #>
    (Get-FrontProcessId) -eq $ProcessId -and @(Get-AxWindows $Context $ProcessId | Where-Object { $_.title -ceq $Title -and $_.main -and $_.focused }).Count -eq 1
}

function Get-CameraWindowFrame($Context, [int] $ProcessId, [int[]] $Except = @()) {
    <# .SYNOPSIS The camera window of the process (Get-CameraWindow) leaving out the windows -Except (e.g. Settings, by number). #>
    @(Get-ProcessWindows $Context $ProcessId | Where-Object { $_.onscreen -and $_.layer -ge 0 -and $_.layer -lt 25 -and $_.width -ge 80 -and $_.height -ge 45 -and $Except -notcontains $_.number } |
            Sort-Object { $_.width * $_.height } -Descending) | Select-Object -First 1
}

function Wait-StableWindow {
    <#
    .SYNOPSIS
        Polls a window (-Find returns it from the window list) every 100 ms until its bounds stayed the same for
        -StableSeconds (the end of an animation, T-038 question 2); the window, or $null after -TimeoutSeconds.
    #>
    param([Parameter(Mandatory)] [scriptblock] $Find, [double] $StableSeconds = 1, [double] $TimeoutSeconds = 10)
    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    $lastKey = $null
    $since = [DateTime]::UtcNow
    while ([DateTime]::UtcNow -lt $deadline) {
        $window = & $Find
        $key = if ($window) { "$($window.x),$($window.y),$($window.width),$($window.height)" } else { '' }
        if ($key -ne $lastKey) { $lastKey = $key; $since = [DateTime]::UtcNow }
        elseif ($window -and ([DateTime]::UtcNow - $since).TotalSeconds -ge $StableSeconds) { return $window }
        Start-Sleep -Milliseconds 100
    }
    $null
}

function Select-SettingsTab($Context, [int] $ProcessId, [string] $Tab, [string] $WindowTitle) {
    <#
    .SYNOPSIS
        Selects a tab (main tab or sub-tab) of the open Settings window; whether the Accessibility tree shows it selected.
    .DESCRIPTION
        AXPress on an Avalonia tab item does not select it (it reports success; first run with an unlocked screen
        2026-11-15, T-127), so the tab gets a real click on its centre: Settings raised first (AXRaise, -WindowTitle,
        default the English title) and only when no other window (e.g. the camera window) covers that point. Then
        AXPress, in case a later Avalonia maps it, and last the keyboard in the raised Settings window: Ctrl-Tab for a
        main tab (SettingsTab*), Cmd-Option-Right for a sub-tab of the printer (PrinterTab*), at most once round.
    #>
    if (-not $WindowTitle) {
        if (-not $script:SettingsTitleEn) { $script:SettingsTitleEn = (Get-VerifyResxStrings (Join-Path $script:Repo 'src' 'LayerPeek.App' 'Resources' 'Strings.resx'))['SettingsTitle'] }
        $WindowTitle = $script:SettingsTitleEn
    }
    $script:tabNode = $null
    if (-not (Wait-VerifyCondition -TimeoutSeconds 15 -Condition { $script:tabNode = Get-AxElement $Context $ProcessId $Tab; $script:tabNode -and $script:tabNode.frame.width -gt 0 })) { return $false }
    if (Test-VerifyAxTabSelected $script:tabNode) { return $true }
    $selected = { Test-VerifyAxTabSelected (Get-AxElement $Context $ProcessId $Tab) }
    $null = Invoke-HelperExit $Context @('axwin', "$ProcessId", $WindowTitle, 'raise')
    Start-Sleep -Milliseconds 300
    $script:tabNode = Get-AxElement $Context $ProcessId $Tab
    if ($script:tabNode) {
        $point = Get-AxCenter $script:tabNode
        $top = Get-VerifyTopWindow @((Invoke-Helper $Context @('windowsat', "$($point[0])", "$($point[1])")) -join "`n" | ConvertFrom-Json)
        if ($top -and $top.pid -eq $ProcessId -and $top.layer -eq 0) {
            $null = Invoke-Helper $Context (@('click') + $point)
            if (Wait-VerifyCondition -TimeoutSeconds 3 -Condition $selected) { return $true }
        }
        else { $null = Save-VerifyEvidence -Context $Context -Name "tab-$Tab-covered.txt" -Content @("the point $($point -join ', ') of $Tab is covered: $(if ($top) { "$($top.owner) ($($top.pid)), window $($top.number), layer $($top.layer)" } else { 'no window' })") }
    }
    $null = Invoke-Helper $Context @('axpress', "$ProcessId", $Tab) -AllowFailure
    if (Wait-VerifyCondition -TimeoutSeconds 2 -Condition $selected) { return $true }
    $group = if ($Tab -like 'PrinterTab*') { 'SettingsPrinterTabs' } else { 'SettingsTabs' }
    $combo = if ($Tab -like 'PrinterTab*') { 'cmd+alt+right' } else { 'ctrl+tab' }
    $count = @((Get-AxSelectedTab $Context $ProcessId $group).Tabs).Count
    for ($i = 0; $i -lt $count; $i++) {
        $null = Invoke-HelperExit $Context @('axwin', "$ProcessId", $WindowTitle, 'raise')
        $null = Invoke-Helper $Context @('key', $combo)
        if (Wait-VerifyCondition -TimeoutSeconds 1 -Condition $selected) { return $true }
    }
    $false
}

function Open-SettingsTab($Context, [int] $ProcessId, [string[]] $Tabs, [string] $Target) {
    <#
    .SYNOPSIS
        Selects the tabs (Select-SettingsTab, in order) of the open Settings window until the element -Target is in the
        Accessibility tree; the element, or $null.
    #>
    foreach ($tab in $Tabs) {
        if (-not (Select-SettingsTab $Context $ProcessId $tab)) { return $null }
    }
    $script:settingsTarget = $null
    $null = Wait-VerifyCondition -TimeoutSeconds 10 -Condition { $script:settingsTarget = Get-AxElement $Context $ProcessId $Target; $script:settingsTarget -and $script:settingsTarget.frame.width -gt 0 }
    $script:settingsTarget
}

function Invoke-CameraPinCheck {
    <#
    .SYNOPSIS
        T-122 / T-117 in one mode of the camera window, with real mouse and key events (CGEvent) and the Accessibility tree:
        the pin switches "always on top" at once (window level, Dock / Cmd-Tab by the rule of T-102, settings.json); a
        mouse click leaves no keyboard focus on the window buttons (the row shows only on hover or focus-within, so with
        the pointer outside the window only the dimmed pin is left), also after another app came to the front and back;
        after a click on the image Tab goes to the first window button; Tab to the pin, Space and Return switch it and the
        focus stays; AXPress (what VoiceOver does) switches it; the context menu (right click and Control-click) has the
        items of the mode with the check mark of the pin; the option survives a restart. Returns problems and "ok: ..." notes.
    #>
    param($Context, $Simulator, [ValidateSet('Normal', 'Mini')] [string] $Mode)
    $c = $Context
    $pinId = 'CameraTopmostButton'
    $buttons = if ($Mode -eq 'Normal') { @('CameraLayoutButton', 'CameraFullScreenButton', 'CameraMiniButton', $pinId) } else { @('CameraFullScreenButton', 'CameraNormalButton', $pinId) }
    $menuItems = if ($Mode -eq 'Normal') { @('CameraLayoutMenuItem', 'CameraTopmostMenuItem') } else { @('CameraTopmostMenuItem') }
    $data = New-TestData -Context $c -Simulator $Simulator -Camera $Simulator.Camera -Window ([ordered]@{ rememberLastPlacement = $false; startMode = $Mode })
    $settingsFile = Join-Path $data.Data 'settings.json'
    $app = Start-TestApp -Context $c -TestData $data
    $appId = $app.Id
    $script:pin = $null
    $readPin = { $script:pin = Get-AxElement $c $appId $pinId; $script:pin }
    $isOn = { [string] $script:pin.value -eq '1' }
    $expectType = { param([bool] $On) if ($On) { 'UIElement' } else { 'Foreground' } }
    $expectState = {
        param([bool] $On, [string] $What)
        if (-not (Wait-VerifyCondition -TimeoutSeconds 5 -Condition { (& $readPin) -and ((& $isOn) -eq $On) })) { "${What}: the pin did not switch $(if ($On) { 'on' } else { 'off' }) (Accessibility value '$($script:pin.value)')"; return }
        $title = [string] $script:pin.title
        if ($title -notmatch "^Always on top: $(if ($On) { 'on' } else { 'off' })$") { "${What}: accessible name '$title' does not say the state" }
        if (-not (Wait-VerifyCondition -TimeoutSeconds 5 -Condition { $w = Get-CameraWindow $c $appId; $w -and (($w.layer -gt 0) -eq $On) })) { "${What}: window level $((Get-CameraWindow $c $appId).layer) does not follow 'always on top' $On" }
        if (-not (Wait-VerifyCondition -TimeoutSeconds 5 -Condition { ($script:type = Get-AppType $appId) -ceq (& $expectType $On) })) { "${What}: LaunchServices type '$($script:type)', expected '$(& $expectType $On)' (Dock and Cmd-Tab, rule of T-102)" }
    }
    $buttonFocused = { $id = Get-AxFocusedId $c $appId; if ($buttons -contains $id) { $id } else { $null } }
    $away = {
        $screen = @(Get-Screens $c | Where-Object main)[0]
        $null = Invoke-Helper $c @('move', "$($screen.visible.x + 4)", "$($screen.visible.y + $screen.visible.height - 4)")
    }
    $key = { param($Combo) $null = Invoke-Helper $c @('key', $Combo); Start-Sleep -Milliseconds 400 }
    $problems = [System.Collections.Generic.List[string]]::new()
    $add = { param($Items) foreach ($item in @($Items)) { if ($item) { $problems.Add([string] $item) } } }
    $notes = [System.Collections.Generic.List[string]]::new()
    try {
        Send-TestCommand $data "camera:$($script:PrinterId)"
        if (-not (Wait-VerifyCondition -TimeoutSeconds 20 -Condition { (Get-CameraWindow $c $appId) -and (& $readPin) })) { return 'no camera window with the pin in the Accessibility tree' }
        if (-not (Wait-VerifyCondition -TimeoutSeconds 10 -Condition { (Get-FrontProcessId) -eq $appId })) { & $add 'the camera window did not come to the front' }
        $initial = & $isOn
        & $add (& $expectState $initial 'at the start')

        # 1. A real mouse click on the pin switches at once; the pointer leaves; no window button keeps the keyboard focus.
        $hash = (Get-FileHash -LiteralPath $settingsFile).Hash
        $null = Invoke-Helper $c (@('click') + (Get-AxCenter $script:pin))
        & $add (& $expectState (-not $initial) 'mouse click')
        if (-not (Wait-VerifyCondition -TimeoutSeconds 5 -Condition { (Get-FileHash -LiteralPath $settingsFile).Hash -ne $hash })) { & $add 'the click did not save the option in settings.json' }
        & $away
        Start-Sleep -Milliseconds 600
        $focused = & $buttonFocused
        if ($focused) { & $add "after the mouse click and the pointer leaving, $focused has the keyboard focus (the whole row stays visible)" }

        # 2. Another app in front and back: the activation does not focus the pin again (T-122).
        if (Invoke-FinderToFront) {
            Send-TestCommand $data 'activate'
            if (-not (Wait-VerifyCondition -TimeoutSeconds 10 -Condition { (Get-FrontProcessId) -eq $appId })) { & $add 'the app did not come back to the front' }
            Start-Sleep -Milliseconds 600
            & $away
            $focused = & $buttonFocused
            if ($focused) { & $add "after switching to Finder and back, $focused has the keyboard focus (the whole row shows)" }
        }
        else { & $add "Finder did not come to the front (front app: $(Get-FrontAppText))" }

        # 3. A click on the image, then Tab: the first window button.
        $surface = Get-AxElement $c $appId 'Surface'
        if ($surface) {
            $point = Get-AxCenter $surface
            $null = Invoke-Helper $c @('click', "$($point[0])", "$([math]::Round($surface.frame.y + $surface.frame.height * 0.7, 1))")
            Start-Sleep -Milliseconds 500
            & $key 'tab'
            $first = Get-AxFocusedId $c $appId
            if ($first -cne $buttons[0]) { & $add "Tab after a click on the image focused '$first', expected $($buttons[0])" }
        }
        else { & $add 'no camera image (Surface) in the Accessibility tree' }

        # 4. Tab to the pin; Space and Return switch it and the focus stays on it (and the app in front: switching "always
        # on top" on makes it an accessory app, T-102, which must not hand the front to another app).
        for ($i = 0; $i -lt 8 -and (Get-AxFocusedId $c $appId) -cne $pinId; $i++) { & $key 'tab' }
        if ((Get-AxFocusedId $c $appId) -cne $pinId) { & $add 'Tab never reached the pin' }
        else {
            foreach ($press in 'space', 'return') {
                $null = & $readPin
                $state = -not (& $isOn)
                & $key $press
                & $add (& $expectState $state $press)
                $after = Get-AxFocusedId $c $appId
                if ($after -cne $pinId) { & $add "after $press (pin $(if ($state) { 'on' } else { 'off' })) the pin lost the keyboard focus (focused: '$after', front app: $(Get-FrontAppText))" }
                if ($after -cne $pinId -and (Get-FrontProcessId) -ne $appId) {
                    Send-TestCommand $data 'activate'
                    $null = Wait-VerifyCondition -TimeoutSeconds 10 -Condition { (Get-FrontProcessId) -eq $appId }
                    for ($i = 0; $i -lt 8 -and (Get-AxFocusedId $c $appId) -cne $pinId; $i++) { & $key 'tab' }
                }
            }
        }

        # 5. AXPress, as VoiceOver activates the pin (VO-Space).
        foreach ($round in 1, 2) {
            $null = & $readPin
            $state = -not (& $isOn)
            $null = Invoke-Helper $c @('axpress', "$appId", $pinId)
            & $add (& $expectState $state "AXPress $round")
        }

        # 5b. Switching "always on top" on while another app is in front (VoiceOver, AXPress) leaves that app in front (T-137).
        $null = & $readPin
        if (& $isOn) { $null = Invoke-Helper $c @('axpress', "$appId", $pinId); $null = Wait-VerifyCondition -TimeoutSeconds 5 -Condition { (& $readPin) -and -not (& $isOn) } }
        if (Invoke-FinderToFront) {
            $finder = Get-FrontProcessId
            $null = Invoke-Helper $c @('axpress', "$appId", $pinId)
            if (-not (Wait-VerifyCondition -TimeoutSeconds 5 -Condition { (& $readPin) -and (& $isOn) })) { & $add 'AXPress with Finder in front did not switch the pin on' }
            Start-Sleep -Milliseconds 1500
            if ((Get-FrontProcessId) -ne $finder) { & $add "switching the pin on with Finder in front took the front ($(Get-FrontAppText))" }
            $state = $true
        }
        else { & $add "Finder did not come to the front (front app: $(Get-FrontAppText))" }

        # 6. The context menu by right click and by Control-click: the items of the mode, the pin's check mark.
        foreach ($click in 'rightclick', 'ctrlclick') {
            Send-TestCommand $data 'activate'
            if (-not (Wait-VerifyCondition -TimeoutSeconds 10 -Condition { (Get-FrontProcessId) -eq $appId })) { & $add "before the $click menu the app did not come to the front" }
            Start-Sleep -Milliseconds 400
            $surface = Get-AxElement $c $appId 'Surface'
            $point = Get-AxCenter $surface
            $null = Invoke-Helper $c @($click, "$($point[0])", "$($point[1])")
            $script:tree = $null
            $opened = Wait-VerifyCondition -TimeoutSeconds 5 -Condition { $script:tree = Get-AxTree $c $appId; $null -ne (Find-AxElement $script:tree 'CameraTopmostMenuItem') }
            $null = Save-VerifyEvidence -Context $c -Name "$($Mode.ToLowerInvariant())-$click-ax.json" -Content ($script:tree | ConvertTo-Json -Depth 100)
            if (-not $opened) { & $add "$(if ($click -eq 'ctrlclick') { 'Control-click' } else { 'right click' }) on the image opened no context menu"; continue }
            $present = @($menuItems | Where-Object { Find-AxElement $script:tree $_ })
            $extra = @('CameraLayoutMenuItem', 'CameraTopmostMenuItem') | Where-Object { $menuItems -notcontains $_ -and (Find-AxElement $script:tree $_) -and (Find-AxElement $script:tree $_).frame.width -gt 0 }
            if ($present.Count -ne $menuItems.Count) { & $add "$click menu lacks $(@($menuItems | Where-Object { $present -notcontains $_ }) -join ', ')" }
            if ($extra) { & $add "$click menu shows $($extra -join ', '), not expected in this mode" }
            $item = Find-AxElement $script:tree 'CameraTopmostMenuItem'
            $notes.Add("$click menu item '$($item.title)' value '$($item.value)'")
            $menuFrame = (Find-AxElement $script:tree 'CameraContextMenu').frame
            $menuShown = { @(Get-ProcessWindows $c $appId | Where-Object { $_.onscreen -and [math]::Abs($_.x - $menuFrame.x) -le 2 -and [math]::Abs($_.y - $menuFrame.y) -le 2 -and [math]::Abs($_.width - $menuFrame.width) -le 2 }).Count -gt 0 }
            & $key 'escape'
            if (-not (Wait-VerifyCondition -TimeoutSeconds 5 -Condition { -not (& $menuShown) })) { & $add "Esc did not close the $click menu" }
        }

        # 7. The option survives a restart.
        $null = & $readPin
        $final = & $isOn
        $code = Stop-TestApp $data $app
        if ($code -ne 0) { & $add "Quit ended the app with $code" }
        $app = Start-TestApp -Context $c -TestData $data
        $appId = $app.Id
        Send-TestCommand $data "camera:$($script:PrinterId)"
        if (-not (Wait-VerifyCondition -TimeoutSeconds 20 -Condition { (Get-CameraWindow $c $appId) -and (& $readPin) })) { & $add 'after the restart: no camera window with the pin' }
        else { & $add (& $expectState $final 'after the restart') }
    }
    finally {
        $null = Stop-TestApp $data $app
    }
    $problems
    "ok: pin $(if ($initial) { 'on' } else { 'off' }) at the start, switched by click, Space, Return and AXPress (window level, Dock type, accessible name 'Always on top: on/off'), no button focused after the click and an app switch, Tab after the image to $($buttons[0]), menus: $($notes -join ', '), kept after the restart"
}

function Request-Accessibility($Context) {
    <#
    .SYNOPSIS
        Whether the terminal has Accessibility (real mouse and key events, the Accessibility tree). An interactive run opens
        System Settings and waits for the user once; -Unattended never asks.
    #>
    if (Test-Accessibility $Context) { return $true }
    if ($Context.Unattended) { return $false }
    Write-VerifyStep $Context 'Add your terminal app in System Settings > Privacy & Security > Accessibility and switch it on (opening it now). Remove it again after the run.' 'Yellow'
    & /usr/bin/open 'x-apple.systempreferences:com.apple.preference.security?Privacy_Accessibility'
    $null = Read-VerifyAnswer -Context $Context -Question 'Is Accessibility switched on for the terminal?'
    Test-Accessibility $Context
}

function Start-HelperProcess($Context, [string[]] $Arguments, [string] $Output) {
    <# .SYNOPSIS The helper as a background process (standard output and errors into -Output); the cleanup kills it at the latest. #>
    $start = [System.Diagnostics.ProcessStartInfo]::new('/bin/sh')
    foreach ($argument in @('-c', 'exec "$0" "$@" >"$LPV_OUT" 2>&1 </dev/null', (Get-Helper $Context)) + $Arguments) { $start.ArgumentList.Add($argument) }
    $start.Environment['LPV_OUT'] = $Output
    $start.UseShellExecute = $false
    $process = [System.Diagnostics.Process]::Start($start)
    Add-VerifyCleanup $Context "helper $($Arguments[0]) $($process.Id)" { param($p) if (-not $p.HasExited) { $p.Kill(); $p.WaitForExit() } } -State $process
    $process
}

function Wait-StandInFront($Context, $Process) {
    <#
    .SYNOPSIS
        Whether the stand-in app (helper 'front', started with Start-HelperProcess) is the front app within 10 s. Its own
        activate() is cooperative on macOS 14+ and may be refused while another app (the app under test) is active (first
        run with an unlocked screen 2026-11-15); then the helper brings it to the front through AXFrontmost.
    #>
    if (Wait-VerifyCondition -TimeoutSeconds 3 -Condition { (Get-FrontProcessId) -eq $Process.Id }) { return $true }
    $null = Invoke-HelperExit $Context @('activate', "$($Process.Id)")
    Wait-VerifyCondition -TimeoutSeconds 7 -Condition { (Get-FrontProcessId) -eq $Process.Id }
}

function Start-FakeGitHub {
    <#
    .SYNOPSIS
        tools/fake-github-releases.ps1 on 127.0.0.1 with a free port: the GitHub release list with one release -Version,
        each answer -DelayMilliseconds late. Ends with this script at the latest (its parent watch) and by the cleanup.
    #>
    param($Context, [string] $Version, [int] $DelayMilliseconds)
    $folder = Join-Path $Context.RunFolder 'tmp' "github-$(New-ShortId)"
    $null = New-Item -ItemType Directory -Force -Path $folder
    $out = Join-Path $folder 'stdout.txt'
    $log = Join-Path $folder 'requests.txt'
    $start = [System.Diagnostics.ProcessStartInfo]::new('/bin/sh')
    # The same PowerShell as this script, also when it is the dotnet tool (Get-PwshCommand of ReleaseTools).
    $arguments = @('-c', 'exec "$0" "$@" >"$LPV_OUT" 2>&1 </dev/null') + @(Get-PwshCommand) + @('-NoProfile', '-NonInteractive', '-File', (Join-Path $PSScriptRoot 'fake-github-releases.ps1'),
        '-Version', $Version, '-DelayMilliseconds', "$DelayMilliseconds", '-RequestLog', $log, '-ParentProcessId', "$PID")
    foreach ($argument in $arguments) { $start.ArgumentList.Add($argument) }
    $start.Environment['LPV_OUT'] = $out
    $start.UseShellExecute = $false
    $process = [System.Diagnostics.Process]::Start($start)
    Add-VerifyCleanup $Context 'stand-in GitHub API' { param($p) if (-not $p.HasExited) { $p.Kill($true); $p.WaitForExit() } } -State $process -RunLast
    $ready = Wait-VerifyCondition -TimeoutSeconds 30 -Condition { $process.HasExited -or ((Test-Path -LiteralPath $out) -and (Select-String -LiteralPath $out -Pattern '^Address: ' -Quiet)) }
    if (-not $ready -or $process.HasExited) { throw "The stand-in GitHub API did not start: $(Get-Content -LiteralPath $out -Raw -ErrorAction SilentlyContinue)" }
    [pscustomobject]@{
        BaseUri    = ([regex]::Match((Get-Content -LiteralPath $out -Raw), 'Address: (\S+)')).Groups[1].Value
        RequestLog = $log
        Process    = $process
    }
}

function Get-FakeGitHubRequests($Server) { if (Test-Path -LiteralPath $Server.RequestLog) { @(Get-Content -LiteralPath $Server.RequestLog) } else { @() } }

function Save-WindowCapture($Context, [int] $ProcessId, [string] $Name) {
    <# .SYNOPSIS A capture of the main window of the process (needs Screen Recording) as evidence; its path and the window, or $null. #>
    $window = Get-VerifyMainWindow @(Get-ProcessWindows $Context $ProcessId)
    if (-not $window) { return $null }
    $file = Join-Path (Get-VerifyEvidenceFolder $Context) "$Name.png"
    & /usr/sbin/screencapture -x -o -l "$($window.number)" $file
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $file)) { return $null }
    [pscustomobject]@{ Path = $file; Window = $window }
}

function Invoke-UpdateCheckPress {
    <#
    .SYNOPSIS
        Presses "Check now" once (-Press) while the helper watches the text of SettingsUpdateCheckResult every 50 ms, and
        judges the timeline (Get-VerifyUpdateCheckVerdict). With -Capture also whether the text is really drawn: the
        window captured before and after, the pixels in the text's frame compared (only when no text was shown before).
        Evidence: <name>-watch.jsonl, <name>-before.png, <name>-after.png.
    #>
    param($Context, [int] $AppId, $Server, [string] $Name, [string] $Expected, [scriptblock] $Press, [switch] $Capture)
    $before = if ($Capture) { Save-WindowCapture $Context $AppId "$Name-before" }
    $requests = @(Get-FakeGitHubRequests $Server).Count
    $watchFile = Join-Path (Get-VerifyEvidenceFolder $Context) "$Name-watch.jsonl"
    $watch = Start-HelperProcess $Context @('axwatch', "$AppId", 'SettingsUpdateCheckResult', '15000', $Expected) $watchFile
    # The first line is the state before the press.
    if (-not (Wait-VerifyCondition -TimeoutSeconds 10 -Condition { (Test-Path -LiteralPath $watchFile) -and (Get-Item -LiteralPath $watchFile).Length -gt 0 })) {
        throw "the Accessibility watch did not start: $(Get-Content -LiteralPath $watchFile -Raw -ErrorAction SilentlyContinue)"
    }
    & $Press
    if (-not $watch.WaitForExit(25000)) { $watch.Kill(); $watch.WaitForExit() }
    $timeline = @(ConvertFrom-VerifyAxWatch @(Get-Content -LiteralPath $watchFile))
    $verdict = Get-VerifyUpdateCheckVerdict -Timeline $timeline -Requests (@(Get-FakeGitHubRequests $Server).Count - $requests) -Expected $Expected
    if ($Capture -and $verdict.Passed) {
        $after = Save-WindowCapture $Context $AppId "$Name-after"
        $frame = $verdict.Shown.frame
        if (-not $before -or -not $after) { $verdict.Detail += '; no window capture' }
        elseif ($verdict.Initial.found -and "$($verdict.Initial.text)") { $verdict.Detail += "; drawing not compared (a text was shown before the press); captures $Name-before.png, $Name-after.png" }
        else {
            $window = $after.Window
            $diffArguments = @('regiondiff', $before.Path, $after.Path, "$($window.width)", "$($frame.x - $window.x)", "$($frame.y - $window.y)", "$($frame.width)", "$($frame.height)")
            $diff = (Invoke-Helper $Context $diffArguments -AllowFailure) -join "`n" | ConvertFrom-Json -ErrorAction SilentlyContinue
            if (-not $diff) { $verdict.Detail += '; the captures could not be compared (the window changed its size?)' }
            elseif ($diff.share -lt 0.01) {
                $verdict.Passed = $false
                $verdict.Detail = "the Accessibility tree shows '$($verdict.Shown.text)', but nothing is drawn in its frame: $($diff.changed) of $($diff.pixels) pixels changed between $Name-before.png and $Name-after.png"
            }
            else { $verdict.Detail += "; drawn ($([math]::Round($diff.share * 100, 1)) % of the pixels in its frame changed, $Name-before.png / $Name-after.png)" }
        }
    }
    $verdict
}

function Invoke-UpdateCheckClicks {
    <#
    .SYNOPSIS
        The user's report of 2026-11-05 (T-127): on macOS "Check now" on the Startup and updates tab showed no result while a
        newer version was available. Against the stand-in GitHub API (one release 9.9.0, each answer 1.5 s late, so that
        "Checking…" is seen) "Check now" is pressed three ways, each in a freshly opened Settings window: a) a real left
        click into the active window, b) one real click while another app is in front (the main hypothesis: on macOS the
        first click into an inactive window may only activate it), c) AXPress (what VoiceOver does). Returns one entry per
        scenario: Scenario, Passed, Detail.
    #>
    param($Context)
    $c = $Context
    $version = '9.9.0'
    $expected = "Version $version is available"
    $server = Start-FakeGitHub -Context $c -Version $version -DelayMilliseconds 1500
    $data = New-TestData -Context $c
    $app = Start-TestApp -Context $c -TestData $data -Environment @{ LAYERPEEK_TEST_UPDATE_SOURCE = $server.BaseUri }
    $appId = $app.Id
    $capture = (Invoke-Helper $c @('screenrecording')) -contains 'true'
    $results = [System.Collections.Generic.List[object]]::new()
    $add = { param([string] $Scenario, [bool] $Passed, [string] $Detail) $results.Add([pscustomobject]@{ Scenario = $Scenario; Passed = $Passed; Detail = $Detail }) }
    $mainWindow = { Get-VerifyMainWindow @(Get-ProcessWindows $c $appId) }
    $toFront = {
        # The app in front is not yet its key window right away (T-140): a second to settle.
        Send-TestCommand $data 'settings'
        $front = Wait-VerifyCondition -TimeoutSeconds 10 -Condition { (Get-FrontProcessId) -eq $appId -and (& $mainWindow) }
        Start-Sleep -Seconds 1
        $front
    }
    $openTab = {
        # A fresh Settings window (a new view model, no result shown yet) on the Startup and updates tab; returns the button.
        if (-not (& $toFront)) { throw "Settings did not come to the front (front app $(Get-FrontAppText))" }
        $null = Invoke-Helper $c @('key', 'cmd+w')
        if (-not (Wait-VerifyCondition -TimeoutSeconds 6 -Condition { -not (& $mainWindow) })) { throw 'Cmd-W did not close Settings (each scenario needs a fresh window)' }
        if (-not (& $toFront)) { throw "Settings did not come to the front again (front app $(Get-FrontAppText))" }
        $script:updateTab = $null
        if (-not (Wait-VerifyCondition -TimeoutSeconds 10 -Condition { $script:updateTab = Get-AxElement $c $appId 'SettingsTabStartupUpdates'; $script:updateTab })) { throw 'no tab SettingsTabStartupUpdates in the Accessibility tree' }
        $selected = { (Get-AxSelectedTab $c $appId 'SettingsTabs').Selected -ceq 'SettingsTabStartupUpdates' }
        $null = Invoke-Helper $c (@('click') + (Get-AxCenter $script:updateTab))
        if (-not (Wait-VerifyCondition -TimeoutSeconds 5 -Condition $selected)) {
            $null = Invoke-Helper $c @('axpress', "$appId", 'SettingsTabStartupUpdates') -AllowFailure
            if (-not (Wait-VerifyCondition -TimeoutSeconds 5 -Condition $selected)) { throw 'neither a click nor AXPress selected the tab Startup and updates' }
        }
        $script:checkButton = $null
        if (-not (Wait-VerifyCondition -TimeoutSeconds 5 -Condition { $script:checkButton = Get-AxElement $c $appId 'SettingsCheckForUpdatesNow'; $script:checkButton -and $script:checkButton.frame.width -gt 0 })) {
            throw 'no button SettingsCheckForUpdatesNow on the tab'
        }
        $null = Save-VerifyEvidence -Context $c -Name 'startup-updates-tab-ax.json' -Content ((Get-AxTree $c $appId) | ConvertTo-Json -Depth 100)
        $script:checkButton
    }
    $press = { param([string] $Name, [scriptblock] $Action) Invoke-UpdateCheckPress -Context $c -AppId $appId -Server $server -Name $Name -Expected $expected -Press $Action -Capture:$capture }
    try {
        # The automatic check 2 s after the start (test mode) shows that the app reaches the stand-in API at all.
        if (-not (Wait-VerifyCondition -TimeoutSeconds 30 -Condition { (Read-TestLog $data) -match "Update check: version $([regex]::Escape($version)) is available" })) {
            & $add 'start' $false "the automatic check after the start did not find $version at $($server.BaseUri) (requests: $(@(Get-FakeGitHubRequests $server).Count); app log saved)"
            return $results
        }

        # a) The active window, a real left click on the button's centre.
        try {
            $button = & $openTab
            $verdict = & $press 'a-click-active' { $null = Invoke-Helper $c (@('click') + (Get-AxCenter $button)) }
            & $add 'a) real click, window active' $verdict.Passed $verdict.Detail
        }
        catch { & $add 'a) real click, window active' $false "not judged: $($_.Exception.Message)" }

        # b) Another app in front (the stand-in app of the helper, which opens no window over Settings), one real click.
        try {
            $button = & $openTab
            $point = Get-AxCenter $button
            $screen = @(Get-Screens $c | Where-Object main)[0]
            $other = Start-HelperProcess $c @('front', "$($screen.visible.x + 16)", "$($screen.visible.y + 16)", '60') (Join-Path $c.RunFolder 'tmp' "front-$(New-ShortId).txt")
            try {
                $otherName = 'the stand-in app'
                if (-not (Wait-StandInFront $c $other)) {
                    # Finder may open a window over Settings: the check below tells.
                    if (-not (Invoke-FinderToFront)) { throw "neither the stand-in app nor Finder came to the front (front app $(Get-FrontAppText))" }
                    $otherName = 'Finder'
                }
                Start-Sleep -Seconds 1
                $at = @((Invoke-Helper $c @('windowsat', "$($point[0])", "$($point[1])")) -join "`n" | ConvertFrom-Json)
                $null = Save-VerifyEvidence -Context $c -Name 'b-windows-at-button.json' -Content (ConvertTo-VerifyJsonArray $at)
                $top = Get-VerifyTopWindow $at
                if (-not $top -or $top.pid -ne $appId) { throw "the button is covered by $(if ($top) { "$($top.owner) ($($top.pid)), window $($top.number)" } else { 'no window' }) at $($point -join ', ')" }
                $script:frontAfterClick = $null
                $verdict = & $press 'b-click-inactive' {
                    $null = Invoke-Helper $c (@('click') + $point)
                    $null = Wait-VerifyCondition -TimeoutSeconds 2 -Condition { (Get-FrontProcessId) -eq $appId }
                    $script:frontAfterClick = Get-FrontAppText
                }
                $activated = $script:frontAfterClick -match "\($appId\)$"
                $how = "one click with $otherName in front: the click $(if ($activated) { 'activated' } else { 'did not activate' }) the app (front app after it: $($script:frontAfterClick))"
                $detail = if ($verdict.Passed) { "$how and ran the check: $($verdict.Detail)" } else { "$how, but $($verdict.Detail)" }
                & $add 'b) one real click, window inactive' $verdict.Passed $detail
            }
            finally { if (-not $other.HasExited) { $other.Kill(); $other.WaitForExit() } }
        }
        catch { & $add 'b) one real click, window inactive' $false "not judged: $($_.Exception.Message)" }

        # c) AXPress, as VoiceOver presses the button (VO-Space).
        try {
            $null = & $openTab
            $verdict = & $press 'c-axpress' { $null = Invoke-Helper $c @('axpress', "$appId", 'SettingsCheckForUpdatesNow') }
            & $add 'c) AXPress' $verdict.Passed $verdict.Detail
        }
        catch { & $add 'c) AXPress' $false "not judged: $($_.Exception.Message)" }
    }
    finally {
        $null = Stop-TestApp $data $app
        $null = Save-VerifyEvidence -Context $c -Name 'github-requests.txt' -Content @(Get-FakeGitHubRequests $server)
    }
    $results
}

function Invoke-CameraModes {
    <#
    .SYNOPSIS
        For each camera window mode: a test app with the camera (-Camera, default the simulator's), the window opened by the
        test command, its frame and the RTSP connection; with -Look the user is asked whether the image and the overlay are
        visible. Returns the problems and the answers.
    #>
    param($Context, [string] $Camera, [switch] $Look)
    $simulator = Get-Simulator $Context
    Set-SimulatorScenario $simulator 'printing'
    $cameraUrl = if ($Camera) { $Camera } else { $simulator.Camera }
    $port = ([uri] $cameraUrl).Port
    if ($port -le 0) { $port = 554 }
    $screens = Get-Screens $Context
    $problems = [System.Collections.Generic.List[string]]::new()
    $notes = [System.Collections.Generic.List[string]]::new()
    foreach ($mode in 'Normal', 'Mini', 'FullScreen') {
        $data = New-TestData -Context $Context -Simulator $simulator -Camera $cameraUrl -Window ([ordered]@{ rememberLastPlacement = $false; startMode = $mode })
        $app = Start-TestApp -Context $Context -TestData $data
        try {
            Send-TestCommand $data "camera:$($script:PrinterId)"
            $script:w = $null
            $opened = Wait-VerifyCondition -TimeoutSeconds 20 -Condition { $script:w = Get-CameraWindow $Context $app.Id; $script:w -and ($mode -ne 'FullScreen' -or (Test-FullScreen $script:w $screens)) }
            $window = $script:w
            $connected = Wait-VerifyCondition -TimeoutSeconds 20 -Condition { (Get-RtspConnections $app.Id $port).Count -gt 0 }
            Start-Sleep -Seconds 5
            $still = (Get-RtspConnections $app.Id $port).Count -gt 0
            $null = Save-VerifyEvidence -Context $Context -Name "$($mode.ToLowerInvariant())-windows.json" -Content (ConvertTo-VerifyJsonArray (Get-ProcessWindows $Context $app.Id))
            $null = Save-VerifyEvidence -Context $Context -Name "$($mode.ToLowerInvariant())-rtsp.txt" -Content (Get-RtspConnections $app.Id $port)
            if (-not $opened) {
                $seen = if ($window) { " (largest window $([math]::Round($window.width)) x $([math]::Round($window.height)) pt at $([math]::Round($window.x)),$([math]::Round($window.y)), layer $($window.layer))" } else { '' }
                $problems.Add("${mode}: no camera window$(if ($mode -eq 'FullScreen') { ' covering a whole screen' })$seen")
            }
            if (-not $connected -or -not $still) { $problems.Add("${mode}: no lasting RTSP connection to port $port") }
            $errors = @((Read-TestLog $data) -split "`n" | Where-Object { $_ -match ' (ERR|CRT) ' -and $_ -match '(?i)camera' })
            if ($errors.Count -gt 0) { $problems.Add("${mode}: camera errors in the log: $($errors[0])") }
            if ($window) { $notes.Add("${mode}: $([math]::Round($window.width)) x $([math]::Round($window.height)) pt, layer $($window.layer)") }
            if ($Look) {
                if (-not (Read-VerifyAnswer -Context $Context -Question "Camera window in mode $($mode): do you see a moving camera image with the print status overlay?")) { $problems.Add("${mode}: the user saw no moving image or no overlay") }
            }
        }
        finally {
            $null = Stop-TestApp $data $app
            $null = Save-VerifyEvidence -Context $Context -Name "$($mode.ToLowerInvariant())-app.log" -Content (Read-TestLog $data)
        }
    }
    [pscustomobject]@{ Problems = $problems.ToArray(); Notes = $notes.ToArray() }
}

function Invoke-DotnetTest($Context, [string] $Label, [string[]] $Arguments, [hashtable] $Environment = @{}) {
    $result = Invoke-VerifyTool -Context $Context -Label $Label -FilePath 'dotnet' -ArgumentList (@('test') + $Arguments) -Environment ($script:DotnetEnv + $Environment) -Show
    $totals = Get-VerifyTestTotals $result.Output
    [pscustomobject]@{ ExitCode = $result.ExitCode; Totals = $totals; Skipped = @(Get-VerifySkippedTests $result.Output); Output = $result.Output }
}

function Format-Totals($Totals) { "total $($Totals.Total), passed $($Totals.Passed), failed $($Totals.Failed), skipped $($Totals.Skipped)" }

function Get-KeychainTestServices {
    @(& /usr/bin/security dump-keychain 2>$null | ForEach-Object { if ("$_" -match '^\s*"svce"<blob>="(?<name>LayerPeek\.Tests[^"]*)"') { $Matches.name } } | Select-Object -Unique)
}

function Use-ProductionProfile {
    <#
    .SYNOPSIS
        Runs -Action with the LayerPeek profile moved aside when the user wants that (the journal of scripts/aot-smoke.ps1:
        data, logs, Preferences, saved state, caches); restored afterwards. Otherwise the action uses the real profile.
    #>
    param($Context, [scriptblock] $Action)
    if (Get-Process -Name 'LayerPeek' -ErrorAction SilentlyContinue) {
        Write-VerifyStep $Context 'LayerPeek is running: quit it (menu > Quit) before this check.' 'Yellow'
        if (-not (Wait-VerifyCondition -TimeoutSeconds 120 -Condition { -not (Get-Process -Name 'LayerPeek' -ErrorAction SilentlyContinue) })) { Skip-VerifyCheck 'LayerPeek kept running.' }
    }
    $backup = Read-VerifyAnswer -Context $Context -Question 'Run this step with an empty LayerPeek profile (your settings, logs and preferences are moved aside and restored afterwards)?'
    if (-not $backup) {
        $Context.Shared.ProductionProfileUsed = @($Context.Shared.ProductionProfileUsed) + $Context.CheckId
        return & $Action
    }
    $smokeProfile = Get-SmokeProfile
    Backup-SmokeProfile -SmokeProfile $smokeProfile
    try { & $Action }
    finally {
        if (Get-Process -Name 'LayerPeek' -ErrorAction SilentlyContinue) {
            Write-VerifyStep $Context 'Quit LayerPeek (menu > Quit) so that the profile can be restored.' 'Yellow'
            $null = Wait-VerifyCondition -TimeoutSeconds 300 -Condition { -not (Get-Process -Name 'LayerPeek' -ErrorAction SilentlyContinue) }
        }
        $null = Restore-SmokeProfile -SmokeProfile $smokeProfile
        Write-VerifyStep $Context 'Your LayerPeek profile is restored.'
    }
}

function Save-ProfileSnapshot([string] $Path, [switch] $LoginItems) {
    # The functions of tools/profile-snapshot.ps1 (dot-sourced here, in this function's scope only).
    . (Join-Path $PSScriptRoot 'profile-snapshot.ps1')
    $state = if ($LoginItems) { Get-ProfileState } else { Get-ProfileState -LoginItems { $null } }
    Save-ProfileState -Path $Path -State $state
}

function Compare-ProfileSnapshot([string] $Path, [switch] $LoginItems) {
    . (Join-Path $PSScriptRoot 'profile-snapshot.ps1')
    $state = if ($LoginItems) { Get-ProfileState } else { Get-ProfileState -LoginItems { $null } }
    Compare-ProfileState -Before (Read-ProfileState -Path $Path) -After $state
}

# ---------------------------------------------------------------------------------------------------------------------
# The checks
# ---------------------------------------------------------------------------------------------------------------------

$checks = @(
    New-VerifyCheck -Id 'mac.build' -Kind Auto -Title 'dotnet build Debug and Release without warnings' `
        -Closes 'T-018 kritérium 4 (build na macOS)', 'T-053 kritérium 6 (část: build)' -Action {
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

    New-VerifyCheck -Id 'mac.test' -Kind Auto -Title 'full dotnet test without failures' `
        -Closes 'T-018 kritérium 4 (testy na macOS)', 'T-095 (zelené testy na macOS, kontrola)' -Action {
        param($c)
        $run = Invoke-DotnetTest $c 'dotnet-test' @('--solution', (Join-Path $script:Repo 'LayerPeek.slnx'))
        if (-not $run.Totals) { return New-VerifyResult FAIL "no test results (build failed?), exit code $($run.ExitCode)." }
        $null = Save-VerifyEvidence -Context $c -Name 'skipped.txt' -Content ($run.Skipped | ForEach-Object { "$($_.Name)`n    $($_.Reason)" })
        if ($run.Totals.Failed -gt 0 -or $run.ExitCode -ne 0) { return New-VerifyResult FAIL "$(Format-Totals $run.Totals); exit code $($run.ExitCode)." }
        New-VerifyResult PASS "$(Format-Totals $run.Totals) (skipped tests with reasons in skipped.txt)"
    }

    New-VerifyCheck -Id 'mac.aot-smoke' -Kind Auto -Title 'Native AOT publish osx-arm64 without warnings and smoke start (scripts/aot-smoke.ps1)' `
        -Closes 'T-053 kritérium 6 (lokální Native AOT publish osx-arm64 bez varování)' -Action {
        param($c)
        $pwsh = @(Get-PwshCommand)
        $arguments = @($pwsh | Select-Object -Skip 1) + @('-NoProfile', '-File', (Join-Path $script:Repo 'scripts' 'aot-smoke.ps1'), '-Runtime', 'osx-arm64')
        $result = Invoke-VerifyTool -Context $c -Label 'aot-smoke' -FilePath $pwsh[0] -ArgumentList $arguments -Environment $script:DotnetEnv -Show
        $warnings = @(Get-VerifyBuildWarnings $result.Output)
        if ($result.ExitCode -ne 0) { return New-VerifyResult FAIL "aot-smoke.ps1 failed (exit code $($result.ExitCode)); see aot-smoke.txt." }
        if ($warnings.Count -gt 0) { return New-VerifyResult FAIL "the AOT publish has $($warnings.Count) warning(s): $($warnings[0])" }
        New-VerifyResult PASS 'published without warnings; the AOT app ran without a Dock icon; profile restored by aot-smoke.ps1.'
    }

    New-VerifyCheck -Id 'mac.keychain' -Kind Auto -Title 'Keychain integration tests on the unlocked login Keychain (no skipped test)' `
        -Closes 'T-021 kritéria 1–5 (round-trip, přepis, velikost písmen, izolace, úklid)', 'T-021 kritérium 8 (zprávy bez tajného údaje)', 'T-021 kritérium 7 (s mac.profile-unchanged)' -Action {
        param($c)
        $run = Invoke-DotnetTest $c 'dotnet-test-keychain' @('--project', (Join-Path $script:Repo 'tests' 'LayerPeek.App.Tests'), '--', '--filter-class', '*KeychainSecretStoreTests')
        if (-not $run.Totals) { return New-VerifyResult FAIL "no test results (build failed?), exit code $($run.ExitCode)." }
        $left = @(Get-KeychainTestServices)
        if ($run.Totals.Failed -gt 0) { return New-VerifyResult FAIL (Format-Totals $run.Totals) }
        if ($run.Totals.Skipped -gt 0) {
            $reason = ($run.Skipped | Select-Object -First 1).Reason
            # Stays a FAIL (a skipped write test never counts); the session note only says why the Keychain may be locked.
            $session = Get-SessionLimitation $c
            $note = if ($session) { " This session has a limitation too ($session): an agent, SSH or screen-locked session cannot unlock the Keychain; run the check in your own Terminal." } else { '' }
            return New-VerifyResult FAIL "$($run.Totals.Skipped) Keychain test(s) skipped (a skipped write test does not count): $reason$note"
        }
        if ($left.Count -gt 0) { return New-VerifyResult FAIL "Keychain items with the test prefix were left: $($left -join ', ')" }
        New-VerifyResult PASS "$(Format-Totals $run.Totals); no LayerPeek.Tests item left in the Keychain."
    }

    New-VerifyCheck -Id 'mac.dock' -Kind Auto -Title 'the app runs without a Dock icon (LSUIElement, LaunchServices type UIElement)' `
        -Closes 'T-019 kritérium 7 (bez ikony v Docku)' -Action {
        param($c)
        $data = New-TestData -Context $c
        $app = Start-TestApp -Context $c -TestData $data
        try {
            $null = Wait-VerifyCondition -TimeoutSeconds 20 -Condition { $null -ne (Get-LaunchServicesAppType -ListOutput @(& /usr/bin/lsappinfo list 2>$null) -ProcessId $app.Id) }
            $list = @(& /usr/bin/lsappinfo list 2>$null)
            $type = Get-LaunchServicesAppType -ListOutput $list -ProcessId $app.Id
            $null = Save-VerifyEvidence -Context $c -Name 'lsappinfo.txt' -Content @($list | Select-String -SimpleMatch "pid = $($app.Id)" -Context 0, 3 | ForEach-Object { $_.ToString() })
            if ($type -cne 'UIElement') { return New-VerifyResult FAIL "LaunchServices type '$type', expected UIElement (no Dock icon, not in Cmd-Tab)." }
            New-VerifyResult PASS 'LaunchServices type UIElement: no Dock icon and not in the Cmd-Tab switcher.'
        }
        finally { $null = Stop-TestApp $data $app }
    }

    New-VerifyCheck -Id 'mac.menubar' -Needs Session -Kind Auto -Title 'status item in the menu bar; Quit ends the app and removes it' `
        -Closes 'T-019 kritérium 7 (ikona jen v menu baru)', 'T-018 kritérium 3 (část: „Ukončit“ ukončí aplikaci)' -Action {
        param($c)
        Invoke-SessionCheck $c {
            $data = New-TestData -Context $c
            $app = Start-TestApp -Context $c -TestData $data
            $shown = Wait-VerifyCondition -TimeoutSeconds 20 -Condition { Test-StatusItem $c $app.Id }
            $null = Save-VerifyEvidence -Context $c -Name 'windows.json' -Content (ConvertTo-VerifyJsonArray (Get-ProcessWindows $c $app.Id))
            $trusted = Test-Accessibility $c
            if ($trusted) { $null = Save-VerifyEvidence -Context $c -Name 'status-items.json' -Content (ConvertTo-VerifyJsonArray (Get-StatusItems $c $app.Id)) }
            # Every process's menu bar level windows: shows who owns the status items when the app's own are missing.
            $menuBar = @((Invoke-Helper $c @('menubar')) -join "`n" | ConvertFrom-Json)
            $null = Save-VerifyEvidence -Context $c -Name 'menubar-windows.json' -Content (ConvertTo-VerifyJsonArray $menuBar)
            $code = Stop-TestApp $data $app
            if (-not $shown -and -not $trusted) {
                Skip-VerifyCheck 'no status item window (layer 25) of the app; this macOS shows status items only to the Accessibility API: switch on Accessibility for the terminal and run it again.'
            }
            if (-not $shown) {
                $owners = @(Get-VerifyMenuBarOwners $menuBar)
                $others = if ($owners.Count -gt 0) { "status item windows (layer 25) of: $($owners -join ', ')" } else { 'no process has a status item window (layer 25)' }
                return New-VerifyResult FAIL "no status item of the app (pid $($app.Id)) in the menu bar (no window on layer 25, nothing in AXExtrasMenuBar, status-items.json); $others (menubar-windows.json)."
            }
            if ($code -ne 0) { return New-VerifyResult FAIL "Quit ended the app with exit code $code." }
            New-VerifyResult PASS 'status item shown in the menu bar; Quit (the same command as the menu item) ended the app with 0 and the item is gone.'
        }
    }

    New-VerifyCheck -Id 'mac.menu-items' -Needs Session, Accessibility -Kind Permission `
        -Permission 'Accessibility for the terminal (System Settings > Privacy & Security > Accessibility) to open the menu of the status item and choose its items, and an unlocked screen' `
        -Title 'the real menu of the status item through the Accessibility API: items and tooltip in six printer states, camera items, an unknown item, Quit from the menu (simulator x20)' `
        -Closes 'T-018 kritérium 3 (menu se zobrazí, aktualizuje, „Ukončit“ z menu; ruční část mac.menu mimo vzhled ikon)', 'T-209 kritérium 3 (mac.menu-items)' -Action {
        param($c)
        if (-not (Request-Accessibility $c)) { Skip-VerifyCheck 'no Accessibility permission for the terminal (System Settings > Privacy & Security > Accessibility; it may need a restart of the terminal).' }
        Invoke-SessionCheck $c {
            # The texts of the language the test data sets (en: the neutral resources), never written into the check.
            $texts = Get-VerifyResxStrings (Join-Path $script:Repo 'src' 'LayerPeek.App' 'Resources' 'Strings.resx')
            $label = { param($Key) ConvertFrom-VerifyAccessText $texts[$Key] }
            $simulator = Get-Simulator $c -Speed 20
            $data = New-TestData -Context $c -Simulator $simulator -Camera $simulator.Camera -Window ([ordered]@{ rememberLastPlacement = $false; startMode = 'Normal' })
            $name = (Get-Content -LiteralPath (Join-Path $data.Data 'settings.json') -Raw | ConvertFrom-Json).printers[0].name
            Set-SimulatorScenario $simulator 'idle'
            $app = Start-TestApp -Context $c -TestData $data
            $problems = [System.Collections.Generic.List[string]]::new()
            $helps = [System.Collections.Generic.List[string]]::new()
            $readTip = { @(Get-StatusItems $c $app.Id | Where-Object { $_.width -gt 0 }) | Select-Object -First 1 | ForEach-Object help }
            # Waits until the tooltip shows the state (the app polls every 5 s; offline needs a failed request first).
            $waitState = {
                param($Key, [switch] $Progress)
                $state = $texts[$Key]
                $ok = Wait-VerifyCondition -TimeoutSeconds 60 -IntervalMilliseconds 1000 -Condition { -not (Get-VerifyToolTipProblem -ToolTip (& $readTip) -Texts $texts -Name $name -State $state -Progress:$Progress) }
                if ($ok) { $null } else { Get-VerifyToolTipProblem -ToolTip (& $readTip) -Texts $texts -Name $name -State $state -Progress:$Progress }
            }
            try {
                if (-not (Wait-VerifyCondition -TimeoutSeconds 20 -IntervalMilliseconds 1000 -Condition { $null -ne (& $readTip) })) {
                    return New-VerifyResult FAIL "no status item of the app (pid $($app.Id)) in AXExtrasMenuBar within 20 s."
                }
                $states = [ordered]@{ idle = 'StateIdle'; printing = 'StatePrinting'; paused = 'StatePaused'; finished = 'StateFinished'; error = 'StateError'; offline = 'StateOffline' }
                foreach ($scenario in $states.Keys) {
                    $progress = $scenario -in 'printing', 'paused'
                    if ($scenario -ne 'idle') { Set-SimulatorScenario $simulator $scenario }
                    $tipProblem = & $waitState $states[$scenario] -Progress:$progress
                    if ($tipProblem) { $problems.Add("${scenario}: $tipProblem"); continue }
                    $menu = Get-StatusMenu $c $app.Id
                    $null = Save-VerifyEvidence -Context $c -Name "menu-$scenario.json" -Content ($menu | ConvertTo-Json -Depth 30)
                    foreach ($problem in Get-VerifyMenuProblems -Items @($menu.items) -Texts $texts -Name $name -State $texts[$states[$scenario]] -Progress:$progress -Camera) { $problems.Add("${scenario}: $problem") }
                    foreach ($help in Get-VerifyMenuHelps -Items @($menu.items)) { if (-not $helps.Contains($help)) { $helps.Add($help) } }
                    if (@(Get-OpenMenuWindows $c $app.Id).Count -gt 0) { $problems.Add("${scenario}: a menu window is still open after the helper closed the menu") }
                }
                $null = Save-VerifyEvidence -Context $c -Name 'disabled-items-help.txt' -Content $helps

                # The camera items through menupress, from a printer state whose item title does not change (no progress).
                # The scenario "offline" switches the printer off; only /_simulator/online switches it on again (a scenario
                # alone leaves it offline: first real run 2026-11-15).
                Set-SimulatorPrinter $simulator 'online'
                Set-SimulatorScenario $simulator 'idle'
                $tipProblem = & $waitState 'StateIdle'
                if ($tipProblem) { $problems.Add("idle again: $tipProblem") }
                $printer = $texts['MenuPrinter'] -f $name, $texts['StateIdle']
                $miss = Invoke-MenuPress $c $app.Id "$printer>No such item"
                $null = Save-VerifyEvidence -Context $c -Name 'menupress-unknown.txt' -Content $miss.Output
                if ($miss.ExitCode -ne 1 -or -not ($miss.Output -join ' ').Contains("'$(& $label 'MenuOpenPrusaLink')'")) { $problems.Add("menupress of an unknown item: exit code $($miss.ExitCode), expected 1 with the titles of the submenu ($($miss.Output -join ' '))") }
                if (@(Get-OpenMenuWindows $c $app.Id).Count -gt 0) { $problems.Add('the menu stayed open after menupress of an unknown item') }
                $show = Invoke-MenuPress $c $app.Id "$printer>$(& $label 'MenuShowCamera')"
                if ($show.ExitCode -ne 0) { $problems.Add("menupress '$(& $label 'MenuShowCamera')': $($show.Output -join ' ')") }
                elseif (-not (Wait-VerifyCondition -TimeoutSeconds 15 -Condition { $null -ne (Get-CameraWindow $c $app.Id) })) { $problems.Add("'$(& $label 'MenuShowCamera')' from the menu opened no camera window within 15 s") }
                else {
                    $finder = Invoke-FinderToFront
                    $front = Invoke-MenuPress $c $app.Id "$printer>$(& $label 'MenuCameraToFront')"
                    if ($front.ExitCode -ne 0) { $problems.Add("menupress '$(& $label 'MenuCameraToFront')': $($front.Output -join ' ')") }
                    elseif (-not (Wait-VerifyCondition -TimeoutSeconds 10 -Condition { (Get-FrontProcessId) -eq $app.Id -and $null -ne (Get-CameraWindow $c $app.Id) })) { $problems.Add("'$(& $label 'MenuCameraToFront')' did not bring the camera window to the front (Finder was in front: $finder; front app $(Get-FrontAppText))") }
                    $hide = Invoke-MenuPress $c $app.Id "$printer>$(& $label 'MenuHideCamera')"
                    if ($hide.ExitCode -ne 0) { $problems.Add("menupress '$(& $label 'MenuHideCamera')': $($hide.Output -join ' ')") }
                    elseif (-not (Wait-VerifyCondition -TimeoutSeconds 10 -Condition { $null -eq (Get-CameraWindow $c $app.Id) })) { $problems.Add("'$(& $label 'MenuHideCamera')' left the camera window open") }
                }

                $quit = Invoke-MenuPress $c $app.Id (& $label 'MenuQuit')
                if ($quit.ExitCode -ne 0) { $problems.Add("menupress '$(& $label 'MenuQuit')': $($quit.Output -join ' ')") }
                elseif (-not $app.WaitForExit(20000)) { $problems.Add("'$(& $label 'MenuQuit')' from the menu did not end the app within 20 s") }
                elseif ($app.ExitCode -ne 0) { $problems.Add("'$(& $label 'MenuQuit')' from the menu ended the app with exit code $($app.ExitCode)") }
                elseif (Test-StatusItem $c $app.Id) { $problems.Add('the status item is still in the menu bar after Quit') }
            }
            finally { $null = Stop-TestApp $data $app }
            if ($problems.Count -gt 0) { return New-VerifyResult FAIL "$($problems.Count) problem(s): $($problems -join '; ')" }
            $withHelp = @($helps | Where-Object { $_ -notlike '*help none' }).Count
            New-VerifyResult PASS "menu items and tooltip followed idle, printing, paused, finished, error and offline (menu-*.json); Show camera, Bring camera to front and Hide camera through menupress, an unknown item gave exit code 1 with the titles and a closed menu; Quit from the menu ended the app with 0 and removed the status item. Disabled items: $withHelp of $($helps.Count) with AXHelp (disabled-items-help.txt)."
        }
    }

    New-VerifyCheck -Id 'mac.settings-front' -Needs Session -Kind Auto -Title 'Settings, About, Diagnostics and a later start come to the front with focus over another app' `
        -Closes 'T-019 kritérium 8 (okno Nastavení v popředí s fokusem)' -Action {
        param($c)
        Invoke-SessionCheck $c {
            $simulator = Get-Simulator $c
            $data = New-TestData -Context $c -Simulator $simulator
            $app = Start-TestApp -Context $c -TestData $data
            $notes = [System.Collections.Generic.List[string]]::new()
            try {
                foreach ($command in 'settings', 'about', 'diagnostics', 'activate') {
                    if (-not (Invoke-FinderToFront)) { return New-VerifyResult FAIL "Finder did not come to the front (needed as the other app); front app: $(Get-FrontAppText)." }
                    Send-TestCommand $data $command
                    $front = Wait-VerifyCondition -TimeoutSeconds 10 -Condition { (Get-FrontProcessId) -eq $app.Id -and (Get-VerifyMainWindow @(Get-ProcessWindows $c $app.Id)) }
                    $null = Save-VerifyEvidence -Context $c -Name "$command-windows.json" -Content (ConvertTo-VerifyJsonArray (Get-ProcessWindows $c $app.Id))
                    if (-not $front) { return New-VerifyResult FAIL "'$command': the app is not the front app with a window (front app $(Get-FrontAppText), app pid $($app.Id))." }
                    $notes.Add($command)
                }
            }
            finally { $null = Stop-TestApp $data $app }
            New-VerifyResult PASS "front app with an open window over Finder after: $($notes -join ', ') (the active app receives the key events)."
        }
    }

    New-VerifyCheck -Id 'mac.plist-localnetwork' -Kind Auto -Title 'NSLocalNetworkUsageDescription in every app language as macOS picks it (NSBundle)' `
        -Closes 'T-097 kritérium 3 (podklad: text dialogu v každém jazyce; dialog sám viz mac.local-network)' -Action {
        param($c)
        $settings = Get-MacBundleSettings
        $languages = @(Get-AppLanguageCodes -SourceText (Get-Content -LiteralPath (Join-Path $script:Repo $settings.AppLanguagesSource) -Raw))
        $localizations = Join-Path $script:Repo $settings.Localizations
        $app = Get-ReleaseApp
        $source = 'the release bundle'
        if (-not $app) {
            # A bundle with only the Info.plist and the localizations, built like New-MacAppBundle does.
            $source = 'a temporary bundle from build/macos'
            $folder = Join-Path $c.RunFolder 'tmp' "plist-$(New-ShortId)"
            $app = Join-Path $folder 'LayerPeek.app'
            $null = New-Item -ItemType Directory -Force -Path (Join-Path $app 'Contents' 'Resources')
            $plist = ConvertTo-MacInfoPlist -TemplateText (Get-Content -LiteralPath (Join-Path $script:Repo $settings.PlistTemplate) -Raw) -Version (Get-AppVersion)
            [System.IO.File]::WriteAllText((Join-Path $app 'Contents' 'Info.plist'), $plist)
            foreach ($file in Get-MacLocalizationFiles $localizations) {
                $target = Join-Path $app 'Contents' 'Resources' $file
                $null = New-Item -ItemType Directory -Force -Path (Split-Path -Parent $target)
                Copy-Item -LiteralPath (Join-Path $localizations $file) -Destination $target
            }
        }
        $problems = [System.Collections.Generic.List[string]]::new()
        foreach ($problem in @(Get-MacLocalizationProblems -Directory $localizations -PlistText (Get-Content -LiteralPath (Join-Path $app 'Contents' 'Info.plist') -Raw) -Languages $languages)) { $problems.Add($problem) }
        $json = (Invoke-Helper $c (@('localized', $app, 'NSLocalNetworkUsageDescription') + $languages)) -join "`n"
        $null = Save-VerifyEvidence -Context $c -Name 'localized.json' -Content $json
        $resolved = $json | ConvertFrom-Json -AsHashtable
        $english = $resolved['en'].value
        foreach ($language in $languages) {
            $entry = $resolved[$language]
            $expected = (ConvertFrom-StringsText ([System.IO.File]::ReadAllText((Join-Path $localizations "$language.lproj" 'InfoPlist.strings'))))['NSLocalNetworkUsageDescription']
            if ($entry.localization -cne $language) { $problems.Add("${language}: macOS picks the localization '$($entry.localization)'") }
            elseif ([string]::IsNullOrWhiteSpace($entry.value)) { $problems.Add("${language}: no text") }
            elseif ($entry.value -cne $expected) { $problems.Add("${language}: the bundle text differs from build/macos/Localizations") }
            elseif ($language -ne 'en' -and $entry.value -ceq $english) { $problems.Add("${language}: the text is the English one") }
        }
        if ($problems.Count -gt 0) { return New-VerifyResult FAIL ($problems -join '; ') }
        New-VerifyResult PASS "$($languages.Count) languages ($($languages -join ', ')) in $source each resolve to their own translated text (localized.json)."
    }

    New-VerifyCheck -Id 'mac.decode' -Kind Auto -Title 'helper decode (AVFoundation, as QuickTime Player plays a file): every frame, monotonic times and five exact seeks of a sample recording; a damaged copy fails' `
        -Closes 'T-210 kritérium 1 (helper decode na t158-same-idr-pic-id.mp4: 5 snímků bez chyby, seek OK; oříznutý mdat exit 1)' -Action {
        param($c)
        $sample = Join-Path (Get-SamplesFolder) 'timelapse' 't158-same-idr-pic-id.mp4'
        if (-not (Test-Path -LiteralPath $sample -PathType Leaf)) { Skip-VerifyCheck "the sample recording $(Hide-VerifyHomePath $sample) is missing (the real camera samples are in ~/LayerPeek-samples, never in the repository; LAYERPEEK_SAMPLES sets another folder)." }
        $good = Invoke-Decode $c $sample 'decode-sample.json'
        $problems = [System.Collections.Generic.List[string]]::new()
        foreach ($problem in Get-VerifyDecodeProblems -Report $good.Report -ExitCode $good.ExitCode -Frames 5) { $problems.Add("sample: $problem") }
        # The damaged copy: cut in the middle of an mdat box, so that a sample is incomplete.
        $bytes = [System.IO.File]::ReadAllBytes($sample)
        $cut = Get-VerifyMdatCut -Bytes $bytes
        if ($null -eq $cut) { return New-VerifyResult FAIL "the sample has no mdat box to cut ($($problems -join '; '))." }
        $folder = Join-Path $c.RunFolder 'tmp' "decode-$(New-ShortId)"
        $null = New-Item -ItemType Directory -Force -Path $folder
        $damaged = Join-Path $folder 'damaged.mp4'
        $stream = [System.IO.File]::Create($damaged)
        try { $stream.Write($bytes, 0, [int] $cut) } finally { $stream.Dispose() }
        try { $bad = Invoke-Decode $c $damaged 'decode-damaged.json' }
        finally { Remove-Item -LiteralPath $folder -Recurse -Force }
        if ($bad.ExitCode -ne 1) { $problems.Add("the damaged copy (cut at byte $cut of $($bytes.Length)) ended with exit code $($bad.ExitCode), expected 1") }
        elseif (@($bad.Report.problems).Count -eq 0) { $problems.Add('the damaged copy ended with exit code 1 but without a problem in its JSON') }
        if ($problems.Count -gt 0) { return New-VerifyResult FAIL "$($problems.Count) problem(s): $($problems -join '; ')" }
        $seeks = @($good.Report.seeks | ForEach-Object { "$($_.at) mean $($_.mean) / sd $($_.stdDev)" }) -join ', '
        New-VerifyResult PASS "t158-same-idr-pic-id.mp4: $($good.Report.codec) $($good.Report.width) x $($good.Report.height), $($good.Report.decodedFrames) of $($good.Report.samples) frames decoded, $($good.Report.duration) s, times monotonic, seeks $seeks (decode-sample.json); the copy cut at byte $cut of $($bytes.Length) (in an mdat) gave exit code 1: $(@($bad.Report.problems)[0]) (decode-damaged.json)."
    }

    New-VerifyCheck -Id 'mac.gatekeeper' -Kind Auto -Title 'Gatekeeper rejects a quarantined copy of the .dmg app (spctl), as docs/release.md describes' `
        -Closes 'T-019 kritérium 12 (část: posouzení Gatekeeperem; dialog viz mac.gatekeeper-dialog)' -Action {
        param($c)
        $version = Get-AppVersion
        $names = Get-MacReleaseFileNames -Version $version -Runtime 'osx-arm64'
        $dmg = Join-Path $script:Repo 'artifacts' 'release' $version 'dist' $names.DiskImage
        if (-not (Test-Path -LiteralPath $dmg)) { Skip-VerifyCheck "no $($names.DiskImage) in artifacts/release/$version/dist; build it first: caffeinate -d -u -i pwsh scripts/release-build.ps1 -SkipFeedSignature" }
        $temp = Join-Path ([System.IO.Path]::GetTempPath()) "layerpeek-verify-gk-$(New-ShortId)"
        $mount = Join-Path $temp 'mnt'
        $copy = Join-Path $temp 'copy'
        $null = New-Item -ItemType Directory -Force -Path $mount, $copy
        try {
            $attach = Invoke-VerifyTool -Context $c -Label 'hdiutil-attach' -FilePath '/usr/bin/hdiutil' -ArgumentList @('attach', '-nobrowse', '-readonly', '-noautoopen', '-mountpoint', $mount, $dmg)
            if ($attach.ExitCode -ne 0) { return New-VerifyResult FAIL 'hdiutil attach of the .dmg failed.' }
            try { & /usr/bin/ditto (Join-Path $mount 'LayerPeek.app') (Join-Path $copy 'LayerPeek.app') }
            finally { $null = & /usr/bin/hdiutil detach $mount -quiet 2>&1; if ($LASTEXITCODE -ne 0) { $null = & /usr/bin/hdiutil detach $mount -force -quiet 2>&1 } }
            $app = Join-Path $copy 'LayerPeek.app'
            $dmgCopy = Join-Path $copy $names.DiskImage
            Copy-Item -LiteralPath $dmg -Destination $dmgCopy
            # What a browser download sets (flags 0083: downloaded, not yet approved), on the copies in the temp folder only.
            $quarantine = '0083;{0:x8};Safari;{1}' -f [DateTimeOffset]::UtcNow.ToUnixTimeSeconds(), [guid]::NewGuid().ToString().ToUpperInvariant()
            foreach ($item in $app, $dmgCopy) { & /usr/bin/xattr -w com.apple.quarantine $quarantine $item }
            $null = Invoke-VerifyTool -Context $c -Label 'xattr' -FilePath '/usr/bin/xattr' -ArgumentList @('-p', 'com.apple.quarantine', $app)
            $null = Invoke-VerifyTool -Context $c -Label 'codesign' -FilePath '/usr/bin/codesign' -ArgumentList @('-dv', '--verbose=2', $app)
            $assessApp = Invoke-VerifyTool -Context $c -Label 'spctl-app' -FilePath '/usr/sbin/spctl' -ArgumentList @('--assess', '-vv', '--type', 'execute', $app)
            $assessDmg = Invoke-VerifyTool -Context $c -Label 'spctl-dmg' -FilePath '/usr/sbin/spctl' -ArgumentList @('--assess', '-vv', '--type', 'open', '--context', 'context:primary-signature', $dmgCopy)
            $verdict = Get-VerifySpctlAssessment -ExitCode $assessApp.ExitCode -Output $assessApp.Output
            $dmgVerdict = Get-VerifySpctlAssessment -ExitCode $assessDmg.ExitCode -Output $assessDmg.Output
            if ($verdict.Accepted) { return New-VerifyResult FAIL "Gatekeeper accepts the ad-hoc signed app ($($verdict.Source)); docs/release.md describes a rejection." }
            New-VerifyResult PASS "app: $($verdict.Verdict ?? 'rejected') (source=$($verdict.Source)); .dmg: $($dmgVerdict.Verdict ?? "exit code $($assessDmg.ExitCode)") (source=$($dmgVerdict.Source)). Expected for an ad-hoc signed, not notarized app: 'Done' / 'Move to Bin', then Privacy & Security > Open Anyway."
        }
        finally { Remove-Item -LiteralPath $temp -Recurse -Force -ErrorAction SilentlyContinue }
    }

    New-VerifyCheck -Id 'mac.release-bundle' -Needs ReleaseBuild, AppNotRunning -Kind Auto -Title 'the apps of the .dmg and the .tar.gz: signature, hardened runtime, load paths, Info.plist, SHA-256, and a start without the developer environment' `
        -Closes 'T-217 kritérium 1 (kontrola .dmg a .tar.gz)', 'T-217 kritérium 2 (start bez prostředí vývojáře)', 'T-026 kritérium „Podepsaný bundle se spustí na čistém Macu“ (start s vyčištěným prostředím, rozhodnutí 2026-11-13; stream se nekontroluje)' -Action {
        param($c)
        foreach ($reason in (Get-ReleaseBuildProblem), (Get-RunningAppProblem)) { if ($reason) { Skip-VerifyCheck $reason } }
        $files = Get-ReleaseBuildFiles
        $problems = [System.Collections.Generic.List[string]]::new()
        # SHA-256 of every file of dist against SHA256SUMS-macos.txt, which must list both downloads.
        $sums = Read-Sha256Sums $files.Sums
        foreach ($problem in @(Get-Sha256SumsProblems $files.Sums)) { $problems.Add($problem) }
        foreach ($path in $files.DiskImage, $files.Archive) {
            if (-not $sums.Contains((Split-Path -Leaf $path))) { $problems.Add("$(Split-Path -Leaf $path) is not listed in $(Split-Path -Leaf $files.Sums).") }
        }
        # .noindex: Spotlight does not index (and register) the copies.
        $temp = Join-Path ([System.IO.Path]::GetTempPath()) "layerpeek-verify-bundle-$(New-ShortId).noindex"
        $mount = Join-Path $temp 'mnt'
        $copies = [ordered]@{ '.dmg' = Join-Path $temp 'dmg' 'LayerPeek.app'; '.tar.gz' = Join-Path $temp 'tar' 'LayerPeek.app' }
        $null = New-Item -ItemType Directory -Force -Path $mount, (Split-Path -Parent $copies['.dmg']), (Split-Path -Parent $copies['.tar.gz'])
        try {
            $attach = Invoke-VerifyTool -Context $c -Label 'hdiutil-attach' -FilePath '/usr/bin/hdiutil' -ArgumentList @('attach', '-nobrowse', '-readonly', '-noautoopen', '-mountpoint', $mount, $files.DiskImage)
            if ($attach.ExitCode -ne 0) { $problems.Add('hdiutil attach of the .dmg failed (hdiutil-attach.txt).') }
            else {
                try { & /usr/bin/ditto (Join-Path $mount 'LayerPeek.app') $copies['.dmg'] }
                finally { Dismount-VerifyImage $mount }
            }
            $untar = Invoke-VerifyTool -Context $c -Label 'tar' -FilePath '/usr/bin/tar' -ArgumentList @('-xzf', $files.Archive, '-C', (Split-Path -Parent $copies['.tar.gz']))
            if ($untar.ExitCode -ne 0) { $problems.Add('tar -xzf of the .tar.gz failed (tar.txt).') }
            $rules = Get-MacLoadPathRules -RepoRoot $script:Repo -HomePath ([Environment]::GetFolderPath('UserProfile'))
            foreach ($label in $copies.Keys) {
                $app = $copies[$label]
                if (-not (Test-Path -LiteralPath (Join-Path $app 'Contents' 'Info.plist') -PathType Leaf)) { $problems.Add("${label}: no LayerPeek.app at the top level."); continue }
                $plist = @(& /usr/bin/plutil -convert xml1 -o - (Join-Path $app 'Contents' 'Info.plist') 2>&1) -join "`n"
                # The same checks as release-build.ps1 (ReleaseTools.psm1), and the stricter load paths of a clean Mac.
                $found = @(Get-MacSignatureProblems $app) + @(Get-MacHardenedRuntimeProblems $app) + @(Get-MacBundleLibraryProblems $app) +
                    @(Get-MacBundleLoadPathProblems -AppPath $app -Rules $rules) + @(Get-MacReleasePlistProblems -PlistText $plist -Version $files.Version -BundleIdentifier $script:BundleId)
                foreach ($problem in $found) { $problems.Add("${label}: $problem") }
            }
            $null = Save-VerifyEvidence -Context $c -Name 'bundle-problems.txt' -Content @($problems)
            # The app of the .dmg, started like on a Mac without the developer tools (aot-smoke.ps1: profile moved aside and back).
            if (Test-Path -LiteralPath $copies['.dmg']) {
                # The app loads FFmpeg only for a camera (and HarfBuzz later too), so its start proves only Avalonia and
                # Skia: every library of the bundle is loaded here with all its dependencies, in the same clean environment.
                $libraries = @(Get-ChildItem -LiteralPath (Join-Path $copies['.dmg'] 'Contents' 'MacOS') -File -Filter '*.dylib' | ForEach-Object FullName | Sort-Object)
                $loadArguments = Get-SmokeCleanEnvironmentArguments -Executable (Get-Helper $c) -Arguments (@('dlopen') + $libraries) -HomePath ([Environment]::GetFolderPath('UserProfile')) -TempPath ([System.IO.Path]::GetTempPath())
                $load = Invoke-VerifyTool -Context $c -Label 'dlopen-clean' -FilePath '/usr/bin/env' -ArgumentList $loadArguments
                if ($load.ExitCode -ne 0) { $problems.Add(".dmg: a library does not load without the developer environment: $(@($load.Output) -join ' ')") }
                $pwsh = @(Get-PwshCommand)
                $arguments = @($pwsh | Select-Object -Skip 1) + @('-NoProfile', '-File', (Join-Path $script:Repo 'scripts' 'aot-smoke.ps1'), '-Runtime', 'osx-arm64', '-PublishDir', (Join-Path $copies['.dmg'] 'Contents' 'MacOS'), '-CleanEnvironment')
                $started = Invoke-VerifyTool -Context $c -Label 'aot-smoke-clean' -FilePath $pwsh[0] -ArgumentList $arguments -Show
                if ($started.ExitCode -ne 0) { $problems.Add("the start of the .dmg app without the developer environment failed (aot-smoke-clean.txt): $(@($started.Output | Where-Object { $_ -match 'FAILED|NOT ' }) | Select-Object -First 1)") }
            }
        }
        finally {
            Dismount-VerifyImage $mount
            Remove-Item -LiteralPath $temp -Recurse -Force -ErrorAction SilentlyContinue
        }
        # Nothing may be left: no attached image, no copy of the app.
        if (@(& /usr/bin/hdiutil info 2>$null) -match [regex]::Escape($mount)) { $problems.Add("the .dmg is still attached at $mount.") }
        if (Test-Path -LiteralPath $temp) { $problems.Add("the copies in $temp could not be deleted.") }
        if ($problems.Count -gt 0) { return New-VerifyResult FAIL "$($problems.Count) problem(s): $($problems[0])" }
        New-VerifyResult PASS "$($files.Version) (build-info.json: $($files.Commit)): .dmg and .tar.gz apps signed (--deep --strict), hardened runtime and entitlements, libraries only from the bundle and the system, Info.plist, SHA-256; in env -i (no DOTNET_ROOT, no Homebrew) all $($libraries.Count) libraries of the .dmg app loaded (dlopen, FFmpeg included) and the app ran as UIElement without a native library error; nothing attached or left."
    }

    New-VerifyCheck -Id 'mac.notifications' -Kind Auto -Title 'notification E2E of the test bundle (delivery, removal on quit and by the cleanup mode)' `
        -Closes 'T-020 kritérium 1 (doručení ze sestavené .app, test bundle)', 'T-020 kritérium 5 (odstranění při ukončení i úklidem po pádu)' -Action {
        param($c)
        $status = Get-TestNotificationStatus $c
        Write-VerifyStep $c "Notifications of 'LayerPeek E2E': $($status.Permission)."
        if ($status.Permission -ne 'Allowed') {
            if ($c.Unattended) { Skip-VerifyCheck "the notifications of 'LayerPeek E2E' are $($status.Permission); macOS would ask: run 'pwsh tools/macos-verify.ps1 -Only mac.notifications' interactively once." }
            if ($status.Permission -eq 'Denied') {
                Write-VerifyStep $c "Allow them: System Settings > Notifications > LayerPeek E2E > Allow notifications (opening System Settings)." 'Yellow'
                & /usr/bin/open 'x-apple.systempreferences:com.apple.Notifications-Settings.extension'
                if (-not (Read-VerifyAnswer -Context $c -Question "Have you allowed the notifications of 'LayerPeek E2E'?")) { Skip-VerifyCheck "notifications of 'LayerPeek E2E' denied; allow them and run -Only mac.notifications again." }
            }
            elseif (-not (Read-VerifyAnswer -Context $c -Question "macOS will ask whether 'LayerPeek E2E' may send notifications (a notification with Options > Allow). Run the test and allow it?")) {
                Skip-VerifyCheck 'declined: macOS was not asked.'
            }
        }
        $run = Invoke-DotnetTest $c 'dotnet-test-notifications' @('--project', (Join-Path $script:Repo 'tests' 'LayerPeek.App.Tests'), '--', '--filter-class', '*MacNotificationsE2ETests') @{ LAYERPEEK_E2E = '1' }
        if (-not $run.Totals) { return New-VerifyResult FAIL "no test results, exit code $($run.ExitCode)." }
        if ($run.Totals.Failed -gt 0) { return New-VerifyResult FAIL (Format-Totals $run.Totals) }
        if ($run.Totals.Skipped -gt 0) { return New-VerifyResult FAIL "the E2E test was skipped: $(($run.Skipped | Select-Object -First 1).Reason)" }
        New-VerifyResult PASS "$(Format-Totals $run.Totals): delivered, removed on quit and by --layerpeek-e2e-clear-toasts after a kill."
    }

    New-VerifyCheck -Id 'mac.display-wait' -Kind Auto -Title 'E2E of the start without an active display (simulated, MacDisplayWaitE2ETests)' `
        -Closes 'T-096 kritérium 1 (podklad: simulované čekání)', 'T-099 kritérium 1 (podklad: tray se připojí ke sledování)' -Action {
        param($c)
        $run = Invoke-DotnetTest $c 'dotnet-test-display-wait' @('--project', (Join-Path $script:Repo 'tests' 'LayerPeek.App.Tests'), '--', '--filter-class', '*MacDisplayWaitE2ETests') @{ LAYERPEEK_E2E = '1' }
        if (-not $run.Totals) { return New-VerifyResult FAIL "no test results, exit code $($run.ExitCode)." }
        if ($run.Totals.Failed -gt 0 -or $run.Totals.Skipped -gt 0) { return New-VerifyResult FAIL "$(Format-Totals $run.Totals) (run with an awake display, e.g. caffeinate -d -u)." }
        New-VerifyResult PASS (Format-Totals $run.Totals)
    }

    New-VerifyCheck -Id 'mac.camera' -Needs Session -Kind Auto -Title 'camera window in normal, mini and full-screen mode with a lasting RTSP stream (simulator)' `
        -Closes 'T-026 kritérium 1 (automatická část: všechny režimy se simulátorem)' -Action {
        param($c)
        Invoke-SessionCheck $c {
            $result = Invoke-CameraModes -Context $c
            if ($result.Problems.Count -gt 0) { return New-VerifyResult FAIL ($result.Problems -join '; ') }
            New-VerifyResult PASS "all modes open and stream: $($result.Notes -join '; ')"
        }
    }

    New-VerifyCheck -Id 'mac.camera-placement' -Needs Session -Kind Auto -Title 'camera window corners and centre on every connected monitor (normal and mini, 16 pt from the usable area)' `
        -Closes 'T-038 kritérium 1 (automatická část: J2 rohy a střed, D1 zvolený monitor)' -Action {
        param($c)
        $simulator = Get-Simulator $c
        $screens = @(Get-Screens $c)
        $null = Save-VerifyEvidence -Context $c -Name 'screens.json' -Content (ConvertTo-VerifyJsonArray $screens)
        $rows = [System.Collections.Generic.List[object]]::new()
        $problems = [System.Collections.Generic.List[string]]::new()
        foreach ($screen in $screens) {
            foreach ($mode in 'Normal', 'Mini') {
                foreach ($corner in $script:Corners) {
                    $window = [ordered]@{
                        rememberLastPlacement = $false
                        startMode             = $mode
                        monitor               = [ordered]@{ displayName = $screen.name; width = [int] ($screen.frame.width * $screen.scale); height = [int] ($screen.frame.height * $screen.scale); devicePath = $screen.uuid }
                        normal                = [ordered]@{ width = 480; height = 270; corner = $corner }
                        mini                  = [ordered]@{ width = 320; height = 180; corner = $corner }
                    }
                    $data = New-TestData -Context $c -Simulator $simulator -Camera $simulator.Camera -Window $window
                    $app = Start-TestApp -Context $c -TestData $data
                    # The usable area when the app places the window: its own Dock icon (a normal window) can make the
                    # Dock smaller afterwards on a narrow display with a full Dock (the Screen Sharing display, 2026-11-15).
                    $placedIn = @(Get-Screens $c | Where-Object uuid -CEQ $screen.uuid | ForEach-Object visible) | Select-Object -First 1
                    try {
                        Send-TestCommand $data "camera:$($script:PrinterId)"
                        $null = Wait-VerifyCondition -TimeoutSeconds 15 -Condition { $null -ne (Get-CameraWindow $c $app.Id) }
                        Start-Sleep -Milliseconds 1500
                        $frame = Get-CameraWindow $c $app.Id
                        # The usable area of that moment: the Dock gets smaller as more apps run on a narrow display (the
                        # Screen Sharing display, 2026-11-15), so the area of the start of the check can be off by points.
                        $visible = @(Get-Screens $c | Where-Object uuid -CEQ $screen.uuid | ForEach-Object visible) | Select-Object -First 1
                        if (-not $visible) { $visible = $screen.visible }
                        $judge = { param($Area) @(Get-VerifyCornerProblems -Window ([pscustomobject]@{ X = $frame.x; Y = $frame.y; Width = $frame.width; Height = $frame.height }) -Area ([pscustomobject]@{ X = $Area.x; Y = $Area.y; Width = $Area.width; Height = $Area.height }) -Corner $corner) }
                        $found = if (-not $frame) { @('no window') } else { @(& $judge $visible) }
                        if ($found.Count -gt 0 -and $placedIn -and "$($placedIn.y),$($placedIn.height)" -ne "$($visible.y),$($visible.height)" -and @(& $judge $placedIn).Count -eq 0) {
                            $found = @()
                            $visible = $placedIn
                        }
                        foreach ($problem in $found) { $problems.Add("$($screen.name) $mode ${corner}: $problem") }
                        $rows.Add([ordered]@{ Screen = $screen.name; Mode = $mode; Corner = $corner; Window = $frame; Visible = $visible; Problems = $found })
                    }
                    finally { $null = Stop-TestApp $data $app }
                }
            }
        }
        $null = Save-VerifyEvidence -Context $c -Name 'placements.json' -Content (ConvertTo-VerifyJsonArray $rows -Depth 6)
        if ($problems.Count -gt 0) { return New-VerifyResult FAIL "$($problems.Count) of $($rows.Count) placements wrong: $($problems[0])" }
        New-VerifyResult PASS "$($rows.Count) placements on $($screens.Count) monitor(s) ($(($screens.name) -join ', ')) are 16 pt from the usable area, never under the menu bar or the Dock."
    }

    New-VerifyCheck -Id 'mac.camera-pin' -Needs Session, Accessibility -Kind Auto -Title 'camera window pin (always on top) with real mouse and key events and the Accessibility API, normal window and mini preview' `
        -Closes 'T-122 (klik na špendlík a odjetí myši, přepnutí aplikace, Tab / mezerník / Enter, Tab po kliku na obraz, aktivace přes Accessibility jako VoiceOver)', 'T-117 kritéria „Tlačítko na hlavním panelu / ikona v Docku“, „Přístupnost“ a ruční část (klik přepne hned, Dock a ⌘Tab, kontextové menu pravým tlačítkem i ⌃-klikem, volba přežije restart)' -Action {
        param($c)
        if (-not (Test-Accessibility $c)) { Skip-VerifyCheck 'needs Accessibility for the terminal (real mouse and key events, the Accessibility tree of the camera window).' }
        Invoke-SessionCheck $c {
            $simulator = Get-Simulator $c
            $problems = [System.Collections.Generic.List[string]]::new()
            $notes = [System.Collections.Generic.List[string]]::new()
            foreach ($mode in 'Normal', 'Mini') {
                $found = @(Invoke-CameraPinCheck -Context $c -Simulator $simulator -Mode $mode)
                foreach ($line in $found) { if ($line.StartsWith('ok: ')) { $notes.Add("${mode}: $($line.Substring(4))") } else { $problems.Add("${mode}: $line") } }
            }
            if ($problems.Count -gt 0) { return New-VerifyResult FAIL ($problems -join '; ') }
            New-VerifyResult PASS ($notes -join '; ')
        }
    }

    New-VerifyCheck -Id 'mac.camera-outline' -Needs Session, Accessibility, ScreenRecording -Kind Auto -Title 'outline of the mini preview in a capture of the real window: at every edge and corner, brighter while the pointer is over it, none in the normal window' `
        -Closes 'T-118 kritéria „Obrys kolem malého náhledu v obou OS“ a „Snímky UI: malý náhled na světlém a tmavém pozadí“, část macOS (obrys vidět, ostré rohy, zesvětlí při najetí, v normálním okně není)' -Action {
        param($c)
        if (-not (Test-Accessibility $c)) { Skip-VerifyCheck 'needs Accessibility for the terminal (moving the pointer, the Accessibility tree).' }
        if ((Invoke-Helper $c @('screenrecording')) -notcontains 'true') { Skip-VerifyCheck 'needs Screen Recording for the terminal (System Settings > Privacy & Security > Screen & System Audio Recording), then a restart of the terminal.' }
        Invoke-SessionCheck $c {
            $problems = [System.Collections.Generic.List[string]]::new()
            $screen = @(Get-Screens $c | Where-Object main)[0]
            # No stream (nothing listens on port 9): the image stays black, so the outline is measured against a steady background.
            $data = New-TestData -Context $c -Simulator (Get-Simulator $c) -Camera 'rtsp://127.0.0.1:9/live' -Window ([ordered]@{ rememberLastPlacement = $false; startMode = 'Mini' })
            $app = Start-TestApp -Context $c -TestData $data
            $measure = {
                param([string] $Name)
                $window = Get-CameraWindow $c $app.Id
                $file = Join-Path (Get-VerifyEvidenceFolder $c) "$Name.png"
                & /usr/sbin/screencapture -x -o -l "$($window.number)" $file
                if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $file)) { throw "screencapture of window $($window.number) failed." }
                # The outline is 1 point: as many pixels as the capture has per point (2 on Retina, 1 at @1x).
                $probe = (Invoke-Helper $c @('ring', $file, '0', '1')) -join "`n" | ConvertFrom-Json
                $pixels = [math]::Max(1, [math]::Round($probe.width / $window.width))
                $edge = (Invoke-Helper $c @('ring', $file, '0', "$pixels")) -join "`n" | ConvertFrom-Json
                $inner = (Invoke-Helper $c @('ring', $file, "$($pixels + 2)", "$pixels")) -join "`n" | ConvertFrom-Json
                [pscustomobject]@{ Pixels = $pixels; Edge = $edge; Inner = $inner }
            }
            $sides = 'top', 'right', 'bottom', 'left'
            try {
                Send-TestCommand $data "camera:$($script:PrinterId)"
                if (-not (Wait-VerifyCondition -TimeoutSeconds 20 -Condition { $w = Get-CameraWindow $c $app.Id; $w -and $w.layer -gt 0 })) { return New-VerifyResult FAIL 'no mini preview opened.' }
                if (-not (Get-AxElement $c $app.Id 'MiniOutline')) { $problems.Add('the mini preview has no outline element (MiniOutline) in the Accessibility tree') }
                $null = Invoke-Helper $c @('move', "$($screen.visible.x + 4)", "$($screen.visible.y + $screen.visible.height - 4)")
                Start-Sleep -Milliseconds 800
                $away = & $measure 'mini-pointer-away'
                $over = Get-CameraWindow $c $app.Id
                $null = Invoke-Helper $c @('move', "$($over.x + $over.width / 2)", "$($over.y + $over.height / 3)")
                Start-Sleep -Milliseconds 800
                $hover = & $measure 'mini-pointer-over'
                $null = Save-VerifyEvidence -Context $c -Name 'rings.json' -Content (@{ away = $away; hover = $hover } | ConvertTo-Json -Depth 6)
                foreach ($side in $sides) {
                    $contrast = $away.Edge.$side - $away.Inner.$side
                    if ($contrast -lt 40) { $problems.Add("no outline at the $side edge ($([math]::Round($away.Edge.$side)) vs $([math]::Round($away.Inner.$side)) inside)") }
                    $brighter = $hover.Edge.$side - $away.Edge.$side
                    if ($brighter -lt 25) { $problems.Add("the outline at the $side edge is not brighter while the pointer is over the preview ($([math]::Round($hover.Edge.$side)) vs $([math]::Round($away.Edge.$side)))") }
                }
                $cornerLow = @($away.Edge.corners | Where-Object { $_ -lt ($away.Inner.top + 40) })
                if ($cornerLow.Count -gt 0) { $problems.Add("$($cornerLow.Count) corner(s) without the outline (rounded or cut corners)") }
                Send-TestCommand $data "camera:$($script:PrinterId)"
            }
            finally { $null = Stop-TestApp $data $app }

            $data = New-TestData -Context $c -Simulator (Get-Simulator $c) -Camera 'rtsp://127.0.0.1:9/live' -Window ([ordered]@{ rememberLastPlacement = $false; startMode = 'Normal' })
            $app = Start-TestApp -Context $c -TestData $data
            try {
                Send-TestCommand $data "camera:$($script:PrinterId)"
                if (-not (Wait-VerifyCondition -TimeoutSeconds 20 -Condition { (Get-CameraWindow $c $app.Id) -and (Get-AxElement $c $app.Id 'Surface') })) { $problems.Add('no normal camera window opened') }
                elseif (Get-AxElement $c $app.Id 'MiniOutline') { $problems.Add('the normal window shows the outline of the mini preview') }
            }
            finally { $null = Stop-TestApp $data $app }

            if ($problems.Count -gt 0) { return New-VerifyResult FAIL ($problems -join '; ') }
            New-VerifyResult PASS "outline $($away.Pixels) px at every edge and corner of the mini preview ($(($sides | ForEach-Object { '{0} {1:0}/{2:0}' -f $_, $away.Edge.$_, $hover.Edge.$_ }) -join ', ') away/over; inside about $([math]::Round($away.Inner.top))), none in the normal window; the colours are fixed (white over the black content), so light and dark appearance look the same. Captures: mini-pointer-away.png, mini-pointer-over.png."
        }
    }

    New-VerifyCheck -Id 'mac.dock-windows' -Needs Session, Accessibility -Kind Permission `
        -Permission "Accessibility for the terminal (System Settings > Privacy & Security > Accessibility) to read the Dock, its menu and the windows and to choose menu items, and an unlocked screen; for the notification part of (5) the notifications of 'LayerPeek E2E' allowed (mac.notifications)" `
        -Title 'Dock and Cmd-Tab follow the windows (LaunchServices type Foreground / UIElement): (1) Settings and the camera window, back to none after the last one; (2) the Dock menu lists the windows, a press on the Dock item and on a window of its menu brings it to the front with the focus; (3) Bring camera to front; (4) the menu bar of the app is back after another app; (5) the camera window closed while the printer is offline and opened again where it was without taking the front, the mini preview on top without a Dock icon, no notification (simulator)' `
        -Closes 'T-102 (ruční ověření: ikona v Docku a ⌘Tab podle oken, menu Docku se seznamem oken, klik na ikonu v Docku, fokus a pořadí oken, „Přenést kameru do popředí“; otázka 3: menu aplikace v menu baru po přepnutí na jinou aplikaci)', 'T-151 (ruční, Dock a ⌘Tab: okno zmizí při offline a objeví se na stejném místě bez krádeže fokusu, pořadí oken, malý náhled navrchu bez ikony v Docku, žádná notifikace)', 'T-215 kritérium 1' -Action {
        param($c)
        if (-not (Request-Accessibility $c)) { Skip-VerifyCheck 'no Accessibility permission for the terminal (System Settings > Privacy & Security > Accessibility; it may need a restart of the terminal).' }
        Invoke-SessionCheck $c {
            $texts = Get-VerifyResxStrings (Join-Path $script:Repo 'src' 'LayerPeek.App' 'Resources' 'Strings.resx')
            $label = { param($Key) ConvertFrom-VerifyAccessText $texts[$Key] }
            $problems = [System.Collections.Generic.List[string]]::new()
            $notes = [System.Collections.Generic.List[string]]::new()
            # Parts the script cannot judge here (notifications not allowed): a SKIP of the check with the reasons, never a PASS.
            $skipped = [System.Collections.Generic.List[string]]::new()
            $bundle = Get-TestBundle $c
            $simulator = Get-Simulator $c
            Set-SimulatorScenario $simulator 'idle'
            $printerId = $script:PrinterId
            # The LaunchServices type (rule of T-102): Foreground = Dock icon and Cmd-Tab, UIElement = neither.
            $expectType = {
                param([int] $AppId, [string] $Expected, [string] $What)
                $script:appType = $null
                if (-not (Wait-VerifyCondition -TimeoutSeconds 10 -Condition { ($script:appType = Get-AppType $AppId) -ceq $Expected })) { $problems.Add("${What}: LaunchServices type '$($script:appType)', expected $Expected") }
            }
            $titled = { param([int] $AppId, [string] $Title) @(Get-AxWindows $c $AppId | Where-Object title -CEQ $Title).Count -gt 0 }
            $close = {
                param([int] $AppId, [string] $Title, [string] $What)
                $result = Invoke-HelperExit $c @('axwin', "$AppId", $Title, 'close')
                if ($result.ExitCode -ne 0 -or -not (Wait-VerifyCondition -TimeoutSeconds 5 -Condition { -not (& $titled $AppId $Title) })) { $problems.Add("${What}: the window '$Title' did not close ($($result.Output -join ' '))") }
            }
            $finder = { if (-not (Invoke-FinderToFront)) { throw "Finder did not come to the front (front app $(Get-FrontAppText))" } }
            # An ordinary other app (helper front regular: Dock icon, its own menu bar) in front, its window at a point.
            $otherApp = {
                param([double] $X, [double] $Y)
                $other = Start-HelperProcess $c @('front', "$X", "$Y", '300', 'regular') (Join-Path $c.RunFolder 'tmp' "front-$(New-ShortId).txt")
                if (-not (Wait-StandInFront $c $other)) { throw "the stand-in app did not come to the front (front app $(Get-FrontAppText))" }
                $other
            }
            $stopOther = { param($Other) if ($Other -and -not $Other.HasExited) { $Other.Kill(); $Other.WaitForExit() } }
            $menuBar = { (Invoke-Helper $c @('focusedmenubar')) -join "`n" | ConvertFrom-Json }

            # (1) to (4): one app with Settings and a normal camera window.
            $data = New-TestData -Context $c -Simulator $simulator -Camera $simulator.Camera -Window ([ordered]@{ rememberLastPlacement = $false; startMode = 'Normal' })
            $name = (Get-Content -LiteralPath (Join-Path $data.Data 'settings.json') -Raw | ConvertFrom-Json).printers[0].name
            $cameraTitle = $texts['CameraTitle'] -f $name
            $settingsTitle = $texts['SettingsTitle']
            $app = Start-TestApp -Context $c -TestData $data
            $appId = $app.Id
            $other = $null
            try {
                & $expectType $appId 'UIElement' '(1) no window'
                Send-TestCommand $data 'settings'
                if (-not (Wait-VerifyCondition -TimeoutSeconds 15 -Condition { & $titled $appId $settingsTitle })) { throw "Settings ('$settingsTitle') did not open" }
                & $expectType $appId 'Foreground' '(1) Settings open'
                & $close $appId $settingsTitle '(1)'
                & $expectType $appId 'UIElement' '(1) Settings closed'
                Send-TestCommand $data "camera:$printerId"
                if (-not (Wait-VerifyCondition -TimeoutSeconds 20 -Condition { & $titled $appId $cameraTitle })) { throw "the camera window ('$cameraTitle') did not open" }
                & $expectType $appId 'Foreground' '(1) camera window open'
                Send-TestCommand $data 'settings'
                if (-not (Wait-VerifyCondition -TimeoutSeconds 15 -Condition { & $titled $appId $settingsTitle })) { throw 'Settings did not open next to the camera window' }
                $null = Save-VerifyEvidence -Context $c -Name 'ax-windows.json' -Content (ConvertTo-VerifyJsonArray (Get-AxWindows $c $appId) -Depth 5)

                # (2) The Dock item: its menu lists both windows; a press on it and on the camera window of its menu.
                $info = Invoke-HelperExit $c @('dock', $bundle.Path, 'info') 'dock-info.txt'
                if ($info.ExitCode -ne 0) { $problems.Add("(2) no Dock item of the test bundle with two windows open: $($info.Output -join ' ')") }
                else {
                    & $finder
                    $menu = Invoke-HelperExit $c @('dock', $bundle.Path, 'menu') 'dock-menu.txt'
                    if ($menu.ExitCode -ne 0) { $problems.Add("(2) the Dock menu did not open: $($menu.Output -join ' ')") }
                    else { foreach ($problem in Get-VerifyDockMenuProblems -MenuItems @($menu.Output -join "`n" | ConvertFrom-Json) -WindowTitles $cameraTitle, $settingsTitle) { $problems.Add("(2) $problem") } }
                    & $finder
                    $press = Invoke-HelperExit $c @('dock', $bundle.Path, 'press') 'dock-press.txt'
                    $script:focusedTitle = $null
                    $raised = $press.ExitCode -eq 0 -and (Wait-VerifyCondition -TimeoutSeconds 10 -Condition { (Get-FrontProcessId) -eq $appId -and ($script:focusedTitle = @(Get-AxWindows $c $appId | Where-Object { $_.main -and $_.focused } | ForEach-Object title)[0]) })
                    if (-not $raised) { $problems.Add("(2) AXPress on the Dock item brought no window of the app to the front with the focus (front app $(Get-FrontAppText); $($press.Output -join ' '))") }
                    else { $notes.Add("(2) the Dock menu lists both windows; the Dock item brought '$($script:focusedTitle)' to the front with the focus") }
                    & $finder
                    $choose = Invoke-HelperExit $c @('dock', $bundle.Path, 'menupress', $cameraTitle) 'dock-menupress.txt'
                    if ($choose.ExitCode -ne 0) { $problems.Add("(2) the item '$cameraTitle' of the Dock menu: $($choose.Output -join ' ')") }
                    elseif (-not (Wait-VerifyCondition -TimeoutSeconds 10 -Condition { Test-AxWindowFocused $c $appId $cameraTitle })) { $problems.Add("(2) the Dock menu item '$cameraTitle' did not bring the camera window to the front with the focus (front app $(Get-FrontAppText))") }
                    else { $notes.Add('(2) its window item brought the camera window to the front with the focus') }
                }

                # (3) "Bring camera to front" of the status item menu (T-209) gives the camera window the focus.
                & $finder
                $printer = $texts['MenuPrinter'] -f $name, $texts['StateIdle']
                $toFront = Invoke-MenuPress $c $appId "$printer>$(& $label 'MenuCameraToFront')"
                if ($toFront.ExitCode -ne 0) { $problems.Add("(3) menupress '$(& $label 'MenuCameraToFront')': $($toFront.Output -join ' ')") }
                elseif (-not (Wait-VerifyCondition -TimeoutSeconds 10 -Condition { Test-AxWindowFocused $c $appId $cameraTitle })) { $problems.Add("(3) '$(& $label 'MenuCameraToFront')' did not give the camera window the focus (front app $(Get-FrontAppText))") }
                else { $notes.Add("(3) '$(& $label 'MenuCameraToFront')' gave the camera window the focus") }

                # (4) T-102 question 3: the menu bar of the app is back after another ordinary app was in front.
                $before = & $menuBar
                $screen = @(Get-Screens $c | Where-Object main)[0]
                $other = & $otherApp ($screen.visible.x + 16) ($screen.visible.y + 16)
                $during = & $menuBar
                $back = Invoke-HelperExit $c @('dock', $bundle.Path, 'press')
                $null = Wait-VerifyCondition -TimeoutSeconds 10 -Condition { (Get-FrontProcessId) -eq $appId }
                Start-Sleep -Milliseconds 500
                $after = & $menuBar
                $null = Save-VerifyEvidence -Context $c -Name 'menu-bar.json' -Content ([ordered]@{ before = $before; otherApp = $during; after = $after } | ConvertTo-Json -Depth 6)
                $beforeTitles = @($before.items | ForEach-Object title)
                $afterTitles = @($after.items | ForEach-Object title)
                if ($before.pid -ne $appId -or $beforeTitles.Count -eq 0) { $problems.Add("(4) with the app in front the menu bar showed pid $($before.pid) with $($beforeTitles.Count) item(s)") }
                elseif ($during.pid -ne $other.Id) { $problems.Add("(4) the stand-in app did not get the menu bar (pid $($during.pid))") }
                elseif ($back.ExitCode -ne 0 -or $after.pid -ne $appId -or ($afterTitles -join '|') -cne ($beforeTitles -join '|')) {
                    $problems.Add("(4) T-102 question 3: the menu bar of the app is not back after the other app (pid $($after.pid), items $($afterTitles -join ', '); before: $($beforeTitles -join ', ')) - a new task")
                }
                else { $notes.Add("(4) T-102 question 3: the menu bar of the app is back after another app ($($afterTitles -join ', '))") }
                & $stopOther $other
                $other = $null

                # (1) The last window closed: no Dock icon again.
                & $close $appId $settingsTitle '(1)'
                & $close $appId $cameraTitle '(1)'
                & $expectType $appId 'UIElement' '(1) the last window closed'
                $notes.Add('(1) type Foreground with Settings or the camera window, UIElement without a window')
            }
            # An error ends (1) to (4) but keeps the problems found so far and still runs (5).
            catch { $problems.Add("(1)-(4) stopped: $($_.Exception.Message)") }
            finally {
                & $stopOther $other
                $null = Stop-TestApp $data $app
            }

            # (5) T-151: an outage of the printer with the camera window open and an ordinary other app in front.
            $closedText = "Closing the camera window of printer ${printerId}: the printer is offline."
            $openedText = "Opening the camera window of printer $printerId again: the printer is online."
            $outage = {
                param($Data, $App, [string] $Part, [switch] $Mini)
                $id = $App.Id
                $type = if ($Mini) { 'UIElement' } else { 'Foreground' }
                Send-TestCommand $Data "camera:$printerId"
                if (-not (Wait-VerifyCondition -TimeoutSeconds 20 -Condition { $w = Get-CameraWindow $c $id; $w -and (-not $Mini -or $w.layer -gt 0) })) { $problems.Add("$Part no camera window opened"); return }
                $before = Wait-StableWindow -Find { Get-CameraWindow $c $id }
                & $expectType $id $type "$Part camera window open"
                $other = & $otherApp ($before.x + 40) ($before.y + 40)
                try {
                    $otherWindow = @(Get-ProcessWindows $c $other.Id | Where-Object { $_.onscreen -and $_.layer -eq 0 }) | Select-Object -First 1
                    $clock = [System.Diagnostics.Stopwatch]::StartNew()
                    Set-SimulatorPrinter $simulator 'offline'
                    if (-not (Wait-VerifyCondition -TimeoutSeconds 90 -IntervalMilliseconds 500 -Condition { (Read-TestLog $Data).Contains($closedText) -and -not (Get-CameraWindow $c $id) })) { $problems.Add("$Part the camera window did not close within 90 s after the printer went offline"); return }
                    $closeSeconds = [math]::Round($clock.Elapsed.TotalSeconds, 1)
                    & $expectType $id 'UIElement' "$Part after the automatic close"
                    $clock.Restart()
                    Set-SimulatorPrinter $simulator 'online'
                    if (-not (Wait-VerifyCondition -TimeoutSeconds 60 -Condition { (Read-TestLog $Data).Contains($openedText) -and (Get-CameraWindow $c $id) })) { $problems.Add("$Part the camera window did not open again within 60 s after the printer came back"); return }
                    $openSeconds = [math]::Round($clock.Elapsed.TotalSeconds, 1)
                    $after = Wait-StableWindow -Find { Get-CameraWindow $c $id }
                    Start-Sleep -Milliseconds 1500
                    $place = Get-VerifyBoundsProblem -Actual $after -Expected $before
                    if ($place) { $problems.Add("$Part the window opened again elsewhere: $place") }
                    if ((Get-FrontProcessId) -ne $other.Id) { $problems.Add("$Part the reopened window took the front from the other app (front app $(Get-FrontAppText))") }
                    & $expectType $id $type "$Part after the reopening"
                    # A point inside both windows: which one is in front there.
                    $at = @((Invoke-Helper $c @('windowsat', "$($before.x + 60)", "$($before.y + 50)")) -join "`n" | ConvertFrom-Json)
                    $null = Save-VerifyEvidence -Context $c -Name "windows-at-$(if ($Mini) { 'mini' } else { 'normal' }).json" -Content (ConvertTo-VerifyJsonArray $at)
                    $order = if ($otherWindow -and $after) { Get-VerifyWindowOrder -Windows $at -Number $after.number -Other $otherWindow.number } else { 'unknown' }
                    if ($Mini -and ($after.layer -le 0 -or $order -ne 'above')) { $problems.Add("$Part the mini preview is not on top (layer $($after.layer), $order the window of the active app)") }
                    [pscustomobject]@{ Close = $closeSeconds; Open = $openSeconds; Order = $order; Window = $after }
                }
                finally {
                    Set-SimulatorPrinter $simulator 'online'
                    & $stopOther $other
                }
            }
            $permission = (Get-TestNotificationStatus $c).Permission
            $toasts = $permission -eq 'Allowed'
            $delivered = {
                $result = Invoke-BundleTool $bundle @('--layerpeek-e2e-notification-list')
                $parsed = if ($result.ExitCode -eq 0) { ConvertFrom-VerifyNotificationList -Output $result.Output -BundleIdentifier $script:TestBundleId }
                if (-not $parsed) { throw "The list mode of the test bundle answered ($($result.ExitCode)): $($result.Output -join ' ')" }
                @($parsed.Notifications)
            }
            # Notifications on except going offline and back (those are notified by design): the reopened window must add none.
            $data = New-TestData -Context $c -Simulator $simulator -Camera $simulator.Camera -Window ([ordered]@{ rememberLastPlacement = $false; startMode = 'Normal' }) -Notifications:$toasts -NotificationOptions ([ordered]@{ offline = $false })
            if ($toasts) { $null = Invoke-BundleTool $bundle @('--layerpeek-e2e-clear-toasts') }
            $app = if ($toasts) { Get-Process -Id (Start-TestAppViaOpen -Context $c -TestData $data -Environment @{ LAYERPEEK_TEST_TOASTS = '1' }) } else { Start-TestApp -Context $c -TestData $data }
            try {
                $known = if ($toasts) { @(& $delivered | ForEach-Object identifier) } else { @() }
                $normal = try { & $outage $data $app '(5)' } catch { $problems.Add("(5) stopped: $($_.Exception.Message)"); $null }
                if ($normal) { $notes.Add("(5) the camera window closed $($normal.Close) s after the printer went offline and opened again $($normal.Open) s after it came back, at the same place ($(Format-VerifyBounds $normal.Window)), $($normal.Order) the window of the active app, which stayed in front") }
                if ($toasts) {
                    $new = @(& $delivered | Where-Object { $known -notcontains $_.identifier })
                    $null = Save-VerifyEvidence -Context $c -Name 'notifications-after-outage.json' -Content (ConvertTo-VerifyJsonArray $new -Depth 6)
                    if ($new.Count -gt 0) { $problems.Add("(5) $($new.Count) new notification(s) during the outage: $(@($new | ForEach-Object { "'$($_.title)' / '$($_.body)'" }) -join ', ')") }
                    else { $notes.Add('(5) no new notification') }
                }
                else { $skipped.Add("(5) no new notification: the notifications of 'LayerPeek E2E' are $permission (allow them once with 'pwsh tools/macos-verify.ps1 -Only mac.notifications')") }
            }
            finally {
                $null = Stop-TestApp $data $app
                if ($toasts) { $null = Invoke-BundleTool $bundle @('--layerpeek-e2e-clear-toasts') }
            }
            # The mini preview (always on top) with the short delays of the test mode.
            $data = New-TestData -Context $c -Simulator $simulator -Camera $simulator.Camera -Window ([ordered]@{ rememberLastPlacement = $false; startMode = 'Mini' })
            $app = Start-TestApp -Context $c -TestData $data -Environment @{ LAYERPEEK_TEST_CAMERA_CLOSE_AFTER_MS = '3000'; LAYERPEEK_TEST_CAMERA_OPEN_AFTER_MS = '2000' }
            try {
                $mini = try { & $outage $data $app '(5) mini preview:' -Mini } catch { $problems.Add("(5) mini preview: stopped: $($_.Exception.Message)"); $null }
                if ($mini) { $notes.Add("(5) the mini preview opened again on top of the active app at the same place without a Dock icon (UIElement)") }
            }
            finally { $null = Stop-TestApp $data $app }

            if ($problems.Count -gt 0) { return New-VerifyResult FAIL "$($problems.Count) problem(s): $($problems -join '; ')" }
            if ($skipped.Count -gt 0) { Skip-VerifyCheck "everything else as expected, not judged: $($skipped -join '; '). Done: $($notes -join '; ')." }
            New-VerifyResult PASS ($notes -join '; ')
        }
    }

    New-VerifyCheck -Id 'mac.mini-after-fullscreen' -Needs Session, Accessibility -Kind Permission `
        -Permission 'Accessibility for the terminal (System Settings > Privacy & Security > Accessibility) to press the window buttons, Esc and Apply and to read the windows, and an unlocked screen' `
        -Title 'from full screen straight into the mini preview three ways (its button, Esc of a full screen opened from the mini preview, Apply in Settings during full screen): after the animation (bounds steady for 1 s) the size and corner of the saved mini preview (1 pt) and no window frame (simulator)' `
        -Closes 'T-038 (otázka 2, krok J5: z celé obrazovky rovnou do malého náhledu – velikost, roh a rámeček po animaci)', 'T-215 kritérium 2' -Action {
        param($c)
        if (-not (Request-Accessibility $c)) { Skip-VerifyCheck 'no Accessibility permission for the terminal (System Settings > Privacy & Security > Accessibility; it may need a restart of the terminal).' }
        Invoke-SessionCheck $c {
            $texts = Get-VerifyResxStrings (Join-Path $script:Repo 'src' 'LayerPeek.App' 'Resources' 'Strings.resx')
            $simulator = Get-Simulator $c
            Set-SimulatorScenario $simulator 'idle'
            $screens = @(Get-Screens $c)
            $problems = [System.Collections.Generic.List[string]]::new()
            $notes = [System.Collections.Generic.List[string]]::new()
            $rows = [System.Collections.Generic.List[object]]::new()
            $data = New-TestData -Context $c -Simulator $simulator -Camera $simulator.Camera -Window ([ordered]@{ rememberLastPlacement = $false; startMode = 'Mini'; mini = [ordered]@{ width = 320; height = 180; corner = 'BottomRight' } })
            $name = (Get-Content -LiteralPath (Join-Path $data.Data 'settings.json') -Raw | ConvertFrom-Json).printers[0].name
            $cameraTitle = $texts['CameraTitle'] -f $name
            $settingsTitle = $texts['SettingsTitle']
            $app = Start-TestApp -Context $c -TestData $data
            $appId = $app.Id
            # The camera window by its number (it stays one window in every mode); other windows (Settings) never count.
            $script:cameraNumber = 0
            $script:otherWindows = @()
            $find = {
                $w = @(Get-ProcessWindows $c $appId | Where-Object { $_.number -eq $script:cameraNumber -and $_.onscreen }) | Select-Object -First 1
                if (-not $w) { $w = Get-CameraWindowFrame $c $appId $script:otherWindows; if ($w) { $script:cameraNumber = $w.number } }
                $w
            }
            $fullScreen = {
                param([string] $Part)
                $null = Invoke-Helper $c @('axpress', "$appId", 'CameraFullScreenButton') -AllowFailure
                $script:full = $null
                if (-not (Wait-VerifyCondition -TimeoutSeconds 15 -Condition { $script:full = & $find; $script:full -and (Test-FullScreen $script:full $screens) })) { $problems.Add("$Part the window did not go full screen ($(Format-VerifyBounds $script:full))"); return $false }
                $null = Wait-StableWindow -Find $find
                $true
            }
            # The window after the animation: size, corner (distances from the usable area of its screen), frame; compared with -Reference.
            $measure = {
                param([string] $Part, $Reference)
                $window = Wait-StableWindow -Find $find -TimeoutSeconds 15
                $ax = @(Get-AxWindows $c $appId | Where-Object title -CEQ $cameraTitle) | Select-Object -First 1
                $screen = Get-VerifyScreenOf $screens $window
                $corner = if ($window -and $screen) { Get-VerifyWindowCorner -Window $window -Area $screen.visible }
                $buttons = @($ax.buttons | Where-Object { $_ })
                $rows.Add([ordered]@{ Part = $Part; Window = $window; Screen = $screen.name; Corner = $corner; Buttons = $buttons; FullScreen = [bool] ($window -and (Test-FullScreen $window $screens)) })
                $frameText = if ($buttons.Count -gt 0) { "a title bar ($($buttons -join ', '))" } else { 'no frame' }
                $text = "$Part $(Format-VerifyBounds $window), corner $($corner.Corner) (left $($corner.Left), top $($corner.Top), right $($corner.Right), bottom $($corner.Bottom) pt), $frameText, layer $($window.layer)"
                if ($Reference) {
                    $size = Get-VerifyBoundsProblem -Actual $window -Expected $Reference.Window -Tolerance 1 -SizeOnly
                    if ($size) { $problems.Add("$Part size $size") }
                    if ($corner.Corner -cne $Reference.Corner.Corner) { $problems.Add("$Part corner $($corner.Corner), expected $($Reference.Corner.Corner)") }
                    else {
                        $edges = @($(if ($corner.Corner.StartsWith('Top')) { 'Top' } else { 'Bottom' }), $(if ($corner.Corner.EndsWith('Left')) { 'Left' } else { 'Right' }))
                        foreach ($edge in $edges) { if ([math]::Abs($corner.$edge - $Reference.Corner.$edge) -gt 1) { $problems.Add("$Part $($edge.ToLowerInvariant()) distance $($corner.$edge) pt, expected $($Reference.Corner.$edge) pt") } }
                    }
                    if ($buttons.Count -gt 0) { $problems.Add("$Part has a window frame ($frameText), expected none") }
                }
                $notes.Add($text)
                [pscustomobject]@{ Window = $window; Corner = $corner }
            }
            try {
                Send-TestCommand $data "camera:$($script:PrinterId)"
                if (-not (Wait-VerifyCondition -TimeoutSeconds 20 -Condition { $w = & $find; $w -and $w.layer -gt 0 })) { return New-VerifyResult FAIL 'no mini preview opened.' }
                $reference = & $measure 'saved mini preview:' $null
                if (-not $reference.Corner) { return New-VerifyResult FAIL "the mini preview is on no screen ($(Format-VerifyBounds $reference.Window))." }

                # (a) The mini preview button of the full screen.
                if (& $fullScreen '(a)') {
                    $null = Invoke-Helper $c @('axpress', "$appId", 'CameraMiniButton') -AllowFailure
                    $null = & $measure '(a) mini preview button:' $reference
                }
                # (b) Esc of a full screen opened from the mini preview (the key goes to the key window of the front app).
                if (& $fullScreen '(b)') {
                    if (-not (Wait-VerifyCondition -TimeoutSeconds 5 -Condition { Test-AxWindowFocused $c $appId $cameraTitle })) { $problems.Add("(b) the full screen window has no keyboard focus for Esc (front app $(Get-FrontAppText))") }
                    else {
                        $null = Invoke-Helper $c @('key', 'escape')
                        $null = & $measure '(b) Esc:' $reference
                    }
                }
                # (c) Apply in Settings (Printers > Camera window) while the window is full screen: the saved start mode is Mini.
                if (& $fullScreen '(c)') {
                    Send-TestCommand $data 'settings'
                    if (-not (Wait-VerifyCondition -TimeoutSeconds 15 -Condition { @(Get-AxWindows $c $appId | Where-Object title -CEQ $settingsTitle).Count -gt 0 })) { $problems.Add('(c) Settings did not open') }
                    else {
                        $script:otherWindows = @(Get-ProcessWindows $c $appId | Where-Object { $_.layer -eq 0 -and $_.number -ne $script:cameraNumber } | ForEach-Object number)
                        if (-not (Open-SettingsTab $c $appId @('SettingsTabPrinters', 'PrinterTabCameraWindow') 'PrinterCameraWindowApply')) { $problems.Add('(c) no Apply button (PrinterCameraWindowApply) on Settings > Printers > Camera window') }
                        else {
                            $null = Invoke-Helper $c @('axpress', "$appId", 'PrinterCameraWindowApply')
                            $null = & $measure '(c) Apply in Settings:' $reference
                        }
                        $null = Invoke-HelperExit $c @('axwin', "$appId", $settingsTitle, 'close')
                    }
                }
            }
            finally {
                $null = Stop-TestApp $data $app
                $null = Save-VerifyEvidence -Context $c -Name 'placements.json' -Content (ConvertTo-VerifyJsonArray $rows -Depth 6)
            }
            if ($problems.Count -gt 0) { return New-VerifyResult FAIL "$($problems.Count) problem(s): $($problems -join '; '). Measured: $($notes -join '; '). The fix approved in T-038 question 2 (place the mini preview again after the animation) is a new task." }
            New-VerifyResult PASS "the mini preview keeps its size, corner and no frame after full screen three ways: $($notes -join '; ')"
        }
    }

    New-VerifyCheck -Id 'mac.camera-live' -Needs Session, Accessibility, ScreenRecording -Kind Permission `
        -Permission 'Accessibility for the terminal (System Settings > Privacy & Security > Accessibility) to press the window buttons, drag and read the windows, Screen Recording for the terminal (window captures), and an unlocked screen' `
        -Title 'live camera image in the normal window, full screen and the mini preview (two captures 1 s apart differ, the print status overlay in the Accessibility tree); while the grip of the mini preview is dragged, the mode switches (also with the green button) and the window moves to another monitor, no capture (every ~100 ms) is black and the stream is never opened again (simulator)' `
        -Closes 'T-026 (krit. „Stream … ve všech režimech okna“, ruční část mac.camera-look: živý obraz a překryv ve všech třech režimech)', 'T-184 (část „nic nebliká a nezčerná, stream se nepřeruší“: tažení úchytu, přepnutí režimu i zeleným tlačítkem, přesun mezi monitory)', 'T-215 kritérium 3' -Action {
        param($c)
        if (-not (Request-Accessibility $c)) { Skip-VerifyCheck 'no Accessibility permission for the terminal (System Settings > Privacy & Security > Accessibility; it may need a restart of the terminal).' }
        if ((Invoke-Helper $c @('screenrecording')) -notcontains 'true') { Skip-VerifyCheck 'needs Screen Recording for the terminal (System Settings > Privacy & Security > Screen & System Audio Recording), then a restart of the terminal.' }
        Invoke-SessionCheck $c {
            $texts = Get-VerifyResxStrings (Join-Path $script:Repo 'src' 'LayerPeek.App' 'Resources' 'Strings.resx')
            $simulator = Get-Simulator $c
            Set-SimulatorScenario $simulator 'printing'
            $port = $simulator.CameraPort
            $screens = @(Get-Screens $c)
            $problems = [System.Collections.Generic.List[string]]::new()
            $notes = [System.Collections.Generic.List[string]]::new()
            $data = New-TestData -Context $c -Simulator $simulator -Camera $simulator.Camera -Window ([ordered]@{ rememberLastPlacement = $false; startMode = 'Normal' })
            $name = (Get-Content -LiteralPath (Join-Path $data.Data 'settings.json') -Raw | ConvertFrom-Json).printers[0].name
            $cameraTitle = $texts['CameraTitle'] -f $name
            $app = Start-TestApp -Context $c -TestData $data
            $appId = $app.Id
            $script:cameraNumber = 0
            $find = {
                $w = @(Get-ProcessWindows $c $appId | Where-Object { $_.number -eq $script:cameraNumber -and $_.onscreen }) | Select-Object -First 1
                if (-not $w) { $w = Get-CameraWindow $c $appId; if ($w) { $script:cameraNumber = $w.number } }
                $w
            }
            $waitMode = {
                param([string] $Mode, [string] $What)
                $script:modeWindow = $null
                $ok = Wait-VerifyCondition -TimeoutSeconds 15 -Condition {
                    $script:modeWindow = & $find
                    $w = $script:modeWindow
                    $w -and $(switch ($Mode) { 'FullScreen' { Test-FullScreen $w $screens } 'Mini' { $w.layer -gt 0 -and -not (Test-FullScreen $w $screens) } default { $w.layer -eq 0 -and -not (Test-FullScreen $w $screens) } })
                }
                if (-not $ok) { $problems.Add("${What}: the window did not reach $Mode ($(Format-VerifyBounds $script:modeWindow), layer $($script:modeWindow.layer))") }
                $null = Wait-StableWindow -Find $find
                $ok
            }
            $capture = {
                param([string] $Name)
                $file = Join-Path (Get-VerifyEvidenceFolder $c) "$Name.png"
                & /usr/sbin/screencapture -x -o -l "$($script:cameraNumber)" $file
                if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $file)) { return $null }
                $file
            }
            # Two captures 1 s apart differ in the image area (Surface); the print status overlay is in the Accessibility tree.
            $live = {
                param([string] $Mode)
                $window = Wait-StableWindow -Find $find
                $surface = Get-AxElement $c $appId 'Surface'
                if (-not $window -or -not $surface) { $problems.Add("${Mode}: no camera window or image (Surface) to capture"); return }
                $first = & $capture "live-$Mode-1"
                Start-Sleep -Seconds 1
                $second = & $capture "live-$Mode-2"
                if (-not $first -or -not $second) { $problems.Add("${Mode}: screencapture of window $($script:cameraNumber) failed"); return }
                $diffArguments = @('regiondiff', $first, $second, "$($window.width)", "$($surface.frame.x - $window.x)", "$($surface.frame.y - $window.y)", "$($surface.frame.width)", "$($surface.frame.height)")
                $diff = (Invoke-Helper $c $diffArguments -AllowFailure) -join "`n" | ConvertFrom-Json -ErrorAction SilentlyContinue
                $verdict = Get-VerifyLiveImageVerdict -Diff $diff
                if (-not $verdict.Live) { $problems.Add("${Mode}: the camera image did not change within 1 s ($($verdict.Detail); live-$Mode-1.png, live-$Mode-2.png)") }
                $overlayId = if ($Mode -eq 'mini') { 'CameraMiniSummary' } else { 'CameraOverlay' }
                $overlay = Get-AxElement $c $appId $overlayId
                $shown = @(if ($overlay) { Get-AxNodes @($overlay) | Where-Object { "$($_.title)$($_.value)$($_.description)" } })
                if (-not $overlay -or $overlay.frame.width -le 0 -or $shown.Count -eq 0) { $problems.Add("${Mode}: no print status overlay ($overlayId) with a text in the Accessibility tree") }
                $notes.Add("${Mode}: live ($($verdict.Detail)), $overlayId with $($shown.Count) text(s)")
            }
            $press = { param([string] $Id) $null = Invoke-Helper $c @('axpress', "$appId", $Id) -AllowFailure }
            $watch = $null
            $frames = Join-Path $c.RunFolder 'tmp' "luma-$(New-ShortId)"
            $watchFile = Join-Path (Get-VerifyEvidenceFolder $c) 'luma-watch.jsonl'
            try {
                Send-TestCommand $data "camera:$($script:PrinterId)"
                if (-not (& $waitMode 'Normal' 'open')) { return New-VerifyResult FAIL "no normal camera window opened: $($problems -join '; ')" }
                if (-not (Wait-VerifyCondition -TimeoutSeconds 20 -Condition { (Get-RtspConnections $appId $port).Count -gt 0 })) { return New-VerifyResult FAIL "no RTSP connection to port $port within 20 s." }
                Start-Sleep -Seconds 2
                & $live 'normal'
                & $press 'CameraFullScreenButton'
                if (& $waitMode 'FullScreen' 'full screen button') { & $live 'full-screen' }
                & $press 'CameraMiniButton'
                if (& $waitMode 'Mini' 'mini preview button') { & $live 'mini' }

                # T-184: a capture every ~100 ms while the window changes; the stream stays the same connection.
                $peers = @(Get-VerifyRtspPeers (Get-RtspConnections $appId $port))
                $logBefore = (Read-TestLog $data).Length
                $watch = Start-HelperProcess $c @('lumawatch', "$($script:cameraNumber)", '100', '180', $frames) $watchFile
                if (-not (Wait-VerifyCondition -TimeoutSeconds 5 -Condition { (Test-Path -LiteralPath $watchFile) -and (Get-Item -LiteralPath $watchFile).Length -gt 0 })) { throw 'the capture watch did not start' }
                Start-Sleep -Milliseconds 800
                $steps = [System.Collections.Generic.List[string]]::new()
                # 1. The top left grip of the mini preview dragged outwards (the preview grows away from its corner).
                $w = & $find
                $null = Invoke-Helper $c @('drag', "$($w.x + 3)", "$($w.y + 3)", "$($w.x - 77)", "$($w.y - 42)", '800')
                $grown = Wait-StableWindow -Find $find
                if (-not $grown -or $grown.width -le $w.width) { $problems.Add("the grip drag did not resize the mini preview ($(Format-VerifyBounds $w) -> $(Format-VerifyBounds $grown))") }
                else { $steps.Add("grip drag $(Format-VerifyBounds $w) -> $(Format-VerifyBounds $grown)") }
                # 2. Mini preview -> normal window, the green button to full screen and back, full screen button, mini button.
                & $press 'CameraNormalButton'
                if (& $waitMode 'Normal' 'normal window button') { $steps.Add('mini preview -> normal window') }
                $green = Invoke-HelperExit $c @('axwin', "$appId", $cameraTitle, 'fullscreen') 'green-button-1.txt'
                if ($green.ExitCode -ne 0) { $problems.Add("the green button: $($green.Output -join ' ')") }
                elseif (& $waitMode 'FullScreen' 'green button') {
                    $steps.Add('green button -> full screen')
                    $green = Invoke-HelperExit $c @('axwin', "$appId", $cameraTitle, 'fullscreen') 'green-button-2.txt'
                    if ($green.ExitCode -ne 0) { $problems.Add("the green button in full screen: $($green.Output -join ' ')") }
                    elseif (& $waitMode 'Normal' 'green button in full screen') { $steps.Add('green button -> normal window') }
                }
                & $press 'CameraFullScreenButton'
                if (& $waitMode 'FullScreen' 'full screen button') { $steps.Add('full screen button') }
                & $press 'CameraMiniButton'
                if (& $waitMode 'Mini' 'mini preview button') { $steps.Add('full screen -> mini preview') }
                # 3. The mini preview dragged by its image to another monitor.
                $w = & $find
                $from = Get-VerifyScreenOf $screens $w
                $to = @($screens | Where-Object { $from -and $_.id -ne $from.id }) | Select-Object -First 1
                if ($to) {
                    $null = Invoke-Helper $c @('drag', "$($w.x + $w.width / 2)", "$($w.y + $w.height / 3)", "$($to.visible.x + $to.visible.width / 2)", "$($to.visible.y + $to.visible.height / 2)", '1200')
                    $moved = Wait-StableWindow -Find $find
                    if ((Get-VerifyScreenOf $screens $moved).id -ne $to.id) { $problems.Add("dragging the mini preview did not move it to $($to.name) ($(Format-VerifyBounds $moved))") }
                    else { $steps.Add("moved from $($from.name) (@$($from.scale)x) to $($to.name) (@$($to.scale)x)") }
                }
                else { $notes.Add('one monitor: no move between monitors') }
                Start-Sleep -Milliseconds 800

                $after = @(Get-VerifyRtspPeers (Get-RtspConnections $appId $port))
                if ($peers.Count -eq 0 -or ($after -join ',') -cne ($peers -join ',')) { $problems.Add("the RTSP connection changed during the steps ($($peers -join ', ') -> $($after -join ', ')): the stream was opened again") }
                $newLog = (Read-TestLog $data).Substring([math]::Min($logBefore, (Read-TestLog $data).Length))
                $failures = @($newLog -split "`n" | Where-Object { $_ -match "Camera stream '.*' (failed|ended|sent no frame)" })
                if ($failures.Count -gt 0) { $problems.Add("the camera stream failed or ended during the steps: $($failures[0])") }
                $notes.Add("steps: $($steps -join ', ')")
            }
            finally {
                if ($watch -and -not $watch.HasExited) { $watch.Kill(); $watch.WaitForExit() }
                $null = Stop-TestApp $data $app
            }
            $captures = @(ConvertFrom-VerifyAxWatch @(Get-Content -LiteralPath $watchFile -ErrorAction SilentlyContinue))
            $black = Get-VerifyBlackFrames -Frames $captures
            if ($black.Frames -lt 20) { $problems.Add("only $($black.Frames) window capture(s) with a value ($($black.Errors) failed; luma-watch.jsonl)") }
            if ($black.Black.Count -gt 0) {
                $darkest = @($black.Black | Sort-Object { [double] $_.mean })[0]
                Copy-Item -LiteralPath (Join-Path $frames $darkest.file) -Destination (Join-Path (Get-VerifyEvidenceFolder $c) 'darkest-capture.png') -ErrorAction SilentlyContinue
                $problems.Add("$($black.Black.Count) of $($black.Frames) captures black (mean luminance below $($black.Threshold), baseline $($black.Baseline)) at $(@($black.Black | Select-Object -First 5 | ForEach-Object { "$($_.ms) ms" }) -join ', ') (darkest-capture.png)")
            }
            if (Test-Path -LiteralPath $frames) { Remove-Item -LiteralPath $frames -Recurse -Force }
            if ($problems.Count -gt 0) { return New-VerifyResult FAIL "$($problems.Count) problem(s): $($problems -join '; ')" }
            New-VerifyResult PASS "$($notes -join '; '); $($black.Frames) captures every ~100 ms, none black (baseline $($black.Baseline), threshold $($black.Threshold), $($black.Errors) failed captures), the RTSP connection stayed the same. Sharpness and colours stay a manual point (T-184)."
        }
    }

    New-VerifyCheck -Id 'mac.camera-next-attempt' -Needs Session, Accessibility -Kind Permission `
        -Permission 'Accessibility for the terminal (System Settings > Privacy & Security > Accessibility) to read the camera window, press its buttons, minimize it and press Test camera, and an unlocked screen' `
        -Title 'the next attempt line of the camera window with the camera off (T-188 / T-194): about 5, 10, 20, 40 s and 1 min, counting down by the second; missing in the mini preview and back with the current value; a mode switch does not shorten the pause; minimized and restored during the minute: a new attempt at once; the camera back: the line goes and the stream runs; the window closed during a pause the recording holds: Test camera in Settings succeeds at once (simulator, real time, about 3 min)' `
        -Closes 'T-188 (ruční část kromě délky textu: hodnoty 5, 10, 20, 40 s a 1 min, malý náhled, minimalizace, přepnutí režimu, po návratu kamery řádek zmizí)', 'T-194 (odpočet po sekundách; „Otestovat kameru“ během pauzy uspěje hned)', 'T-215 kritérium 4' -Action {
        param($c)
        if (-not (Request-Accessibility $c)) { Skip-VerifyCheck 'no Accessibility permission for the terminal (System Settings > Privacy & Security > Accessibility; it may need a restart of the terminal).' }
        Invoke-SessionCheck $c {
            $texts = Get-VerifyResxStrings (Join-Path $script:Repo 'src' 'LayerPeek.App' 'Resources' 'Strings.resx')
            $secondsFormat = $texts['CameraNextAttemptSeconds']
            $minutesFormat = $texts['CameraNextAttemptMinutes']
            $connecting = $texts['CameraConnecting']
            $success = ($texts['SettingsCameraTestSuccess'] -split '\{0\}', 2)[0]
            $simulator = Get-Simulator $c
            Set-SimulatorScenario $simulator 'printing'
            $port = $simulator.CameraPort
            $screens = @(Get-Screens $c)
            $problems = [System.Collections.Generic.List[string]]::new()
            $notes = [System.Collections.Generic.List[string]]::new()
            # The recording of the print holds the stream (and its pause) while the window is closed (T-194).
            $data = New-TestData -Context $c -Simulator $simulator -Camera $simulator.Camera -Window ([ordered]@{ rememberLastPlacement = $false; startMode = 'Normal' }) -CameraOptions ([ordered]@{ recordPrints = $true }) -RecordingFolder 'Recordings'
            $name = (Get-Content -LiteralPath (Join-Path $data.Data 'settings.json') -Raw | ConvertFrom-Json).printers[0].name
            $cameraTitle = $texts['CameraTitle'] -f $name
            $settingsTitle = $texts['SettingsTitle']
            $app = Start-TestApp -Context $c -TestData $data
            $appId = $app.Id
            $watchFile = Join-Path (Get-VerifyEvidenceFolder $c) 'next-attempt-watch.jsonl'
            $watch = $null
            # The line now (the last line of the watch: it prints every change): seconds, or $null when it is not shown.
            $current = {
                $last = @(ConvertFrom-VerifyAxWatch @(Get-Content -LiteralPath $watchFile -ErrorAction SilentlyContinue)) | Select-Object -Last 1
                if ($last -and $last.found -and $last.frame.width -gt 0) { ConvertFrom-VerifyNextAttemptText -Text "$($last.text)" -SecondsFormat $secondsFormat -MinutesFormat $minutesFormat } else { $null }
            }
            $waitLine = {
                param([scriptblock] $Match, [int] $Seconds)
                $script:line = $null
                Wait-VerifyCondition -TimeoutSeconds $Seconds -IntervalMilliseconds 200 -Condition { $script:line = & $current; & $Match $script:line }
            }
            $press = { param([string] $Id) $null = Invoke-Helper $c @('axpress', "$appId", $Id) -AllowFailure }
            $window = { param([string] $Title) @(Get-AxWindows $c $appId | Where-Object title -CEQ $Title) | Select-Object -First 1 }
            $clock = [System.Diagnostics.Stopwatch]::StartNew()
            try {
                Send-TestCommand $data "camera:$($script:PrinterId)"
                if (-not (Wait-VerifyCondition -TimeoutSeconds 20 -Condition { (& $window $cameraTitle) -and (Get-RtspConnections $appId $port).Count -gt 0 })) { return New-VerifyResult FAIL "no streaming camera window within 20 s (RTSP port $port)." }
                $watch = Start-HelperProcess $c @('axwatch', "$appId", 'CameraNextAttempt', '420000') $watchFile
                if (-not (Wait-VerifyCondition -TimeoutSeconds 10 -Condition { (Test-Path -LiteralPath $watchFile) -and (Get-Item -LiteralPath $watchFile).Length -gt 0 })) { throw "the Accessibility watch did not start: $(Get-Content -LiteralPath $watchFile -Raw -ErrorAction SilentlyContinue)" }
                if ($null -ne (& $current)) { $problems.Add('the next attempt line is shown while the camera streams') }
                $clock.Restart()
                Set-SimulatorCamera $simulator 'offline'

                # The pause of 20 s: the mini preview hides the line, the window shows it again with the current value; full screen and back.
                if (-not (& $waitLine { param($s) $null -ne $s -and $s -ge 15 -and $s -le 20 } 90)) { throw "the line never showed the pause of 20 s within 90 s (last: $($script:line))" }
                $null = & $waitLine { param($s) $null -ne $s -and $s -le 17 } 10
                $before = $script:line
                if ($null -eq $before) { throw 'the line vanished during the pause of 20 s' }
                $beforeAt = $clock.Elapsed.TotalSeconds
                & $press 'CameraMiniButton'
                if (-not (& $waitLine { param($s) $null -eq $s } 3)) { $problems.Add("the line stays in the mini preview ($($script:line) s)") }
                Start-Sleep -Milliseconds 1500
                & $press 'CameraNormalButton'
                if (-not (& $waitLine { param($s) $null -ne $s } 3)) { $problems.Add('back in the window the line is missing') }
                else {
                    $expected = $before - ($clock.Elapsed.TotalSeconds - $beforeAt)
                    if ([math]::Abs($script:line - $expected) -gt 2) { $problems.Add("back in the window the line shows $($script:line) s, expected about $([math]::Round($expected)) s (the current value)") }
                    else { $notes.Add("the line left the mini preview and came back with the current value ($($script:line) s)") }
                }
                & $press 'CameraFullScreenButton'
                $null = Wait-VerifyCondition -TimeoutSeconds 10 -Condition { $w = Get-CameraWindow $c $appId; $w -and (Test-FullScreen $w $screens) }
                Start-Sleep -Seconds 1
                & $press 'CameraNormalButton'
                $null = Wait-VerifyCondition -TimeoutSeconds 10 -Condition { $w = Get-CameraWindow $c $appId; $w -and -not (Test-FullScreen $w $screens) }

                # The minute: minimized to the Dock and restored, a new attempt at once ("Connecting…" or a new pause of 5 s).
                if (-not (& $waitLine { param($s) $s -eq 60 } 120)) { throw "the line never showed about 1 min within 120 s (last: $($script:line))" }
                Start-Sleep -Seconds 2
                $minimize = Invoke-HelperExit $c @('axwin', "$appId", $cameraTitle, 'minimize') 'minimize.txt'
                if ($minimize.ExitCode -ne 0 -or -not (Wait-VerifyCondition -TimeoutSeconds 5 -Condition { (& $window $cameraTitle).minimized })) { $problems.Add("the camera window did not minimize: $($minimize.Output -join ' ')") }
                Start-Sleep -Seconds 2
                $restoredAt = $clock.Elapsed.TotalSeconds
                $null = Invoke-HelperExit $c @('axwin', "$appId", $cameraTitle, 'restore') 'restore.txt'
                $script:attempt = $null
                $attempted = Wait-VerifyCondition -TimeoutSeconds 3 -IntervalMilliseconds 100 -Condition {
                    $line = & $current
                    if ($null -ne $line -and $line -le 5) { $script:attempt = "a new pause of $line s"; return $true }
                    if (@(Get-AxNodes (Get-AxTree $c $appId) | Where-Object { "$($_.value)|$($_.title)|$($_.description)".Contains($connecting) }).Count -gt 0) { $script:attempt = "'$connecting'"; return $true }
                    $false
                }
                if (-not $attempted) { $problems.Add("restored during the minute, no new attempt within 3 s (line: $(& $current) s)") }
                else { $notes.Add("minimized and restored during the minute: $($script:attempt) $([math]::Round($clock.Elapsed.TotalSeconds - $restoredAt, 1)) s later") }

                # The camera back during the short pause: the line goes, the stream runs.
                $null = & $waitLine { param($s) $null -ne $s -and $s -le 5 } 10
                Set-SimulatorCamera $simulator 'online'
                $back = Wait-VerifyCondition -TimeoutSeconds 20 -Condition { $null -eq (& $current) -and (Get-RtspConnections $appId $port).Count -gt 0 }
                Start-Sleep -Seconds 2
                $overlay = Get-AxElement $c $appId 'CameraOverlay'
                if (-not $back -or $null -ne (& $current)) { $problems.Add("after the camera came back the line stays or no stream runs (line $(& $current) s, RTSP connections $((Get-RtspConnections $appId $port).Count))") }
                elseif (-not $overlay) { $problems.Add('after the camera came back the window shows no overlay (CameraOverlay)') }
                else { $notes.Add('the camera back: the line went, the stream runs') }

                # T-194: the window closed during a pause the recording holds; Test camera with the camera back succeeds at once.
                Set-SimulatorCamera $simulator 'offline'
                if (-not (& $waitLine { param($s) $null -ne $s -and $s -ge 15 -and $s -le 20 } 90)) { throw "after the second outage the line never showed the pause of 20 s (last: $($script:line))" }
                $pauseAt = $clock.Elapsed.TotalSeconds
                $pauseLeft = $script:line
                $null = Invoke-HelperExit $c @('axwin', "$appId", $cameraTitle, 'close') 'close.txt'
                if (-not (Wait-VerifyCondition -TimeoutSeconds 5 -Condition { -not (& $window $cameraTitle) })) { $problems.Add('the camera window did not close') }
                Set-SimulatorCamera $simulator 'online'
                Send-TestCommand $data 'settings'
                if (-not (Open-SettingsTab $c $appId @('SettingsTabPrinters', 'PrinterTabCamera') 'PrinterCameraTest')) { $problems.Add('no Test camera button (PrinterCameraTest) on Settings > Printers > Camera') }
                else {
                    $pressedAt = $clock.Elapsed.TotalSeconds
                    & $press 'PrinterCameraTest'
                    $script:result = ''
                    $passed = Wait-VerifyCondition -TimeoutSeconds 15 -Condition { $e = Get-AxElement $c $appId 'PrinterCameraTestResult'; $script:result = "$(@($e.value, $e.title, $e.description) | Where-Object { $_ } | Select-Object -First 1)"; $script:result.StartsWith($success) }
                    $took = [math]::Round($clock.Elapsed.TotalSeconds - $pressedAt, 1)
                    $left = [math]::Round($pauseLeft - ($pressedAt - $pauseAt), 1)
                    if (-not $passed) { $problems.Add("Test camera during the pause did not succeed within 15 s ('$($script:result)')") }
                    elseif ($took -ge $left) { $problems.Add("Test camera succeeded after $took s, not before the pause would have ended ($left s left)") }
                    else { $notes.Add("Test camera during the pause (window closed, $left s left) succeeded after $took s: '$($script:result)'") }
                }
                $null = Invoke-HelperExit $c @('axwin', "$appId", $settingsTitle, 'close')
            }
            finally {
                if ($watch -and -not $watch.HasExited) { $watch.Kill(); $watch.WaitForExit() }
                Set-SimulatorCamera $simulator 'online'
                $null = Stop-TestApp $data $app
            }
            $pauses = @(Get-VerifyNextAttemptPauses -Timeline @(ConvertFrom-VerifyAxWatch @(Get-Content -LiteralPath $watchFile)) -SecondsFormat $secondsFormat -MinutesFormat $minutesFormat)
            $null = Save-VerifyEvidence -Context $c -Name 'pauses.json' -Content (ConvertTo-VerifyJsonArray $pauses -Depth 6)
            foreach ($problem in Get-VerifyCountdownProblems -Pauses $pauses -Expected 5, 10, 20, 40, 60) { $problems.Add($problem) }
            $seen = "pauses $(@($pauses | Select-Object -First 5 | ForEach-Object { "$($_.Start) s" }) -join ', ') counting down by the second, $([math]::Round($clock.Elapsed.TotalMinutes, 1)) min in all"
            if ($problems.Count -gt 0) { return New-VerifyResult FAIL "$($problems.Count) problem(s): $($problems -join '; ') ($seen; next-attempt-watch.jsonl, pauses.json)" }
            New-VerifyResult PASS "$seen; $($notes -join '; ')"
        }
    }

    New-VerifyCheck -Id 'mac.camera-autoclose' -Needs Session -Kind Auto -Title 'E2E of the camera window closed while the printer is offline and opened again once it is back, with Finder in front: Finder stays the active app (MacCameraWindowAutoCloseE2ETests, unlocked screen)' `
        -Closes 'T-151 (krit. „Automaticky otevřené okno nebere fokus aktivnímu oknu jiné aplikace“, část macOS)', 'T-216 kritérium 1' -Action {
        param($c)
        Invoke-SessionCheck $c {
            $run = Invoke-DotnetTest $c 'dotnet-test-camera-autoclose' @('--project', (Join-Path $script:Repo 'tests' 'LayerPeek.App.Tests'), '--', '--filter-class', '*MacCameraWindowAutoCloseE2ETests') @{ LAYERPEEK_E2E = '1' }
            if (-not $run.Totals) { return New-VerifyResult FAIL "no test results, exit code $($run.ExitCode)." }
            # Every display asleep: the app waits for one before its UI starts, so the camera command never arrives (as Start-TestApp).
            if ($run.Totals.Failed -gt 0 -and ($run.Output -join "`n") -match 'No active display') { Skip-VerifyCheck 'every display is asleep (the app waits for one before its UI starts): wake the display and run the script under caffeinate -d -u.' }
            if ($run.Totals.Failed -gt 0) { return New-VerifyResult FAIL (Format-Totals $run.Totals) }
            $reason = "$((@($run.Skipped) | Select-Object -First 1).Reason)"
            # The test closes and reopens the window also with a locked screen, but then the login window is in front.
            if ($run.Totals.Skipped -gt 0 -and $reason -match 'the screen is locked') { Skip-VerifyCheck "the focus was not judged: $reason" }
            if ($run.Totals.Skipped -gt 0) { return New-VerifyResult FAIL "the E2E test was skipped: $reason" }
            if ($run.Totals.Passed -lt 1) { return New-VerifyResult FAIL "no test ran: $(Format-Totals $run.Totals)" }
            New-VerifyResult PASS "$(Format-Totals $run.Totals): the window closed while the printer was offline and opened again, Finder stayed the active app."
        }
    }

    New-VerifyCheck -Id 'mac.camera-pin-ax' -Needs Session, Accessibility -Kind Auto -Title 'the pin of the camera window in the Accessibility tree (instead of listening to VoiceOver), Czech UI: its name "Vždy navrchu: zapnuto / vypnuto" follows its state and AXPress switches it in the normal window and the mini preview; no pin in full screen; open Settings show the state (simulator)' `
        -Closes 'T-122 (VoiceOver: jméno a stav špendlíku, aktivace ho přepne, v normálním okně i malém náhledu; kontrola přes Accessibility, rozhodnutí 2026-11-13)', 'T-117 (ruční část, jen VoiceOver: „Vždy navrchu: zapnuto / vypnuto“, na celé obrazovce špendlík není, otevřené Nastavení ukáže stav)', 'T-216 kritérium 2' -Action {
        param($c)
        if (-not (Test-Accessibility $c)) { Skip-VerifyCheck 'needs Accessibility for the terminal (the Accessibility tree of the camera window and of Settings).' }
        Invoke-SessionCheck $c {
            $texts = Get-AppTexts 'cs'
            $onName = $texts['CameraTopmostOn']
            $offName = $texts['CameraTopmostOff']
            $simulator = Get-Simulator $c
            $screens = @(Get-Screens $c)
            $problems = [System.Collections.Generic.List[string]]::new()
            $notes = [System.Collections.Generic.List[string]]::new()
            foreach ($mode in 'Normal', 'Mini') {
                $data = New-TestData -Context $c -Simulator $simulator -Camera $simulator.Camera -Language 'cs' -Window ([ordered]@{ rememberLastPlacement = $false; startMode = $mode })
                $app = Start-TestApp -Context $c -TestData $data
                $appId = $app.Id
                $script:pin = $null
                $readPin = { $script:pin = Get-AxElement $c $appId 'CameraTopmostButton'; $script:pin -and $script:pin.frame.width -gt 0 }
                # The name says the state the value has (AXValue 1 = on); -On: the state expected.
                $expect = {
                    param([bool] $On, [string] $What)
                    $want = if ($On) { $onName } else { $offName }
                    if (-not (Wait-VerifyCondition -TimeoutSeconds 5 -Condition { (& $readPin) -and ([string] $script:pin.value -eq '1') -eq $On -and [string] $script:pin.title -ceq $want })) {
                        $problems.Add("${mode} ${What}: the pin has the name '$($script:pin.title)' and value '$($script:pin.value)', expected '$want'")
                        return $false
                    }
                    $true
                }
                $settingsBox = "PrinterCameraWindow$($mode)Topmost"
                try {
                    Send-TestCommand $data "camera:$($script:PrinterId)"
                    if (-not (Wait-VerifyCondition -TimeoutSeconds 20 -Condition { (Get-CameraWindow $c $appId) -and (& $readPin) })) { $problems.Add("${mode}: no camera window with the pin in the Accessibility tree"); continue }
                    $initial = [string] $script:pin.value -eq '1'
                    $null = & $expect $initial 'at the start'
                    # AXPress (what VoiceOver does with VO-Space) twice: on and off again, the name with it.
                    foreach ($round in 1, 2) {
                        $state = -not ([string] $script:pin.value -eq '1')
                        $null = Invoke-Helper $c @('axpress', "$appId", 'CameraTopmostButton')
                        $null = & $expect $state "after AXPress $round"
                    }
                    # Open Settings show the state of the pin and follow it.
                    Send-TestCommand $data 'settings'
                    $box = Open-SettingsTab $c $appId @('SettingsTabPrinters', 'PrinterTabCameraWindow') $settingsBox
                    if (-not $box) { $problems.Add("${mode}: Settings > Printers > Camera window shows no $settingsBox") }
                    else {
                        $null = & $readPin
                        $state = [string] $script:pin.value -eq '1'
                        if (([string] $box.value -eq '1') -ne $state) { $problems.Add("${mode}: Settings show '$($box.value)' for 'always on top', the pin is $(if ($state) { 'on' } else { 'off' })") }
                        $null = Invoke-Helper $c @('axpress', "$appId", 'CameraTopmostButton')
                        $null = & $expect (-not $state) 'with Settings open'
                        $script:box = $null
                        if (-not (Wait-VerifyCondition -TimeoutSeconds 5 -Condition { $script:box = Get-AxElement $c $appId $settingsBox; $script:box -and (([string] $script:box.value -eq '1') -eq (-not $state)) })) { $problems.Add("${mode}: the open Settings did not follow the pin ($settingsBox value '$($script:box.value)')") }
                        else { $notes.Add("${mode}: open Settings follow the pin ($settingsBox '$($script:box.title)')") }
                        $null = Invoke-Helper $c @('axpress', "$appId", 'SettingsCancel') -AllowFailure
                        $null = Wait-VerifyCondition -TimeoutSeconds 10 -Condition { -not (Get-AxElement $c $appId $settingsBox) }
                    }
                    # Full screen has no pin (it is never on top); back to the mode afterwards.
                    $null = Invoke-Helper $c @('axpress', "$appId", 'CameraFullScreenButton')
                    if (-not (Wait-VerifyCondition -TimeoutSeconds 15 -Condition { $w = Get-CameraWindow $c $appId; $w -and (Test-FullScreen $w $screens) })) { $problems.Add("${mode}: the full screen button did not reach full screen") }
                    else {
                        Start-Sleep -Seconds 1
                        if (& $readPin) { $problems.Add("${mode}: in full screen the pin is in the Accessibility tree ('$($script:pin.title)')") }
                        else { $notes.Add("${mode}: no pin in full screen") }
                        $back = if ($mode -eq 'Normal') { 'CameraNormalButton' } else { 'CameraMiniButton' }
                        $null = Invoke-Helper $c @('axpress', "$appId", $back) -AllowFailure
                        if (-not (Wait-VerifyCondition -TimeoutSeconds 15 -Condition { & $readPin })) { $problems.Add("${mode}: after full screen ($back) the pin did not come back") }
                    }
                    $notes.Add("${mode}: the pin '$($script:pin.title)' (started $(if ($initial) { 'on' } else { 'off' })), AXPress switched it with its name")
                }
                finally { $null = Stop-TestApp $data $app }
            }
            if ($problems.Count -gt 0) { return New-VerifyResult FAIL "$($problems.Count) problem(s): $($problems -join '; ')" }
            New-VerifyResult PASS "names '$onName' / '$offName': $($notes -join '; ')"
        }
    }

    New-VerifyCheck -Id 'mac.detection-region' -Needs Session, Accessibility -Kind Permission `
        -Permission 'Accessibility for the terminal (System Settings > Privacy & Security > Accessibility) to press buttons, drag the mouse, post the arrow keys and read the Accessibility tree, and an unlocked screen' `
        -Title 'the bed region editor of print failure detection (Settings > Printers > Detection, a Core One of the simulator): Load frame shows a frame, a mouse drag draws a region, the arrows move it by 1 %, Cmd-arrows by 10 %, Shift-arrows change its size; its accessible name "Bed region: ... % from the left ..." matches settings.json after Save; Default for Core One sets the region of the code' `
        -Closes 'T-154 (ruční část macOS, editor výřezu: tažení myší, šipky, ⌘+šipky o 10 %, popisek „Výřez podložky: … % zleva …“; bez skutečné Buddy3D)', 'T-216 kritérium 3' -Action {
        param($c)
        if (-not (Request-Accessibility $c)) { Skip-VerifyCheck 'no Accessibility permission for the terminal (System Settings > Privacy & Security > Accessibility; it may need a restart of the terminal).' }
        Invoke-SessionCheck $c {
            $texts = Get-AppTexts 'en'
            $format = $texts['SettingsDetectionRegionName']
            $source = Get-Content -LiteralPath (Join-Path $script:Repo 'src' 'LayerPeek.Core' 'Detection' 'DetectionRegion.cs') -Raw
            $code = [regex]::Match($source, 'CoreOneBed \{ get; \} = new\((?<v>[^)]+)\)')
            if (-not $code.Success) { return New-VerifyResult FAIL 'DetectionRegion.CoreOneBed was not found in src/LayerPeek.Core/Detection/DetectionRegion.cs.' }
            $coreOne = [double[]] @($code.Groups['v'].Value -split ',' | ForEach-Object { [double]::Parse($_.Trim(), [cultureinfo]::InvariantCulture) })
            $coreOnePercent = [double[]] @($coreOne | ForEach-Object { [math]::Round($_ * 100, [MidpointRounding]::AwayFromZero) })
            $simulator = Get-Simulator $c
            Set-SimulatorScenario $simulator 'idle'
            # A Core One by its chosen image, so that "Default for Core One" shows also before the model is detected.
            $data = New-TestData -Context $c -Simulator $simulator -Camera $simulator.Camera -Detection -PrinterOptions ([ordered]@{ image = [ordered]@{ mode = 'Model'; model = 'CoreOne' } })
            $settingsFile = Join-Path $data.Data 'settings.json'
            $problems = [System.Collections.Generic.List[string]]::new()
            $notes = [System.Collections.Generic.List[string]]::new()
            $app = Start-TestApp -Context $c -TestData $data
            $appId = $app.Id
            $script:editor = $null
            # Its accessible name (AXTitle, or AXDescription for a control without its own role) as whole percent.
            $readRegion = {
                $script:editor = Get-AxElement $c $appId 'PrinterDetectionRegion'
                if ($script:editor) { ConvertFrom-VerifyFormattedText -Format $format -Text ([string] (@($script:editor.title, $script:editor.description) | Where-Object { $_ } | Select-Object -First 1)) }
            }
            $openEditor = {
                Send-TestCommand $data 'settings'
                $found = Open-SettingsTab $c $appId @('SettingsTabPrinters', 'PrinterTabDetection') 'PrinterDetectionRegion'
                if (-not $found) { throw 'Settings > Printers > Detection shows no bed region editor (PrinterDetectionRegion)' }
                & $readRegion
            }
            $saved = { $json = Get-Content -LiteralPath $settingsFile -Raw | ConvertFrom-Json; $p = @($json.printers)[0]; if ($p.PSObject.Properties['detection'] -and $p.detection.PSObject.Properties['region']) { $p.detection.region } }
            $save = {
                param([string] $What)
                $hash = (Get-FileHash -LiteralPath $settingsFile).Hash
                $null = Invoke-Helper $c @('axpress', "$appId", 'SettingsSave')
                if (-not (Wait-VerifyCondition -TimeoutSeconds 10 -Condition { (Get-FileHash -LiteralPath $settingsFile).Hash -ne $hash })) { $problems.Add("${What}: Save did not write settings.json") }
                $null = Wait-VerifyCondition -TimeoutSeconds 10 -Condition { -not (Get-AxElement $c $appId 'PrinterDetectionRegion') }
                $null = Save-VerifyEvidence -Context $c -Name "settings-$What.json" -Path $settingsFile
                & $saved
            }
            try {
                # The default of a Core One before any edit.
                $region = & $openEditor
                $problem = Get-VerifyRegionProblem -Actual $region -Expected $coreOnePercent
                if ($problem) { $problems.Add("the default region of a Core One ('$($script:editor.title)$($script:editor.description)'): $problem") }

                # Load frame: a frame of the simulator's camera in the editor (the note "no frame" goes, no error).
                $null = Invoke-Helper $c @('axpress', "$appId", 'PrinterDetectionLoadFrame')
                $loaded = Wait-VerifyCondition -TimeoutSeconds 30 -Condition { $t = Get-AxTree $c $appId; -not (Find-AxElement $t 'PrinterDetectionNoFrame') -and -not (Find-AxElement $t 'PrinterDetectionLoadFrameFailed') -and (Find-AxElement $t 'PrinterDetectionRegion') }
                $null = Save-VerifyEvidence -Context $c -Name 'detection-after-load.json' -Content ((Get-AxTree $c $appId) | ConvertTo-Json -Depth 100)
                if (-not $loaded) { $problems.Add("Load frame showed no frame within 30 s ($((Get-AxElement $c $appId 'PrinterDetectionLoadFrameFailed').title))") }
                else { $notes.Add('Load frame showed a frame of the simulator') }

                # A mouse drag outside the region draws a new one: 5 % / 5 % to 45 % / 40 % of the frame (16:9 like the editor).
                $null = & $readRegion
                $f = $script:editor.frame
                $aspect = 16 / 9
                $w = [math]::Min($f.width, $f.height * $aspect)
                $h = $w / $aspect
                $ix = $f.x + ($f.width - $w) / 2
                $iy = $f.y + ($f.height - $h) / 2
                $point = { param([double] $X, [double] $Y) @("$([math]::Round($ix + $w * $X, 1))", "$([math]::Round($iy + $h * $Y, 1))") }
                $null = Invoke-Helper $c (@('drag') + (& $point 0.05 0.05) + (& $point 0.45 0.40) + @('800'))
                Start-Sleep -Milliseconds 500
                $region = & $readRegion
                $problem = Get-VerifyRegionProblem -Actual $region -Expected 5, 5, 40, 35 -Tolerance 2
                if ($problem) { $problems.Add("the drag from 5 / 5 % to 45 / 40 % of the frame: $problem") }
                else { $notes.Add("the drag drew $($region -join ' / ') %") }
                if ((Get-AxFocusedId $c $appId) -cne 'PrinterDetectionRegion') { $problems.Add("after the drag the editor has no keyboard focus (focused: '$(Get-AxFocusedId $c $appId)')") }

                # The keys: each changes one value by 1 or 10 percentage points (Cmd = 10 %, Shift = the size).
                $steps = @(
                    @{ Key = 'right'; Delta = 1, 0, 0, 0 }, @{ Key = 'down'; Delta = 0, 1, 0, 0 }, @{ Key = 'shift+right'; Delta = 0, 0, 1, 0 },
                    @{ Key = 'shift+down'; Delta = 0, 0, 0, 1 }, @{ Key = 'cmd+right'; Delta = 10, 0, 0, 0 }, @{ Key = 'cmd+down'; Delta = 0, 10, 0, 0 },
                    @{ Key = 'cmd+shift+right'; Delta = 0, 0, 10, 0 }, @{ Key = 'cmd+left'; Delta = -10, 0, 0, 0 }, @{ Key = 'up'; Delta = 0, -1, 0, 0 }
                )
                foreach ($step in $steps) {
                    if (-not $region) { break }
                    $want = [double[]] @(for ($i = 0; $i -lt 4; $i++) { $region[$i] + $step.Delta[$i] })
                    $null = Invoke-Helper $c @('key', $step.Key)
                    $script:keyRegion = $null
                    $null = Wait-VerifyCondition -TimeoutSeconds 3 -Condition { $script:keyRegion = & $readRegion; -not (Get-VerifyRegionProblem -Actual $script:keyRegion -Expected $want) }
                    $problem = Get-VerifyRegionProblem -Actual $script:keyRegion -Expected $want
                    if ($problem) { $problems.Add("key $($step.Key) from $($region -join ' / ') %: $problem") }
                    $region = $script:keyRegion
                }
                if ($region) { $notes.Add("the keys $(@($steps | ForEach-Object Key) -join ', ') moved and sized it to $($region -join ' / ') % ('$($script:editor.title)$($script:editor.description)')") }

                # Save: settings.json has the region the name says; opened again, the same name.
                $named = $region
                $file = & $save 'region'
                $problem = Get-VerifyRegionProblem -Actual (ConvertTo-VerifyRegionPercent $file) -Expected $named
                if ($problem) { $problems.Add("settings.json after Save, against the name before: $problem") }
                $region = & $openEditor
                $problem = Get-VerifyRegionProblem -Actual $region -Expected $named
                if ($problem) { $problems.Add("Settings opened again: $problem") }

                # Default for Core One: the region of the code, saved as it is.
                $null = Invoke-Helper $c @('axpress', "$appId", 'PrinterDetectionCoreOne')
                $script:coreRegion = $null
                $null = Wait-VerifyCondition -TimeoutSeconds 5 -Condition { $script:coreRegion = & $readRegion; -not (Get-VerifyRegionProblem -Actual $script:coreRegion -Expected $coreOnePercent) }
                $problem = Get-VerifyRegionProblem -Actual $script:coreRegion -Expected $coreOnePercent
                if ($problem) { $problems.Add("Default for Core One: $problem") }
                $file = & $save 'core-one'
                $exact = $file -and [math]::Abs([double] $file.x - $coreOne[0]) -lt 1e-9 -and [math]::Abs([double] $file.y - $coreOne[1]) -lt 1e-9 -and [math]::Abs([double] $file.width - $coreOne[2]) -lt 1e-9 -and [math]::Abs([double] $file.height - $coreOne[3]) -lt 1e-9
                if (-not $exact) { $problems.Add("settings.json after Default for Core One and Save: $($file | ConvertTo-Json -Compress), expected the code's $($coreOne -join ', ')") }
                else { $notes.Add("Default for Core One saved $($coreOne -join ', ') (DetectionRegion.CoreOneBed)") }
            }
            finally {
                $null = Stop-TestApp $data $app
            }
            if ($problems.Count -gt 0) { return New-VerifyResult FAIL "$($problems.Count) problem(s): $($problems -join '; ')" }
            New-VerifyResult PASS ($notes -join '; ')
        }
    }

    New-VerifyCheck -Id 'mac.appkit-language' -Needs Session, Accessibility -Kind Permission `
        -Permission 'Accessibility for the terminal (System Settings > Privacy & Security > Accessibility) to open the image panel and the context menu of a text field and read them, and an unlocked screen' `
        -Title 'T-097 question 5 (a finding, not a failure): with LayerPeek in another language than the system (de, or fr on a German system), the language of the buttons of the printer image panel (NSOpenPanel) and of the items of the context menu of a text field (simulator)' `
        -Closes 'T-097 (otázka 5: v jakém jazyce jsou prvky AppKitu při jazyce LayerPeeku jiném než jazyk systému; zjištění i do T-097)', 'T-216 kritérium 4' -Action {
        param($c)
        if (-not (Request-Accessibility $c)) { Skip-VerifyCheck 'no Accessibility permission for the terminal (System Settings > Privacy & Security > Accessibility; it may need a restart of the terminal).' }
        Invoke-SessionCheck $c {
            $system = @(& /usr/bin/defaults read -g AppleLanguages 2>$null | ForEach-Object { if ("$_" -match '"?(?<l>[A-Za-z]{2,3}(-[A-Za-z0-9]+)*)"?,?\s*$') { $Matches.l } })
            $language = Get-VerifyOtherLanguage -SystemLanguages $system
            $simulator = Get-Simulator $c
            $data = New-TestData -Context $c -Simulator $simulator -Language $language
            $app = Start-TestApp -Context $c -TestData $data
            $appId = $app.Id
            $findings = [System.Collections.Generic.List[string]]::new()
            $failures = [System.Collections.Generic.List[string]]::new()
            $describe = { param([string[]] $Titles) $l = @(Get-VerifyTitleLanguages -Titles $Titles); if ($l.Count) { ($l | ForEach-Object { "$($_.Language) ($($_.Matches))" }) -join ', ' } else { 'not recognised' } }
            try {
                Send-TestCommand $data 'settings'
                if (-not (Open-SettingsTab $c $appId @('SettingsTabPrinters') 'PrinterImageButton')) { throw 'Settings show no printer image button (PrinterImageButton)' }
                # 1. The printer image panel: its default and cancel buttons.
                $null = Invoke-Helper $c @('axpress', "$appId", 'PrinterImageButton')
                if (-not (Wait-VerifyCondition -TimeoutSeconds 5 -Condition { $null -ne (Get-AxElement $c $appId 'PrinterImageCustom') })) { $failures.Add('the menu of the printer image did not open') }
                else {
                    $item = Get-AxElement $c $appId 'PrinterImageCustom'
                    $pressed = Invoke-HelperExit $c @('axpress', "$appId", 'PrinterImageCustom')
                    if ($pressed.ExitCode -ne 0) { $point = Get-AxCenter $item; $null = Invoke-Helper $c @('click', "$($point[0])", "$($point[1])") }
                    $panel = Invoke-HelperExit $c @('panel', "$appId", '10') 'panel.json'
                    $found = if ($panel.ExitCode -eq 0) { @($panel.Output -join "`n" | ConvertFrom-Json -Depth 10) | Select-Object -First 1 }
                    if (-not $found) { $failures.Add("the image panel did not open ($($panel.Output -join ' '))") }
                    else {
                        $titles = @($found.defaultButton.title, $found.cancelButton.title) | Where-Object { $_ }
                        $findings.Add("image panel ($($found.process)): buttons '$($titles -join "', '")' = $(& $describe $titles)")
                    }
                    $null = Invoke-HelperExit $c @('panelcancel', "$appId")
                }
                # A panel left open (one the helper did not recognise) would take the Control-click below and show its
                # own file menu (first run with an unlocked screen 2026-11-15): never read a menu over an open sheet.
                $sheetOpen = { @(Get-AxNodes (Get-AxTree $c $appId) | Where-Object role -EQ 'AXSheet').Count -gt 0 }
                if (& $sheetOpen) { $null = Invoke-Helper $c @('key', 'escape'); Start-Sleep -Milliseconds 800 }
                # 2. The context menu of a text field (the printer name): Control-click, then its items in the AX tree.
                $field = Get-AxElement $c $appId 'PrinterName'
                if (& $sheetOpen) { $failures.Add('a sheet (the image panel) stayed open over Settings, the context menu of the text field was not read') }
                elseif (-not $field) { $failures.Add('no text field PrinterName') }
                else {
                    $point = Get-AxCenter $field
                    $before = @(Get-AxNodes (Get-AxTree $c $appId) | Where-Object role -EQ 'AXMenuItem' | ForEach-Object title)
                    $script:menuTitles = @()
                    foreach ($click in 'ctrlclick', 'rightclick') {
                        $null = Invoke-Helper $c @($click, "$($point[0])", "$($point[1])")
                        if (Wait-VerifyCondition -TimeoutSeconds 5 -Condition { $script:menuTitles = @(Get-AxNodes (Get-AxTree $c $appId) | Where-Object { $_.role -eq 'AXMenuItem' -and $_.title -and $before -notcontains $_.title } | ForEach-Object title); $script:menuTitles.Count -gt 0 }) { break }
                    }
                    $null = Save-VerifyEvidence -Context $c -Name 'text-menu.json' -Content ((Get-AxTree $c $appId) | ConvertTo-Json -Depth 100)
                    if ($script:menuTitles.Count -eq 0) { $failures.Add('Control-click and right click on the printer name opened no context menu in the Accessibility tree (text-menu.json)') }
                    else { $findings.Add("text field context menu: '$($script:menuTitles -join "', '")' = $(& $describe $script:menuTitles)") }
                    $null = Invoke-Helper $c @('key', 'escape')
                }
                $null = Invoke-Helper $c @('axpress', "$appId", 'SettingsCancel') -AllowFailure
            }
            finally { $null = Stop-TestApp $data $app }
            $null = Save-VerifyEvidence -Context $c -Name 'finding.txt' -Content (@("system languages: $($system -join ', ')", "LayerPeek language: $language") + $findings + $failures)
            $head = "system $($system -join ', '), LayerPeek $language"
            # A decision point: what AppKit shows is the finding; only nothing read at all is a failure of the check.
            if ($findings.Count -eq 0) { return New-VerifyResult FAIL "$head; nothing could be read: $($failures -join '; ')" }
            New-VerifyResult PASS "finding for T-097 question 5 ($head): $($findings -join '; ')$(if ($failures.Count) { "; not read: $($failures -join '; ')" })"
        }
    }

    New-VerifyCheck -Id 'mac.open-fake' -Needs Session, Accessibility -Kind Auto -Title 'links open through /usr/bin/open, never a planted open of PATH (T-053 L4): the app runs with a fake open first in PATH that leaves a marker; the repository link of About and Open PrusaLink of the menu (simulator) open the default browser, which loads the web of the simulator through the proxy (HTTP 200 in the detailed log); the licences of About open; the marker stays missing' `
        -Closes 'T-053 (krit. „Ručně na Macu: /usr/bin/open otevírá odkazy a web PrusaLink přes proxy (L4)“; přihlášení do webu přes proxy ověřují E2E testy T-028 / T-107)', 'T-216 kritérium 5' -Action {
        param($c)
        if (-not (Test-Accessibility $c)) { Skip-VerifyCheck 'needs Accessibility for the terminal (clicks on the links of About, the menu of the status item).' }
        Invoke-SessionCheck $c {
            $texts = Get-AppTexts 'en'
            $problems = [System.Collections.Generic.List[string]]::new()
            $notes = [System.Collections.Generic.List[string]]::new()
            # The fake open: leaves a marker only (never its arguments: the proxy address carries an access token).
            $fake = Join-Path $c.RunFolder 'tmp' "fake-open-$(New-ShortId)"
            $null = New-Item -ItemType Directory -Force -Path $fake
            $marker = Join-Path $fake 'called'
            [System.IO.File]::WriteAllText((Join-Path $fake 'open'), "#!/bin/sh`n: > '$marker'`nexit 0`n")
            & /bin/chmod 755 (Join-Path $fake 'open')
            $path = "${fake}:$([Environment]::GetEnvironmentVariable('PATH'))"
            $resolved = (@(& /usr/bin/env "PATH=$path" /bin/sh -c 'command -v open') -join '').Trim()
            if ($resolved -cne (Join-Path $fake 'open')) { return New-VerifyResult FAIL "the fake open is not the first open of PATH (command -v open: '$resolved')." }
            $notes.Add('the fake open was first in PATH (command -v open)')
            $handlers = try { (@(& /usr/bin/plutil -convert json -o - (Join-Path ([Environment]::GetFolderPath('UserProfile')) 'Library' 'Preferences' 'com.apple.LaunchServices' 'com.apple.launchservices.secure.plist') 2>$null) -join "`n") | ConvertFrom-Json -Depth 20 } catch { $null }
            $browser = Get-VerifyDefaultBrowser $handlers
            $browserPids = { @((Invoke-Helper $c @('apps', $browser)) -join "`n" | ConvertFrom-Json) }
            $browserWindows = { param([int] $Id) @(Get-AxWindows $c $Id | ForEach-Object title) }
            $runningBefore = @(& $browserPids)
            $windowsBefore = @(foreach ($id in $runningBefore) { & $browserWindows $id })
            $simulator = Get-Simulator $c
            Set-SimulatorScenario $simulator 'idle'
            $data = New-TestData -Context $c -Simulator $simulator -SettingsOptions ([ordered]@{ debugLogging = $true })
            $name = (Get-Content -LiteralPath (Join-Path $data.Data 'settings.json') -Raw | ConvertFrom-Json).printers[0].name
            $app = Start-TestApp -Context $c -TestData $data -Environment @{ PATH = $path }
            $appId = $app.Id
            $browserFront = { $front = Get-FrontProcessId; $front -and (@(& $browserPids) -contains $front) }
            $logSince = { param([int] $Length) $log = Read-TestLog $data; if ($log.Length -ge $Length) { $log.Substring($Length) } else { $log } }
            # The Debug test mode hands the addresses to opened-urls.txt instead of the browser (RecordingUrlLauncher, T-067;
            # first run with an unlocked screen 2026-11-15): then /usr/bin/open and the browser are not exercised (a SKIP),
            # and the check loads the recorded proxy address itself (never written anywhere: it carries the access token).
            $skipped = [System.Collections.Generic.List[string]]::new()
            $recordedUrls = { $file = Join-Path $data.Data 'opened-urls.txt'; if (Test-Path -LiteralPath $file) { @(Get-Content -LiteralPath $file | Where-Object { $_ }) } else { @() } }
            try {
                # 1. About: the repository link opens the default browser; the licences open their window.
                Send-TestCommand $data 'about'
                $script:link = $null
                $null = Wait-VerifyCondition -TimeoutSeconds 15 -Condition { $script:link = @(Get-AxNodes (Get-AxTree $c $appId) | Where-Object { $_.frame -and (@($_.title, $_.description, $_.value) -ccontains $texts['AboutRepository']) }) | Select-Object -First 1; $null -ne $script:link }
                if (-not $script:link) { $problems.Add("About shows no link '$($texts['AboutRepository'])'") }
                else {
                    $point = Get-AxCenter $script:link
                    $null = Invoke-Helper $c @('click', "$($point[0])", "$($point[1])")
                    if (Wait-VerifyCondition -TimeoutSeconds 20 -Condition { (& $browserFront) -or (Test-VerifyUrlsRecorded (Read-TestLog $data)) }) {
                        if (Test-VerifyUrlsRecorded (Read-TestLog $data)) {
                            $github = @(& $recordedUrls | Where-Object { ([uri] $_).Host -ceq 'github.com' }).Count
                            if ($github -eq 0) { $problems.Add('the repository link recorded no github.com address (opened-urls.txt)') }
                            $skipped.Add("the repository link: the Debug test mode recorded the github.com address instead of opening the browser ($browser)")
                        }
                        else { $notes.Add("the repository link brought the default browser ($browser) to the front") }
                    }
                    else { $problems.Add("the repository link did not bring the default browser ($browser) to the front (front app $(Get-FrontAppText))") }
                }
                Send-TestCommand $data 'about'
                $null = Wait-VerifyCondition -TimeoutSeconds 10 -Condition { (Get-FrontProcessId) -eq $appId }
                $licences = @(Get-AxNodes (Get-AxTree $c $appId) | Where-Object { $_.frame -and (@($_.title, $_.description, $_.value) -ccontains $texts['AboutThirdPartyLicenses']) }) | Select-Object -First 1
                if (-not $licences) { $problems.Add("About shows no link '$($texts['AboutThirdPartyLicenses'])'") }
                else {
                    $point = Get-AxCenter $licences
                    $null = Invoke-Helper $c @('click', "$($point[0])", "$($point[1])")
                    if (-not (Wait-VerifyCondition -TimeoutSeconds 10 -Condition { @(Get-AxWindows $c $appId | Where-Object title -CEQ $texts['LicensesTitle']).Count -gt 0 })) { $problems.Add("the licences link opened no window '$($texts['LicensesTitle'])'") }
                    else {
                        $notes.Add("the licences opened the window '$($texts['LicensesTitle'])'")
                        $null = Invoke-HelperExit $c @('axwin', "$appId", $texts['LicensesTitle'], 'close')
                    }
                }
                $null = Invoke-HelperExit $c @('axwin', "$appId", $texts['AboutTitle'], 'close')

                # 2. Open PrusaLink: the browser loads the web of the simulator through the proxy of the app.
                $logLength = (Read-TestLog $data).Length
                $item = "$($texts['MenuPrinter'] -f $name, $texts['StateIdle'])>$(ConvertFrom-VerifyAccessText $texts['MenuOpenPrusaLink'])"
                $press = Invoke-MenuPress $c $appId $item
                if ($press.ExitCode -ne 0) { $problems.Add("menupress '$item': $($press.Output -join ' ')") }
                else {
                    $script:answers = @()
                    # The test mode: the check opens the recorded proxy address itself, as the browser would (one GET).
                    $script:proxyUrl = $null
                    if ((Wait-VerifyCondition -TimeoutSeconds 10 -Condition { $script:proxyUrl = @(& $recordedUrls | Where-Object { ([uri] $_).Host -ceq '127.0.0.1' }) | Select-Object -Last 1; $script:proxyUrl -or (& $browserFront) }) -and $script:proxyUrl) {
                        try { $null = Invoke-WebRequest -Uri $script:proxyUrl -TimeoutSec 15 -MaximumRedirection 5 -SkipHttpErrorCheck }
                        catch { $problems.Add("the recorded address of the web proxy did not answer: $($_.Exception.GetType().Name)") }
                        $skipped.Add('Open PrusaLink: the Debug test mode recorded the proxy address instead of opening the browser (the check loaded it itself)')
                    }
                    $answered = Wait-VerifyCondition -TimeoutSeconds 30 -IntervalMilliseconds 500 -Condition { $script:answers = @(Get-VerifyProxyAnswers (& $logSince $logLength) | Where-Object { $_.Printer -ceq $script:PrinterId }); @($script:answers | Where-Object { $_.Status -eq 200 }).Count -gt 0 }
                    if (-not $answered) { $problems.Add("the web proxy answered no request with 200 within 30 s after Open PrusaLink (answers: $(@($script:answers | ForEach-Object { "$($_.Method) $($_.Path) $($_.Status)" }) -join ', '))") }
                    else { $notes.Add("Open PrusaLink: the browser loaded the simulator's web through the proxy ($(@($script:answers | Select-Object -First 3 | ForEach-Object { "$($_.Method) $($_.Path) $($_.Status)" }) -join ', '))") }
                }
                if (Test-Path -LiteralPath $marker) { $problems.Add('the fake open of PATH ran (T-053 L4: the app must start /usr/bin/open)') }
                else { $notes.Add('the fake open never ran') }
            }
            finally {
                $null = Stop-TestApp $data $app
                # The browser as it was: the windows the links opened closed, the browser quit when it was not running.
                foreach ($id in @(& $browserPids)) {
                    if ($runningBefore -notcontains $id) { $null = Invoke-HelperExit $c @('terminate', "$id"); $notes.Add("the browser started by the links was quit"); continue }
                    $new = @(& $browserWindows $id | Where-Object { $_ -and $windowsBefore -notcontains $_ })
                    foreach ($title in $new) { $null = Invoke-HelperExit $c @('axwin', "$id", $title, 'close') }
                    $notes.Add($(if ($new.Count) { "$($new.Count) new browser window(s) closed" } else { 'the links opened in tabs of the running browser (left open)' }))
                }
            }
            if ($problems.Count -gt 0) { return New-VerifyResult FAIL "$($problems.Count) problem(s): $($problems -join '; ')" }
            if ($skipped.Count -gt 0) { Skip-VerifyCheck "not judged: /usr/bin/open and the default browser ($($skipped -join '; '); a test mode that opens them is T-238). Done: $($notes -join '; ')." }
            New-VerifyResult PASS ($notes -join '; ')
        }
    }

    New-VerifyCheck -Id 'mac.hw-decoding-switch' -Needs Session, Accessibility -Kind Permission `
        -Permission 'Accessibility for the terminal (System Settings > Privacy & Security > Accessibility) to choose Hardware decoding in Settings and read the camera windows, and an unlocked screen; Screen Recording optional (two captures of the image instead of the Accessibility state)' `
        -Title 'Hardware decoding switched while the camera window is open (Automatic -> Off -> Automatic, Save): the image goes on within about 2 s (a key frame) and the log says "Camera video decoding: ..." with the new path; two simulators, two printers, two camera windows: both show the image and one VTDecoderXPCService serves both (Debug test bundle, the Buddy3D clip at 25 fps when present)' `
        -Closes 'T-205 (body (4) přepnutí volby za běhu a (5) dvě kamery s jedním VTDecoderXPCService; testovací Debug bundle, rozhodnutí 2026-11-13)', 'T-216 kritérium 6' -Action {
        param($c)
        if (-not (Request-Accessibility $c)) { Skip-VerifyCheck 'no Accessibility permission for the terminal (System Settings > Privacy & Security > Accessibility; it may need a restart of the terminal).' }
        Invoke-SessionCheck $c {
            $texts = Get-AppTexts 'en'
            $capture = (Invoke-Helper $c @('screenrecording')) -contains 'true'
            $clip = Get-Buddy3DClip
            $problems = [System.Collections.Generic.List[string]]::new()
            $notes = [System.Collections.Generic.List[string]]::new()
            $notes.Add($(if ($clip) { 'the Buddy3D clip at 25 fps' } else { "the simulator's own clip at 25 fps (no $((Join-Path (Get-SamplesFolder) 'clips' 'coreone-20261103-1905-60s.mp4')))" }))
            $first = Get-Simulator $c -Clip $clip -Fps 25
            $second = Get-Simulator $c -Clip $clip -Fps 25 -Instance 'second'
            foreach ($s in $first, $second) { Set-SimulatorScenario $s 'printing' }
            $xpc = { @(& /usr/bin/pgrep -x VTDecoderXPCService 2>$null | ForEach-Object { [int] $_ }) }
            # The image of a camera window goes on: two captures differ (Screen Recording), else its print status overlay
            # and no connection state ("No signal", "Connecting...") in the Accessibility tree.
            $states = @($texts['CameraNoSignal'], $texts['CameraConnecting'])
            $running = {
                param([int] $AppId, [string] $Title, [string] $What)
                $script:axState = $null
                $ok = Wait-VerifyCondition -TimeoutSeconds 5 -IntervalMilliseconds 200 -Condition {
                    $tree = @(Get-AxTree $c $AppId | Where-Object { $_.title -ceq $Title })
                    $shown = @(Get-AxNodes $tree | ForEach-Object { "$($_.title)"; "$($_.value)" } | Where-Object { $_ })
                    $script:axState = @($shown | Where-Object { $states -contains $_ })
                    (Find-AxElement $tree 'CameraOverlay') -and $script:axState.Count -eq 0
                }
                if (-not $ok) { $problems.Add("${What}: the window '$Title' shows $(if ($script:axState) { "'$($script:axState -join "', '")'" } else { 'no print status overlay' })"); return }
                if (-not $capture) { return 'running (Accessibility)' }
                $window = @(Get-ProcessWindows $c $AppId | Where-Object { $_.onscreen -and $_.layer -eq 0 -and $_.width -ge 80 }) | Sort-Object { $_.width * $_.height } -Descending | Select-Object -First 1
                $tree = @(Get-AxTree $c $AppId | Where-Object { $_.title -ceq $Title })
                $surface = Find-AxElement $tree 'Surface'
                $axWindow = @($tree)[0]
                $number = @(Get-ProcessWindows $c $AppId | Where-Object { $_.onscreen -and [math]::Abs($_.x - $axWindow.frame.x) -le 2 -and [math]::Abs($_.y - $axWindow.frame.y) -le 2 }) | Select-Object -First 1
                if (-not $number -or -not $surface) { return 'running (Accessibility; no window to capture)' }
                $files = foreach ($i in 1, 2) {
                    $file = Join-Path (Get-VerifyEvidenceFolder $c) "$($What -replace '[^A-Za-z0-9]+', '-')-$i.png"
                    & /usr/sbin/screencapture -x -o -l "$($number.number)" $file
                    $file
                    if ($i -eq 1) { Start-Sleep -Seconds 1 }
                }
                $diff = (Invoke-Helper $c @('regiondiff', $files[0], $files[1], "$($number.width)", "$($surface.frame.x - $number.x)", "$($surface.frame.y - $number.y)", "$($surface.frame.width)", "$($surface.frame.height)") -AllowFailure) -join "`n" | ConvertFrom-Json -ErrorAction SilentlyContinue
                $verdict = Get-VerifyLiveImageVerdict -Diff $diff
                if (-not $verdict.Live) { $problems.Add("${What}: the image of '$Title' did not change within 1 s ($($verdict.Detail))"); return }
                "live ($($verdict.Detail))"
            }
            $chooseDecoding = {
                param($Data, [int] $AppId, [string] $Choice)
                $hash = (Get-FileHash -LiteralPath (Join-Path $Data.Data 'settings.json')).Hash
                Send-TestCommand $Data 'settings'
                $combo = Open-SettingsTab $c $AppId @('SettingsTabCamera') 'HardwareDecoding'
                if (-not $combo) { throw 'Settings > Camera shows no HardwareDecoding' }
                $windows = @(Get-ProcessWindows $c $AppId).Count
                $center = Get-AxCenter $combo
                $null = Invoke-Helper $c @('click', "$($center[0])", "$($center[1])")
                if (Wait-VerifyCondition -TimeoutSeconds 5 -Condition { @(Get-ProcessWindows $c $AppId).Count -gt $windows }) {
                    Start-Sleep -Milliseconds 400
                    $inCombo = { param($Node) $f = $Node.frame; $b = $combo.frame; $x = $f.x + $f.width / 2; $y = $f.y + $f.height / 2; $x -ge $b.x -and $x -le $b.x + $b.width -and $y -ge $b.y -and $y -le $b.y + $b.height }
                    $item = @(Get-AxNodes (Get-AxTree $c $AppId) | Where-Object { $_.frame -and (@($_.title, $_.value, $_.description) -ccontains $Choice) -and -not (& $inCombo $_) }) | Select-Object -First 1
                    if ($item) { $point = Get-AxCenter $item; $null = Invoke-Helper $c @('click', "$($point[0])", "$($point[1])") }
                    else { $null = Invoke-Helper $c @('key', 'escape') }
                    Start-Sleep -Milliseconds 600
                }
                $shown = Get-AxElement $c $AppId 'HardwareDecoding'
                if (-not ($shown -and (@(Get-AxNodes @($shown) | ForEach-Object { $_.title; $_.value }) -ccontains $Choice))) { $null = Invoke-Helper $c @('axpress', "$AppId", 'SettingsCancel') -AllowFailure; throw "HardwareDecoding does not show '$Choice' after choosing it" }
                $clock = [System.Diagnostics.Stopwatch]::StartNew()
                $null = Invoke-Helper $c @('axpress', "$AppId", 'SettingsSave')
                if (-not (Wait-VerifyCondition -TimeoutSeconds 10 -Condition { (Get-FileHash -LiteralPath (Join-Path $Data.Data 'settings.json')).Hash -ne $hash })) { $problems.Add("Save of '$Choice' did not write settings.json") }
                $clock
            }

            # (4) One camera window; Automatic -> Off -> Automatic.
            $data = New-TestData -Context $c -Simulator $first -Camera $first.Camera -Window ([ordered]@{ rememberLastPlacement = $false; startMode = 'Normal' })
            $name = (Get-Content -LiteralPath (Join-Path $data.Data 'settings.json') -Raw | ConvertFrom-Json).printers[0].name
            $cameraTitle = $texts['CameraTitle'] -f $name
            $app = Start-TestApp -Context $c -TestData $data
            try {
                Send-TestCommand $data "camera:$($script:PrinterId)"
                if (-not (Wait-VerifyCondition -TimeoutSeconds 20 -Condition { @(Get-VerifyDecodingPaths (Read-TestLog $data)).Count -gt 0 })) { throw 'the camera window logged no "Camera video decoding" line within 20 s' }
                $start = @(Get-VerifyDecodingPaths (Read-TestLog $data))[-1]
                if ($start -cne 'videotoolbox') { $problems.Add("(4) Automatic decodes with '$start', expected videotoolbox") }
                $state = & $running $app.Id $cameraTitle '(4) Automatic'
                if ($state) { $notes.Add("(4) Automatic: $start, $state") }
                foreach ($step in @(@{ Choice = $texts['SettingsHardwareDecodingOff']; Expected = 'software*'; File = 'Off' }, @{ Choice = $texts['SettingsHardwareDecodingAutomatic']; Expected = 'videotoolbox'; File = $null })) {
                    $count = @(Get-VerifyDecodingPaths (Read-TestLog $data)).Count
                    $clock = & $chooseDecoding $data $app.Id $step.Choice
                    $saved = (Get-Content -LiteralPath (Join-Path $data.Data 'settings.json') -Raw | ConvertFrom-Json)
                    $value = if ($saved.PSObject.Properties['cameraHardwareDecoding']) { [string] $saved.cameraHardwareDecoding } else { $null }
                    if ($value -cne $step.File) { $problems.Add("(4) '$($step.Choice)' saved cameraHardwareDecoding '$value'") }
                    $script:paths = @()
                    $logged = Wait-VerifyCondition -TimeoutSeconds 10 -IntervalMilliseconds 200 -Condition { $script:paths = @(Get-VerifyDecodingPaths (Read-TestLog $data) | Select-Object -Skip $count); @($script:paths | Where-Object { $_ -like $step.Expected }).Count -gt 0 }
                    $seconds = [math]::Round($clock.Elapsed.TotalSeconds, 1)
                    if (-not $logged) { $problems.Add("(4) after '$($step.Choice)' no log line 'Camera video decoding: $($step.Expected)' within 10 s (logged: $($script:paths -join ', '))"); continue }
                    $state = & $running $app.Id $cameraTitle "(4) $($step.Choice)"
                    if ($state) { $notes.Add("(4) $($step.Choice): '$(@($script:paths)[-1])' $seconds s after Save, $state") }
                }
            }
            finally {
                $null = Stop-TestApp $data $app
                $null = Save-VerifyEvidence -Context $c -Name 'switch-app.log' -Content (Read-TestLog $data)
            }

            # (5) Two printers of two simulators, two camera windows: one VTDecoderXPCService more than before.
            $baseline = @(& $xpc)
            $data = New-TestData -Context $c -Simulator $first -Camera $first.Camera -Window ([ordered]@{ rememberLastPlacement = $false; startMode = 'Normal' })
            $names = @((Get-Content -LiteralPath (Join-Path $data.Data 'settings.json') -Raw | ConvertFrom-Json).printers[0].name)
            $names += Add-TestPrinter $data $second 'verify2'
            $app = Start-TestApp -Context $c -TestData $data
            try {
                foreach ($id in $script:PrinterId, 'verify2') { Send-TestCommand $data "camera:$id"; Start-Sleep -Seconds 1 }
                $titles = @($names | ForEach-Object { $texts['CameraTitle'] -f $_ })
                if (-not (Wait-VerifyCondition -TimeoutSeconds 30 -Condition { @(Get-AxWindows $c $app.Id | Where-Object { $titles -ccontains $_.title }).Count -eq 2 -and @(Get-VerifyDecodingPaths (Read-TestLog $data) | Where-Object { $_ -ceq 'videotoolbox' }).Count -ge 2 })) {
                    $problems.Add("(5) not two camera windows decoding with videotoolbox within 30 s (windows: $(@(Get-AxWindows $c $app.Id | ForEach-Object title) -join ', '); decoding: $(@(Get-VerifyDecodingPaths (Read-TestLog $data)) -join ', '))")
                }
                Start-Sleep -Seconds 2
                foreach ($title in $titles) { $state = & $running $app.Id $title "(5) $title"; if ($state) { $notes.Add("(5) '$title': $state") } }
                $peers = foreach ($s in $first, $second) { (Get-RtspConnections $app.Id $s.CameraPort).Count }
                if (@($peers | Where-Object { $_ -lt 1 }).Count) { $problems.Add("(5) RTSP connections per camera: $($peers -join ', ')") }
                $now = @(& $xpc)
                $new = @($now | Where-Object { $baseline -notcontains $_ })
                $null = Save-VerifyEvidence -Context $c -Name 'vtdecoder.txt' -Content @("before: $($baseline -join ', ')", "with two windows: $($now -join ', ')")
                if ($new.Count -ne 1) { $problems.Add("(5) $($new.Count) new VTDecoderXPCService process(es) for two camera windows (pids $($new -join ', '); before $($baseline -join ', ')), expected exactly 1") }
                else { $notes.Add("(5) one VTDecoderXPCService ($($new[0])) for both windows$(if ($baseline.Count) { " ($($baseline.Count) of other apps before)" })") }
            }
            finally {
                $null = Stop-TestApp $data $app
                $null = Save-VerifyEvidence -Context $c -Name 'two-cameras-app.log' -Content (Read-TestLog $data)
            }
            if ($problems.Count -gt 0) { return New-VerifyResult FAIL "$($problems.Count) problem(s): $($problems -join '; ')" }
            New-VerifyResult PASS ($notes -join '; ')
        }
    }

    New-VerifyCheck -Id 'mac.keys-czech-layout' -Needs Session, Accessibility -Kind Permission `
        -Permission 'Accessibility for the terminal (System Settings > Privacy & Security > Accessibility) to post real key events, and an unlocked screen; the Czech keyboard layout is selected for the check and the previous one always selected again' `
        -Title 'T-100 question 1 with the software Czech layout (com.apple.keylayout.Czech), the US layout as the control: Cmd-Shift-] / Cmd-Shift-[ by their physical keys (right of P), from the name field and from a focused tab, switch the main tab of Settings and nothing else happens (no new window, the text field keeps its text); the previous input source is selected again' `
        -Closes 'T-100 (otázka 1 pro softwarové české rozložení: ⌘⇧] / ⌘⇧[ přepnou hlavní záložky Nastavení a nic jiného se nestane; vestavěná a Logitech klávesnice zůstávají ruční)', 'T-216 kritérium 7' -Action {
        param($c)
        if (-not (Request-Accessibility $c)) { Skip-VerifyCheck 'no Accessibility permission for the terminal (System Settings > Privacy & Security > Accessibility; it may need a restart of the terminal).' }
        Invoke-SessionCheck $c {
            # The Czech layout of the question, then the US layout as the control: a failure with both is no matter of
            # the layout (first run with an unlocked screen 2026-11-15: Cmd-Shift-[ did not go back with the Czech one).
            $layouts = 'com.apple.keylayout.Czech', 'com.apple.keylayout.US'
            $before = (Invoke-Helper $c @('inputsource')) -join "`n" | ConvertFrom-Json
            $simulator = Get-Simulator $c
            $data = New-TestData -Context $c -Simulator $simulator
            $problems = [System.Collections.Generic.List[string]]::new()
            $notes = [System.Collections.Generic.List[string]]::new()
            $steps = [System.Collections.Generic.List[object]]::new()
            $app = Start-TestApp -Context $c -TestData $data
            $appId = $app.Id
            $source = $null
            $sourceOut = $null
            # The printer name only while the Printers tab shows it (on another tab the field is not in the tree at all).
            $readName = {
                param([string] $Evidence)
                $field = Open-SettingsTab $c $appId @('SettingsTabPrinters') 'PrinterName'
                if ($field) { return [string] $field.value }
                $null = Save-VerifyEvidence -Context $c -Name "no-printer-name-$Evidence.json" -Content ((Get-AxTree $c $appId) | ConvertTo-Json -Depth 100)
                $null
            }
            # The name field focused by a click (the shortcuts start from a text box, as when the user edits the name).
            $focusName = {
                $point = Get-AxCenter (Get-AxElement $c $appId 'PrinterName')
                $null = Invoke-Helper $c @('click', "$($point[0])", "$($point[1])")
                Start-Sleep -Milliseconds 500
            }
            $step = {
                param([string] $Layout, [string] $Combo, $From, [int] $Direction)
                $null = Invoke-Helper $c @('key', $Combo)
                Start-Sleep -Milliseconds 600
                $to = Get-AxSelectedTab $c $appId 'SettingsTabs'
                $focused = Get-AxFocusedId $c $appId
                $problem = Get-VerifyTabMoveProblem -Tabs $From.Tabs -Before $From.Selected -After $to.Selected -Step $Direction
                $keyWindow = @(Get-AxWindows $c $appId | Where-Object focused | ForEach-Object title) -join ', '
                $steps.Add([ordered]@{ Layout = $Layout; Keys = $Combo; Before = $From.Selected; After = $to.Selected; Focused = $focused; Front = Get-FrontAppText; KeyWindow = $keyWindow; Problem = $problem })
                $short = $Layout -replace '^com\.apple\.keylayout\.', ''
                if ($problem) { $problems.Add("${short}: $(($Combo -replace 'cmd\+shift\+', 'Cmd-Shift-')): $problem (focus afterwards: '$focused')") } else { $notes.Add("${short}: $(($Combo -replace 'cmd\+shift\+', 'Cmd-Shift-')) $($From.Selected) -> $($to.Selected)") }
                $to
            }
            try {
                Send-TestCommand $data 'settings'
                if (-not (Open-SettingsTab $c $appId @('SettingsTabPrinters') 'PrinterName')) { throw 'Settings show no PrinterName' }
                foreach ($layout in $layouts) {
                    $short = $layout -replace '^com\.apple\.keylayout\.', ''
                    # The layout for at most 2 min; the helper selects the previous one again when it ends, also after an error here.
                    $sourceOut = Join-Path $c.RunFolder 'tmp' "inputsource-$(New-ShortId).txt"
                    $source = Start-HelperProcess $c @('inputsource', $layout, '120') $sourceOut
                    if (-not (Wait-VerifyCondition -TimeoutSeconds 10 -Condition { (Test-Path -LiteralPath $sourceOut) -and (Select-String -LiteralPath $sourceOut -Pattern '"selected"' -Quiet) })) { throw "the layout $layout was not selected: $(Get-Content -LiteralPath $sourceOut -Raw -ErrorAction SilentlyContinue)" }
                    $selected = (Get-Content -LiteralPath $sourceOut | Select-Object -First 1) | ConvertFrom-Json
                    if ($selected.selected -cne $layout) { throw "the input source is '$($selected.selected)' after selecting $layout" }
                    $notes.Add("input source $($selected.previous) -> $layout$(if ($selected.enabledHere) { ' (enabled for the check)' })")
                    # The text field focused (a click), its text and the windows before the shortcuts.
                    $text = & $readName "$short-before"
                    if ($null -eq $text) { throw "${short}: Settings did not show the printer name (PrinterName; no-printer-name-$short-before.json)" }
                    $windows = @(Get-AxWindows $c $appId).Count
                    # ] then [ (the focus is on the tab item after the first), and [ then ] from the text box again: tells
                    # a key that never arrives from one that only fails with the focus on a tab.
                    & $focusName
                    $start = Get-AxSelectedTab $c $appId 'SettingsTabs'
                    $next = & $step $layout 'cmd+shift+]' $start 1
                    $null = & $step $layout 'cmd+shift+[' $next -1
                    if ($null -ne (& $readName "$short-middle")) {
                        & $focusName
                        $start = Get-AxSelectedTab $c $appId 'SettingsTabs'
                        $previous = & $step $layout 'cmd+shift+[' $start -1
                        $null = & $step $layout 'cmd+shift+]' $previous 1
                    }
                    if (@(Get-AxWindows $c $appId).Count -ne $windows) { $problems.Add("${short}: the shortcuts changed the windows of the app ($windows -> $(@(Get-AxWindows $c $appId).Count))") }
                    $after = & $readName "$short-after"
                    if ($null -eq $after) { $problems.Add("${short}: the printer name could not be read after the shortcuts (the Printers tab did not come back)") }
                    elseif ($after -cne $text) { $problems.Add("${short}: the shortcuts changed the printer name '$text' to '$after'") }
                    else { $notes.Add("${short}: no new window, the printer name unchanged") }
                    # SIGTERM: the helper selects the previous input source again before it ends.
                    if (-not $source.HasExited) { & /bin/kill -TERM $source.Id; $null = $source.WaitForExit(10000) }
                    $null = Save-VerifyEvidence -Context $c -Name "inputsource-$short.jsonl" -Path $sourceOut
                    $source = $null
                }
                $null = Invoke-Helper $c @('axpress', "$appId", 'SettingsCancel') -AllowFailure
            }
            finally {
                if ($source -and -not $source.HasExited) { & /bin/kill -TERM $source.Id; $null = $source.WaitForExit(10000) }
                $null = Stop-TestApp $data $app
                $null = Save-VerifyEvidence -Context $c -Name 'steps.json' -Content (ConvertTo-VerifyJsonArray $steps)
                if ($sourceOut -and (Test-Path -LiteralPath $sourceOut)) { $null = Save-VerifyEvidence -Context $c -Name 'inputsource-last.jsonl' -Path $sourceOut }
                $current = ((Invoke-Helper $c @('inputsource')) -join "`n" | ConvertFrom-Json).current
                if ($current -cne $before.current) { $problems.Add("the input source is '$current' after the check, before '$($before.current)' (select it again in the menu bar)") }
                else { $notes.Add("input source $current again") }
            }
            if ($problems.Count -gt 0) { return New-VerifyResult FAIL "$($problems.Count) problem(s) (T-100 question 1: a new task; steps.json): $($problems -join '; ')" }
            New-VerifyResult PASS "T-100 question 1, software Czech layout (US as the control): $($notes -join '; ')"
        }
    }

    New-VerifyCheck -Id 'mac.recording-files' -Kind Auto -Title 'files of the automatic print recording (a camera outage, the next print, the end a minute later) and of the dashcam triggers error, attention and stopped; every file decodes (simulator x20 with a real Buddy3D clip)' `
        -Closes 'T-145 (simulátorová část: soubor <datum>_<čas>_<název tisku>.mp4, uzavření asi minutu po konci, další tisk = nový soubor)', 'T-147 (kamera simulátoru se skutečným klipem Buddy3D: oba soubory nahrávky s výpadkem, …mp4 a …-2.mp4, decode OK)', 'T-148 (simulátorová část: spouštěče error / attention / stopped → …_dashcam-<spouštěč>.mp4, decode OK)', 'T-210 kritérium 5 a 6 (nahrávka simulátoru decode OK; nic mimo dočasnou složku, ~/Movies/LayerPeek beze změny)' -Action {
        param($c)
        $clip = Get-Buddy3DClip
        $simulator = Get-Simulator $c -Speed 20 -Clip $clip
        $movies = Get-MoviesRecordingsState
        $problems = [System.Collections.Generic.List[string]]::new()
        $notes = [System.Collections.Generic.List[string]]::new()
        $folderOf = { param($Data) Join-Path $Data.Recordings ((Get-Content -LiteralPath (Join-Path $Data.Data 'settings.json') -Raw | ConvertFrom-Json).printers[0].name) }

        # 1. Automatic recording of a print: a camera outage gives "-2", the next print a new file, the end a minute later.
        Set-SimulatorScenario $simulator 'idle'
        $data = New-TestData -Context $c -Simulator $simulator -Camera $simulator.Camera -CameraOptions ([ordered]@{ recordPrints = $true }) -RecordingFolder 'Recordings'
        $app = Start-TestApp -Context $c -TestData $data
        $printFiles = @()
        try {
            Start-Sleep -Seconds 6
            Set-SimulatorScenario $simulator 'printing'
            $first = Wait-RecordingEvent $data 'Writes' 0 60
            if (-not $first) { $problems.Add('print 1: no recording started within 60 s of printing') }
            else {
                Start-Sleep -Seconds 8
                Set-SimulatorCamera $simulator 'offline'
                Start-Sleep -Seconds 10
                Set-SimulatorCamera $simulator 'online'
                $second = Wait-RecordingEvent $data 'Writes' 1 60
                $expected = "$([System.IO.Path]::GetFileNameWithoutExtension($first.File))-2.mp4"
                if (-not $second) { $problems.Add("print 1: no second file within 60 s after the camera came back (expected $expected)") }
                elseif ($second.File -cne $expected) { $problems.Add("print 1: the file after the outage is '$($second.File)', expected '$expected'") }
                Start-Sleep -Seconds 6
                # The next print within the minute after the end: a new file of its own.
                Set-SimulatorScenario $simulator 'finished'
                Start-Sleep -Seconds 10
                Set-SimulatorScenario $simulator 'printing'
                $third = Wait-RecordingEvent $data 'Writes' 2 60
                if (-not $third) { $problems.Add('print 2: no new file within 60 s of the next print') }
                else {
                    $parts = ConvertFrom-VerifyRecordingFileName $third.File
                    if (-not $parts -or $parts.Segment -ne 1 -or $parts.Stamp -ceq (ConvertFrom-VerifyRecordingFileName $first.File).Stamp) { $problems.Add("print 2: the file '$($third.File)' is not a new recording") }
                    Start-Sleep -Seconds 8
                    # The change of the job ended the first recording already (its two files).
                    $endedBefore = Get-RecordingEventCount $data 'Ended'
                    Set-SimulatorScenario $simulator 'finished'
                    $finished = [System.Diagnostics.Stopwatch]::StartNew()
                    $ended = Wait-RecordingEvent $data 'Ended' $endedBefore 150
                    $seconds = [math]::Round($finished.Elapsed.TotalSeconds)
                    if (-not $ended) { $problems.Add('print 2: the recording did not end within 150 s of the end of the print') }
                    elseif ($seconds -lt 50 -or $seconds -gt 90) { $problems.Add("print 2: the recording ended $seconds s after the end of the print, expected about a minute (50 to 90 s)") }
                    else { $notes.Add("the recording ended $seconds s after the end of the print") }
                }
            }
            foreach ($failure in @(Get-VerifyRecordingEvents (Read-TestLog $data) | Where-Object Kind -CEQ 'Failed')) { $problems.Add("recording: $($failure.Text)") }
        }
        finally { $null = Stop-TestApp $data $app }
        $printFiles = @(Get-RecordingFiles $data.Recordings)
        $names = @($printFiles.Name)
        $null = Save-VerifyEvidence -Context $c -Name 'print-files.txt' -Content $names
        if ($printFiles.Count -ne 3) { $problems.Add("print recordings: $($printFiles.Count) file(s) ($($names -join ', ')), expected 3 (print 1, its -2 after the outage, print 2)") }
        foreach ($file in $printFiles) {
            $parts = ConvertFrom-VerifyRecordingFileName $file.Name
            if (-not $parts -or $parts.Kind -cne 'print' -or -not $parts.Title) { $problems.Add("'$($file.Name)' is not named <date>_<time>_<print title>.mp4") }
            if ($file.Directory.FullName -cne (& $folderOf $data)) { $problems.Add("'$($file.Name)' is not in the printer's folder") }
        }
        $decoded = Test-Recordings $c $printFiles -MinSeconds 2
        foreach ($problem in $decoded.Problems) { $problems.Add($problem) }
        # T-213: print 1 with its outage (…mp4 and …-2.mp4) for mac.file-dialogs, which then records nothing of its own.
        $start = @($printFiles | Where-Object { $first -and $_.Name -ceq $first.File })
        $outage = @($printFiles | Where-Object { $second -and $_.Name -ceq $second.File })
        if ($start.Count -eq 1 -and $outage.Count -eq 1 -and $decoded.Problems.Count -eq 0) { $c.Shared.FileDialogRecording = [pscustomobject]@{ Recording = $start[0]; Continuation = $outage[0] } }

        # 2. The dashcam: each trigger during a print saves the last minutes and 30 s after the event.
        Set-SimulatorScenario $simulator 'idle'
        $dash = New-TestData -Context $c -Simulator $simulator -Camera $simulator.Camera -CameraOptions ([ordered]@{ dashcam = $true }) -RecordingFolder 'Recordings'
        $app = Start-TestApp -Context $c -TestData $dash
        try {
            Start-Sleep -Seconds 6
            foreach ($trigger in 'error', 'attention', 'stopped') {
                Set-SimulatorScenario $simulator 'printing'
                Start-Sleep -Seconds 15
                $before = Get-RecordingEventCount $dash 'DashcamSaved'
                Set-SimulatorScenario $simulator $trigger
                $saved = Wait-RecordingEvent $dash 'DashcamSaved' $before 90
                if (-not $saved) { $problems.Add("dashcam ${trigger}: nothing saved within 90 s"); continue }
                $file = @(Get-RecordingFiles $dash.Recordings | Where-Object { (ConvertFrom-VerifyRecordingFileName $_.Name).Trigger -ceq $trigger })
                if ($file.Count -ne 1) { $problems.Add("dashcam ${trigger}: $($file.Count) file(s) named …_dashcam-$trigger.mp4") }
            }
            foreach ($failure in @(Get-VerifyRecordingEvents (Read-TestLog $dash) | Where-Object Kind -CEQ 'Failed')) { $problems.Add("dashcam: $($failure.Text)") }
        }
        finally { $null = Stop-TestApp $dash $app; Set-SimulatorScenario $simulator 'idle' }
        $dashFiles = @(Get-RecordingFiles $dash.Recordings)
        $null = Save-VerifyEvidence -Context $c -Name 'dashcam-files.txt' -Content @($dashFiles.Name)
        foreach ($file in $dashFiles) {
            $parts = ConvertFrom-VerifyRecordingFileName $file.Name
            if (-not $parts -or $parts.Kind -cne 'dashcam') { $problems.Add("'$($file.Name)' is not a dashcam save") }
        }
        # 15 s of the print before the event (at most the 2 minutes kept) and 30 s after it.
        $dashDecoded = Test-Recordings $c $dashFiles -MinSeconds 25 -MaxSeconds 160
        foreach ($problem in $dashDecoded.Problems) { $problems.Add($problem) }

        # 3. Nothing outside the test data.
        if ((Get-MoviesRecordingsState) -cne $movies) { $problems.Add('~/Movies/LayerPeek changed during the check') }
        if ($problems.Count -gt 0) { return New-VerifyResult FAIL "$($problems.Count) problem(s): $($problems -join '; ')" }
        $source = if ($clip) { 'the real Buddy3D clip of the samples' } else { 'the built-in Buddy3D clip (the 60 s clip of ~/LayerPeek-samples is missing)' }
        New-VerifyResult PASS "camera: $source. Automatic recording: $($names -join ', '); $($notes -join '; '); dashcam: $(@($dashFiles.Name) -join ', '). Every file decoded: $(@($decoded.Notes + $dashDecoded.Notes) -join '; '). Only in the test data, ~/Movies/LayerPeek unchanged."
    }

    New-VerifyCheck -Id 'mac.recording' -Needs Session, Accessibility -Kind Permission `
        -Permission 'Accessibility for the terminal (System Settings > Privacy & Security > Accessibility) to choose the recording items of the menu and read the windows, and an unlocked screen' `
        -Title 'manual recording from the menu bar menu: items, tooltip "Recording", REC and Stop in all three camera window modes, stop from the menu and the window button, Play last recording (the file handed to the system), Open recordings folder (Finder), an unwritable folder (simulator x20)' `
        -Closes 'T-146 (menu, tooltip, ● REC ve všech třech režimech, zastavení z menu i tlačítkem, předání souboru systému, Finder v podsložce tiskárny, nezapisovatelná složka; bez notifikace T-212, „Změnit…“ T-213 a uříznutí T-214)', 'T-210 kritérium 3 (mac.recording)' -Action {
        param($c)
        if (-not (Request-Accessibility $c)) { Skip-VerifyCheck 'no Accessibility permission for the terminal (System Settings > Privacy & Security > Accessibility; it may need a restart of the terminal).' }
        Invoke-SessionCheck $c {
            $texts = Get-VerifyResxStrings (Join-Path $script:Repo 'src' 'LayerPeek.App' 'Resources' 'Strings.resx')
            $label = { param($Key) ConvertFrom-VerifyAccessText $texts[$Key] }
            $simulator = Get-Simulator $c -Speed 20 -Clip (Get-Buddy3DClip)
            Set-SimulatorScenario $simulator 'idle'
            $movies = Get-MoviesRecordingsState
            $problems = [System.Collections.Generic.List[string]]::new()
            $notes = [System.Collections.Generic.List[string]]::new()
            $files = [System.Collections.Generic.List[System.IO.FileInfo]]::new()
            # Paths as the system reports them (/private/var/... for /var/...), without a trailing separator.
            $plain = { param([string] $Path) [System.IO.Path]::TrimEndingDirectorySeparator(($Path -replace '^/private/', '/')) }
            $waitIdle = {
                param($App, $Name)
                Wait-VerifyCondition -TimeoutSeconds 60 -IntervalMilliseconds 1000 -Condition { -not (Get-VerifyToolTipProblem -ToolTip ((Get-StatusToolTipLines $c $App.Id) -join "`n") -Texts $texts -Name $Name -State $texts['StateIdle']) }
            }
            $isRecording = { param($App) (Get-StatusToolTipLines $c $App.Id) -ccontains $texts['ToolTipRecording'] }

            # 1. One recording per camera window mode: started from the menu, REC and Stop in the window, stopped from the
            #    menu (normal window, full screen) or by the window button (mini preview).
            foreach ($mode in 'Normal', 'Mini', 'FullScreen') {
                $data = New-TestData -Context $c -Simulator $simulator -Camera $simulator.Camera -Window ([ordered]@{ rememberLastPlacement = $false; startMode = $mode }) -RecordingFolder 'Recordings'
                $name = (Get-Content -LiteralPath (Join-Path $data.Data 'settings.json') -Raw | ConvertFrom-Json).printers[0].name
                $printer = $texts['MenuPrinter'] -f $name, $texts['StateIdle']
                $printerFolder = Join-Path $data.Recordings $name
                $app = Start-TestApp -Context $c -TestData $data
                try {
                    if (-not (& $waitIdle $app $name)) { $problems.Add("${mode}: the tooltip did not show '$name' ready within 60 s"); continue }
                    if ($mode -eq 'Normal') {
                        $menu = Get-StatusMenu $c $app.Id
                        $null = Save-VerifyEvidence -Context $c -Name 'menu-before.json' -Content ($menu | ConvertTo-Json -Depth 30)
                        foreach ($problem in Get-VerifyMenuProblems -Items @($menu.items) -Texts $texts -Name $name -State $texts['StateIdle'] -Camera) { $problems.Add("menu: $problem") }
                        foreach ($help in Get-VerifyMenuHelps -Items @($menu.items)) { if ($help -clike "*>$(& $label 'MenuPlayLastRecording'):*") { $notes.Add($help) } }
                    }
                    $writes = Get-RecordingEventCount $data 'Writes'
                    $start = Invoke-MenuPress $c $app.Id "$printer>$(& $label 'MenuStartRecording')"
                    if ($start.ExitCode -ne 0) { $problems.Add("${mode}: menupress '$(& $label 'MenuStartRecording')': $($start.Output -join ' ')"); continue }
                    if (-not (Wait-VerifyCondition -TimeoutSeconds 20 -Condition { & $isRecording $app })) { $problems.Add("${mode}: the tooltip did not show '$($texts['ToolTipRecording'])' within 20 s") }
                    if (-not (Wait-RecordingEvent $data 'Writes' $writes 30)) { $problems.Add("${mode}: no recording file within 30 s") }
                    Send-TestCommand $data "camera:$($script:PrinterId)"
                    if (-not (Wait-VerifyCondition -TimeoutSeconds 20 -Condition { $null -ne (Get-CameraWindow $c $app.Id) })) { $problems.Add("${mode}: no camera window within 20 s"); continue }
                    $script:tree = $null
                    $rec = Wait-VerifyCondition -TimeoutSeconds 10 -Condition {
                        $script:tree = Get-AxTree $c $app.Id
                        $indicator = Find-AxElement $script:tree 'CameraRecordingIndicator'
                        $indicator -and (@($indicator.title, $indicator.value, $indicator.description) -ccontains $texts['CameraRecordingIndicator'])
                    }
                    $null = Save-VerifyEvidence -Context $c -Name "$($mode.ToLowerInvariant())-ax.json" -Content ($script:tree | ConvertTo-Json -Depth 100)
                    if (-not $rec) { $problems.Add("${mode}: no '$($texts['CameraRecordingIndicator'])' (CameraRecordingIndicator) in the camera window") }
                    $button = Find-AxElement $script:tree 'CameraRecordButton'
                    if (-not $button -or @($button.title, $button.description, $button.help) -cnotcontains $texts['RecordingStop']) { $problems.Add("${mode}: the record button (CameraRecordButton) is not '$($texts['RecordingStop'])'") }
                    $ended = Get-RecordingEventCount $data 'Ended'
                    # At least 1 s of video for the decode below (a stop right after REC gave 0.92 s, 2026-11-15).
                    Start-Sleep -Seconds 2
                    if ($mode -eq 'Mini') { $null = Invoke-Helper $c @('axpress', "$($app.Id)", 'CameraRecordButton') }
                    else {
                        # Full screen hides the menu bar of its display: the pointer at its top edge shows it, as the user
                        # does (the status item's menu did not open otherwise, first run with an unlocked screen 2026-11-15).
                        $reveal = {
                            if ($mode -ne 'FullScreen') { return }
                            $w = Get-CameraWindow $c $app.Id
                            if ($w) { $null = Invoke-Helper $c @('move', "$($w.x + $w.width / 2)", "$($w.y + 1)"); Start-Sleep -Milliseconds 1200 }
                        }
                        & $reveal
                        $stop = Invoke-MenuPress $c $app.Id "$printer>$(& $label 'MenuStopRecording')"
                        if ($stop.ExitCode -ne 0) { & $reveal; $stop = Invoke-MenuPress $c $app.Id "$printer>$(& $label 'MenuStopRecording')" }
                        if ($stop.ExitCode -ne 0 -and $mode -eq 'FullScreen' -and ($stop.Output -join ' ') -match 'no status item|did not open') {
                            # macOS gives the status items of a hidden full-screen menu bar to no one (not in AXExtrasMenuBar,
                            # 2026-11-15): the window button stops it there, as in the mini preview; the menu is judged in Normal.
                            $notes.Add("FullScreen: the status item is not reachable while the menu bar is hidden ($($stop.Output -join ' ')); stopped by the window button")
                            $null = Invoke-Helper $c @('axpress', "$($app.Id)", 'CameraRecordButton')
                        }
                        elseif ($stop.ExitCode -ne 0) { $problems.Add("${mode}: menupress '$(& $label 'MenuStopRecording')': $($stop.Output -join ' ')") }
                    }
                    if (-not (Wait-RecordingEvent $data 'Ended' $ended 30)) { $problems.Add("${mode}: the recording did not end within 30 s of $(if ($mode -eq 'Mini') { 'the window button' } else { 'the menu item' })") }
                    if (-not (Wait-VerifyCondition -TimeoutSeconds 15 -Condition { -not (& $isRecording $app) })) { $problems.Add("${mode}: the tooltip still shows '$($texts['ToolTipRecording'])'") }

                    if ($mode -eq 'Normal') {
                        # Play last recording: the test mode hands the file to opened-files.txt instead of QuickTime Player.
                        $last = @(Get-RecordingFiles $data.Recordings) | Select-Object -Last 1
                        $opened = Join-Path $data.Data 'opened-files.txt'
                        # The item is enabled once the app knows the finished file; up to 10 s for the menu to show it.
                        $script:playItem = $null
                        $null = Wait-VerifyCondition -TimeoutSeconds 10 -IntervalMilliseconds 1000 -Condition {
                            $menu = Get-StatusMenu $c $app.Id
                            $script:playItem = Find-MenuEntry (@(Get-VerifyMenuEntries $menu.items)[0]).submenu (& $label 'MenuPlayLastRecording')
                            $script:playItem -and $script:playItem.enabled
                        }
                        $null = Save-VerifyEvidence -Context $c -Name 'menu-after-stop.json' -Content ((Get-StatusMenu $c $app.Id) | ConvertTo-Json -Depth 30)
                        $play = Invoke-MenuPress $c $app.Id "$printer>$(& $label 'MenuPlayLastRecording')"
                        if ($play.ExitCode -ne 0) { $problems.Add("menupress '$(& $label 'MenuPlayLastRecording')': $($play.Output -join ' ')") }
                        elseif (-not $last -or -not (Wait-VerifyCondition -TimeoutSeconds 10 -Condition { (Test-Path -LiteralPath $opened) -and (@(Get-Content -LiteralPath $opened) | ForEach-Object { & $plain $_ }) -ccontains (& $plain $last.FullName) })) {
                            $problems.Add("'$(& $label 'MenuPlayLastRecording')' did not hand $(if ($last) { $last.Name } else { 'the recording' }) to the system (opened-files.txt)")
                        }
                        else { $notes.Add("'$(& $label 'MenuPlayLastRecording')' handed $($last.Name) to the system") }
                        # Open recordings folder: a Finder window of the printer's folder, closed again.
                        $finder = @(Get-Process -Name 'Finder' -ErrorAction SilentlyContinue | ForEach-Object Id) | Select-Object -First 1
                        $folderPress = Invoke-MenuPress $c $app.Id "$printer>$(& $label 'MenuOpenRecordingsFolder')"
                        $script:finderWindow = $null
                        if ($folderPress.ExitCode -ne 0) { $problems.Add("menupress '$(& $label 'MenuOpenRecordingsFolder')': $($folderPress.Output -join ' ')") }
                        elseif (-not $finder -or -not (Wait-VerifyCondition -TimeoutSeconds 15 -Condition {
                                    # By AXDocument; Finder of macOS 27 gives its windows none, then by the title (the folder name).
                                    $script:finderWindow = @((Invoke-Helper $c @('axwindows', "$finder")) -join "`n" | ConvertFrom-Json | Where-Object { if ($_.document) { (& $plain ([uri] $_.document).LocalPath) -ceq (& $plain $printerFolder) } else { $_.title -ceq (Split-Path -Leaf $printerFolder) } }) | Select-Object -First 1
                                    $null -ne $script:finderWindow })) {
                            # Evidence: what Finder showed instead (titles and documents of its windows) and the app's log.
                            if ($finder) { $null = Save-VerifyEvidence -Context $c -Name 'finder-windows.json' -Content ((Invoke-Helper $c @('axwindows', "$finder") -AllowFailure) -join "`n") }
                            $problems.Add("'$(& $label 'MenuOpenRecordingsFolder')' showed no Finder window of the printer's folder within 15 s (finder-windows.json; front app $(Get-FrontAppText))")
                        }
                        else {
                            $notes.Add("Finder showed the printer's folder ('$($script:finderWindow.title)')")
                            $close = @(& (Get-Helper $c) 'axclose' "$finder" $printerFolder 2>&1)
                            if ($LASTEXITCODE -ne 0) { $problems.Add("the Finder window did not close: $($close -join ' ')") }
                        }
                    }
                }
                finally {
                    $null = Stop-TestApp $data $app
                    foreach ($file in Get-RecordingFiles $data.Recordings) { $files.Add($file) }
                }
            }

            # 2. A recordings folder without write permission: the start fails without a crash and without a file.
            $locked = New-TestData -Context $c -Simulator $simulator -Camera $simulator.Camera -RecordingFolder 'Locked'
            $null = New-Item -ItemType Directory -Force -Path $locked.Recordings
            & /bin/chmod 0500 $locked.Recordings
            Add-VerifyCleanup $c 'write permission of the locked recordings folder' { param($p) if (Test-Path -LiteralPath $p) { & /bin/chmod 0700 $p } } -State $locked.Recordings
            $name = (Get-Content -LiteralPath (Join-Path $locked.Data 'settings.json') -Raw | ConvertFrom-Json).printers[0].name
            $app = Start-TestApp -Context $c -TestData $locked
            try {
                if (-not (& $waitIdle $app $name)) { $problems.Add("locked folder: the tooltip did not show '$name' ready within 60 s") }
                else {
                    $start = Invoke-MenuPress $c $app.Id "$($texts['MenuPrinter'] -f $name, $texts['StateIdle'])>$(& $label 'MenuStartRecording')"
                    $failure = if ($start.ExitCode -eq 0) { Wait-RecordingEvent $locked 'Failed' 0 20 }
                    Start-Sleep -Seconds 3
                    if ($start.ExitCode -ne 0) { $problems.Add("locked folder: menupress '$(& $label 'MenuStartRecording')': $($start.Output -join ' ')") }
                    elseif (-not $failure) { $problems.Add('locked folder: no failure of the recording in the log within 20 s') }
                    elseif ($failure.Text -notmatch 'FolderUnavailable') { $problems.Add("locked folder: the recording failed for another reason: $($failure.Text)") }
                    if ($app.HasExited) { $problems.Add("locked folder: the app ended with $($app.ExitCode)") }
                    elseif (& $isRecording $app) { $problems.Add("locked folder: the tooltip shows '$($texts['ToolTipRecording'])'") }
                    if (@(Get-ChildItem -LiteralPath $locked.Data -Recurse -File -Filter '*.mp4').Count -gt 0) { $problems.Add('locked folder: a recording file was written') }
                    # What Settings > Recording shows (open question 3 of T-210: the message appears after "Change…", T-213).
                    if (-not $app.HasExited) {
                        Send-TestCommand $locked 'settings'
                        $null = Select-SettingsTab $c $app.Id 'SettingsTabRecording'
                        Start-Sleep -Seconds 1
                        $tree = Get-AxTree $c $app.Id
                        $null = Save-VerifyEvidence -Context $c -Name 'locked-settings-ax.json' -Content ($tree | ConvertTo-Json -Depth 100)
                        $shown = Find-AxElement $tree 'SettingsRecordingFolderError'
                        $notes.Add("locked folder: the start failed ($(if ($failure) { $failure.Text -replace '^.*could not start ', '' }))$(if ($shown) { ", Settings > Recording shows '$(@($shown.value, $shown.title, $shown.description) | Where-Object { $_ } | Select-Object -First 1)'" } else { ', Settings > Recording shows no message (T-210 question 3)' })")
                    }
                }
            }
            finally { $null = Stop-TestApp $locked $app }

            # 3. Every recording decodes; nothing outside the test data.
            $null = Save-VerifyEvidence -Context $c -Name 'files.txt' -Content @($files.Name)
            if ($files.Count -ne 3) { $problems.Add("$($files.Count) recording(s) ($(@($files.Name) -join ', ')), expected 3 (one per window mode)") }
            $decoded = Test-Recordings $c $files.ToArray() -MinSeconds 1
            foreach ($problem in $decoded.Problems) { $problems.Add($problem) }
            if ((Get-MoviesRecordingsState) -cne $movies) { $problems.Add('~/Movies/LayerPeek changed during the check') }
            if ($problems.Count -gt 0) { return New-VerifyResult FAIL "$($problems.Count) problem(s): $($problems -join '; ')" }
            New-VerifyResult PASS "menu items before the first recording (menu-before.json), '$($texts['ToolTipRecording'])' in the tooltip and '$($texts['CameraRecordingIndicator'])' with '$($texts['RecordingStop'])' in the normal window, the mini preview and full screen, stopped from the menu and by the window button; $($notes -join '; '); decoded: $($decoded.Notes -join '; ')."
        }
    }

    New-VerifyCheck -Id 'mac.dashcam' -Needs Session, Accessibility -Kind Permission `
        -Permission 'Accessibility for the terminal (System Settings > Privacy & Security > Accessibility) to choose the dashcam item of the menu and read the context menu of the camera window, and an unlocked screen' `
        -Title '"Save the last 2 min" in the menu bar menu and the context menu of the camera window (disabled out of a print with its tip, enabled during a print), a manual save, and the item during an automatic recording (simulator x20)' `
        -Closes 'T-148 (simulátorová část: položka v menu a v místní nabídce okna kamery, …_dashcam-manual.mp4 decode OK; spouštěče v mac.recording-files)', 'T-145 (simulátorová část: během automatického nahrávání je „Uložit posledních 2 min“ zakázané s tipem „Nahrává se celý tisk.“)', 'T-210 kritérium 4 (mac.dashcam spolu s mac.recording-files)' -Action {
        param($c)
        if (-not (Request-Accessibility $c)) { Skip-VerifyCheck 'no Accessibility permission for the terminal (System Settings > Privacy & Security > Accessibility; it may need a restart of the terminal).' }
        Invoke-SessionCheck $c {
            $texts = Get-VerifyResxStrings (Join-Path $script:Repo 'src' 'LayerPeek.App' 'Resources' 'Strings.resx')
            $label = { param($Key) ConvertFrom-VerifyAccessText $texts[$Key] }
            $save = ConvertFrom-VerifyAccessText ($texts['MenuSaveDashcam'] -f 2)
            $simulator = Get-Simulator $c -Speed 20 -Clip (Get-Buddy3DClip)
            Set-SimulatorScenario $simulator 'idle'
            $movies = Get-MoviesRecordingsState
            $problems = [System.Collections.Generic.List[string]]::new()
            $notes = [System.Collections.Generic.List[string]]::new()
            $tipOf = { param($Item) if ($Item.help) { "'$($Item.help)'" } else { 'none' } }
            # The menu item: present, enabled as expected; its tip (AXHelp) is noted, a wrong one is a problem.
            $judge = {
                param($Where, $Item, [bool] $Enabled, [string] $Tip)
                if (-not $Item) { $problems.Add("${Where}: no '$save'"); return }
                if ([bool] $Item.enabled -ne $Enabled) { $problems.Add("${Where}: '$save' is $(if ($Item.enabled) { 'enabled' } else { 'disabled' }), expected $(if ($Enabled) { 'enabled' } else { 'disabled' })") }
                if ($Tip -and $Item.help -and $Item.help -cne $Tip) { $problems.Add("${Where}: the tip of '$save' is '$($Item.help)', expected '$Tip'") }
                if (-not $Enabled) { $notes.Add("${Where}: disabled, tip $(& $tipOf $Item)") }
            }
            $menuItem = {
                param($App, $Where)
                $menu = Get-StatusMenu $c $App.Id
                $null = Save-VerifyEvidence -Context $c -Name "menu-$Where.json" -Content ($menu | ConvertTo-Json -Depth 30)
                $printer = @(Get-VerifyMenuEntries $menu.items)[0]
                [pscustomobject]@{ Header = $printer.title; Item = Find-MenuEntry $printer.submenu $save; Submenu = @($printer.submenu) }
            }
            $contextItem = {
                param($Data, $App, $Where)
                Send-TestCommand $Data 'activate'
                $null = Wait-VerifyCondition -TimeoutSeconds 10 -Condition { (Get-FrontProcessId) -eq $App.Id }
                Start-Sleep -Milliseconds 400
                $point = Get-AxCenter (Get-AxElement $c $App.Id 'Surface')
                $null = Invoke-Helper $c @('ctrlclick', "$($point[0])", "$($point[1])")
                $script:tree = $null
                $null = Wait-VerifyCondition -TimeoutSeconds 5 -Condition { $script:tree = Get-AxTree $c $App.Id; $null -ne (Find-AxElement $script:tree 'CameraSaveDashcamMenuItem') }
                $null = Save-VerifyEvidence -Context $c -Name "context-menu-$Where.json" -Content ($script:tree | ConvertTo-Json -Depth 100)
                $item = Find-AxElement $script:tree 'CameraSaveDashcamMenuItem'
                $null = Invoke-Helper $c @('key', 'escape')
                Start-Sleep -Milliseconds 500
                $item
            }
            $waitState = {
                param($App, $Name, $Key, [switch] $Progress)
                Wait-VerifyCondition -TimeoutSeconds 60 -IntervalMilliseconds 1000 -Condition { -not (Get-VerifyToolTipProblem -ToolTip ((Get-StatusToolTipLines $c $App.Id) -join "`n") -Texts $texts -Name $Name -State $texts[$Key] -Progress:$Progress) }
            }

            # 1. Dashcam on, no automatic recording: disabled out of a print, enabled during it, a manual save.
            $data = New-TestData -Context $c -Simulator $simulator -Camera $simulator.Camera -CameraOptions ([ordered]@{ dashcam = $true }) -Window ([ordered]@{ rememberLastPlacement = $false; startMode = 'Normal' }) -RecordingFolder 'Recordings'
            $name = (Get-Content -LiteralPath (Join-Path $data.Data 'settings.json') -Raw | ConvertFrom-Json).printers[0].name
            $app = Start-TestApp -Context $c -TestData $data
            $manual = @()
            try {
                if (-not (& $waitState $app $name 'StateIdle')) { return New-VerifyResult FAIL "the tooltip did not show '$name' ready within 60 s." }
                & $judge 'menu, idle' (& $menuItem $app 'idle').Item $false $texts['DashcamNotPrinting']
                Send-TestCommand $data "camera:$($script:PrinterId)"
                if (-not (Wait-VerifyCondition -TimeoutSeconds 20 -Condition { $null -ne (Get-CameraWindow $c $app.Id) })) { $problems.Add('no camera window within 20 s') }
                else { & $judge 'context menu, idle' (& $contextItem $data $app 'idle') $false $texts['DashcamNotPrinting'] }

                Set-SimulatorScenario $simulator 'printing'
                if (-not (& $waitState $app $name 'StatePrinting' -Progress)) { $problems.Add('the tooltip did not show the print within 60 s') }
                # The dashcam fills during the print.
                Start-Sleep -Seconds 15
                $during = & $menuItem $app 'printing'
                & $judge 'menu, printing' $during.Item $true ''
                if (Get-CameraWindow $c $app.Id) { & $judge 'context menu, printing' (& $contextItem $data $app 'printing') $true '' }
                $before = Get-RecordingEventCount $data 'DashcamSaved'
                $press = Invoke-MenuPress $c $app.Id "$($during.Header)>$save"
                # The progress in the printer's title may have moved on since the menu was read.
                if ($press.ExitCode -ne 0) { $press = Invoke-MenuPress $c $app.Id "$((& $menuItem $app 'printing-again').Header)>$save" }
                if ($press.ExitCode -ne 0) { $problems.Add("menupress '$save': $($press.Output -join ' ')") }
                elseif (-not (Wait-RecordingEvent $data 'DashcamSaved' $before 90)) { $problems.Add("'$save' saved nothing within 90 s") }
            }
            finally {
                $null = Stop-TestApp $data $app
                Set-SimulatorScenario $simulator 'idle'
            }
            $manual = @(Get-RecordingFiles $data.Recordings)
            $null = Save-VerifyEvidence -Context $c -Name 'files.txt' -Content @($manual.Name)
            if ($manual.Count -ne 1 -or (ConvertFrom-VerifyRecordingFileName $manual[0].Name).Trigger -cne 'manual') { $problems.Add("manual save: files $(@($manual.Name) -join ', '), expected one …_dashcam-manual.mp4") }
            # 15 s of the print before the click and 30 s after it.
            $decoded = Test-Recordings $c $manual -MinSeconds 25 -MaxSeconds 160
            foreach ($problem in $decoded.Problems) { $problems.Add($problem) }

            # 2. Automatic recording and dashcam: the whole print is recorded, the item is disabled with its tip.
            $both = New-TestData -Context $c -Simulator $simulator -Camera $simulator.Camera -CameraOptions ([ordered]@{ recordPrints = $true; dashcam = $true }) -RecordingFolder 'Recordings'
            $bothName = (Get-Content -LiteralPath (Join-Path $both.Data 'settings.json') -Raw | ConvertFrom-Json).printers[0].name
            $app = Start-TestApp -Context $c -TestData $both
            try {
                if (-not (& $waitState $app $bothName 'StateIdle')) { $problems.Add("automatic recording: the tooltip did not show '$bothName' ready within 60 s") }
                else {
                    Set-SimulatorScenario $simulator 'printing'
                    if (-not (Wait-VerifyCondition -TimeoutSeconds 60 -IntervalMilliseconds 1000 -Condition { (Get-StatusToolTipLines $c $app.Id) -ccontains $texts['ToolTipRecording'] })) { $problems.Add("automatic recording: the tooltip did not show '$($texts['ToolTipRecording'])' within 60 s") }
                    $recorded = & $menuItem $app 'automatic'
                    & $judge 'menu, automatic recording' $recorded.Item $false $texts['DashcamWholePrintRecorded']
                    if (-not (Find-MenuEntry $recorded.Submenu (& $label 'MenuStopRecording'))) { $problems.Add("automatic recording: no '$(& $label 'MenuStopRecording')' in the submenu") }
                }
            }
            finally {
                $null = Stop-TestApp $both $app
                Set-SimulatorScenario $simulator 'idle'
            }
            if ((Get-MoviesRecordingsState) -cne $movies) { $problems.Add('~/Movies/LayerPeek changed during the check') }
            if ($problems.Count -gt 0) { return New-VerifyResult FAIL "$($problems.Count) problem(s): $($problems -join '; ')" }
            New-VerifyResult PASS "'$save' $($notes -join '; '); enabled during the print in both menus; the manual save gave $(@($manual.Name) -join ', ') ($($decoded.Notes -join '; '))."
        }
    }

    New-VerifyCheck -Id 'mac.timelapse-files' -Kind Auto -Title 'files of the automatic timelapse of a print by time (a pause of 30 s takes no frame) and by layer (a print of 12 layers of 0.2 mm with a pause: the start, 11 layer changes and the finished print); every file decodes (simulator x20 with a real Buddy3D clip)' `
        -Closes 'T-160 (přehrání …_timelapse.mp4 nahrazené helper decode: každý snímek, seek, počet snímků podle intervalu a doby tisku, pauza bez snímku; kamera simulátoru se skutečným klipem Buddy3D)', 'T-159 (simulátorová část: tisk s osou Z po vrstvách, počet snímků = začátek + změny vrstvy + hotový díl, pauza snímek nepřidá)', 'T-211 kritérium 3 a část kritérií 1 a 2 (soubory a decode bez menu)' -Action {
        param($c)
        $clip = Get-Buddy3DClip
        $simulator = Get-Simulator $c -Speed 20 -Clip $clip -Layers $script:TimelapseLayers
        $movies = Get-MoviesRecordingsState
        $problems = [System.Collections.Generic.List[string]]::new()
        $notes = [System.Collections.Generic.List[string]]::new()

        # 1. By time: the timelapse of a print with a pause of 30 s in it.
        Set-SimulatorScenario $simulator 'idle'
        $data = New-TestData -Context $c -Simulator $simulator -Camera $simulator.Camera -CameraOptions ([ordered]@{ timelapsePrints = $true }) -RecordingFolder 'Recordings' -RecordingOptions ([ordered]@{ timelapseIntervalSeconds = $script:TimelapseInterval }) -PollingSeconds 1
        $app = Start-TestApp -Context $c -TestData $data
        $spans = [System.Collections.Generic.List[double]]::new()
        try {
            Start-Sleep -Seconds 4
            Set-SimulatorScenario $simulator 'printing'
            $started = Wait-RecordingEvent $data 'TimelapseStarted' 0 60
            if (-not $started) { $problems.Add('by time: no timelapse started within 60 s of the print') }
            else {
                $capturing = [System.Diagnostics.Stopwatch]::StartNew()
                if ($started.Mode -cne 'Automatic') { $problems.Add("by time: the timelapse started as $($started.Mode), expected Automatic") }
                Start-Sleep -Seconds 30
                Set-SimulatorScenario $simulator 'paused'
                $spans.Add($capturing.Elapsed.TotalSeconds)
                Start-Sleep -Seconds 30
                Set-SimulatorScenario $simulator 'printing'
                $capturing.Restart()
                Start-Sleep -Seconds 20
                $spans.Add($capturing.Elapsed.TotalSeconds)
            }
            foreach ($failure in @(Get-VerifyRecordingEvents (Read-TestLog $data) | Where-Object Kind -CEQ 'TimelapseFailed')) { $problems.Add("by time: $($failure.Text)") }
        }
        # Quitting completes the file of the running timelapse.
        finally { $null = Stop-TestApp $data $app; Set-SimulatorScenario $simulator 'idle' }
        $byTime = @(Get-TimelapseFiles $data.Recordings)
        if ($byTime.Count -ne 1) { $problems.Add("by time: $($byTime.Count) timelapse file(s) ($(@($byTime.Name) -join ', ')), expected one …_timelapse.mp4") }
        $range = if ($spans.Count -eq 2) { Get-VerifyTimelapseFrameRange -Spans $spans.ToArray() -IntervalSeconds $script:TimelapseInterval -SlackSeconds $script:TimelapseSlack }
        foreach ($file in $byTime) {
            $decoded = Test-Timelapse $c $file -Range $range
            foreach ($problem in $decoded.Problems) { $problems.Add("by time: $problem") }
            # T-213: the timelapse for mac.file-dialogs, which then records nothing of its own.
            if ($byTime.Count -eq 1 -and $decoded.Problems.Count -eq 0) { $c.Shared.FileDialogTimelapse = $file }
            if ($decoded.Note) { $notes.Add("by time ($([math]::Round($spans[0])) s, a pause of 30 s, $([math]::Round($spans[1])) s, every $($script:TimelapseInterval) s): $($decoded.Note)") }
        }

        # 2. By layer: a whole print of 12 layers with a pause of 15 s; the start, every layer change and the end.
        $layers = New-TestData -Context $c -Simulator $simulator -Camera $simulator.Camera -CameraOptions ([ordered]@{ timelapsePrints = $true }) -RecordingFolder 'Recordings' -RecordingOptions ([ordered]@{ timelapseTrigger = 'layer' }) -PollingSeconds 1
        $app = Start-TestApp -Context $c -TestData $layers
        try {
            Start-Sleep -Seconds 4
            Set-SimulatorScenario $simulator 'printing'
            if (-not (Wait-RecordingEvent $layers 'TimelapseStarted' 0 60)) { $problems.Add('by layer: no timelapse started within 60 s of the print') }
            else {
                Start-Sleep -Seconds 40
                Set-SimulatorScenario $simulator 'paused'
                Start-Sleep -Seconds 15
                Set-SimulatorScenario $simulator 'printing'
                # The rest of the print (90 s at x20 in all) and the frame of the finished part (its next key frame).
                if (-not (Wait-VerifyCondition -TimeoutSeconds 120 -IntervalMilliseconds 1000 -Condition { (Get-SimulatorPrinterState $simulator) -ceq 'FINISHED' })) { $problems.Add('by layer: the simulated print did not finish within 120 s') }
                Start-Sleep -Seconds 8
            }
            foreach ($failure in @(Get-VerifyRecordingEvents (Read-TestLog $layers) | Where-Object Kind -CEQ 'TimelapseFailed')) { $problems.Add("by layer: $($failure.Text)") }
        }
        finally { $null = Stop-TestApp $layers $app; Set-SimulatorScenario $simulator 'idle' }
        $byLayer = @(Get-TimelapseFiles $layers.Recordings)
        if ($byLayer.Count -ne 1) { $problems.Add("by layer: $($byLayer.Count) timelapse file(s) ($(@($byLayer.Name) -join ', ')), expected one …_timelapse.mp4") }
        # A staircase of the simulator has no short layer and no z-hop (the tolerance of T-159 is for real prints): exact.
        $expected = Get-VerifyLayerTimelapseFrames -Layers $script:TimelapseLayers
        foreach ($file in $byLayer) {
            $decoded = Test-Timelapse $c $file -Range ([pscustomobject]@{ Min = $expected; Max = $expected })
            foreach ($problem in $decoded.Problems) { $problems.Add("by layer: $problem") }
            if ($decoded.Note) { $notes.Add("by layer ($($script:TimelapseLayers) layers, a pause of 15 s): $($decoded.Note)") }
        }

        if ((Get-MoviesRecordingsState) -cne $movies) { $problems.Add('~/Movies/LayerPeek changed during the check') }
        if ($problems.Count -gt 0) { return New-VerifyResult FAIL "$($problems.Count) problem(s): $($problems -join '; ')" }
        $source = if ($clip) { 'the real Buddy3D clip of the samples' } else { 'the built-in Buddy3D clip (the 60 s clip of ~/LayerPeek-samples is missing)' }
        New-VerifyResult PASS "camera: $source. $($notes -join '; '). Only in the test data, ~/Movies/LayerPeek unchanged."
    }

    New-VerifyCheck -Id 'mac.timelapse' -Needs Session, Accessibility -Kind Permission `
        -Permission 'Accessibility for the terminal (System Settings > Privacy & Security > Accessibility) to choose the timelapse items of the menu and read and press the timelapse button of the camera window, and an unlocked screen' `
        -Title 'manual timelapse from the menu bar menu: Start / Stop items, "TL" in the normal window and the mini preview, dimmed during a pause (accessible name), stopped by the window button (AXPress) and from the menu, Play last timelapse (the file handed to the system), every file decodes with the frames of its interval (simulator x20)' `
        -Closes 'T-161 (ruční start / stop z menu i z okna kamery, „TL“ v okně i v malém náhledu, ztlumení během pauzy přes AX, „Přehrát poslední timelapse“ = předání souboru systému, tip zakázané položky; bez notifikace T-212 a uříznutí T-214)', 'T-211 kritérium 1 (mac.timelapse)' -Action {
        param($c)
        if (-not (Request-Accessibility $c)) { Skip-VerifyCheck 'no Accessibility permission for the terminal (System Settings > Privacy & Security > Accessibility; it may need a restart of the terminal).' }
        Invoke-SessionCheck $c {
            $texts = Get-VerifyResxStrings (Join-Path $script:Repo 'src' 'LayerPeek.App' 'Resources' 'Strings.resx')
            $label = { param($Key) ConvertFrom-VerifyAccessText $texts[$Key] }
            $simulator = Get-Simulator $c -Speed 20 -Clip (Get-Buddy3DClip) -Layers $script:TimelapseLayers
            $movies = Get-MoviesRecordingsState
            $problems = [System.Collections.Generic.List[string]]::new()
            $notes = [System.Collections.Generic.List[string]]::new()
            # Paths as the system reports them (/private/var/... for /var/...), without a trailing separator.
            $plain = { param([string] $Path) [System.IO.Path]::TrimEndingDirectorySeparator(($Path -replace '^/private/', '/')) }
            # An accessible name of two lines (the tip during a pause) compared with its whitespace folded.
            $fold = { param([string] $Text) ($Text -replace '\s+', ' ').Trim() }
            $named = { param($Node, [string] $Expected) $Node -and @(@($Node.title, $Node.description, $Node.help) | Where-Object { $_ -and (& $fold $_) -ceq (& $fold $Expected) }).Count -gt 0 }
            $waitState = {
                param($App, $Name, $Key, [switch] $Progress)
                Wait-VerifyCondition -TimeoutSeconds 60 -IntervalMilliseconds 1000 -Condition { -not (Get-VerifyToolTipProblem -ToolTip ((Get-StatusToolTipLines $c $App.Id) -join "`n") -Texts $texts -Name $Name -State $texts[$Key] -Progress:$Progress) }
            }
            $printerMenu = {
                param($App, [string] $Evidence)
                $menu = Get-StatusMenu $c $App.Id
                if ($Evidence) { $null = Save-VerifyEvidence -Context $c -Name "menu-$Evidence.json" -Content ($menu | ConvertTo-Json -Depth 30) }
                $printer = @(Get-VerifyMenuEntries $menu.items)[0]
                [pscustomobject]@{ Header = $printer.title; Submenu = @($printer.submenu) }
            }
            $press = {
                param($App, [string] $Key)
                $item = & $label $Key
                $result = Invoke-MenuPress $c $App.Id "$((& $printerMenu $App).Header)>$item"
                # The progress in the printer's title may have moved on since the menu was read.
                if ($result.ExitCode -ne 0) { $result = Invoke-MenuPress $c $App.Id "$((& $printerMenu $App).Header)>$item" }
                if ($result.ExitCode -ne 0) { $problems.Add("menupress '$item': $($result.Output -join ' ')") }
                $result.ExitCode -eq 0
            }
            $showsIndicator = {
                param($Tree)
                $indicator = Find-AxElement $Tree 'CameraTimelapseIndicator'
                $indicator -and (@($indicator.title, $indicator.value, $indicator.description) -ccontains $texts['CameraTimelapseIndicator'])
            }

            # One manual timelapse per window mode: started from the menu, "TL" in the window; the normal window also pauses,
            # stops by the window button and plays the file; the mini preview stops from the menu.
            foreach ($mode in 'Normal', 'Mini') {
                $where = $mode.ToLowerInvariant()
                Set-SimulatorScenario $simulator 'idle'
                $data = New-TestData -Context $c -Simulator $simulator -Camera $simulator.Camera -Window ([ordered]@{ rememberLastPlacement = $false; startMode = $mode }) -RecordingFolder 'Recordings' -RecordingOptions ([ordered]@{ timelapseIntervalSeconds = $script:TimelapseInterval }) -PollingSeconds 1
                $name = (Get-Content -LiteralPath (Join-Path $data.Data 'settings.json') -Raw | ConvertFrom-Json).printers[0].name
                $spans = [System.Collections.Generic.List[double]]::new()
                $app = Start-TestApp -Context $c -TestData $data
                try {
                    if (-not (& $waitState $app $name 'StateIdle')) { $problems.Add("${mode}: the tooltip did not show '$name' ready within 60 s"); continue }
                    if ($mode -eq 'Normal') {
                        # Before the first timelapse: Start, and Play last timelapse disabled with its tip (noted).
                        $idle = & $printerMenu $app 'idle'
                        if (-not (Find-MenuEntry $idle.Submenu (& $label 'MenuStartTimelapse'))) { $problems.Add("idle: no '$(& $label 'MenuStartTimelapse')' in the submenu") }
                        $play = Find-MenuEntry $idle.Submenu (& $label 'MenuPlayLastTimelapse')
                        if (-not $play) { $problems.Add("idle: no '$(& $label 'MenuPlayLastTimelapse')' in the submenu") }
                        elseif ($play.enabled) { $problems.Add("idle: '$(& $label 'MenuPlayLastTimelapse')' is enabled without a timelapse") }
                        else {
                            $notes.Add("'$(& $label 'MenuPlayLastTimelapse')' disabled before the first timelapse, tip $(if ($play.help) { "'$($play.help)'" } else { 'none (AXHelp empty: the tip does not reach the NSMenu)' })")
                            if ($play.help -and $play.help -cne $texts['MenuNoTimelapseYet']) { $problems.Add("idle: the tip of '$(& $label 'MenuPlayLastTimelapse')' is '$($play.help)', expected '$($texts['MenuNoTimelapseYet'])'") }
                        }
                    }
                    Set-SimulatorScenario $simulator 'printing'
                    if (-not (& $waitState $app $name 'StatePrinting' -Progress)) { $problems.Add("${mode}: the tooltip did not show the print within 60 s") }
                    $before = Get-RecordingEventCount $data 'TimelapseStarted'
                    if (-not (& $press $app 'MenuStartTimelapse')) { continue }
                    $started = Wait-RecordingEvent $data 'TimelapseStarted' $before 30
                    if (-not $started) { $problems.Add("${mode}: no timelapse started within 30 s of '$(& $label 'MenuStartTimelapse')'"); continue }
                    $capturing = [System.Diagnostics.Stopwatch]::StartNew()
                    if ($started.Mode -cne 'Manual') { $problems.Add("${mode}: the timelapse started as $($started.Mode), expected Manual") }
                    if (-not (Find-MenuEntry (& $printerMenu $app "$where-running").Submenu (& $label 'MenuStopTimelapse'))) { $problems.Add("${mode}: no '$(& $label 'MenuStopTimelapse')' in the submenu while the timelapse runs") }
                    Send-TestCommand $data "camera:$($script:PrinterId)"
                    if (-not (Wait-VerifyCondition -TimeoutSeconds 20 -Condition { $null -ne (Get-CameraWindow $c $app.Id) })) { $problems.Add("${mode}: no camera window within 20 s"); continue }
                    $script:timelapseTree = $null
                    $indicator = Wait-VerifyCondition -TimeoutSeconds 10 -Condition { $script:timelapseTree = Get-AxTree $c $app.Id; & $showsIndicator $script:timelapseTree }
                    $null = Save-VerifyEvidence -Context $c -Name "$where-ax.json" -Content ($script:timelapseTree | ConvertTo-Json -Depth 100)
                    if (-not $indicator) { $problems.Add("${mode}: no '$($texts['CameraTimelapseIndicator'])' (CameraTimelapseIndicator) in the camera window") }
                    if (-not (& $named (Find-AxElement $script:timelapseTree 'CameraTimelapseButton') $texts['TimelapseStop'])) { $problems.Add("${mode}: the timelapse button (CameraTimelapseButton) is not '$($texts['TimelapseStop'])'") }

                    if ($mode -eq 'Normal') {
                        # A pause: no frames; the button is dimmed and its accessible name says why, so does the menu item.
                        Start-Sleep -Seconds 10
                        Set-SimulatorScenario $simulator 'paused'
                        $spans.Add($capturing.Elapsed.TotalSeconds)
                        $pausedName = "$($texts['TimelapseStop'])`n$($texts['TimelapsePaused'])"
                        $dimmed = Wait-VerifyCondition -TimeoutSeconds 15 -Condition { $script:timelapseTree = Get-AxTree $c $app.Id; & $named (Find-AxElement $script:timelapseTree 'CameraTimelapseButton') $pausedName }
                        $null = Save-VerifyEvidence -Context $c -Name 'paused-ax.json' -Content ($script:timelapseTree | ConvertTo-Json -Depth 100)
                        if (-not $dimmed) { $problems.Add("pause: the timelapse button is not '$(& $fold $pausedName)' within 15 s") }
                        else {
                            $stop = Find-MenuEntry (& $printerMenu $app 'paused').Submenu (& $label 'MenuStopTimelapse')
                            $notes.Add("during the pause the button reads '$(& $fold $pausedName)', the menu item tip $(if ($stop -and $stop.help) { "'$($stop.help)'" } else { 'none' })")
                            if ($stop -and $stop.help -and $stop.help -cne $texts['TimelapsePaused']) { $problems.Add("pause: the tip of '$(& $label 'MenuStopTimelapse')' is '$($stop.help)', expected '$($texts['TimelapsePaused'])'") }
                        }
                        Start-Sleep -Seconds 15
                        Set-SimulatorScenario $simulator 'printing'
                        $capturing.Restart()
                        if (-not (Wait-VerifyCondition -TimeoutSeconds 15 -Condition { & $named (Find-AxElement (Get-AxTree $c $app.Id) 'CameraTimelapseButton') $texts['TimelapseStop'] })) { $problems.Add('pause: the timelapse button is still dimmed 15 s after the print resumed') }
                        Start-Sleep -Seconds 10
                        $null = Invoke-Helper $c @('axpress', "$($app.Id)", 'CameraTimelapseButton')
                    }
                    else {
                        Start-Sleep -Seconds 15
                        $null = & $press $app 'MenuStopTimelapse'
                    }
                    $spans.Add($capturing.Elapsed.TotalSeconds)
                    $how = if ($mode -eq 'Normal') { 'the window button' } else { 'the menu item' }
                    if (-not (Wait-VerifyCondition -TimeoutSeconds 30 -Condition { -not (& $showsIndicator (Get-AxTree $c $app.Id)) })) { $problems.Add("${mode}: '$($texts['CameraTimelapseIndicator'])' still shows 30 s after the stop by $how") }
                    $after = & $printerMenu $app "$where-stopped"
                    if (-not (Find-MenuEntry $after.Submenu (& $label 'MenuStartTimelapse'))) { $problems.Add("${mode}: no '$(& $label 'MenuStartTimelapse')' in the submenu after the stop by $how") }

                    if ($mode -eq 'Normal') {
                        $button = Find-AxElement (Get-AxTree $c $app.Id) 'CameraTimelapseButton'
                        if ($button -and -not (& $named $button $texts['TimelapseStart'])) { $problems.Add("the timelapse button is not '$($texts['TimelapseStart'])' after the stop") }
                        # Play last timelapse: the test mode hands the file to opened-files.txt instead of QuickTime Player.
                        $last = @(Get-TimelapseFiles $data.Recordings) | Select-Object -Last 1
                        $play = Find-MenuEntry $after.Submenu (& $label 'MenuPlayLastTimelapse')
                        $opened = Join-Path $data.Data 'opened-files.txt'
                        if (-not $play -or -not $play.enabled) { $problems.Add("'$(& $label 'MenuPlayLastTimelapse')' is not enabled after the timelapse") }
                        elseif (& $press $app 'MenuPlayLastTimelapse') {
                            if (-not $last -or -not (Wait-VerifyCondition -TimeoutSeconds 10 -Condition { (Test-Path -LiteralPath $opened) -and (@(Get-Content -LiteralPath $opened) | ForEach-Object { & $plain $_ }) -ccontains (& $plain $last.FullName) })) {
                                $problems.Add("'$(& $label 'MenuPlayLastTimelapse')' did not hand $(if ($last) { $last.Name } else { 'the timelapse' }) to the system (opened-files.txt)")
                            }
                            else { $notes.Add("'$(& $label 'MenuPlayLastTimelapse')' handed $($last.Name) to the system") }
                        }
                    }
                    foreach ($failure in @(Get-VerifyRecordingEvents (Read-TestLog $data) | Where-Object Kind -CEQ 'TimelapseFailed')) { $problems.Add("${mode}: $($failure.Text)") }
                }
                finally { $null = Stop-TestApp $data $app; Set-SimulatorScenario $simulator 'idle' }
                $files = @(Get-TimelapseFiles $data.Recordings)
                if ($files.Count -ne 1) { $problems.Add("${mode}: $($files.Count) timelapse file(s) ($(@($files.Name) -join ', ')), expected one …_timelapse.mp4"); continue }
                $range = if ($spans.Count -gt 0) { Get-VerifyTimelapseFrameRange -Spans $spans.ToArray() -IntervalSeconds $script:TimelapseInterval -SlackSeconds $script:TimelapseSlack }
                $decoded = Test-Timelapse $c $files[0] -Range $range
                foreach ($problem in $decoded.Problems) { $problems.Add("${mode}: $problem") }
                if ($decoded.Note) { $notes.Add("${mode}: $($decoded.Note)") }
            }
            if ((Get-MoviesRecordingsState) -cne $movies) { $problems.Add('~/Movies/LayerPeek changed during the check') }
            if ($problems.Count -gt 0) { return New-VerifyResult FAIL "$($problems.Count) problem(s): $($problems -join '; ')" }
            New-VerifyResult PASS "'$(& $label 'MenuStartTimelapse')' -> '$(& $label 'MenuStopTimelapse')', '$($texts['CameraTimelapseIndicator'])' and '$($texts['TimelapseStop'])' in the normal window and the mini preview, stopped by the window button and from the menu; $($notes -join '; ')."
        }
    }

    New-VerifyCheck -Id 'mac.timelapse-layers' -Needs Session, Accessibility -Kind Permission `
        -Permission 'Accessibility for the terminal (System Settings > Privacy & Security > Accessibility) to choose the timelapse trigger in Settings and read its fields, and an unlocked screen' `
        -Title 'Settings > Recording: "Every N-th layer (estimate)" swaps the number next to it (interval <-> "Layers between frames", accessible names), its tip explains the estimate from the Z axis (AXHelp), saved in settings.json and shown again after reopening (the frames of such a print: mac.timelapse-files)' `
        -Closes 'T-159 (volba je v UI a uloží se: výměna čísla, tip, settings.json, znovu otevřené Nastavení; VoiceOver nahrazený popisky AX; tisk s počtem snímků v mac.timelapse-files)', 'T-211 kritérium 2 (mac.timelapse-layers spolu s mac.timelapse-files)' -Action {
        param($c)
        if (-not (Request-Accessibility $c)) { Skip-VerifyCheck 'no Accessibility permission for the terminal (System Settings > Privacy & Security > Accessibility; it may need a restart of the terminal).' }
        Invoke-SessionCheck $c {
            $texts = Get-VerifyResxStrings (Join-Path $script:Repo 'src' 'LayerPeek.App' 'Resources' 'Strings.resx')
            $byTime = $texts['SettingsTimelapseTriggerTime']
            $byLayer = $texts['SettingsTimelapseTriggerLayer']
            # The fixed frame rate of every timelapse video (T-158), in the hints of both numbers.
            $fps = 30
            $simulator = Get-Simulator $c
            $data = New-TestData -Context $c -Simulator $simulator -Camera $simulator.Camera -RecordingFolder 'Recordings'
            $settingsFile = Join-Path $data.Data 'settings.json'
            $problems = [System.Collections.Generic.List[string]]::new()
            $named = { param($Node, [string] $Expected) $Node -and (@($Node.title, $Node.description) -ccontains $Expected) }
            # Whether a node or one of its children shows a text (the combo box shows its item as a child).
            $shows = { param($Node, [string] $Text) $Node -and (@(Get-AxNodes @($Node) | ForEach-Object { $_.title; $_.value; $_.description }) -ccontains $Text) }
            # A hidden field is missing from the tree or has no size.
            $isShown = { param($Node) $Node -and $Node.frame -and $Node.frame.width -gt 0 -and $Node.frame.height -gt 0 }
            $app = Start-TestApp -Context $c -TestData $data
            $openRecordingTab = {
                param([string] $Evidence)
                Send-TestCommand $data 'settings'
                $null = Select-SettingsTab $c $app.Id 'SettingsTabRecording'
                $script:layerTree = $null
                $null = Wait-VerifyCondition -TimeoutSeconds 10 -Condition { $script:layerTree = Get-AxTree $c $app.Id; $null -ne (Find-AxElement $script:layerTree 'SettingsTimelapseTrigger') }
                $null = Save-VerifyEvidence -Context $c -Name "settings-$Evidence.json" -Content ($script:layerTree | ConvertTo-Json -Depth 100)
                $script:layerTree
            }
            $judge = {
                param($Tree, [string] $Where, [bool] $Layer)
                $combo = Find-AxElement $Tree 'SettingsTimelapseTrigger'
                if (-not $combo) { $problems.Add("${Where}: no SettingsTimelapseTrigger on the Recording tab"); return }
                if (-not (& $named $combo $texts['SettingsTimelapseTrigger'])) { $problems.Add("${Where}: the trigger is not named '$($texts['SettingsTimelapseTrigger'])'") }
                $choice = if ($Layer) { $byLayer } else { $byTime }
                if (-not (& $shows $combo $choice)) { $problems.Add("${Where}: the trigger does not show '$choice'") }
                if ($combo.help -cne $texts['SettingsTimelapseTriggerHint']) { $problems.Add("${Where}: the tip (AXHelp) of the trigger is '$($combo.help)', expected the estimate from the Z axis ('$($texts['SettingsTimelapseTriggerHint'])')") }
                $interval = Find-AxElement $Tree 'SettingsTimelapseInterval'
                $every = Find-AxElement $Tree 'SettingsTimelapseEveryNthLayer'
                $shown, $hidden, $key, $hint = if ($Layer) { $every, $interval, 'SettingsTimelapseEveryNthLayer', 'SettingsTimelapseEveryNthLayerHint' } else { $interval, $every, 'SettingsTimelapseInterval', 'SettingsTimelapseIntervalHint' }
                if (-not (& $isShown $shown)) { $problems.Add("${Where}: no '$($texts[$key])' ($key) next to the trigger") }
                else {
                    if (-not (& $named $shown $texts[$key])) { $problems.Add("${Where}: the number is not named '$($texts[$key])'") }
                    if ($shown.help -cne ($texts[$hint] -f $fps)) { $problems.Add("${Where}: the tip of '$($texts[$key])' is '$($shown.help)'") }
                }
                if (& $isShown $hidden) { $problems.Add("${Where}: the other number ($($hidden.identifier)) still shows") }
            }
            $recording = { $json = Get-Content -LiteralPath $settingsFile -Raw | ConvertFrom-Json; if ($json.PSObject.Properties['recording']) { $json.recording } }
            try {
                # 1. By time (the default): the interval next to the trigger.
                $tree = & $openRecordingTab 'by-time'
                & $judge $tree 'by time' $false
                # 2. Choose "Every N-th layer": the list opens with a click, then its item (or Down and Return).
                $combo = Find-AxElement $tree 'SettingsTimelapseTrigger'
                if ($combo) {
                    $windows = @(Get-ProcessWindows $c $app.Id).Count
                    $center = Get-AxCenter $combo
                    $null = Invoke-Helper $c @('click', "$($center[0])", "$($center[1])")
                    if (-not (Wait-VerifyCondition -TimeoutSeconds 5 -Condition { @(Get-ProcessWindows $c $app.Id).Count -gt $windows })) { $problems.Add('a click did not open the list of SettingsTimelapseTrigger') }
                    else {
                        Start-Sleep -Milliseconds 400
                        $inCombo = { param($Node) $f = $Node.frame; $b = $combo.frame; $x = $f.x + $f.width / 2; $y = $f.y + $f.height / 2; $x -ge $b.x -and $x -le $b.x + $b.width -and $y -ge $b.y -and $y -le $b.y + $b.height }
                        $item = @(Get-AxNodes (Get-AxTree $c $app.Id) | Where-Object { $_.frame -and (@($_.title, $_.value, $_.description) -ccontains $byLayer) -and -not (& $inCombo $_) }) | Select-Object -First 1
                        if ($item) { $point = Get-AxCenter $item; $null = Invoke-Helper $c @('click', "$($point[0])", "$($point[1])") }
                        else { $null = Invoke-Helper $c @('key', 'down'); Start-Sleep -Milliseconds 300; $null = Invoke-Helper $c @('key', 'return') }
                        Start-Sleep -Milliseconds 800
                    }
                    $tree = Get-AxTree $c $app.Id
                    $null = Save-VerifyEvidence -Context $c -Name 'settings-by-layer.json' -Content ($tree | ConvertTo-Json -Depth 100)
                    & $judge $tree 'by layer' $true
                    # 3. Saved: settings.json has the trigger; Settings closes.
                    $null = Invoke-Helper $c @('axpress', "$($app.Id)", 'SettingsSave')
                    if (-not (Wait-VerifyCondition -TimeoutSeconds 10 -Condition { $r = & $recording; $r -and $r.PSObject.Properties['timelapseTrigger'] -and $r.timelapseTrigger -ceq 'layer' })) { $problems.Add("Save did not write recording.timelapseTrigger = 'layer' into settings.json") }
                    $null = Save-VerifyEvidence -Context $c -Name 'settings.json' -Path $settingsFile
                    if (-not (Wait-VerifyCondition -TimeoutSeconds 10 -Condition { -not (Get-AxElement $c $app.Id 'SettingsTimelapseTrigger') })) { $problems.Add('Settings did not close after Save') }
                    # 4. Opened again: the choice stayed.
                    & $judge (& $openRecordingTab 'reopened') 'reopened' $true
                    $null = Invoke-Helper $c @('axpress', "$($app.Id)", 'SettingsCancel') -AllowFailure
                }
                if ($app.HasExited) { $problems.Add("the app ended with $($app.ExitCode)") }
            }
            finally { $null = Stop-TestApp $data $app }
            if ($problems.Count -gt 0) { return New-VerifyResult FAIL "$($problems.Count) problem(s): $($problems -join '; ')" }
            New-VerifyResult PASS "'$byLayer' swaps '$($texts['SettingsTimelapseInterval'])' for '$($texts['SettingsTimelapseEveryNthLayer'])', the trigger's tip (AXHelp) is '$($texts['SettingsTimelapseTriggerHint'])'; saved as recording.timelapseTrigger = 'layer' and shown again after reopening Settings. Trees: settings-*.json."
        }
    }

    New-VerifyCheck -Id 'mac.notifications-content' -Needs Session, Accessibility -Kind Permission `
        -Permission "Accessibility for the terminal (System Settings > Privacy & Security > Accessibility) to press the notifications and their buttons in Notification Centre and read the app windows, an unlocked screen, and the notifications of 'LayerPeek E2E' allowed (mac.notifications) with the alert style Persistent" `
        -Title 'content of the delivered notifications of the test bundle (--layerpeek-e2e-notification-list): (a) print failure alarm with the snapshot, the False alarm button (a record, the app stays behind) and the click (camera window); (b) Camera recording failed and Timelapse failed for a folder on a missing volume, a click opens Settings > Recording; (c) the test notification with and without sound, replaced by the next one, a click opens Settings > General' `
        -Closes 'T-154 (notifikace poplachu: titulek, text, příloha, tlačítko „Falešný poplach“ u prvního poplachu, záznam bez přenesení do popředí, klik otevře okno kamery, snímek ve složce nahrávek, prázdná cache; bez vzhledu banneru a editoru výřezu)', 'T-146 / T-161 (notifikace „Nahrávání kamery selhalo“ a „Timelapse selhal“, klik otevře Nastavení › Nahrávání)', 'T-101 (zkušební upozornění se zvukem i bez, nahrazení, klik otevře Nastavení › Obecné; bez stavu „zamítnuto“ a notDetermined)', 'T-212 kritéria 2–4' -Action {
        param($c)
        if (-not (Request-Accessibility $c)) { Skip-VerifyCheck 'no Accessibility permission for the terminal (System Settings > Privacy & Security > Accessibility; it may need a restart of the terminal).' }
        $permission = (Get-TestNotificationStatus $c).Permission
        if ($permission -ne 'Allowed') { Skip-VerifyCheck "the notifications of 'LayerPeek E2E' are $permission; allow them once with 'pwsh tools/macos-verify.ps1 -Only mac.notifications' (interactive)." }
        Invoke-SessionCheck $c {
            $texts = Get-VerifyResxStrings (Join-Path $script:Repo 'src' 'LayerPeek.App' 'Resources' 'Strings.resx')
            $bundle = Get-TestBundle $c
            $problems = [System.Collections.Generic.List[string]]::new()
            $notes = [System.Collections.Generic.List[string]]::new()
            # Parts macOS does not let the script do (a button or a banner not in AX): a SKIP of the check with the reasons.
            $skipped = [System.Collections.Generic.List[string]]::new()
            $list = {
                $result = Invoke-BundleTool $bundle @('--layerpeek-e2e-notification-list')
                $parsed = if ($result.ExitCode -eq 0) { ConvertFrom-VerifyNotificationList -Output $result.Output -BundleIdentifier $script:TestBundleId }
                if (-not $parsed) { throw "The list mode of the test bundle answered ($($result.ExitCode)): $($result.Output -join ' ')" }
                $parsed
            }
            $clear = { $null = Invoke-BundleTool $bundle @('--layerpeek-e2e-clear-toasts') }
            # The first delivered notification that matches (polled once a second); the list it was found in stays in $script:lastList.
            $waitFor = {
                param([scriptblock] $Match, [int] $Seconds, [string] $Evidence)
                $script:lastList = $null
                $script:found = $null
                $null = Wait-VerifyCondition -TimeoutSeconds $Seconds -IntervalMilliseconds 1000 -Condition {
                    $script:lastList = & $list
                    $script:found = @($script:lastList.Notifications | Where-Object -FilterScript $Match) | Select-Object -First 1
                    $null -ne $script:found
                }
                if ($Evidence) { $null = Save-VerifyEvidence -Context $c -Name "list-$Evidence.json" -Content ($script:lastList | ConvertTo-Json -Depth 10) }
                $script:found
            }
            # helper ncpress: $true when the action was performed; a banner or button macOS keeps out of AX is a skipped part.
            $ncpress = {
                param([string] $Part, [string] $Text, [string] $Action, [string] $Evidence)
                $output = @(& (Get-Helper $c) 'ncpress' $Text $Action 2>&1 | ForEach-Object { "$_" })
                $code = $LASTEXITCODE
                $null = Save-VerifyEvidence -Context $c -Name "ncpress-$Evidence.txt" -Content (@("ncpress '$Text' '$Action': exit code $code") + $output)
                switch ($code) {
                    0 { return $true }
                    1 { $skipped.Add("${Part} ${Action} on '$Text': no such banner or entry in the AX tree of Notification Centre (set the alert style of 'LayerPeek E2E' to Persistent)") }
                    4 { $skipped.Add("${Part} ${Action} on '$Text': the banner action is not in AX ($((@($output) | Select-Object -Last 1)))") }
                    default { $problems.Add("${Part} ncpress '$Text' '$Action' ended with ${code}: $($output -join ' ')") }
                }
                $false
            }
            $selectedTab = { param([int] $AppId) (Get-AxSelectedTab $c $AppId 'SettingsTabs').Selected }
            $waitTab = { param([int] $AppId, [string] $Tab) Wait-VerifyCondition -TimeoutSeconds 15 -IntervalMilliseconds 500 -Condition { (& $selectedTab $AppId) -ceq $Tab } }
            $headline = { param($Notification) @("$($Notification.body)" -split "`n")[0] }
            $simulator = Get-Simulator $c

            # (a) T-154: the alarm of the detector of the test mode, sound as for a printer error (ImportantOnly).
            & $clear
            Set-SimulatorScenario $simulator 'idle'
            $data = New-TestData -Context $c -Simulator $simulator -Camera $simulator.Camera -RecordingFolder 'Recordings' -Notifications -Sound 'ImportantOnly' -Detection -PollingSeconds 2
            $name = (Get-Content -LiteralPath (Join-Path $data.Data 'settings.json') -Raw | ConvertFrom-Json).printers[0].name
            $alarmPrefix = ($texts['NotificationPrintFailure'] -split '\{0\}')[0]
            $isAlarm = { $_.title -ceq $name -and "$($_.body)".StartsWith($alarmPrefix, [StringComparison]::Ordinal) }
            $app = Get-Process -Id (Start-TestAppViaOpen -Context $c -TestData $data -Environment @{ LAYERPEEK_TEST_TOASTS = '1'; LAYERPEEK_TEST_FAKE_DETECTOR = '1' })
            try {
                Start-Sleep -Seconds 4
                Set-SimulatorScenario $simulator 'printing'
                $alarm = & $waitFor $isAlarm 90 'alarm'
                if (-not $alarm) { $problems.Add("(a) no alarm notification of '$name' within 90 s of the print") }
                else {
                    $expected = [ordered]@{
                        Title = $name; BodyPrefix = $alarmPrefix; HasSound = $true; Attachments = 1; AttachmentType = 'public.jpeg'
                        LaunchArgument = "camera=$($script:PrinterId)"; ActionArgumentPrefix = 'false-alarm='
                        Category = 'layerpeek.with-action'; ActionTitle = $texts['NotificationFalseAlarm']
                    }
                    foreach ($problem in @(Get-VerifyNotificationProblems -Notification $alarm -Expected $expected -Categories $script:lastList.Categories)) { $problems.Add("(a) first alarm: $problem") }
                    # The button: a local record, the app stays behind, nothing opens.
                    if (& $ncpress '(a)' (& $headline $alarm) $texts['NotificationFalseAlarm'] 'false-alarm') {
                        $records = Join-Path $data.Data 'detection' 'false-alarms'
                        if (-not (Wait-VerifyCondition -TimeoutSeconds 15 -Condition { @(Get-ChildItem -LiteralPath $records -Filter '*.json' -File -ErrorAction SilentlyContinue).Count -gt 0 })) { $problems.Add("(a) '$($texts['NotificationFalseAlarm'])' saved no detection/false-alarms/*.json within 15 s") }
                        Start-Sleep -Seconds 1
                        if ((Get-FrontProcessId) -eq $app.Id) { $problems.Add("(a) '$($texts['NotificationFalseAlarm'])' brought the app to the front") }
                        if (Get-CameraWindow $c $app.Id) { $problems.Add("(a) '$($texts['NotificationFalseAlarm'])' opened a window") }
                        $notes.Add("'$($texts['NotificationFalseAlarm'])' recorded the alarm, the app stayed behind ($(Get-FrontAppText) in front)")
                    }
                    $snapshots = @(Get-ChildItem -LiteralPath $data.Recordings -Recurse -File -Filter '*_alarm-e2e-alarm.jpg' -ErrorAction SilentlyContinue)
                    if ($snapshots.Count -eq 0) { $problems.Add('(a) no snapshot *_alarm-e2e-alarm.jpg in the recordings folder') }
                    $cache = @(Get-ChildItem -LiteralPath (Join-Path $data.Data 'cache' 'notification-images') -Recurse -File -ErrorAction SilentlyContinue)
                    if ($cache.Count -gt 0) { $problems.Add("(a) $($cache.Count) file(s) left in the cache notification-images: $(@($cache.Name) -join ', ')") }

                    # The click on the alarm of the next print (one alarm per print; the earlier notifications go first).
                    & $clear
                    Set-SimulatorScenario $simulator 'idle'
                    Start-Sleep -Seconds 6
                    Set-SimulatorScenario $simulator 'printing'
                    $second = & $waitFor $isAlarm 90 'second-alarm'
                    if (-not $second) { $problems.Add("(a) no alarm notification of the next print within 90 s") }
                    elseif (& $ncpress '(a)' (& $headline $second) 'AXPress' 'alarm-click') {
                        if (-not (Wait-VerifyCondition -TimeoutSeconds 20 -Condition { $null -ne (Get-CameraWindow $c $app.Id) })) { $problems.Add('(a) the click on the alarm opened no camera window within 20 s') }
                        else { $notes.Add('the click on the alarm opened the camera window') }
                    }
                    # The same sound as a printer error.
                    $errorPrefix = $texts['NotificationError']
                    Set-SimulatorScenario $simulator 'error'
                    $printerError = & $waitFor { $_.title -ceq $name -and "$($_.body)".StartsWith($errorPrefix, [StringComparison]::Ordinal) } 30 'error'
                    if (-not $printerError) { $problems.Add("(a) no '$errorPrefix' notification within 30 s") }
                    elseif ([bool] $printerError.hasSound -ne [bool] $alarm.hasSound) { $problems.Add("(a) the alarm has sound $($alarm.hasSound), a printer error $($printerError.hasSound)") }
                }
            }
            finally { $null = Stop-TestApp $data $app; Set-SimulatorScenario $simulator 'idle'; & $clear }

            # (b) T-146 / T-161: the folder of the recordings is on a volume that is not mounted.
            $missing = "/Volumes/LPV-missing-$(New-ShortId)"
            if (Test-Path -LiteralPath $missing) { throw "$missing exists." }
            $data = New-TestData -Context $c -Simulator $simulator -Camera $simulator.Camera -CameraOptions ([ordered]@{ recordPrints = $true; timelapsePrints = $true }) -RecordingFolder "$missing/Recordings" -Notifications -PollingSeconds 2
            $name = (Get-Content -LiteralPath (Join-Path $data.Data 'settings.json') -Raw | ConvertFrom-Json).printers[0].name
            $app = Get-Process -Id (Start-TestAppViaOpen -Context $c -TestData $data -Environment @{ LAYERPEEK_TEST_TOASTS = '1' })
            try {
                Start-Sleep -Seconds 4
                Set-SimulatorScenario $simulator 'printing'
                foreach ($key in 'NotificationRecordingFailed', 'NotificationTimelapseFailed') {
                    $text = $texts[$key]
                    $failure = & $waitFor { $_.title -ceq $name -and (& $headline $_) -ceq $text } 60 $key
                    if (-not $failure) { $problems.Add("(b) no '$text' notification of '$name' within 60 s"); continue }
                    foreach ($problem in @(Get-VerifyNotificationProblems -Notification $failure -Expected ([ordered]@{ Title = $name; BodyLine = $text; Attachments = 0; LaunchArgument = 'settings=recording' }))) { $problems.Add("(b) '$text': $problem") }
                    # A click opens Settings on the Recording tab (from another tab when Settings is open already).
                    if ($null -ne (Get-AxElement $c $app.Id 'SettingsTabGeneral')) {
                        $null = Select-SettingsTab $c $app.Id 'SettingsTabGeneral'
                        $null = & $waitTab $app.Id 'SettingsTabGeneral'
                    }
                    if (& $ncpress '(b)' $text 'AXPress' $key) {
                        if (& $waitTab $app.Id 'SettingsTabRecording') { $notes.Add("the click on '$text' opened Settings > Recording") }
                        else { $problems.Add("(b) the click on '$text' did not show Settings > Recording within 15 s (selected: '$(& $selectedTab $app.Id)')") }
                    }
                }
                if (Test-Path -LiteralPath $missing) { $problems.Add("(b) $missing was created") }
            }
            finally { $null = Stop-TestApp $data $app; Set-SimulatorScenario $simulator 'idle'; & $clear }

            # (c) T-101: the test notification of Settings > General, with the sound setting All and Off.
            foreach ($sound in 'All', 'Off') {
                $data = New-TestData -Context $c -Notifications -Sound $sound
                $app = Get-Process -Id (Start-TestAppViaOpen -Context $c -TestData $data -Environment @{ LAYERPEEK_TEST_TOASTS = '1' })
                try {
                    Send-TestCommand $data 'settings'
                    if (-not (Wait-VerifyCondition -TimeoutSeconds 15 -Condition { $null -ne (Get-AxElement $c $app.Id 'SettingsTestNotification') })) { $problems.Add("(c) sound ${sound}: no SettingsTestNotification in Settings within 15 s"); continue }
                    $isTest = { $_.identifier -ceq 'layerpeek-test-notification' }
                    $null = Invoke-Helper $c @('axpress', "$($app.Id)", 'SettingsTestNotification')
                    $first = & $waitFor $isTest 20 "test-$sound"
                    $expected = [ordered]@{ Title = $texts['NotificationTestTitle']; BodyPrefix = $texts['NotificationTestBody']; HasSound = ($sound -ne 'Off'); Attachments = 0; LaunchArgument = 'settings=general' }
                    foreach ($problem in @(Get-VerifyNotificationProblems -Notification $first -Expected $expected)) { $problems.Add("(c) sound ${sound}: $problem") }
                    if (-not $first -or $sound -ne 'All') { continue }
                    # A second one replaces the first: still one with that identifier, delivered later.
                    Start-Sleep -Seconds 2
                    $null = Invoke-Helper $c @('axpress', "$($app.Id)", 'SettingsTestNotification')
                    $replaced = & $waitFor { $_.identifier -ceq 'layerpeek-test-notification' -and $_.deliveredAt -and $_.deliveredAt -ne $first.deliveredAt } 20 'test-replaced'
                    $count = @($script:lastList.Notifications | Where-Object -FilterScript $isTest).Count
                    if (-not $replaced) { $problems.Add('(c) the second test notification was not delivered within 20 s') }
                    elseif ($count -ne 1) { $problems.Add("(c) $count test notifications after the second one, expected 1 (replaced)") }
                    else { $notes.Add('the second test notification replaced the first') }
                    # A click brings Settings to the General tab.
                    $null = Select-SettingsTab $c $app.Id 'SettingsTabRecording'
                    if (-not (& $waitTab $app.Id 'SettingsTabRecording')) { $problems.Add('(c) Settings did not switch to the Recording tab before the click'); continue }
                    if (& $ncpress '(c)' $texts['NotificationTestTitle'] 'AXPress' 'test-click') {
                        if (& $waitTab $app.Id 'SettingsTabGeneral') { $notes.Add('the click on the test notification opened Settings > General') }
                        else { $problems.Add("(c) the click on the test notification did not show Settings > General within 15 s (selected: '$(& $selectedTab $app.Id)')") }
                    }
                }
                finally { $null = Stop-TestApp $data $app; & $clear }
            }

            if ($problems.Count -gt 0) { return New-VerifyResult FAIL "$($problems.Count) problem(s): $($problems -join '; ')" }
            if ($skipped.Count -gt 0) { Skip-VerifyCheck "content as expected, not done: $($skipped -join '; '). Done: $($notes -join '; ')." }
            New-VerifyResult PASS "(a) alarm with the snapshot (public.jpeg), category with '$($texts['NotificationFalseAlarm'])', sound as a printer error; (b) '$($texts['NotificationRecordingFailed'])' and '$($texts['NotificationTimelapseFailed'])'; (c) test notification with and without sound; $($notes -join '; ')."
        }
    }

    New-VerifyCheck -Id 'mac.file-dialogs' -Needs Session, Accessibility -Kind Permission `
        -Permission "Accessibility for the terminal (System Settings > Privacy & Security > Accessibility) to choose the menu items, type a path into the open panels (Cmd-Shift-G) and read the windows, and an unlocked screen; for the notification parts the notifications of 'LayerPeek E2E' allowed (mac.notifications)" `
        -Title 'the system open panels (NSOpenPanel) through Accessibility: (a) Settings > Recording "Change…" (the chosen folder in the field and saved, an unwritable folder with its message); (b) "Export timelapse to JPEG…": a free-standing panel in front over another app with its title, folder and .mp4 filter, one panel only, the camera window keeps running, a recording refused, the progress, every frame, a cancel without leftovers; (c) "Create timelapse from recording…": the same with a recording with a camera outage, 30 fps, the JPEG export panel during the job (simulator x20)' `
        -Closes 'T-146 („Změnit…“ otevře systémový dialog složky, nezapisovatelná složka ukáže hlášku)', 'T-165 (UI akce exportu do JPEG na Macu: volné okno v popředí, titulek, složka, jen .mp4, nahrávka odmítnuta, průběh, snímky, jedno okno, kamera běží, zrušení bez pozůstatků)', 'T-164 (UI akce timelapse z nahrávky na Macu: volné okno, titulek, průběh, soubor s částí po výpadku, 30 fps, timelapse odmítnut, zrušení bez pozůstatků, dialog exportu během vytváření)', 'T-213 kritéria 1–3' -Action {
        param($c)
        if (-not (Request-Accessibility $c)) { Skip-VerifyCheck 'no Accessibility permission for the terminal (System Settings > Privacy & Security > Accessibility; it may need a restart of the terminal).' }
        Invoke-SessionCheck $c {
            $texts = Get-VerifyResxStrings (Join-Path $script:Repo 'src' 'LayerPeek.App' 'Resources' 'Strings.resx')
            $label = { param($Key) ConvertFrom-VerifyAccessText $texts[$Key] }
            $problems = [System.Collections.Generic.List[string]]::new()
            $notes = [System.Collections.Generic.List[string]]::new()
            # Parts the script cannot judge here (notifications not allowed): a SKIP of the check with the reasons, never a PASS.
            $skipped = [System.Collections.Generic.List[string]]::new()
            $plain = { param([string] $Path) [System.IO.Path]::TrimEndingDirectorySeparator(($Path -replace '^/private/', '/')) }
            $shown = { param($Node) @($Node.value, $Node.title, $Node.description) | Where-Object { $_ } | Select-Object -First 1 }
            $movies = Get-MoviesRecordingsState
            $bundle = Get-TestBundle $c
            $permission = (Get-TestNotificationStatus $c).Permission
            $toasts = $permission -eq 'Allowed'
            $clear = { if ($toasts) { $null = Invoke-BundleTool $bundle @('--layerpeek-e2e-clear-toasts') } }

            # The helper with its exit code; -Evidence keeps its output.
            $run = {
                param([string[]] $Arguments, [string] $Evidence)
                $output = @(& (Get-Helper $c) @Arguments 2>&1 | ForEach-Object { "$_" })
                $code = $LASTEXITCODE
                if ($Evidence) { $null = Save-VerifyEvidence -Context $c -Name $Evidence -Content (@("$($Arguments -join ' '): exit code $code") + $output) }
                [pscustomobject]@{ ExitCode = $code; Output = $output }
            }
            $panels = {
                param([int] $AppId, [double] $Seconds)
                $result = & $run @('panel', "$AppId", "$Seconds")
                if ($result.ExitCode -ne 0) { throw "helper panel: $($result.Output -join ' ')" }
                $found = @($result.Output -join "`n" | ConvertFrom-Json -Depth 20)
                # None found: the windows and sheets of the app as evidence (what the helper did not recognise as a panel).
                if ($found.Count -eq 0) { $null = Save-VerifyEvidence -Context $c -Name "no-panel-$(New-ShortId).json" -Content ((Get-AxTree $c $AppId) | ConvertTo-Json -Depth 100) }
                $found
            }
            # Chooses an item of the menu and waits for exactly one open panel; $null (and a problem) otherwise.
            $openPanel = {
                param([int] $AppId, [string] $Item, [string] $Evidence)
                $press = Invoke-MenuPress $c $AppId $Item
                if ($press.ExitCode -ne 0) { $problems.Add("${Evidence}: menupress '$Item': $($press.Output -join ' ')"); return $null }
                $found = @(& $panels $AppId 10)
                $null = Save-VerifyEvidence -Context $c -Name "panel-$Evidence.json" -Content (ConvertTo-Json -InputObject $found -Depth 20)
                if ($found.Count -ne 1) { $problems.Add("${Evidence}: $($found.Count) open panel(s) after '$Item', expected 1"); return $null }
                $found[0]
            }
            # Chooses a path in the open panel (helper panelgo); whether it was chosen.
            $choose = {
                param([int] $AppId, [string] $Path, [string] $Evidence)
                $go = & $run @('panelgo', "$AppId", $Path) "panelgo-$Evidence.txt"
                if ($go.ExitCode -ne 0) { $problems.Add("${Evidence}: panelgo '$(Split-Path -Leaf $Path)' ended with $($go.ExitCode): $($go.Output -join ' ')") }
                $go.ExitCode -eq 0
            }
            # No panel and no menu left open (each closed if one is), before the app quits.
            $leftOpen = {
                param($App, [string] $Part)
                if ($App.HasExited) { return }
                try {
                    $left = @(& $panels $App.Id 0)
                    if ($left.Count -gt 0) { $problems.Add("${Part}: $($left.Count) panel(s) still open at the end"); $null = & $run @('panelcancel', "$($App.Id)") "panelcancel-$Part.txt" }
                    if (@(Get-OpenMenuWindows $c $App.Id).Count -gt 0) { $problems.Add("${Part}: a menu is still open at the end"); $null = Invoke-Helper $c @('key', 'escape') -AllowFailure }
                }
                catch { $problems.Add("${Part}: the end state was not readable: $($_.Exception.Message)") }
            }
            $events = { param($Data) @(Get-VerifyFileJobEvents (Read-TestLog $Data)) }
            $waitEvent = {
                param($Data, [string] $Kind, [string] $Name, [int] $Seconds)
                $script:fileJobEvent = $null
                $null = Wait-VerifyCondition -TimeoutSeconds $Seconds -IntervalMilliseconds 500 -Condition {
                    $script:fileJobEvent = @(& $events $Data | Where-Object { $_.Kind -ceq $Kind -and $_.Name -ceq $Name }) | Select-Object -Last 1
                    $null -ne $script:fileJobEvent
                }
                $script:fileJobEvent
            }
            # The percentages of a running job the menu shows (helper menu, about once a second) until -Until is true.
            $percents = {
                param([int] $AppId, [string] $Format, [scriptblock] $Until, [int] $Seconds)
                $seen = [System.Collections.Generic.List[int]]::new()
                $clock = [System.Diagnostics.Stopwatch]::StartNew()
                while ($clock.Elapsed.TotalSeconds -lt $Seconds -and -not (& $Until)) {
                    $percent = Get-VerifyMenuPercent -Items @((Get-StatusMenu $c $AppId).items) -Format $Format
                    if ($null -ne $percent -and ($seen.Count -eq 0 -or $seen[-1] -ne $percent)) { $seen.Add($percent) }
                    Start-Sleep -Milliseconds 300
                }
                $seen.ToArray()
            }
            $grows = { param([int[]] $Seen) $Seen.Count -ge 2 -and @(for ($i = 1; $i -lt $Seen.Count; $i++) { if ($Seen[$i] -le $Seen[$i - 1]) { $i } }).Count -eq 0 }
            # A delivered notification of the printer with that first line (and reason line); a skipped part without permission.
            $notified = {
                param([string] $Part, [string] $Title, [string] $Headline, [string] $Line)
                if (-not $toasts) { $skipped.Add("$Part notification '$Headline': the notifications of 'LayerPeek E2E' are $permission (allow them once with 'pwsh tools/macos-verify.ps1 -Only mac.notifications')"); return }
                $script:delivered = $null
                $null = Wait-VerifyCondition -TimeoutSeconds 20 -IntervalMilliseconds 1000 -Condition {
                    $result = Invoke-BundleTool $bundle @('--layerpeek-e2e-notification-list')
                    $list = if ($result.ExitCode -eq 0) { ConvertFrom-VerifyNotificationList -Output $result.Output -BundleIdentifier $script:TestBundleId }
                    $script:delivered = @($list.Notifications | Where-Object { $_.title -ceq $Title -and @("$($_.body)" -split "`n")[0] -ceq $Headline }) | Select-Object -First 1
                    $null -ne $script:delivered
                }
                $expected = [ordered]@{ Title = $Title; BodyLine = $Headline }
                if ($Line) { $expected.BodyLine = $Line }
                $found = Get-VerifyNotificationProblems -Notification $script:delivered -Expected $expected
                if (-not $script:delivered) { $problems.Add("$Part no notification '$Headline' of '$Title' within 20 s") }
                elseif (@($found).Count -gt 0) { $problems.Add("$Part notification '$Headline': $($found -join '; ')") }
                else { $notes.Add("$Part notification '$Headline$(if ($Line) { " / $Line" })'") }
            }
            $simulator = Get-Simulator $c -Speed 20 -Clip (Get-Buddy3DClip)
            $waitIdle = {
                param($App, $Name)
                Wait-VerifyCondition -TimeoutSeconds 60 -IntervalMilliseconds 1000 -Condition { -not (Get-VerifyToolTipProblem -ToolTip ((Get-StatusToolTipLines $c $App.Id) -join "`n") -Texts $texts -Name $Name -State $texts['StateIdle']) }
            }
            # The test app; the file jobs read their file -DelayMs slower per item (LAYERPEEK_TEST_FILE_JOB_DELAY_MS), so
            # that the menu shows their progress and a cancel comes in the middle. Through LaunchServices for notifications.
            $startApp = {
                param($Data, [int] $DelayMs)
                $environment = @{ LAYERPEEK_TEST_FILE_JOB_DELAY_MS = "$DelayMs" }
                if (-not $toasts) { return Start-TestApp -Context $c -TestData $Data -Environment $environment }
                $environment.LAYERPEEK_TEST_TOASTS = '1'
                Get-Process -Id (Start-TestAppViaOpen -Context $c -TestData $Data -Environment $environment)
            }
            # The media in the printer's folder of a test data folder, and a file of another type.
            $prepare = {
                param($Data, $Media)
                $name = (Get-Content -LiteralPath (Join-Path $Data.Data 'settings.json') -Raw | ConvertFrom-Json).printers[0].name
                $folder = Join-Path $Data.Recordings $name
                $null = New-Item -ItemType Directory -Force -Path $folder
                foreach ($file in $Media.Recording, $Media.Continuation, $Media.Timelapse) { Copy-Item -LiteralPath $file.FullName -Destination $folder }
                [System.IO.File]::WriteAllText((Join-Path $folder 'notes.txt'), "not a recording`n")
                [pscustomobject]@{
                    Name = $name; Folder = $folder; Printer = $texts['MenuPrinter'] -f $name, $texts['StateIdle']
                    Recording = Join-Path $folder $Media.Recording.Name; Continuation = Join-Path $folder $Media.Continuation.Name
                    Timelapse = Join-Path $folder $Media.Timelapse.Name; Other = Join-Path $folder 'notes.txt'
                }
            }
            $leftovers = { param([string] $Folder, [string] $Marker) @(Get-ChildItem -LiteralPath $Folder -Force | Where-Object { $_.Name -clike "*$Marker*" } | ForEach-Object Name) }

            # (a) T-146: Settings > Recording > "Change…": an unwritable folder, then a folder that is taken and saved.
            $data = New-TestData -Context $c -RecordingFolder 'Recordings'
            $chosen = Join-Path $data.Data 'Chosen'
            $locked = Join-Path $data.Data 'Locked'
            $null = New-Item -ItemType Directory -Force -Path $chosen, $locked
            & /bin/chmod 0500 $locked
            Add-VerifyCleanup $c 'write permission of the locked folder' { param($p) if (Test-Path -LiteralPath $p) { & /bin/chmod 0700 $p } } -State $locked
            $app = Start-TestApp -Context $c -TestData $data
            try {
                Send-TestCommand $data 'settings'
                if (-not (Select-SettingsTab $c $app.Id 'SettingsTabRecording')) { throw 'Settings did not show the Recording tab (SettingsTabRecording) within 15 s' }
                if (-not (Wait-VerifyCondition -TimeoutSeconds 10 -Condition { $null -ne (Get-AxElement $c $app.Id 'SettingsRecordingFolderChange') })) { throw 'no SettingsRecordingFolderChange on the Recording tab within 10 s' }
                foreach ($step in 'locked', 'chosen') {
                    $folder = if ($step -eq 'locked') { $locked } else { $chosen }
                    $press = & $run @('axpress', "$($app.Id)", 'SettingsRecordingFolderChange') "axpress-a-$step.txt"
                    if ($press.ExitCode -ne 0) { $problems.Add("(a) ${step}: AXPress on '$($texts['SettingsRecordingFolderChange'])' failed"); continue }
                    $found = @(& $panels $app.Id 10)
                    $null = Save-VerifyEvidence -Context $c -Name "panel-a-$step.json" -Content (ConvertTo-Json -InputObject $found -Depth 20)
                    if ($found.Count -ne 1) { $problems.Add("(a) ${step}: $($found.Count) open panel(s) after '$($texts['SettingsRecordingFolderChange'])', expected 1"); continue }
                    if ($step -eq 'locked') {
                        foreach ($problem in Get-VerifyPanelProblems -Panel $found[0] -Expected ([ordered]@{ Title = $texts['SettingsRecordingFolderPickerTitle'] })) { $problems.Add("(a) panel: $problem") }
                        $notes.Add("(a) the folder panel is $(if ($found[0].sheet) { 'a sheet of Settings' } else { 'a free-standing window' }) ($($found[0].role) $($found[0].subrole)), buttons '$($found[0].defaultButton.title)' / '$($found[0].cancelButton.title)'")
                    }
                    if (-not (& $choose $app.Id $folder "a-$step")) { continue }
                    if ($step -eq 'locked') {
                        $script:folderError = $null
                        $null = Wait-VerifyCondition -TimeoutSeconds 10 -Condition { $script:folderError = & $shown (Get-AxElement $c $app.Id 'SettingsRecordingFolderError'); $script:folderError -ceq $texts['SettingsRecordingFolderNotWritable'] }
                        if ($script:folderError -cne $texts['SettingsRecordingFolderNotWritable']) { $problems.Add("(a) the unwritable folder shows '$($script:folderError)', expected '$($texts['SettingsRecordingFolderNotWritable'])'") }
                        else { $notes.Add("(a) the unwritable folder shows '$($script:folderError)'") }
                        continue
                    }
                    $script:field = $null
                    $null = Wait-VerifyCondition -TimeoutSeconds 10 -Condition { $script:field = & $shown (Get-AxElement $c $app.Id 'SettingsRecordingFolder'); (& $plain $script:field) -ceq (& $plain $chosen) }
                    if ((& $plain $script:field) -cne (& $plain $chosen)) { $problems.Add("(a) the field shows '$($script:field)' after choosing $chosen"); continue }
                    $left = & $shown (Get-AxElement $c $app.Id 'SettingsRecordingFolderError')
                    if ($left) { $problems.Add("(a) the chosen folder still shows '$left'") }
                    $null = Invoke-Helper $c @('axpress', "$($app.Id)", 'SettingsSave')
                    $settings = Join-Path $data.Data 'settings.json'
                    if (-not (Wait-VerifyCondition -TimeoutSeconds 10 -Condition { (& $plain (Get-Content -LiteralPath $settings -Raw | ConvertFrom-Json).recording.folder) -ceq (& $plain $chosen) })) { $problems.Add('(a) Save did not write the chosen folder into settings.json within 10 s') }
                    else { $notes.Add('(a) the chosen folder was in the field and saved') }
                }
            }
            finally { & $leftOpen $app '(a)'; $null = Stop-TestApp $data $app }

            # (b) T-165 and (c) T-164 on the media of the recording and timelapse checks (or a print of their own).
            $media = Get-FileDialogMedia $c
            $notes.Add("media: $($media.Recording.Name), $($media.Continuation.Name) and $($media.Timelapse.Name) from $($media.Source)")
            $exportTitle = { param($Files) "$($Files.Printer)>$(& $label 'MenuExportTimelapseJpeg')" }
            $exportCancel = { param($Files) "$($Files.Printer)>$(((ConvertFrom-VerifyAccessText $texts['MenuCancelTimelapseJpegExport']) -split '\{0\}')[0])*" }
            Set-SimulatorScenario $simulator 'idle'

            # (b) T-165: the JPEG export, 500 ms per item read (a timelapse of about ten frames takes about 10 s).
            & $clear
            $data = New-TestData -Context $c -Simulator $simulator -Camera $simulator.Camera -RecordingFolder 'Recordings' -Notifications:$toasts -PollingSeconds 2
            $files = & $prepare $data $media
            $app = & $startApp $data 500
            $other = $null
            try {
                if (-not (& $waitIdle $app $files.Name)) { throw "the tooltip did not show '$($files.Name)' ready within 60 s" }
                Send-TestCommand $data "camera:$($script:PrinterId)"
                if (-not (Wait-VerifyCondition -TimeoutSeconds 20 -Condition { $null -ne (Get-CameraWindow $c $app.Id) })) { $problems.Add('(b) no camera window within 20 s') }
                $screen = @(Get-Screens $c | Where-Object main)[0]
                $other = Start-HelperProcess $c @('front', "$($screen.visible.x + 16)", "$($screen.visible.y + 16)", '600') (Join-Path $c.RunFolder 'tmp' "front-$(New-ShortId).txt")
                if (-not (Wait-StandInFront $c $other)) { $problems.Add("(b) the stand-in app did not come to the front ($(Get-FrontAppText) in front)") }

                # The panel: free-standing, in front over the other app, its title, the printer's folder, only .mp4.
                $panel = & $openPanel $app.Id (& $exportTitle $files) 'b-open'
                if ($panel) {
                    foreach ($problem in Get-VerifyPanelProblems -Panel $panel -Expected ([ordered]@{ Sheet = $false; Title = $texts['TimelapseExportPickerTitle']; Folder = $files.Folder; Front = $true })) { $problems.Add("(b) panel: $problem") }
                    if ((Get-FrontProcessId) -ne $panel.pid) { $problems.Add("(b) $(Get-FrontAppText) is in front, not the app of the panel") }
                    $notes.Add("(b) the panel ($($panel.role) $($panel.subrole)) is in $(if ($panel.pid -eq $app.Id) { 'the app process' } else { "$($panel.process) ($($panel.bundleIdentifier), pid $($panel.pid))" }), buttons '$($panel.defaultButton.title)' / '$($panel.cancelButton.title)'")
                    $items = Get-VerifyPanelItemProblems -Items @($panel.items) -Allowed (Split-Path -Leaf $files.Recording), (Split-Path -Leaf $files.Timelapse) -Refused 'notes.txt'
                    foreach ($problem in $items.Problems) { $problems.Add("(b) panel items: $problem") }
                    if ($items.Unknown.Count -gt 0) { $notes.Add("(b) AX shows no enabled state of $($items.Unknown -join ', ') (the filter is judged by choosing notes.txt)") }
                    # A second click while the panel is open opens nothing new.
                    $again = Invoke-MenuPress $c $app.Id (& $exportTitle $files)
                    Start-Sleep -Seconds 2
                    $count = @(& $panels $app.Id 0).Count
                    if ($again.ExitCode -ne 0) { $problems.Add("(b) the second menupress: $($again.Output -join ' ')") }
                    elseif ($count -ne 1) { $problems.Add("(b) $count open panels after the second click, expected 1") }
                    else { $notes.Add('(b) a second click opened no second panel') }
                    # The camera window keeps running meanwhile (the app answers Accessibility, the stream stays connected).
                    $tree = Get-AxTree $c $app.Id
                    $null = Save-VerifyEvidence -Context $c -Name 'b-camera-ax.json' -Content ($tree | ConvertTo-Json -Depth 100)
                    $connected = @(Get-RtspConnections $app.Id $simulator.CameraPort).Count
                    if (-not (Get-CameraWindow $c $app.Id) -or -not (Find-AxElement $tree 'CameraOverlay')) { $problems.Add('(b) the camera window is gone or has no CameraOverlay while the panel is open') }
                    elseif (Find-AxElement $tree 'CameraNextAttempt') { $problems.Add('(b) the camera window waits for a reconnect while the panel is open (CameraNextAttempt)') }
                    elseif ($connected -lt 1) { $problems.Add('(b) no RTSP connection of the camera window while the panel is open') }
                    else { $notes.Add('(b) the camera window kept running while the panel was open') }
                    # Only .mp4: a text file cannot be chosen (the panel is cancelled), the app gets nothing.
                    $go = & $run @('panelgo', "$($app.Id)", $files.Other) 'panelgo-b-other.txt'
                    Start-Sleep -Seconds 2
                    if ($go.ExitCode -eq 0) { $problems.Add('(b) notes.txt could be chosen in the panel') }
                    elseif (@(& $events $data | Where-Object Name -CEQ 'notes.txt').Count -gt 0) { $problems.Add('(b) the app got notes.txt') }
                    else { $notes.Add("(b) notes.txt could not be chosen (panelgo $($go.ExitCode))") }
                }

                # A recording instead of a timelapse: refused with its notification.
                if (& $openPanel $app.Id (& $exportTitle $files) 'b-recording') {
                    if (& $choose $app.Id $files.Recording 'b-recording') {
                        $refused = & $waitEvent $data 'NotExported' (Split-Path -Leaf $files.Recording) 20
                        if (-not $refused -or $refused.Problem -cne 'NotATimelapse') { $problems.Add("(b) the recording was not refused as NotATimelapse within 20 s ($(if ($refused) { $refused.Problem } else { 'no result' }))") }
                        & $notified '(b)' $files.Name $texts['NotificationTimelapseExportFailed'] $texts['NotificationTimelapseExportNotATimelapse']
                    }
                }

                # The timelapse: the progress in the menu, then a folder with every frame.
                $timelapseName = Split-Path -Leaf $files.Timelapse
                $output = Join-Path $files.Folder "$([System.IO.Path]::GetFileNameWithoutExtension($timelapseName))_jpg"
                $decoded = Invoke-Decode $c $files.Timelapse 'decode-b-timelapse.json'
                if (& $openPanel $app.Id (& $exportTitle $files) 'b-timelapse') {
                    if (& $choose $app.Id $files.Timelapse 'b-timelapse') {
                        $seen = & $percents $app.Id $texts['MenuCancelTimelapseJpegExport'] { $null -ne (@(& $events $data | Where-Object { $_.Kind -ceq 'Exported' }) | Select-Object -First 1) } 90
                        $exported = & $waitEvent $data 'Exported' (Split-Path -Leaf $output) 10
                        if (-not (& $grows $seen)) { $problems.Add("(b) the menu did not show a growing percentage (seen: $($seen -join ', '))") }
                        else { $notes.Add("(b) '$((& $label 'MenuCancelTimelapseJpegExport') -f '…')' with $($seen -join ', ') %") }
                        $jpegs = @(Get-ChildItem -LiteralPath $output -File -Filter '*.jpg' -ErrorAction SilentlyContinue | Sort-Object Name | ForEach-Object Name)
                        $frames = if ($decoded.Report) { [int] $decoded.Report.decodedFrames } else { -1 }
                        $names = @(1..([math]::Max(1, $frames)) | ForEach-Object { '{0:D4}.jpg' -f $_ })
                        if (-not $exported) { $problems.Add("(b) no export into $(Split-Path -Leaf $output) within 90 s") }
                        elseif ($jpegs.Count -ne $frames -or $exported.Frames -ne $frames -or ($jpegs -join '|') -cne ($names -join '|')) { $problems.Add("(b) $($jpegs.Count) JPEG file(s) ($(@($jpegs | Select-Object -First 3) -join ', ')…), the log $($exported.Frames), the timelapse decodes $frames frames") }
                        else { $notes.Add("(b) $(Split-Path -Leaf $output) with $frames frames 0001.jpg…") }
                        & $notified '(b)' $files.Name $texts['NotificationTimelapseExportDone'] ($texts['NotificationTimelapseExportFrames'] -f $frames)
                    }
                }

                # A cancel in the middle leaves no folder, not even the hidden .….partial.
                if (Test-Path -LiteralPath $output) { Remove-Item -LiteralPath $output -Recurse -Force }
                $before = @(& $events $data | Where-Object Kind -CEQ 'NotExported').Count
                if (& $openPanel $app.Id (& $exportTitle $files) 'b-cancel') {
                    if (& $choose $app.Id $files.Timelapse 'b-cancel') {
                        $null = Wait-VerifyCondition -TimeoutSeconds 30 -Condition { $p = Get-VerifyMenuPercent -Items @((Get-StatusMenu $c $app.Id).items) -Format $texts['MenuCancelTimelapseJpegExport']; $null -ne $p -and $p -ge 1 }
                        $cancel = Invoke-MenuPress $c $app.Id (& $exportCancel $files)
                        if ($cancel.ExitCode -ne 0) { $problems.Add("(b) menupress cancel: $($cancel.Output -join ' ')") }
                        $cancelled = & $waitEvent $data 'NotExported' $timelapseName 30
                        Start-Sleep -Seconds 1
                        $rest = & $leftovers $files.Folder '_timelapse_jpg'
                        if (-not $cancelled -or $cancelled.Problem -cne 'Cancelled' -or @(& $events $data | Where-Object Kind -CEQ 'NotExported').Count -le $before) { $problems.Add("(b) the export was not cancelled within 30 s ($(if ($cancelled) { $cancelled.Problem } else { 'no result' }))") }
                        elseif ($rest.Count -gt 0) { $problems.Add("(b) the cancelled export left $($rest -join ', ')") }
                        else { $notes.Add('(b) the cancel in the middle left nothing (ls -a)') }
                    }
                }
            }
            finally {
                & $leftOpen $app '(b)'
                if ($other -and -not $other.HasExited) { $other.Kill(); $null = $other.WaitForExit(5000) }
                $null = Stop-TestApp $data $app
                & $clear
            }

            # (c) T-164: the timelapse from a recording, 20 ms per packet read (the recording with its outage takes about 25 s).
            $data = New-TestData -Context $c -Simulator $simulator -Camera $simulator.Camera -RecordingFolder 'Recordings' -RecordingOptions ([ordered]@{ timelapseIntervalSeconds = $script:TimelapseInterval }) -Notifications:$toasts -PollingSeconds 2
            $files = & $prepare $data $media
            $create = "$($files.Printer)>$(& $label 'MenuCreateTimelapseFromRecording')"
            $createCancel = "$($files.Printer)>$(((ConvertFrom-VerifyAccessText $texts['MenuCancelTimelapseFromRecording']) -split '\{0\}')[0])*"
            $recordingName = Split-Path -Leaf $files.Recording
            $output = Join-Path $files.Folder "$([System.IO.Path]::GetFileNameWithoutExtension($recordingName))_recording_timelapse.mp4"
            $app = & $startApp $data 20
            try {
                if (-not (& $waitIdle $app $files.Name)) { throw "the tooltip did not show '$($files.Name)' ready within 60 s" }
                # The panel, then a timelapse instead of a recording: refused with its notification.
                $panel = & $openPanel $app.Id $create 'c-timelapse'
                if ($panel) {
                    foreach ($problem in Get-VerifyPanelProblems -Panel $panel -Expected ([ordered]@{ Sheet = $false; Title = $texts['TimelapseFromRecordingPickerTitle']; Folder = $files.Folder; Front = $true })) { $problems.Add("(c) panel: $problem") }
                    if (& $choose $app.Id $files.Timelapse 'c-timelapse') {
                        $refused = & $waitEvent $data 'NotMade' (Split-Path -Leaf $files.Timelapse) 20
                        if (-not $refused -or $refused.Problem -cne 'NotARecording') { $problems.Add("(c) the timelapse was not refused as NotARecording within 20 s ($(if ($refused) { $refused.Problem } else { 'no result' }))") }
                        & $notified '(c)' $files.Name $texts['NotificationTimelapseFromRecordingFailed'] $texts['NotificationTimelapseFromRecordingNotARecording']
                    }
                }

                # The recording with its outage: the progress, then a timelapse at 30 fps with the part after the outage.
                if (& $openPanel $app.Id $create 'c-recording') {
                    if (& $choose $app.Id $files.Recording 'c-recording') {
                        $seen = & $percents $app.Id $texts['MenuCancelTimelapseFromRecording'] { $null -ne (@(& $events $data | Where-Object { $_.Kind -ceq 'Made' }) | Select-Object -First 1) } 120
                        $made = & $waitEvent $data 'Made' $recordingName 10
                        if (-not (& $grows $seen)) { $problems.Add("(c) the menu did not show a growing percentage (seen: $($seen -join ', '))") }
                        else { $notes.Add("(c) '$((& $label 'MenuCancelTimelapseFromRecording') -f '…')' with $($seen -join ', ') %") }
                        if (-not $made) { $problems.Add("(c) no timelapse from $recordingName within 120 s") }
                        elseif (-not (Test-Path -LiteralPath $output)) { $problems.Add("(c) no $(Split-Path -Leaf $output) next to the recording ($(@(Get-ChildItem -LiteralPath $files.Folder -Name) -join ', '))") }
                        else {
                            $durations = @(foreach ($file in $files.Recording, $files.Continuation) { $d = Invoke-Decode $c $file "decode-c-$(Split-Path -LeafBase $file).json"; if ($d.Report) { [double] $d.Report.duration } else { 0 } })
                            $interval = [double] $script:TimelapseInterval
                            $total = $durations[0] + $durations[1]
                            $result = Invoke-Decode $c $output 'decode-c-output.json'
                            $frames = if ($result.Report) { [int] $result.Report.decodedFrames } else { -1 }
                            $fps = if ($result.Report) { [double] $result.Report.nominalFrameRate } else { 0 }
                            $min = [math]::Max([math]::Floor($durations[0] / $interval) + 2, [math]::Floor($total / $interval) - 2)
                            $max = [math]::Ceiling($total / $interval) + 1
                            foreach ($problem in $result.Problems) { $problems.Add("(c) $(Split-Path -Leaf $output): $problem") }
                            if ($frames -lt $min -or $frames -gt $max) { $problems.Add("(c) $frames frame(s), expected $min to $max (every $interval s of $([math]::Round($durations[0], 1)) s and $([math]::Round($durations[1], 1)) s after the outage)") }
                            elseif ([math]::Abs($fps - 30) -gt 0.5) { $problems.Add("(c) $fps fps, expected 30") }
                            else { $notes.Add("(c) $(Split-Path -Leaf $output): $frames frames at $fps fps (the log: $($made.Frames) in $($made.Files) file(s)), with the part after the outage") }
                            & $notified '(c)' $files.Name $texts['NotificationTimelapseFromRecordingDone'] ($texts['NotificationTimelapseFromRecordingFrames'] -f $made.Frames)
                        }
                    }
                }

                # During a job the JPEG export panel opens once, a second click opens nothing; a cancel leaves no file.
                # A slower job (200 ms per packet, minutes): the panel steps before the cancel take about a minute, the
                # job at 20 ms ended before them (2026-11-15).
                $null = Stop-TestApp $data $app
                $app = & $startApp $data 200
                if (-not (& $waitIdle $app $files.Name)) { throw "the tooltip did not show '$($files.Name)' ready within 60 s (cancel part)" }
                if (Test-Path -LiteralPath $output) { Remove-Item -LiteralPath $output -Force }
                $before = @(& $events $data | Where-Object Kind -CEQ 'NotMade').Count
                if (& $openPanel $app.Id $create 'c-cancel') {
                    if (& $choose $app.Id $files.Recording 'c-cancel') {
                        $null = Wait-VerifyCondition -TimeoutSeconds 30 -Condition { $p = Get-VerifyMenuPercent -Items @((Get-StatusMenu $c $app.Id).items) -Format $texts['MenuCancelTimelapseFromRecording']; $null -ne $p -and $p -ge 1 }
                        $export = & $openPanel $app.Id (& $exportTitle $files) 'c-export'
                        if ($export) {
                            foreach ($problem in Get-VerifyPanelProblems -Panel $export -Expected ([ordered]@{ Title = $texts['TimelapseExportPickerTitle'] })) { $problems.Add("(c) export panel during the job: $problem") }
                            $null = Invoke-MenuPress $c $app.Id (& $exportTitle $files)
                            Start-Sleep -Seconds 2
                            $count = @(& $panels $app.Id 0).Count
                            if ($count -ne 1) { $problems.Add("(c) $count open panels after a second click during the job, expected 1") }
                            else { $notes.Add('(c) during the job the JPEG export panel opened once, a second click opened nothing') }
                            $null = & $run @('panelcancel', "$($app.Id)") 'panelcancel-c-export.txt'
                        }
                        $cancel = Invoke-MenuPress $c $app.Id $createCancel
                        if ($cancel.ExitCode -ne 0) { $problems.Add("(c) menupress cancel: $($cancel.Output -join ' ')") }
                        $cancelled = & $waitEvent $data 'NotMade' $recordingName 30
                        Start-Sleep -Seconds 1
                        $rest = & $leftovers $files.Folder '_recording_timelapse'
                        if (-not $cancelled -or $cancelled.Problem -cne 'Cancelled' -or @(& $events $data | Where-Object Kind -CEQ 'NotMade').Count -le $before) { $problems.Add("(c) the job was not cancelled within 30 s ($(if ($cancelled) { $cancelled.Problem } else { 'no result' }))") }
                        elseif ($rest.Count -gt 0) { $problems.Add("(c) the cancelled job left $($rest -join ', ')") }
                        else { $notes.Add('(c) the cancel in the middle left nothing (ls -a)') }
                    }
                }
            }
            finally { & $leftOpen $app '(c)'; $null = Stop-TestApp $data $app; & $clear }

            if ((Get-MoviesRecordingsState) -cne $movies) { $problems.Add('~/Movies/LayerPeek changed during the check') }
            if ($problems.Count -gt 0) { return New-VerifyResult FAIL "$($problems.Count) problem(s): $($problems -join '; ')" }
            if ($skipped.Count -gt 0) { Skip-VerifyCheck "everything else as expected, not judged: $($skipped -join '; '). Done: $($notes -join '; ')." }
            New-VerifyResult PASS "$($notes -join '; ')."
        }
    }

    New-VerifyCheck -Id 'mac.icons' -Kind Manual -Title 'menu bar icons on light and dark menu bars, app icon next to system app icons (contact sheet)' `
        -Closes 'T-018 kritérium 1 (čitelnost ve světlém i tmavém režimu)', 'T-098 kritérium 2 (velikost vedle systémových aplikací)' -Action {
        param($c)
        $sheet = Join-Path (Get-VerifyEvidenceFolder $c) 'icons.png'
        $appIcon = Get-ReleaseApp
        if (-not $appIcon) { $appIcon = Join-Path $script:Repo 'src' 'LayerPeek.App' 'Assets' 'AppIcon' 'app-icon-macos-1024.png' }
        $system = @('/System/Library/CoreServices/Finder.app', '/System/Applications/Calculator.app', '/System/Applications/System Settings.app', '/System/Applications/App Store.app') | Where-Object { Test-Path -LiteralPath $_ }
        $null = Invoke-Helper $c (@('icons', $sheet, (Join-Path $script:Repo 'tests' 'LayerPeek.App.UiTests' 'Snapshots' 'macOS' 'MenuBarIcons'), $appIcon) + $system)
        & /usr/bin/open $sheet
        $answers = @(
            Confirm-VerifyStep $c 'In icons.png (opened in Preview): are all menu bar icons readable on the light and the dark bar, also at @1x?'
            Confirm-VerifyStep $c 'Is the LayerPeek app icon about as big as the system app icons at every size?'
        )
        if ($answers -contains $false) { return New-VerifyResult FAIL 'the user answered no (see steps.txt).' }
        New-VerifyResult MANUAL "confirmed on the contact sheet icons.png ($(if (Get-ReleaseApp) { 'icon of the release bundle' } else { 'master PNG; build the release bundle for the .icns' }))."
    }

    New-VerifyCheck -Id 'mac.menu' -Kind Manual -Title 'menu bar icon, tooltip and menu follow the printer states; Quit from the real menu' `
        -Closes 'T-018 kritérium 1 (stavy ikony)', 'T-018 kritérium 3 (menu se zobrazí, aktualizuje, „Ukončit“)', 'T-116 kritérium 3 (obrázky tiskáren v menu)' -Action {
        param($c)
        $simulator = Get-Simulator $c
        $data = New-TestData -Context $c -Simulator $simulator
        Set-SimulatorScenario $simulator 'idle'
        $app = Start-TestApp -Context $c -TestData $data
        Write-VerifyStep $c 'The test app shows its own icon in the menu bar (tooltip starts with [E2E]). For each state: look at the icon, hover for the tooltip, open the menu.'
        foreach ($scenario in 'idle', 'printing', 'paused', 'finished', 'error', 'attention', 'offline') {
            Set-SimulatorScenario $simulator $scenario
            Start-Sleep -Seconds 7
            $null = Read-VerifyText -Context $c -Question "State '$scenario' is shown now. Press Enter for the next state"
        }
        $states = Confirm-VerifyStep $c 'Did the icon, the tooltip and the menu items follow every state (also after switching System Settings > Appearance between Light and Dark)?'
        $printerIcons = Confirm-VerifyStep $c 'Does the printer item show its coloured printer picture, sharp (also on an @1x display), readable in Light and Dark and on the highlighted item, without shifting the other items or its submenu?'
        Write-VerifyStep $c 'Now click the [E2E] menu bar icon and choose Quit.' 'Yellow'
        $quit = $app.WaitForExit(120000)
        if (-not $quit) { $null = Stop-TestApp $data $app }
        if (-not $states) { return New-VerifyResult FAIL 'the user saw a state that the icon or the menu did not follow.' }
        if (-not $printerIcons) { return New-VerifyResult FAIL 'the user saw a printer picture in the menu that was blurred, unreadable or misaligned (T-116).' }
        if (-not $quit -or $app.ExitCode -ne 0) { return New-VerifyResult FAIL 'Quit from the menu did not end the app with 0 within 2 minutes.' }
        New-VerifyResult MANUAL 'states and printer pictures confirmed by the user; Quit from the real menu ended the app with 0.'
    }

    New-VerifyCheck -Id 'mac.camera-look' -Kind Manual -Title 'you see the moving camera image and the overlay in every window mode (simulator)' `
        -Closes 'T-026 kritérium 1 (obraz a překryv ve všech režimech, simulátor)' -Action {
        param($c)
        $result = Invoke-CameraModes -Context $c -Look
        if ($result.Problems.Count -gt 0) { return New-VerifyResult FAIL ($result.Problems -join '; ') }
        New-VerifyResult MANUAL "image and overlay confirmed in every mode: $($result.Notes -join '; ')"
    }

    New-VerifyCheck -Id 'mac.camera-buddy3d' -Kind Manual -Title 'a real Buddy3D camera in every window mode' `
        -Closes 'T-026 kritérium 1 (skutečná kamera Buddy3D)' -Action {
        param($c)
        $url = Read-VerifyText -Context $c -Question 'RTSP address of your Buddy3D camera without a user name or password (e.g. rtsp://192.168.1.50/live), or empty to skip'
        if (-not $url) { Skip-VerifyCheck 'no camera address given.' }
        if ($url -notmatch '^rtsp://[^/@\s]+(/\S*)?$') { return New-VerifyResult FAIL "'$url' is not an rtsp:// address without credentials." }
        $result = Invoke-CameraModes -Context $c -Camera $url -Look
        if ($result.Problems.Count -gt 0) { return New-VerifyResult FAIL ($result.Problems -join '; ') }
        New-VerifyResult MANUAL "Buddy3D stream confirmed in every mode: $($result.Notes -join '; ')"
    }

    New-VerifyCheck -Id 'mac.keys' -Needs Session, Accessibility -Kind Permission -Permission 'Accessibility for Terminal (System Settings > Privacy & Security > Accessibility), to post real key events' `
        -Title 'keyboard shortcuts as real key events: Ctrl-Cmd-F, Esc, Cmd-., Cmd-W, Cmd-Shift-]' `
        -Closes 'T-100 kritérium 4 (část: zkratky jako skutečné události klávesnice)' -Action {
        param($c)
        if (-not (Request-Accessibility $c)) { Skip-VerifyCheck 'no Accessibility permission for the terminal (System Settings > Privacy & Security > Accessibility; it may need a restart of the terminal).' }
        $simulator = Get-Simulator $c
        $data = New-TestData -Context $c -Simulator $simulator -Camera $simulator.Camera -Window ([ordered]@{ rememberLastPlacement = $false; startMode = 'Normal' })
        $app = Start-TestApp -Context $c -TestData $data
        $screens = Get-Screens $c
        $problems = [System.Collections.Generic.List[string]]::new()
        $tabMove = 'not checked'
        $listCheck = 'not checked'
        $key = { param($Combo) $null = Invoke-Helper $c @('key', $Combo); Start-Sleep -Milliseconds 400 }
        # The app in front is not yet its key window, and a window that reached the full-screen frame still animates: a key
        # event right then is lost (T-140), so both settle for a second first.
        $frontWith = { param($Command) Send-TestCommand $data $Command; $front = Wait-VerifyCondition -TimeoutSeconds 10 -Condition { (Get-FrontProcessId) -eq $app.Id }; Start-Sleep -Seconds 1; $front }
        $isFull = { $w = Get-CameraWindow $c $app.Id; $w -and (Test-FullScreen $w $screens) }
        $settledFull = { $full = Wait-VerifyCondition -TimeoutSeconds 6 -Condition $isFull; Start-Sleep -Seconds 1; $full }
        $noWindow = { -not (Get-CameraWindow $c $app.Id) }
        try {
            if (-not (& $frontWith "camera:$($script:PrinterId)")) { $problems.Add('the camera window did not come to the front') }
            & $key 'ctrl+cmd+f'
            if (-not (& $settledFull)) { $problems.Add('Ctrl-Cmd-F did not enter full screen') }
            & $key 'escape'
            if (-not (Wait-VerifyCondition -TimeoutSeconds 6 -Condition { -not (& $isFull) })) { $problems.Add('Esc did not leave full screen') }
            & $key 'ctrl+cmd+f'
            $null = & $settledFull
            & $key 'cmd+period'
            if (-not (Wait-VerifyCondition -TimeoutSeconds 6 -Condition { -not (& $isFull) })) { $problems.Add('Cmd-. did not leave full screen') }
            & $key 'cmd+w'
            if (-not (Wait-VerifyCondition -TimeoutSeconds 6 -Condition $noWindow)) { $problems.Add('Cmd-W did not close the camera window') }
            if ($app.HasExited) { $problems.Add('Cmd-W ended the app') }
            $settingsFile = Join-Path $data.Data 'settings.json'
            $hash = (Get-FileHash -LiteralPath $settingsFile).Hash
            foreach ($closer in 'cmd+w', 'cmd+period') {
                if (-not (& $frontWith 'settings')) { $problems.Add('Settings did not come to the front'); continue }
                if ($closer -eq 'cmd+w') {
                    $before = Get-AxSelectedTab $c $app.Id 'SettingsTabs'
                    & $key 'cmd+shift+]'
                    & $key 'cmd+shift+]'
                    $after = Get-AxSelectedTab $c $app.Id 'SettingsTabs'
                    if (-not $before.Selected -or -not $after.Selected) {
                        $null = Save-VerifyEvidence -Context $c -Name 'settings-tabs.json' -Content ($after.Tree | ConvertTo-Json -Depth 100)
                        $problems.Add('the selected main tab of Settings could not be read from the Accessibility tree (settings-tabs.json)')
                    }
                    else {
                        $expected = $before.Tabs[($before.Tabs.IndexOf($before.Selected) + 2) % $before.Tabs.Count]
                        if ($after.Selected -ne $expected) { $problems.Add("Cmd-Shift-] twice went from $($before.Selected) to $($after.Selected), not to $expected") }
                        else { $tabMove = "$($before.Selected) -> $($after.Selected)" }
                    }
                }
                if ($closer -eq 'cmd+period') {
                    # T-100 question 3: with a list open, Cmd-. closes only the list (like Esc); the next one is Cancel.
                    # The language list on the General tab (Printers -> Widgets -> General); T-170 removed the printer's
                    # connection type list.
                    & $key 'cmd+shift+]'
                    & $key 'cmd+shift+]'
                    $combo = Get-AxElement $c $app.Id 'SettingsLanguage'
                    $windows = @(Get-ProcessWindows $c $app.Id).Count
                    if (-not $combo) { $listCheck = 'not checked (no SettingsLanguage)' }
                    else {
                        $center = Get-AxCenter $combo
                        $null = Invoke-Helper $c @('click', "$($center[0])", "$($center[1])")
                        if (-not (Wait-VerifyCondition -TimeoutSeconds 5 -Condition { @(Get-ProcessWindows $c $app.Id).Count -gt $windows })) { $problems.Add('a click did not open the list SettingsLanguage') }
                        else {
                            Start-Sleep -Milliseconds 500
                            & $key 'cmd+period'
                            if (-not (Wait-VerifyCondition -TimeoutSeconds 5 -Condition { @(Get-ProcessWindows $c $app.Id).Count -le $windows })) { $problems.Add('Cmd-. did not close the open list') }
                            elseif (-not (Get-VerifyMainWindow @(Get-ProcessWindows $c $app.Id))) { $problems.Add('Cmd-. with an open list closed Settings, not only the list') }
                            else { $listCheck = 'closed only the open list first' }
                            Start-Sleep -Milliseconds 500
                        }
                    }
                }
                & $key $closer
                if (-not (Wait-VerifyCondition -TimeoutSeconds 6 -Condition { -not (Get-VerifyMainWindow @(Get-ProcessWindows $c $app.Id)) })) {
                    $problems.Add("$closer did not close Settings")
                    # Closed another way, so that the next steps are judged on their own.
                    & $key 'cmd+w'
                    $null = Wait-VerifyCondition -TimeoutSeconds 6 -Condition { -not (Get-VerifyMainWindow @(Get-ProcessWindows $c $app.Id)) }
                }
            }
            if ((Get-FileHash -LiteralPath $settingsFile).Hash -ne $hash) { $problems.Add('closing Settings with a shortcut changed settings.json') }
            foreach ($window in 'about', 'diagnostics') {
                if (-not (& $frontWith $window)) { $problems.Add("$window did not come to the front"); continue }
                & $key 'cmd+w'
                if (-not (Wait-VerifyCondition -TimeoutSeconds 6 -Condition { -not (Get-VerifyMainWindow @(Get-ProcessWindows $c $app.Id)) })) { $problems.Add("Cmd-W did not close $window") }
            }
        }
        finally {
            $null = Stop-TestApp $data $app
            $null = Save-VerifyEvidence -Context $c -Name 'app.log' -Content (Read-TestLog $data)
        }
        if ($problems.Count -gt 0) { return New-VerifyResult FAIL ($problems -join '; ') }
        New-VerifyResult PASS "Ctrl-Cmd-F, Esc, Cmd-. and Cmd-W in the camera window; Cmd-Shift-] twice ($tabMove), Cmd-W and Cmd-. in Settings (no change saved; Cmd-. $listCheck); Cmd-W in About and Diagnostics."
    }

    New-VerifyCheck -Id 'mac.update-check' -Needs Session, Accessibility -Kind Permission -Permission 'Accessibility for the terminal (System Settings > Privacy & Security > Accessibility) for real mouse clicks and the Accessibility tree, and an unlocked screen; Screen Recording optional (window captures)' `
        -Title '"Check now" in Settings > Startup and updates shows its result after a real click into the active and into an inactive window and after AXPress (stand-in GitHub API on 127.0.0.1)' `
        -Closes 'T-127 položka „Kontrola aktualizací: text výsledku po skutečném kliknutí (macOS, hlášeno uživatelem 2026-11-05)“' -Action {
        param($c)
        # Nothing can be judged with a locked screen; Accessibility is asked for only in an unlocked session.
        $limitation = Get-SessionLimitation $c
        if ($limitation) { Skip-VerifyCheck "cannot be judged in this session: $limitation$(if (-not (Test-Accessibility $c)) { '; the terminal also has no Accessibility permission (System Settings > Privacy & Security > Accessibility)' }). Unlock the screen and run it again." }
        if (-not (Request-Accessibility $c)) { Skip-VerifyCheck 'no Accessibility permission for the terminal (System Settings > Privacy & Security > Accessibility; it may need a restart of the terminal).' }
        Invoke-SessionCheck $c {
            $scenarios = @(Invoke-UpdateCheckClicks -Context $c)
            $null = Save-VerifyEvidence -Context $c -Name 'scenarios.json' -Content (ConvertTo-VerifyJsonArray $scenarios)
            $text = ($scenarios | ForEach-Object { "$($_.Scenario): $(if ($_.Passed) { 'ok' } else { 'FAILED' }) - $($_.Detail)" }) -join '; '
            $appearance = if ((& /usr/bin/defaults read -g AppleInterfaceStyle 2>$null) -eq 'Dark') { 'dark' } else { 'light' }
            $captures = if ((Invoke-Helper $c @('screenrecording')) -contains 'true') { 'with window captures' } else { 'without window captures (no Screen Recording for the terminal)' }
            if (@($scenarios | Where-Object { -not $_.Passed }).Count -gt 0) { return New-VerifyResult FAIL "$text ($appearance appearance, $captures)" }
            New-VerifyResult PASS "$text ($appearance appearance, $captures)"
        }
    }

    New-VerifyCheck -Id 'mac.notification-click' -Kind Manual -Title 'a click on a delivered notification of the test bundle opens the printer web' `
        -Closes 'T-020 kritérium 2 (kliknutí otevře web tiskárny; aplikace běží)' -Action {
        param($c)
        $status = Get-TestNotificationStatus $c
        if ($status.Permission -ne 'Allowed') { Skip-VerifyCheck "the notifications of 'LayerPeek E2E' are $($status.Permission); run mac.notifications first." }
        $simulator = Get-Simulator $c
        Set-SimulatorScenario $simulator 'printing'
        $data = New-TestData -Context $c -Simulator $simulator -Notifications
        $null = Start-TestAppViaOpen -Context $c -TestData $data -Environment @{ LAYERPEEK_TEST_TOASTS = '1' }
        Start-Sleep -Seconds 12
        Set-SimulatorScenario $simulator 'paused'
        if (-not (Wait-VerifyCondition -TimeoutSeconds 45 -Condition { (Get-TestNotificationStatus $c).Delivered -gt 0 })) { return New-VerifyResult FAIL 'no notification of the test bundle was delivered.' }
        Write-VerifyStep $c "Click the 'LayerPeek E2E' notification now (the banner, or in Notification Centre). Waiting up to 2 minutes." 'Yellow'
        $urls = Join-Path $data.Data 'opened-urls.txt'
        $clicked = Wait-VerifyCondition -TimeoutSeconds 120 -Condition { (Test-Path -LiteralPath $urls) -and (Get-Content -LiteralPath $urls | Where-Object { $_ }) }
        $null = Save-VerifyEvidence -Context $c -Name 'opened-urls.txt' -Path $urls
        $null = Save-VerifyEvidence -Context $c -Name 'app.log' -Content (Read-TestLog $data)
        $null = Send-VerifyInstanceCommand -SocketPath $data.Socket -Command 'quit'
        if (-not $clicked) { return New-VerifyResult FAIL 'no address was opened within 2 minutes of the notification.' }
        New-VerifyResult PASS "the click opened $((Get-Content -LiteralPath $urls | Select-Object -First 1)) (recorded by the test mode instead of a browser)."
    }

    New-VerifyCheck -Id 'mac.socket-other-user' -Kind Permission -Permission 'sudo to connect to the instance socket as the user nobody (your administrator password, or none with the rule of tools/macos-verify-sudoers.sh)' `
        -Title 'a connection of another user to the instance socket is refused' `
        -Closes 'T-053 kritérium 1 (spojení od jiného UID se odmítne, skutečný test)' -Action {
        param($c)
        $data = New-TestData -Context $c -SharedTemp
        $app = Start-TestApp -Context $c -TestData $data
        try {
            if (-not (Send-VerifyInstanceCommand -SocketPath $data.Socket -Command 'about')) { return New-VerifyResult FAIL 'the own user could not use the instance channel (control).' }
            # Reachable for another user: the folder and the socket open to everyone, as an attacker would need.
            & /bin/chmod 755 $data.Data
            & /bin/chmod 777 $data.Socket
            # T-140: with the rule of tools/macos-verify-sudoers.sh nc runs as nobody without a password (also unattended).
            $probe = (& /usr/bin/sudo -n -u nobody /usr/bin/nc -h 2>&1 | Out-String)
            if ($probe -match 'password is required|may not run|not allowed') {
                if ($c.Unattended) { Skip-VerifyCheck "sudo needs a password for nc as nobody; install the rule once: sudo bash tools/macos-verify-sudoers.sh" }
                Write-VerifyStep $c 'sudo asks for your password (only for: sudo -u nobody nc -U <socket>; without it next time: sudo bash tools/macos-verify-sudoers.sh).' 'Yellow'
                & /usr/bin/sudo -v
                if ($LASTEXITCODE -ne 0) { Skip-VerifyCheck 'sudo was not authorized.' }
            }
            $attempt = Invoke-VerifyTool -Context $c -Label 'nc-as-nobody' -FilePath '/bin/sh' -ArgumentList @('-c', "printf 'activate\n' | /usr/bin/sudo -n -u nobody /usr/bin/nc -U -w 3 '$($data.Socket)'; echo `"nc exit code `$?`"")
            & /usr/bin/sudo -k
            Start-Sleep -Seconds 2
            $log = Read-TestLog $data
            $null = Save-VerifyEvidence -Context $c -Name 'app.log' -Content $log
            if ($log -notmatch 'Refused a connection of another user \(uid \d+\)') { return New-VerifyResult FAIL "the app did not log a refused connection; nc printed: $($attempt.Output -join ' ')" }
            if ($log -match 'The instance command Activate reached') { return New-VerifyResult FAIL 'the command of the other user reached the app.' }
            New-VerifyResult PASS "refused: '$((($log -split "`n") | Where-Object { $_ -match 'Refused a connection' } | Select-Object -First 1).Trim())'"
        }
        finally { $null = Stop-TestApp $data $app }
    }

    New-VerifyCheck -Id 'mac.display-sleep' -Needs Session, NoScreenSharing -Kind Permission -Permission 'putting the display to sleep (pmset displaysleepnow) and waking it (caffeinate -u); you may have to unlock the screen' `
        -Title 'real start while every display sleeps: monitoring runs and notifies, the menu bar item appears after wake-up' `
        -Closes 'T-096 kritérium 1 (skutečný start s uspaným displejem)', 'T-099 kritérium 1 (ikona po probuzení se stavem)', 'T-099 kritérium 3 (skutečný start se zhasnutým displejem)', 'T-099 kritéria 2 a 4 (sezení 8.2: oznámení události tisku ještě se zhasnutým displejem; potřebuje povolená oznámení z mac.notifications)' -Action {
        param($c)
        $simulator = Get-Simulator $c
        Set-SimulatorScenario $simulator 'printing'
        # Session 8.2: with the notifications of the test bundle allowed (mac.notifications), the pause while the display
        # sleeps must be delivered before the wake-up; macOS delivers them only to an app started through LaunchServices.
        # T-140: a Screen Sharing client keeps the display awake (assertion "Remote user active"): pmset displaysleepnow
        # then turns nothing off and the app never waits for a display, so nothing could be judged.
        if (@(& /usr/bin/pmset -g assertions 2>$null) -match 'Remote user active') { Skip-VerifyCheck 'Screen Sharing is connected and keeps the display awake (pmset -g assertions: "Remote user active"); run it at the Mac itself without Screen Sharing.' }
        $notify = (Get-TestNotificationStatus $c).Permission -eq 'Allowed'
        $data = New-TestData -Context $c -Simulator $simulator -Notifications:$notify
        Write-VerifyStep $c 'The display goes to sleep in 5 s and the script wakes it after about 40 s. Do not touch mouse or keyboard meanwhile; unlock the screen afterwards if asked.' 'Yellow'
        Start-Sleep -Seconds 5
        & /usr/bin/pmset displaysleepnow
        Start-Sleep -Seconds 5
        $app = if ($notify) { Get-Process -Id (Start-TestAppViaOpen -Context $c -TestData $data -Environment @{ LAYERPEEK_TEST_TOASTS = '1' } -NoWait) }
        else { Start-TestApp -Context $c -TestData $data -NoWait }
        try {
            $waiting = Wait-VerifyCondition -TimeoutSeconds 30 -Condition { (Read-TestLog $data) -match 'No active display' }
            $monitoring = Wait-VerifyCondition -TimeoutSeconds 30 -Condition { (Read-TestLog $data) -match 'Starting printer monitoring and notifications while waiting for an active display\.' }
            Set-SimulatorScenario $simulator 'paused'
            $deliveredAsleep = $false
            if ($notify) {
                $deliveredAsleep = Wait-VerifyCondition -TimeoutSeconds 30 -Condition { (Get-TestNotificationStatus $c).Delivered -gt 0 }
                # Delivered before the UI started, so while the app still waited for a display.
                $deliveredAsleep = $deliveredAsleep -and (Read-TestLog $data) -notmatch 'starting the UI\.'
            }
            else { Start-Sleep -Seconds 20 }
            & /usr/bin/caffeinate -u -t 2
            Write-VerifyStep $c 'Display woken. Unlock the screen if it is locked.' 'Yellow'
            Start-Sleep -Seconds 3
            # T-140: unattended nobody unlocks the screen; with a password delay after the display sleeps (System Settings >
            # Lock Screen) of more than about a minute it stays unlocked.
            $locked = Get-SessionLimitation $c
            if ($locked -and $c.Unattended) { Skip-VerifyCheck "the screen is locked after the wake-up ($locked); set a longer password delay after the display sleeps in System Settings > Lock Screen." }
            if ($locked) { $null = Wait-VerifyCondition -TimeoutSeconds 180 -Condition { -not (Get-SessionLimitation $c) } }
            $ui = Wait-VerifyCondition -TimeoutSeconds 180 -Condition { (Read-TestLog $data) -match 'starting the UI\.' }
            $item = Wait-VerifyCondition -TimeoutSeconds 30 -Condition { Test-StatusItem $c $app.Id }
            # The tooltip of the menu bar item (AXHelp) tells what the icon shows: the paused printer, not "Starting...".
            $tooltip = ''
            $shows = Wait-VerifyCondition -TimeoutSeconds 30 -Condition {
                $items = @(Get-StatusItems $c $app.Id | Where-Object { $_ -and $_.width -gt 0 })
                $script:tooltip = [string] ($items | Select-Object -First 1).help
                $script:tooltip -match 'Paused' -and $script:tooltip -notmatch 'Starting'
            }
            $tooltip = $script:tooltip
            $null = Save-VerifyEvidence -Context $c -Name 'status-items.json' -Content (ConvertTo-VerifyJsonArray (Get-StatusItems $c $app.Id))
            $log = Read-TestLog $data
            $null = Save-VerifyEvidence -Context $c -Name 'app.log' -Content $log
            $problems = @(
                if (-not $waiting) { "no 'No active display' in the log (did the display sleep?)" }
                if (-not $monitoring) { 'monitoring did not start while waiting' }
                if (-not $ui) { 'the UI did not start after the wake-up' }
                if ($log -notmatch 'The menu bar item attaches to the monitoring that already runs\.') { 'the menu bar item did not attach to the running monitoring' }
                if (-not $item) { 'no status item in the menu bar after the wake-up' }
                if ($item -and -not $shows) { "the menu bar item does not show the paused printer (tooltip: '$($tooltip -replace "`n", ' / ')')" }
                if ($notify -and -not $deliveredAsleep) { 'the notification of the pause was not delivered while the display slept' }
                if ($app.HasExited) { 'the app ended' }
            )
            if ($problems.Count -gt 0) { return New-VerifyResult FAIL ($problems -join '; ') }
            $notification = if ($notify) { ' The notification of the pause was delivered while the display slept.' } else { " Notification part not run: the notifications of 'LayerPeek E2E' are not allowed (run mac.notifications first)." }
            New-VerifyResult PASS "waited without a display with monitoring running; after the wake-up the menu bar item attached and showed the current state (tooltip '$($tooltip -replace "`n", ' / ')').$notification"
        }
        finally { $null = Stop-TestApp $data $app }
    }

    New-VerifyCheck -Id 'mac.gatekeeper-dialog' -Kind Manual -Title 'the Gatekeeper dialogs of the downloaded .dmg (guided, session 1 of docs/macos-manual-testing.md)' `
        -Closes 'T-019 kritérium 12 (dialog Gatekeeperu, texty do T-019)', 'T-026 kritérium 3 (podklad: aplikace z .dmg se spustí)' -Action {
        param($c)
        Write-VerifyStep $c 'Follow docs/macos-manual-testing.md, session 1, steps 2-4: download the .dmg with Safari, start LayerPeek from the disk image, then install it to Applications and start it from Finder (Open Anyway).'
        $null = Read-VerifyText -Context $c -Question 'Press Enter when done'
        $quarantine = Join-Path ([Environment]::GetFolderPath('UserProfile')) 'Downloads' "LayerPeek-$(Get-AppVersion)-osx-arm64.dmg"
        if (Test-Path -LiteralPath $quarantine) { $null = Invoke-VerifyTool -Context $c -Label 'xattr-download' -FilePath '/usr/bin/xattr' -ArgumentList @('-p', 'com.apple.quarantine', $quarantine) }
        $title = Read-VerifyText -Context $c -Question 'Exact text of the first Gatekeeper dialog (copy it; empty if none appeared)'
        $buttons = Read-VerifyText -Context $c -Question 'Its buttons'
        $null = Save-VerifyEvidence -Context $c -Name 'dialog.txt' -Content @("Dialog: $title", "Buttons: $buttons")
        $ok = @(
            Confirm-VerifyStep $c 'Did "Open Anyway" in Privacy & Security start LayerPeek, and does the next start open without asking?'
            Confirm-VerifyStep $c 'From the .dmg window: is "Open at login" in Settings > Startup and updates disabled with a reason?'
        )
        if ($ok -contains $false) { return New-VerifyResult FAIL 'the user answered no (steps.txt).' }
        New-VerifyResult MANUAL "dialog '$title' [$buttons]; Open Anyway worked (dialog.txt: copy into T-019 and compare with docs/release.md)."
    }

    New-VerifyCheck -Id 'mac.login-item' -Kind Manual -Title 'Open at login after a real log out and log in (/Applications/LayerPeek.app, two runs)' `
        -Closes 'T-019 kritérium 9 (autostart po přihlášení)' -Action {
        param($c)
        $installed = '/Applications/LayerPeek.app'
        if (-not (Test-Path -LiteralPath $installed)) { Skip-VerifyCheck 'LayerPeek is not in /Applications (install it from the .dmg first, mac.gatekeeper-dialog).' }
        if (-not (Read-VerifyAnswer -Context $c -Question 'Have you switched on "Open at login" in LayerPeek (Settings > Startup and updates), saved, and logged out and in since then?')) {
            Write-VerifyStep $c 'Do now: docs/macos-manual-testing.md session 7, steps 1-2 (switch on, Save, log out, log in). Then run: pwsh tools/macos-verify.ps1 -Only mac.login-item' 'Yellow'
            Skip-VerifyCheck 'pending: switch on Open at login, log out and in, then run -Only mac.login-item again.'
        }
        $ps = Invoke-VerifyTool -Context $c -Label 'ps' -FilePath '/bin/ps' -ArgumentList @('-axww', '-o', 'pid=,lstart=,command=')
        $null = Invoke-VerifyTool -Context $c -Label 'who' -FilePath '/usr/bin/who'
        $running = @($ps.Output | Where-Object { $_ -match [regex]::Escape("$installed/Contents/MacOS/LayerPeek") })
        if ($running.Count -eq 0) { return New-VerifyResult FAIL 'LayerPeek from /Applications is not running after the login.' }
        $ok = @(
            Confirm-VerifyStep $c 'Did LayerPeek start by itself at the login (menu bar icon), and is the start time in ps.txt right after your login (who.txt)?'
            Confirm-VerifyStep $c 'Session 7 step 3: switched off in System Settings > Login Items, does LayerPeek Settings show the hint and the button that opens Login Items?'
            Confirm-VerifyStep $c 'Session 7 step 4: switched off in LayerPeek and saved, is the item gone from Login Items?'
        )
        if ($ok -contains $false) { return New-VerifyResult FAIL 'the user answered no (steps.txt).' }
        New-VerifyResult MANUAL "started at login: $($running[0].Trim())"
    }

    New-VerifyCheck -Id 'mac.local-network' -Kind Manual -Title 'the Local Network dialog in the system language, Deny and Allow (real printer in the LAN, /Applications app)' `
        -Closes 'T-097 kritérium 3 (dialog v jazyce systému)', 'T-097 kritérium 4 (odepření bez pádu, srozumitelná hláška)' -Action {
        param($c)
        if (-not (Test-Path -LiteralPath '/Applications/LayerPeek.app')) { Skip-VerifyCheck 'LayerPeek is not in /Applications.' }
        Use-ProductionProfile $c {
            Write-VerifyStep $c 'Follow docs/macos-manual-testing.md, session 4 (start LayerPeek from Finder, add your printer in the LAN). The dialog appears with the first connection to the LAN.'
            $text = Read-VerifyText -Context $c -Question 'Exact text of the Local Network dialog and the system language (empty if none appeared)'
            $null = Save-VerifyEvidence -Context $c -Name 'dialog.txt' -Content $text
            $ok = @(
                Confirm-VerifyStep $c 'Is the dialog text the LayerPeek text in the system language?'
                Confirm-VerifyStep $c 'After "Don''t Allow": no crash, Diagnostics / Test connection show the Local Network hint?'
                Confirm-VerifyStep $c 'Allowed again in Privacy & Security > Local Network: does the connection come back by itself?'
            )
            if ($ok -contains $false) { New-VerifyResult FAIL 'the user answered no (steps.txt).' }
            else { New-VerifyResult MANUAL "dialog: $text" }
        }
    }

    New-VerifyCheck -Id 'mac.notifications-production' -Kind Manual -Title 'notifications of /Applications/LayerPeek.app, and the hint when they are switched off' `
        -Closes 'T-020 kritérium 1 (ze sestavené .app)', 'T-020 kritérium 3 (odepřené oprávnění: hláška a tlačítko v Nastavení)' -Action {
        param($c)
        if (-not (Test-Path -LiteralPath '/Applications/LayerPeek.app')) { Skip-VerifyCheck 'LayerPeek is not in /Applications.' }
        $simulator = Get-Simulator $c
        Set-SimulatorScenario $simulator 'printing'
        Use-ProductionProfile $c {
            Write-VerifyStep $c "Start /Applications/LayerPeek.app from Finder and add the simulator printer: address $($simulator.Address), user $($simulator.User), password $($simulator.Password) (test values of the simulator, not a secret)."
            $null = Read-VerifyText -Context $c -Question 'Press Enter when the printer shows as printing'
            Set-SimulatorScenario $simulator 'paused'
            Write-VerifyStep $c 'The simulator printer is paused now: macOS asks about notifications the first time; allow them.'
            $ok = @(
                Confirm-VerifyStep $c 'Did a notification with the printer name and the LayerPeek icon appear?'
                Confirm-VerifyStep $c 'Switched off in System Settings > Notifications > LayerPeek: does Settings > General show the red hint, and does its button open that page? Switched on again, does the hint go away?'
                Confirm-VerifyStep $c 'Have you removed the simulator printer in Settings again (its password leaves the Keychain)?'
            )
            if ($ok -contains $false) { New-VerifyResult FAIL 'the user answered no (steps.txt).' }
            else { New-VerifyResult MANUAL 'notification of the installed app and the hint confirmed.' }
        }
    }

    New-VerifyCheck -Id 'mac.keyboard-physical' -Kind Manual -Title 'shortcuts on the built-in and an external (Logitech) keyboard, also with the Czech layout' `
        -Closes 'T-100 kritérium 4 (ověřeno uživatelem včetně Logitech klávesnice)' -Action {
        param($c)
        $simulator = Get-Simulator $c
        $data = New-TestData -Context $c -Simulator $simulator -Camera $simulator.Camera -Window ([ordered]@{ rememberLastPlacement = $false; startMode = 'Normal' })
        $app = Start-TestApp -Context $c -TestData $data
        try {
            Send-TestCommand $data "camera:$($script:PrinterId)"
            Write-VerifyStep $c 'The test app runs with a camera window (tray icon [E2E]). Follow docs/macos-manual-testing.md session 6 step 3 on the built-in keyboard, then on the external keyboard, then with the Czech layout.'
            $ok = @(
                Confirm-VerifyStep $c 'Built-in keyboard: do Ctrl-Cmd-F, Esc, Cmd-., Cmd-W, Cmd-Shift-] / [ and Cmd-Opt-arrows work as described?'
                Confirm-VerifyStep $c 'External (Logitech) keyboard: the same?'
                Confirm-VerifyStep $c 'Czech layout: do Cmd-Shift-] / [ (keys right of P) switch the tabs and nothing else happens?'
            )
            $notes = Read-VerifyText -Context $c -Question 'Notes (e.g. what fn+F11 did), or empty'
            $null = Save-VerifyEvidence -Context $c -Name 'notes.txt' -Content $notes
            if ($ok -contains $false) { return New-VerifyResult FAIL "the user answered no (steps.txt). $notes" }
            New-VerifyResult MANUAL "confirmed on both keyboards and the Czech layout. $notes"
        }
        finally { $null = Stop-TestApp $data $app }
    }

    New-VerifyCheck -Id 'mac.profile-unchanged' -Kind Auto -Final -Title 'the watched places of the user profile are unchanged after the run (tools/profile-snapshot.ps1)' `
        -Closes 'T-021 kritérium 7 (kontrola před/po)', 'T-095 (izolace testů na macOS, kontrola)' -Action {
        param($c)
        $before = $c.Shared.ProfileBefore
        if (-not $before -or -not (Test-Path -LiteralPath $before)) { return New-VerifyResult FAIL 'no snapshot from the start of the run.' }
        $comparison = Compare-ProfileSnapshot -Path $before -LoginItems:($c.Shared.LoginItems -eq $true)
        $null = Save-VerifyEvidence -Context $c -Name 'profile-before.json' -Path $before
        $null = Save-VerifyEvidence -Context $c -Name 'profile-changes.txt' -Content (@($comparison.Changes) + @($comparison.NotChecked | ForEach-Object { "not checked: $_" }))
        Remove-Item -LiteralPath $before -Force
        if ($comparison.Changes.Count -eq 0) { return New-VerifyResult PASS "no change$(if ($comparison.NotChecked.Count) { " (not checked: $($comparison.NotChecked -join ', '))" })." }
        $used = @($c.Shared.ProductionProfileUsed | Where-Object { $_ })
        if ($used.Count -gt 0) { Skip-VerifyCheck "$($comparison.Changes.Count) change(s), expected: the production app ran with your profile in $($used -join ', ') (profile-changes.txt)." }
        New-VerifyResult FAIL "$($comparison.Changes.Count) change(s): $($comparison.Changes[0]) (profile-changes.txt)"
    }
)

# ---------------------------------------------------------------------------------------------------------------------
# Presets and preconditions (T-208)
# ---------------------------------------------------------------------------------------------------------------------

# The checks of each preset; they run in definition order (builds and tests first, the shared test bundle and simulator
# are made once). release: everything that runs without a human; later tasks (T-209 to T-217) add their checks here.
$presets = [ordered]@{
    release = @(
        'mac.build', 'mac.test', 'mac.aot-smoke', 'mac.keychain', 'mac.dock', 'mac.menubar', 'mac.menu-items', 'mac.settings-front',
        'mac.plist-localnetwork', 'mac.decode', 'mac.gatekeeper', 'mac.release-bundle', 'mac.notifications', 'mac.display-wait', 'mac.camera',
        'mac.camera-placement', 'mac.camera-pin', 'mac.camera-outline', 'mac.dock-windows', 'mac.mini-after-fullscreen', 'mac.camera-live',
        'mac.camera-next-attempt', 'mac.camera-autoclose', 'mac.camera-pin-ax', 'mac.detection-region', 'mac.appkit-language', 'mac.open-fake',
        'mac.hw-decoding-switch', 'mac.keys-czech-layout', 'mac.recording-files', 'mac.recording', 'mac.dashcam',
        'mac.timelapse-files', 'mac.timelapse', 'mac.timelapse-layers', 'mac.notifications-content', 'mac.file-dialogs', 'mac.keys', 'mac.update-check',
        'mac.socket-other-user', 'mac.display-sleep', 'mac.profile-unchanged'
    )
}

# What each precondition name of -Needs means: { param($Context) } returns $null when it is met, otherwise the reason.
$preconditions = [ordered]@{
    Session         = { param($c) Get-SessionLimitation $c }
    Accessibility   = { param($c) if (-not (Test-Accessibility $c)) { 'the terminal has no Accessibility permission (System Settings > Privacy & Security > Accessibility; then restart the terminal)' } }
    ScreenRecording = { param($c) if ((Invoke-Helper $c @('screenrecording')) -notcontains 'true') { 'the terminal has no Screen Recording permission (System Settings > Privacy & Security > Screen & System Audio Recording; then restart the terminal)' } }
    NoScreenSharing = { param($c) if (@(& /usr/bin/pmset -g assertions 2>$null) -match 'Remote user active') { 'Screen Sharing is connected and keeps the display awake (pmset -g assertions: "Remote user active")' } }
    ReleaseBuild    = { param($c) Get-ReleaseBuildProblem }
    AppNotRunning   = { param($c) Get-RunningAppProblem }
}

# The preconditions that are checked without the Swift helper.
$script:HelperFreeNeeds = @('NoScreenSharing', 'ReleaseBuild', 'AppNotRunning')

function Get-MacPreconditionState($Context, [string[]] $Needs) {
    <# .SYNOPSIS Checks the preconditions once: name -> $null (met) or the reason; all unmet when the helper does not compile. #>
    $state = [ordered]@{}
    $helperProblem = $null
    try { $null = Get-Helper $Context } catch { $helperProblem = "the helper could not be built: $($_.Exception.Message)" }
    foreach ($need in $Needs) {
        if ($helperProblem -and $need -notin $script:HelperFreeNeeds) { $state[$need] = $helperProblem; continue }
        try { $state[$need] = & $preconditions[$need] $Context }
        catch { $state[$need] = "could not be checked: $($_.Exception.Message)" }
    }
    $state
}

function Test-CaffeinateParent {
    <# .SYNOPSIS Whether this script runs under caffeinate (a parent process of any level). #>
    $process = Get-Process -Id $PID
    while ($process) {
        if ($process.ProcessName -eq 'caffeinate') { return $true }
        $process = $process.Parent
    }
    $false
}

# ---------------------------------------------------------------------------------------------------------------------
# Main
# ---------------------------------------------------------------------------------------------------------------------

$presetIds = $null
if ($Preset) {
    if (-not $presets.Contains($Preset)) { Write-Host "Unknown preset '$Preset' (presets: $($presets.Keys -join ', '))." -ForegroundColor Red; exit 2 }
    if ($Only) { Write-Host '-Preset and -Only do not go together (use -Skip to leave out checks of the preset).' -ForegroundColor Red; exit 2 }
    $presetIds = [string[]] $presets[$Preset]
}

if ($ListChecks) {
    if ($presetIds) {
        try { $listed = @(Select-VerifyPreset -Checks $checks -Name $Preset -Ids $presetIds) }
        catch { Write-Host $_.Exception.Message -ForegroundColor Red; exit 2 }
        Write-Output "Preset ${Preset}: $($listed.Count) checks, unattended; allowed in advance: $((@($listed | Where-Object Kind -EQ 'Permission').Id) -join ', ')"
        Format-VerifyCheckList -Checks $listed | Write-Output
        exit 0
    }
    Format-VerifyCheckList -Checks $checks | Write-Output
    exit 0
}

if ($SelfTest) {
    $problems = @(Invoke-VerifySelfTest -TempRoot ([System.IO.Path]::GetTempPath())) + @(Test-VerifyCheckDefinitions -Checks $checks -TasksRoot (Join-Path $script:Repo 'tasks'))
    foreach ($check in $checks) { if ($check.Id -notlike 'mac.*') { $problems += "The check $($check.Id) is not a mac.* check." } }
    foreach ($name in $presets.Keys) { $problems += @(Test-VerifyPreset -Checks $checks -Name $name -Ids $presets[$name]) }
    foreach ($need in Get-VerifyNeeds $checks) { if (-not $preconditions.Contains($need)) { $problems += "A check needs the precondition $need, which the script does not know." } }
    if ($problems.Count -gt 0) {
        Write-Output 'SELF-TEST FAILED:'
        $problems | ForEach-Object { Write-Output "  $_" }
        exit 1
    }
    Write-Output "SELF-TEST PASSED: engine, both modes, $($checks.Count) check definitions and the presets $($presets.Keys -join ', ')."
    exit 0
}

if (-not $IsMacOS) {
    Write-Host 'tools/macos-verify.ps1 runs on macOS only (use tools/windows-verify.ps1 on Windows).' -ForegroundColor Red
    exit 2
}

try { $selected = if ($presetIds) { @(Select-VerifyPreset -Checks $checks -Name $Preset -Ids $presetIds -Skip $Skip) } else { @(Select-VerifyChecks -Checks $checks -Only $Only -Skip $Skip) } }
catch { Write-Host $_.Exception.Message -ForegroundColor Red; exit 2 }
$Allow = @($Allow | ForEach-Object { $_ -split ',' } | ForEach-Object { $_.Trim() } | Where-Object { $_ })
if ($presetIds) {
    # A preset never asks: unattended, with its Permission checks allowed in advance.
    $Unattended = [switch] $true
    $Allow = @(@($Allow) + @($selected | Where-Object Kind -EQ 'Permission' | ForEach-Object Id) | Select-Object -Unique)
}
foreach ($pattern in $Allow) {
    if (-not ($checks | Where-Object { $_.Kind -eq 'Permission' -and $_.Id -like $pattern })) {
        Write-Host "-Allow '$pattern' matches no Permission check (see -ListChecks)." -ForegroundColor Red
        exit 2
    }
}
if ($selected.Count -eq 0) { Write-Host 'No check selected.' -ForegroundColor Red; exit 2 }
if ($PWD.ProviderPath -ne (Resolve-PhysicalPath $PWD.ProviderPath)) {
    Write-Host 'Note: the terminal is in the repository through a symbolic link; the script uses the physical path for dotnet.' -ForegroundColor Yellow
}

$root = if ($OutputRoot) { $OutputRoot } else { Join-Path $script:Repo 'artifacts' 'verify' }
$runFolder = New-VerifyRunFolder -Root $root
$context = New-VerifyContext -RunFolder $runFolder -Unattended:$Unattended -Allow $Allow
$context.Shared.ProductionProfileUsed = @()
$started = [DateTimeOffset]::Now
Write-Host "LayerPeek macOS verification ($($context.Unattended ? 'unattended' : 'interactive')$(if ($Preset) { ", preset $Preset" })), $($selected.Count) check(s); report in $runFolder"

# T-208: in an unattended run nobody can fix a missing precondition, so it is checked once here and the checks that need
# it end with SKIP and the reason, unrun (an interactive run lets each check ask for its permission itself).
$preconditionState = [ordered]@{}
if ($presetIds) {
    $preconditionState.Awake = if (Test-CaffeinateParent) { 'met (runs under caffeinate)' }
    else {
        $null = Start-Process -FilePath '/usr/bin/caffeinate' -ArgumentList @('-d', '-i', '-u', '-w', "$PID")
        'met (caffeinate -d -i -u started by the script)'
    }
}
$needs = @(Get-VerifyNeeds $selected)
if ($context.Unattended -and $needs.Count -gt 0) {
    $state = Get-MacPreconditionState $context $needs
    foreach ($need in $needs) {
        if ($state[$need]) { $context.Unmet[$need] = $state[$need]; $preconditionState[$need] = "not met: $($state[$need])" }
        else { $preconditionState[$need] = 'met' }
    }
    $unmetLines = @(Format-VerifyUnmetNeeds -Checks $selected -Unmet $context.Unmet)
    if ($unmetLines.Count -gt 0) { $unmetLines | ForEach-Object { Write-Host $_ -ForegroundColor Yellow } }
    else { Write-Host "Preconditions met: $($needs -join ', ')." }
}

# Leftovers of an interrupted run: test bundles of earlier runs (and their LaunchServices registrations).
$bundleRoot = Join-Path $script:Repo 'artifacts' 'verify-bundle'
if (Test-Path -LiteralPath $bundleRoot) {
    foreach ($old in Get-ChildItem -LiteralPath $bundleRoot -Directory) {
        $app = Join-Path $old.FullName 'LayerPeek E2E.app'
        Remove-TestBundle ([pscustomobject]@{ Path = $app; Executable = Join-Path $app 'Contents' 'MacOS' 'LayerPeek'; Folder = $old.FullName })
    }
}
# The Keychain items of the test prefix (never the production service LayerPeek), also after a crashed test run.
Add-VerifyCleanup $context 'Keychain test items' {
    foreach ($service in Get-KeychainTestServices) {
        for ($i = 0; $i -lt 50; $i++) { $null = & /usr/bin/security delete-generic-password -s $service 2>&1; if ($LASTEXITCODE -ne 0) { break } }
    }
}
if ($selected | Where-Object Id -EQ 'mac.profile-unchanged') {
    $context.Shared.LoginItems = (-not $Unattended) -and (Read-VerifyAnswer -Context $context -Question 'Include the Login Items in the before/after check of the profile (macOS asks for an administrator authorization for sfltool)?')
    $context.Shared.ProfileBefore = Join-Path ([System.IO.Path]::GetTempPath()) "layerpeek-verify-profile-$(Split-Path -Leaf $runFolder).json"
    Save-ProfileSnapshot -Path $context.Shared.ProfileBefore -LoginItems:$context.Shared.LoginItems
}

$results = Invoke-VerifyRun -Checks $selected -Context $context
$info = Get-VerifyRunInfo -RepoRoot $script:Repo -Script 'tools/macos-verify.ps1' -Unattended:$Unattended -Selected @($selected.Id) -Allow $Allow -Preset $Preset -Preconditions $preconditionState -Started $started
$info.Seconds = [math]::Round(([DateTimeOffset]::Now - $started).TotalSeconds)
$report = Write-VerifyReport -Folder $runFolder -Info $info -Results $results
$latest = Set-VerifyLatestLink -Root $root -RunFolder $runFolder
Write-Host ''
Format-VerifySummary -Results $results | ForEach-Object { Write-Host $_ }
Write-Host "Report: $($report.Markdown) ($(Format-VerifyDuration $info.Seconds)$(if ($latest) { "; also $latest" }))"
Write-Host 'Known leftovers no tool can remove: "LayerPeek E2E" in System Settings > Notifications; the terminal in Privacy & Security > Accessibility if you allowed it for mac.keys, mac.menu-items, mac.update-check, mac.recording, mac.dashcam, mac.timelapse, mac.timelapse-layers, mac.notifications-content, mac.file-dialogs, mac.dock-windows, mac.mini-after-fullscreen, mac.camera-live, mac.camera-next-attempt, mac.detection-region, mac.appkit-language, mac.hw-decoding-switch or mac.keys-czech-layout.'
exit (Get-VerifyExitCode -Results $results)
